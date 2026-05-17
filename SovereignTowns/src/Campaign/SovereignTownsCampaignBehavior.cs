using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SovereignTowns.Audit;
using SovereignTowns.Battle;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Lifecycle;
using SovereignTowns.Managers;
using SovereignTowns.Models;
using SovereignTowns.Patrol;
using SovereignTowns.Recruitment;
using SovereignTowns.SallyForth;
using SovereignTowns.Transfer;
using SovereignTowns.Ui;
using SovereignTowns.WebConfig;
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
/// Campaign 事件分发中心：首府、招募、调拨、巡逻、出击等 Manager 在此初始化并转发事件。
/// </summary>
public sealed class SovereignTownsCampaignBehavior : CampaignBehaviorBase
{
    private PartyLifecycleManager? _lifecycle;
    private CapitalRegistry? _capitalRegistry;
    private RecruitmentManager? _recruitmentManager;
    private PrisonerRecruitmentManager? _prisonerRecruitmentManager;
    private CapitalLogisticsManager? _capitalLogisticsManager;
    private BattleLootManager? _battleLootManager;
    private GarrisonTransferManager? _transferManager;
    private PatrolManager? _patrolManager;
    private SallyForthManager? _sallyForthManager;
    private SovereignTowns.SettlementManagement.VanillaSuppressionManager? _vanillaSuppression;

