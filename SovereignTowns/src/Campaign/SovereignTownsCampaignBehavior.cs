using System;
using SovereignTowns.Audit;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Integration;
using SovereignTowns.Lifecycle;
using SovereignTowns.Llm;
using SovereignTowns.Managers;
using SovereignTowns.Models;
using SovereignTowns.Patrol;
using SovereignTowns.Recruitment;
using SovereignTowns.SallyForth;
using SovereignTowns.Transfer;
using SovereignTowns.Ui;
using SovereignTowns.Ui.MapRibbon;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Campaign;

/// <summary>
/// 主 CampaignBehavior — 事件分发中心。
/// MVP 3.5 起包含：Lifecycle + Recruitment + TownGarrison + CastleSupport + GarrisonTransfer 5 个 Manager。
/// </summary>
public sealed class SovereignTownsCampaignBehavior : CampaignBehaviorBase
{
    private PartyLifecycleManager? _lifecycle;
    private CapitalManager? _capitalManager;
    private RecruitmentManager? _recruitmentManager;
    private PrisonerRecruitmentManager? _prisonerRecruitmentManager;
    private TownGarrisonManager? _townGarrisonManager;
    private CastleSupportManager? _castleSupportManager;
    private GarrisonTransferManager? _transferManager;
    private PatrolManager? _patrolManager;
    private SallyForthManager? _sallyForthManager;
    private LLMReasoningService? _llmService;
    private LLMConfig _llmConfig = new LLMConfig();

    /// <summary>
    /// SyncData 在 OnSessionLaunched 之前运行 — 此时 <see cref="_capitalManager"/> 还未构造。
    /// 暂存从存档读到的 stringId，待 OnSessionLaunched 创建 <see cref="CapitalManager"/> 时回灌。
    /// </summary>
    private string? _pendingCapitalStringId;