    /// <summary>
    /// SyncData(load) → OnSessionLaunched 之间的暂存：clanStringId → settlementStringId。
    /// 用户 2026-05-14 二次决策：仅回补"首府"这一项 mod 自定义存档；scheduler/ledger 仍瞬态。
    /// 玩家 + AI 全在此 dict（取决于存档当时 ApplyToAiSettlementsToo 是否开启）。
    /// </summary>
    private Dictionary<string, string>? _pendingCapitals;

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
        // 仅回补"首府"持久化（用户 2026-05-14 二次决策）。
        // Scheduler 历史 / Finance ledger / Snapshot 仍不存 — 重载后由 daily/hourly + RebuildFromCampaign 重建。
        // vanilla [SaveableField] / SovereignTownsTypeDefiner 不在此方法范围。
        //
        // 序列化形式：单一 key "st_capitals_json"，值是 Dictionary<clanStringId, settlementStringId> 的 JSON。
        // 失败任何一步都落 _pendingCapitals = null（不阻断 mod 启动；Initialize() 走随机抽取兜底）。
        try
        {
            string capitalsJson = string.Empty;
            if (dataStore.IsSaving)
            {
                try
                {
                    var dict = _capitalRegistry?.ExportCapitals() ?? new Dictionary<string, string>();
                    capitalsJson = JsonConvert.SerializeObject(dict);
                }
                catch (Exception exSave)
                {
                    Logger.Error("SovereignTowns: SyncData export failed; writing empty payload", exSave);
                    capitalsJson = string.Empty;
                }
            }

            dataStore.SyncData("st_capitals_json", ref capitalsJson);

            if (dataStore.IsLoading)
            {
                if (!string.IsNullOrEmpty(capitalsJson))
                {
                    try
                    {
                        _pendingCapitals = JsonConvert.DeserializeObject<Dictionary<string, string>>(capitalsJson);
                    }
                    catch (Exception exLoad)
                    {
                        Logger.Error("SovereignTowns: failed to parse st_capitals_json; falling back to empty", exLoad);
                        _pendingCapitals = null;
                    }
                }
                else
                {
                    _pendingCapitals = null;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SovereignTowns: SyncData(st_capitals_json) failed", ex);
            _pendingCapitals = null;
        }
    }

    private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
    {
        try
        {
            ConfigurationManager.Initialize();
            DecisionAuditLogger.Initialize();

            // B7.1 (fix 2026-05-14): dump troops.json HERE, not in OnGameStart — by the time
            // OnSessionLaunched fires the full CharacterObject pool from spnpccharacters.xml
            // (and every other mod's troop xml) is registered with MBObjectManager. Earlier
            // attempts at OnGameStart produced count=0 because that hook runs before campaign
            // object xml ingestion.
            try { SovereignTowns.WebConfig.TroopDumper.Dump(); }
            catch (Exception ex) { Logger.Error("TroopDumper.Dump failed (swallowed)", ex); }

            _lifecycle = new PartyLifecycleManager();
            _lifecycle.Initialize();

            // 注：3 个自定义 GameModel (STPartySizeLimitModel/STPartySpeedModel/STPartyWageModel)
            // 必须在 OnGameStart(Game, IGameStarter) 内调 AddModel —— 此处 OnSessionLaunched 太晚，
            // Campaign 已 finalized，AddModel 会破坏内部 model list 致后续 vanilla 计算崩溃。
            // 2026-05-13 启动崩溃修复：已搬到 SovereignTownsSubModule.OnGameStart。

            // B7.15：CapitalManager 多 clan 化 — registry 接管 player + 可选 AI。
            _capitalRegistry = new CapitalRegistry(_lifecycle);
            // 2026-05-14 Task X：把 SyncData 暂存的 clan→capital 映射喂给 registry，
            // 让 EnsureForClan 在 Initialize 内走"沿用持久化 stringId"分支；为空 → 走随机抽取兜底。
            if (_pendingCapitals != null)
            {
                _capitalRegistry.RestoreCapitals(_pendingCapitals);
                _pendingCapitals = null;
            }
            _capitalRegistry.Initialize();

            _transferManager = new GarrisonTransferManager(_lifecycle);
            _battleLootManager = new BattleLootManager(_capitalRegistry);
            // B7.27：sally 先构造，patrol 接受 sally 引用以做支援判定
            _sallyForthManager = new SallyForthManager(_lifecycle, _capitalRegistry, _battleLootManager);
            _patrolManager = new PatrolManager(_lifecycle, _capitalRegistry, _sallyForthManager, _battleLootManager);

            // 2026-05-12 审查 B-WarPartyComponent.OnFinalize 修复：sally party 战场被歼灭时
            // 由 vanilla 直接 destroy → roster 残留兵员丢失；订阅 MobilePartyDestroyed 抢救存活兵回 home。
            // 注意：PartyLifecycleManager 内部已订阅同事件做 untrack（不冲突，事件支持多订阅）。
            try
            {
                // Lambda 自带 try-catch 兜底，避免 vanilla MulticastDelegate 链因本订阅抛而中断后续订阅者
                // （多个 mod / 内部 PartyLifecycleManager 都订阅同一事件）。
                CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, (party, destroyer) =>
                {
                    try { _sallyForthManager?.OnMobilePartyDestroyed(party, destroyer); }
                    catch (Exception lambdaEx) { Logger.Error("MobilePartyDestroyed sally rescue lambda threw", lambdaEx); }
                });
            }
            catch (Exception ex) { Logger.Error("Failed to subscribe MobilePartyDestroyed for SallyForth rescue", ex); }

            // DiagnosticGameMenu 仍只关心 player capital（UI 玩家视角）— 传 registry，菜单内部走 GetForPlayer。
            DiagnosticGameMenu.Register(campaignGameStarter, _capitalRegistry);
            // B7.22：自家 party encounter 拦截 — 玩家碰到征兵队 / 调拨队等会进入对话而非战斗界面
            STPartyDialogRegistration.Register(campaignGameStarter);

            // B7: ribbon retired. Player config is now web-only via DiagnosticGameMenu's
            // "打开网页控制面板" town menu option + WebConfigServer.

            _recruitmentManager = new RecruitmentManager(_lifecycle, _capitalRegistry);
            _prisonerRecruitmentManager = new PrisonerRecruitmentManager(_capitalRegistry);
            _capitalLogisticsManager = new CapitalLogisticsManager(
                _capitalRegistry,
                _recruitmentManager,
                _transferManager);

            // B7.14：抑制 vanilla 在我们接管的城镇/城堡上的 GarrisonAutoRecruitment。
            // 时序：必须在 RecruitmentManager 构造之后；否则 vanilla 在 Settlement.All 初次扫描前 hook 上来可能错过。
            _vanillaSuppression = new SovereignTowns.SettlementManagement.VanillaSuppressionManager();
            _vanillaSuppression.Initialize();

            Logger.Info($"OnSessionLaunched: 全部 Manager 就绪 (含 Capital + SallyForth + VanillaSuppression) ConfigVersion={ConfigurationManager.Current.ConfigVersion}");
            Logger.Info($"  features: AutoGarrison={ConfigurationManager.Current.EnabledFeatures.AutoGarrison} " +
                        $"AutoRecruitment={ConfigurationManager.Current.EnabledFeatures.AutoRecruitment} " +
                        $"AutoPatrol={ConfigurationManager.Current.EnabledFeatures.AutoPatrol} " +
                        $"TroopTransfers={ConfigurationManager.Current.EnabledFeatures.TroopTransfers}");

            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment)
                Logger.Info("  HINT: AutoRecruitment 已禁用。global.json 改为 true 启用");
            if (!ConfigurationManager.Current.EnabledFeatures.TroopTransfers)
                Logger.Info("  HINT: TroopTransfers 已禁用。global.json 改为 true 启用");
            if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol)
                Logger.Info("  HINT: AutoPatrol 已禁用。global.json 改为 true 启用");

            // B7.5: 提示玩家打开网页面板 — 但 URL 含 session token，不能写日志 / 聊天面板（玩家分享
            // ModLogs 截图就会泄漏，攻击者可在 session 期间写任意配置）。改为只提示从城镇菜单进入。
            try
            {
                if (SovereignTowns.WebConfig.WebConfigServer.IsRunning)
                {
                    int port = SovereignTowns.WebConfig.WebConfigServer.Port;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[主权城镇] 网页控制面板已启动。请进入任意城镇菜单点「打开网页控制面板」。", Colors.Green));
                    Logger.Info($"WebConfigServer listening on 127.0.0.1:{port} (token withheld from logs/UI)");
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[主权城镇] 网页控制面板未启动（端口冲突/沙盒拒绝），详见日志。", Colors.Yellow));
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
            DrainWebConfigSync();
            _capitalLogisticsManager?.EvaluateAll();
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
            DrainWebConfigSync();
            // 首行性能：每个 Manager 内部都有 PartyComponent 类型过滤
            _recruitmentManager?.OnHourlyTickParty(party);
            _transferManager?.OnHourlyTickParty(party);
            _patrolManager?.OnHourlyTickParty(party);
            _sallyForthManager?.OnHourlyTickParty(party);
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
            DrainWebConfigSync();
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
            DrainWebConfigSync();
            _sallyForthManager?.OnHourlyTickSettlement(settlement);
            // 用户明确：XP 注入 + 俘虏招募仅在首府进行；招兵/调拨由 CapitalLogisticsManager 在 DailyTick 统一调度。
            // B7.15 multi-clan：以"该 settlement 的 ownerClan 是否把它当首府"为准 — 玩家或 AI 都按各自首府走。
            var mgr = _capitalRegistry?.GetForSettlement(settlement);
            var capitalSettlement = _capitalRegistry?.GetCapitalForClan(mgr?.OwnerClan);
            // B7.20：诊断日志 — 让玩家在 ModLogs 直接看到 daily tick 是否走到首府路径
            if (settlement != null && (settlement.IsTown || settlement.IsCastle) && settlement.OwnerClan == Clan.PlayerClan)
            {
                Logger.Info($"OnDailyTickSettlement '{settlement.Name}' (ownerClan={settlement.OwnerClan?.StringId}): " +
                            $"registry hasMgr={mgr != null} capital={capitalSettlement?.Name?.ToString() ?? "<none>"} matches={(capitalSettlement == settlement)}");
            }
            if (capitalSettlement != null && settlement == capitalSettlement)
            {
                Upgrades.GarrisonXpInjector.GiveDailyXpToGarrison(settlement);
                _prisonerRecruitmentManager?.OnDailyTickSettlement(settlement);
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
            _battleLootManager?.OnMapEventEnded(mapEvent);
            _sallyForthManager?.OnMapEventEnded(mapEvent);
            _patrolManager?.OnMapEventEnded(mapEvent);
            _recruitmentManager?.OnMapEventEnded(mapEvent);
        }
        catch (Exception ex)
        {
            Logger.Error("OnMapEventEnded forwarding failed", ex);
        }
    }

    private static void DrainWebConfigSync()
    {
        try { WebConfigGameThreadSync.Drain(); }
        catch (Exception ex) { Logger.Error("WebConfigGameThreadSync.Drain failed", ex); }
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
            DrainWebConfigSync();
            // B7.15 multi-clan：registry 内部转发到 oldOwner / newOwner clan 的 manager，
            // 并在 AI toggle 开启时给新获得城的 AI clan 自动 EnsureForClan。
            _capitalRegistry?.OnSettlementOwnerChanged(settlement, openToClaim, newOwner, oldOwner, capturerHero, detail);
        }
        catch (Exception ex)
        {
            Logger.Error($"OnSettlementOwnerChanged forwarding failed (settlement='{settlement?.Name}')", ex);
        }
    }

}