    public override void RegisterEvents()
    {
        try
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, OnHourlyTickParty);
            CampaignEvents.HourlyTickSettlementEvent.AddNonSerializedListener(this, OnHourlyTickSettlement);
            // 2026-05-12 审查 B1 修复：HourlyTickSettlement 在城内停留时会跳 tick，
            // 加 DailyTickSettlement 兜底，确保 sortie 评估最坏延迟 ≤ 24h（实际多数 ≤ 1h）。
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            // MVP 5：战斗结束 → SallyForth 战后回程（精确触发，vs GDS 的 TickEvent 轮询 bug）
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            Logger.Info("SovereignTownsCampaignBehavior: events registered");
        }
        catch (Exception ex)
        {
            Logger.Error("RegisterEvents failed", ex);
        }
    }

    public override void SyncData(IDataStore dataStore)
    {
        // 仅持久化首府 settlement StringId；其余 Manager 状态从 vanilla state 重建。
        // 注意：SyncData 在 OnSessionLaunched 之前运行，_capitalManager 此时仍为 null —
        // 写入路径：若 _capitalManager 已构造则取最新值，否则回退到 _pendingCapitalStringId（旧存档→新加载循环）；
        // 读取路径：把读到的 stringId 暂存到 _pendingCapitalStringId，OnSessionLaunched 创建 CapitalManager 时再回灌。
        string? capStringId = _capitalManager?.GetCapitalStringId() ?? _pendingCapitalStringId;
        dataStore.SyncData("st_capital_stringid", ref capStringId);
        if (dataStore.IsLoading)
        {
            _pendingCapitalStringId = capStringId;
        }
    }

    private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
    {
        try
        {
            ConfigurationManager.Initialize();
            DecisionAuditLogger.Initialize();

            _lifecycle = new PartyLifecycleManager();
            _lifecycle.Initialize();

            // 注：3 个自定义 GameModel (STPartySizeLimitModel/STPartySpeedModel/STPartyWageModel)
            // 必须在 OnGameStart(Game, IGameStarter) 内调 AddModel —— 此处 OnSessionLaunched 太晚，
            // Campaign 已 finalized，AddModel 会破坏内部 model list 致后续 vanilla 计算崩溃。
            // 2026-05-13 启动崩溃修复：已搬到 SovereignTownsSubModule.OnGameStart。

            _capitalManager = new CapitalManager(_lifecycle);
            _capitalManager.RestoreFromStringId(_pendingCapitalStringId);
            _capitalManager.Initialize();
            _pendingCapitalStringId = null; // 已回灌，清理暂存

            _transferManager = new GarrisonTransferManager(_lifecycle);
            _castleSupportManager = new CastleSupportManager(_capitalManager, _transferManager);
            _patrolManager = new PatrolManager(_lifecycle, _capitalManager);
            _sallyForthManager = new SallyForthManager(_lifecycle, _capitalManager);

            // 2026-05-12 审查 B-WarPartyComponent.OnFinalize 修复：sally party 战场被歼灭时
            // 由 vanilla 直接 destroy → roster 残留兵员丢失；订阅 MobilePartyDestroyed 抢救存活兵回 home。
            // 注意：PartyLifecycleManager 内部已订阅同事件做 untrack（不冲突，事件支持多订阅）。
            try
            {
                CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this,
                    (party, destroyer) => _sallyForthManager?.OnMobilePartyDestroyed(party, destroyer));
            }
            catch (Exception ex) { Logger.Error("Failed to subscribe MobilePartyDestroyed for SallyForth rescue", ex); }

            DiagnosticGameMenu.Register(campaignGameStarter, _capitalManager);
            SafeUninstallMenu.Register(campaignGameStarter);
            MCMIntegration.TryRegister();

            // 大地图常驻 ribbon — 点击直接打开控制面板。Inject 内部幂等 + try/catch，
            // MapScreen 尚未存在时只记日志，由 OnGameMenuOpened 兜底重试。
            SovereignTownsRibbonInjector.Inject();

            _llmConfig = LoadLlmConfigOrDefault();
            ILLMProvider provider = _llmConfig.Provider?.ToLowerInvariant() switch
            {
                "ollama" => new LocalOllamaProvider(_llmConfig),
                "openai_compatible" => new RemoteOpenAICompatibleProvider(_llmConfig),
                _ => new NoOpLLMProvider()
            };
            _llmService = new LLMReasoningService(provider, _llmConfig);

            _recruitmentManager = new RecruitmentManager(_lifecycle, _capitalManager);
            _prisonerRecruitmentManager = new PrisonerRecruitmentManager();
            _townGarrisonManager = new TownGarrisonManager(
                recruitmentManager: _recruitmentManager,
                llmService: _llmService,
                capitalManager: _capitalManager,
                castleSupportManager: _castleSupportManager,
                transferManager: _transferManager,
                lifecycle: _lifecycle);

            Logger.Info($"OnSessionLaunched: 全部 Manager 就绪 (8 个, 含 Capital + SallyForth) ConfigVersion={ConfigurationManager.Current.ConfigVersion} MCM={MCMIntegration.GetDiagnosticInfo()} LLM=provider:{provider.Name} configured:{provider.IsConfigured}");
            Logger.Info($"  features: AutoGarrison={ConfigurationManager.Current.EnabledFeatures.AutoGarrison} " +
                        $"AutoRecruitment={ConfigurationManager.Current.EnabledFeatures.AutoRecruitment} " +
                        $"AutoPatrol={ConfigurationManager.Current.EnabledFeatures.AutoPatrol} " +
                        $"CastleSupport={ConfigurationManager.Current.EnabledFeatures.CastleSupport}");

            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment)
                Logger.Info("  HINT: AutoRecruitment 已禁用。global.json 改为 true 启用");
            if (!ConfigurationManager.Current.EnabledFeatures.CastleSupport)
                Logger.Info("  HINT: CastleSupport 已禁用。global.json 改为 true 启用");
            if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol)
                Logger.Info("  HINT: AutoPatrol 已禁用。global.json 改为 true 启用");

            // B7.5: announce web config endpoint to the player. URL contains the auth token —
            // displayed once per session so they can copy it manually if needed, but the
            // normal path is via the "打开网页控制面板" town menu option.
            try
            {
                if (SovereignTowns.WebConfig.WebConfigServer.IsRunning)
                {
                    string url = SovereignTowns.WebConfig.WebConfigServer.GetBrowserUrl();
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[Sovereign Towns] 网页控制面板：{url}", Colors.Green));
                    Logger.Info($"WebConfigServer URL: {url}");
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[Sovereign Towns] 网页控制面板未启动（端口冲突/沙盒拒绝），详见日志。", Colors.Yellow));
                }
            }
            catch (Exception ex) { Logger.Error("WebConfig URL announce failed (swallowed)", ex); }
        }
        catch (Exception ex)
        {
            Logger.Error("OnSessionLaunched failed", ex);
        }
    }

    /// <summary>
    /// 读档后回调（vanilla 在 OnSessionLaunched 之后触发）：重建
    /// <see cref="PartyLifecycleManager"/> 的 _tracked 索引，
    /// 避免上限 / 空闲检测在加载存档后失效（CountActive 永远为 0 漏洞）。
    /// </summary>
    private void OnGameLoaded(CampaignGameStarter starter)
    {
        try
        {
            _lifecycle?.RebuildFromCampaign();
        }
        catch (Exception ex)
        {
            Logger.Error("OnGameLoaded RebuildFromCampaign failed", ex);
        }
    }

    private void OnDailyTick()
    {
        try
        {
            _townGarrisonManager?.EvaluateAll();

            // CastleSupport 调拨决策
            if (_castleSupportManager != null && _transferManager != null
                && ConfigurationManager.Current.EnabledFeatures.CastleSupport)
            {
                var tasks = _castleSupportManager.EvaluateAll();
                Logger.Info($"DailyTick: CastleSupport 产出 {tasks.Count} 个调拨任务");
                foreach (var task in tasks)
                {
                    try
                    {
                        var dispatched = _transferManager.TryDispatchTransfer(task);
                        if (!dispatched)
                            Logger.Info($"  transfer task declined: {task.Source.Name} → {task.Destination.Name}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("TryDispatchTransfer 单任务失败", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DailyTick failed", ex);
        }
    }

    private void OnHourlyTickParty(MobileParty party)
    {
        try
        {
            // 首行性能：每个 Manager 内部都有 PartyComponent 类型过滤
            _recruitmentManager?.OnHourlyTickParty(party);
            _transferManager?.OnHourlyTickParty(party);
            _patrolManager?.OnHourlyTickParty(party);
            _sallyForthManager?.OnHourlyTickParty(party);

            // 兜底：OnSessionLaunched 时若 MapScreen 尚未实例化（罕见路径：教程/角色创建），
            // 用 hourly tick 重试一次。Inject 自身幂等 + 内部检查 IsInjected 后立即 return。
            if (!SovereignTownsRibbonInjector.IsInjected)
                SovereignTownsRibbonInjector.Inject();
        }
        catch (Exception ex)
        {
            Logger.Error("OnHourlyTickParty failed", ex);
        }
    }

    private void OnHourlyTickSettlement(Settlement settlement)
    {
        try
        {
            _patrolManager?.OnHourlyTickSettlement(settlement);
            _sallyForthManager?.OnHourlyTickSettlement(settlement);
        }
        catch (Exception ex)
        {
            Logger.Error("OnHourlyTickSettlement failed", ex);
        }
    }

    /// <summary>
    /// 兜底：vanilla HourlyTickSettlement 在城内停留时可能跳 tick（已知现象），
    /// daily 用同一评估逻辑重新检查一次。SallyForthManager 内部已有上限/冷却保护，重复触发无副作用。
    /// </summary>
    private void OnDailyTickSettlement(Settlement settlement)
    {
        try
        {
            _sallyForthManager?.OnHourlyTickSettlement(settlement);
            // 用户明确：XP 注入 + 俘虏招募 + 升级触发 仅在首府进行；非首府/城堡走 CastleSupport 调拨。
            var capitalSettlement = _capitalManager?.GetCapitalSettlement();
            if (capitalSettlement != null && settlement == capitalSettlement)
            {
                Upgrades.GarrisonXpInjector.GiveDailyXpToGarrison(settlement);
                _prisonerRecruitmentManager?.OnDailyTickSettlement(settlement);
                Recruitment.CapitalInPlaceRecruiter.RecruitFromCapitalNotables(settlement);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnDailyTickSettlement failed", ex);
        }
    }

    /// <summary>
    /// 战斗结束回调（vanilla <c>CampaignEvents.MapEventEnded</c>，签名 <c>Action&lt;MapEvent&gt;</c>）。
    /// 转发到 <see cref="SallyForthManager"/>；try-catch 包裹避免影响 vanilla 事件链。
    /// </summary>
    private void OnMapEventEnded(MapEvent mapEvent)
    {
        try
        {
            _sallyForthManager?.OnMapEventEnded(mapEvent);
            _patrolManager?.OnMapEventEnded(mapEvent);
        }
        catch (Exception ex)
        {
            Logger.Error("OnMapEventEnded forwarding failed", ex);
        }
    }

    /// <summary>
    /// settlement 易主回调（vanilla
    /// <c>CampaignEvents.OnSettlementOwnerChangedEvent</c>，签名
    /// <c>(Settlement, bool, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)</c>）。
    /// 转发到 <see cref="CapitalManager"/>；try-catch 包裹避免影响 vanilla 事件链。
    /// </summary>
    private void OnSettlementOwnerChanged(
        Settlement settlement,
        bool openToClaim,
        Hero newOwner,
        Hero oldOwner,
        Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        try
        {
            _capitalManager?.OnSettlementOwnerChanged(settlement, openToClaim, newOwner, oldOwner, capturerHero, detail);
        }
        catch (Exception ex)
        {
            Logger.Error($"OnSettlementOwnerChanged forwarding failed (settlement='{settlement?.Name}')", ex);
        }
    }

    /// <summary>
    /// 从 Modules/SovereignTowns/Configs/llm.json 读取 LLMConfig。
    /// 文件不存在 / 解析失败 → 返回默认 NoOp 配置。
    /// </summary>
    private static LLMConfig LoadLlmConfigOrDefault()
    {
        try
        {
            var modulePath = TaleWorlds.ModuleManager.ModuleHelper.GetModuleFullPath("SovereignTowns");
            var path = System.IO.Path.Combine(modulePath, "Configs", "llm.json");
            if (!System.IO.File.Exists(path))
            {
                Logger.Info("LLM: llm.json 不存在，使用默认 NoOp 配置（路径：" + path + "）");
                return new LLMConfig();
            }
            var txt = System.IO.File.ReadAllText(path);
            var cfg = ParseLlmConfig(txt);
            Logger.Info($"LLM: 已加载 {path} provider={cfg.Provider} enableLong={cfg.EnableForLongTermPlanning} enableUser={cfg.EnableForUserAdvice}");
            return cfg;
        }
        catch (Exception ex)
        {
            Logger.Warn("LLM: llm.json 加载失败，退回 NoOp：" + ex.Message);
            return new LLMConfig();
        }
    }

    private static LLMConfig ParseLlmConfig(string json)
        => Newtonsoft.Json.JsonConvert.DeserializeObject<LLMConfig>(json) ?? new LLMConfig();
}
