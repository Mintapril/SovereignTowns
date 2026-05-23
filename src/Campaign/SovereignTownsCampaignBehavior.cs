using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SovereignTowns.Audit;
using SovereignTowns.Common;
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
using TaleWorlds.Localization;
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
    private RecruitmentDispatcher? _recruitmentDispatcher;
    private PrisonerRecruitmentManager? _prisonerRecruitmentManager;
    private CapitalLogisticsManager? _capitalLogisticsManager;
    private TransferDispatcher? _transferDispatcher;
    private PatrolDispatcher? _patrolDispatcher;
    private SallyDispatcher? _sallyDispatcher;

    // B16.2: 静态 accessor — StSallyPartyComponent.NotifyDispatcherEnded 通过它通知 cooldown 重置。
    private static SallyDispatcher? _staticSallyDispatcher;
    public static SallyDispatcher? SallyDispatcher => _staticSallyDispatcher;

    /// <summary>
    /// 2026-05-23 反注册路径：CampaignBehaviorBase 没有 OnRemovedBehavior hook（已通过反射确认），
    /// 故 SovereignTownsSubModule.OnSubModuleUnloaded 需要一个进程级入口调本实例的 Uninstall()。
    /// 用静态字段保留当前活跃 behavior 实例；跨存档重新构造时由 OnSessionLaunched 覆盖。
    /// </summary>
    private static SovereignTownsCampaignBehavior? _instance;
    internal static SovereignTownsCampaignBehavior? Instance => _instance;

    /// <summary>
    /// R3：跨存档去重 — `ConfigurationManager.OnConfigChanged` 是 static event，
    /// 同进程内连续加载多个存档会创建多个 behavior 实例，每实例的 `-=` 因 delegate
    /// target 是新 this，无法删除前一实例的订阅 → static event 永远持有旧实例。
    /// 用静态字段保留"上一轮注册的 delegate"，新 RegisterEvents 时先 unsubscribe 它。
    /// </summary>
    private static Action<string?>? _registeredConfigChangedHandler;
    private SovereignTowns.SettlementManagement.VanillaSuppressionManager? _vanillaSuppression;
    private SovereignTowns.SettlementManagement.VanillaPatrolSuppressor? _vanillaPatrolSuppressor;

    /// <summary>
    /// 大地图左侧常驻「打开控制面板」按钮的 MapView。MapScreen.Instance 在会话早期为 null，
    /// 故由 CampaignEvents.TickEvent 懒初始化（镜像 IG UIManager.TryInitializeImprovedGarrisonsUI）。
    /// </summary>
    private SovereignTowns.Ui.ControlPanel.ControlPanelMapButtonView? _mapButtonView;

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
            // 首府后勤评估迁到 HourlyTickEvent + 无状态间隔门控(CapitalLogisticsTickHours,默认 6h)。
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, OnHourlyTickParty);
            CampaignEvents.HourlyTickSettlementEvent.AddNonSerializedListener(this, OnHourlyTickSettlement);
            // 2026-05-12 审查 B1 修复：HourlyTickSettlement 在城内停留时会跳 tick，
            // 加 DailyTickSettlement 兜底，确保 sortie 评估最坏延迟 ≤ 24h（实际多数 ≤ 1h）。
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            // MVP 5：战斗结束 → SallyForth 战后回程（精确触发，vs GDS 的 TickEvent 轮询 bug）
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            // P0-3：宣战事件 — 玩家氏族征兵队目标村庄成为敌对时立即撤退
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            // P0-4：英雄换氏族事件 — 玩家换氏族时迁移所有在途队伍
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);
            // Task 5：高频 tick — 懒初始化大地图控制面板按钮（MapScreen.Instance 会话早期为 null）。
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnCampaignTick);

            // B17.4 B1 / R3：config 变更 → 重规划 in-flight recruiter（让 TownGarrisonRule 更改即时生效）。
            // 跨存档去重：用静态字段记住"上一轮注册的实例 delegate"，新一轮 RegisterEvents 时
            // 先 -= 旧的，再 += 自己。这样 static event 上始终只有 1 个订阅者，旧 behavior 也能被 GC。
            if (_registeredConfigChangedHandler != null)
            {
                ConfigurationManager.OnConfigChanged -= _registeredConfigChangedHandler;
            }
            _registeredConfigChangedHandler = OnConfigChangedHandler;
            ConfigurationManager.OnConfigChanged += _registeredConfigChangedHandler;
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

        // 2026-05-23 Plan B：金库余额不再独立持久化 —— 首府金库 ≡ vanilla Clan.Gold，
        // 由 vanilla Saveable 系统自动保存 / 恢复。原 st_treasuries_json 块已删除。
    }

    private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
    {
        try
        {
            // 反注册路径：把当前 behavior 实例钉到静态字段，SubModule.OnSubModuleUnloaded 才能找到它调 Uninstall。
            // 跨存档（新 behavior 实例）这里直接覆盖；旧 behavior 在它自己的 Uninstall 内会清 listeners。
            _instance = this;

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

            // B16.0：PartyMergeService 改为 singleton — 所有调用方通过 Instance 访问。必须在
            // _lifecycle 构造之后、任何 Manager 构造（它们的字段初始化器会读 Instance）之前。
            SovereignTowns.Lifecycle.PartyMergeService.Initialize(_lifecycle);

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

            _transferDispatcher = new TransferDispatcher(_lifecycle);
            // T2: BattleLootManager 已删除（doc §20 #2）；所有 ST 队伍走基类自资金路径（§14 队伍资金）
            // B7.27：sally 先构造（component 通过 SovereignTownsCampaignBehavior.SallyDispatcher 静态 accessor 拿到 dispatcher）
            _sallyDispatcher = new SallyDispatcher(_lifecycle, _capitalRegistry);
            _staticSallyDispatcher = _sallyDispatcher;
            // B16.4: PatrolDispatcher 仅创建端；状态机 + 支援判定均在 StPatrolPartyComponent，所以不再注入 sally / battleLoot
            _patrolDispatcher = new PatrolDispatcher(_lifecycle, _capitalRegistry);

            // 2026-05-12 审查 B-WarPartyComponent.OnFinalize 修复：sally party 战场被歼灭时
            // 由 vanilla 直接 destroy → roster 残留兵员丢失；订阅 MobilePartyDestroyed 抢救存活兵回 home。
            // 注意：PartyLifecycleManager 内部已订阅同事件做 untrack（不冲突，事件支持多订阅）。
            try
            {
                // Lambda 自带 try-catch 兜底，避免 vanilla MulticastDelegate 链因本订阅抛而中断后续订阅者
                // （多个 mod / 内部 PartyLifecycleManager 都订阅同一事件）。
                // B16.2: 统一 StPartyComponent 路由 — 任意 StPartyComponent 子类（sally/transfer/...）
                // 销毁时单点分派到 component.OnDestroyed；component 自带 try-catch + 业务（救援残兵 / 通知 dispatcher）。
                CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, (party, destroyer) =>
                {
                    try
                    {
                        if (party?.PartyComponent is SovereignTowns.Parties.StPartyComponent stc)
                            stc.OnDestroyed(party, destroyer);
                    }
                    catch (Exception lambdaEx) { Logger.Error("MobilePartyDestroyed component-dispatch lambda threw", lambdaEx); }
                });
            }
            catch (Exception ex) { Logger.Error("Failed to subscribe MobilePartyDestroyed for StPartyComponent dispatch", ex); }

            // DiagnosticGameMenu 仍只关心 player capital（UI 玩家视角）— 传 registry，菜单内部走 GetForPlayer。
            DiagnosticGameMenu.Register(campaignGameStarter, _capitalRegistry);
            // B7.22：自家 party encounter 拦截 — 玩家碰到征兵队 / 调拨队等会进入对话而非战斗界面
            STPartyDialogRegistration.Register(campaignGameStarter);

            // B7: ribbon retired. Player config is now web-only via DiagnosticGameMenu's
            // "打开网页控制面板" town menu option + WebConfigServer.

            _recruitmentDispatcher = new RecruitmentDispatcher(_lifecycle, _capitalRegistry);
            _prisonerRecruitmentManager = new PrisonerRecruitmentManager(_capitalRegistry);
            _capitalLogisticsManager = new CapitalLogisticsManager(
                _capitalRegistry,
                _recruitmentDispatcher,
                _transferDispatcher,
                _patrolDispatcher);

            // B7.14：抑制 vanilla 在我们接管的城镇/城堡上的 GarrisonAutoRecruitment。
            // 时序：必须在 RecruitmentDispatcher 构造之后；否则 vanilla 在 Settlement.All 初次扫描前 hook 上来可能错过。
            _vanillaSuppression = new SovereignTowns.SettlementManagement.VanillaSuppressionManager();
            _vanillaSuppression.Initialize();

            // 禁用受管氏族定居点的 vanilla 巡逻队（哨所自带巡逻）。
            _vanillaPatrolSuppressor = new SovereignTowns.SettlementManagement.VanillaPatrolSuppressor();
            _vanillaPatrolSuppressor.Initialize();

            Logger.Info($"OnSessionLaunched: 全部 Manager 就绪 (含 Capital + SallyDispatcher + VanillaSuppression) ConfigVersion={ConfigurationManager.Current.ConfigVersion}");
            Logger.Info($"  features: AutoRecruitment={ConfigurationManager.Current.EnabledFeatures.AutoRecruitment} " +
                        $"AutoPatrol={ConfigurationManager.Current.EnabledFeatures.AutoPatrol} " +
                        $"TroopTransfers={ConfigurationManager.Current.EnabledFeatures.TroopTransfers}");

            if (!ConfigurationManager.Current.EnabledFeatures.AutoRecruitment)
                Logger.Info("  HINT: AutoRecruitment 已禁用。global.json 改为 true 启用");
            if (!ConfigurationManager.Current.EnabledFeatures.TroopTransfers)
                Logger.Info("  HINT: TroopTransfers 已禁用。global.json 改为 true 启用");
            if (!ConfigurationManager.Current.EnabledFeatures.AutoPatrol)
                Logger.Info("  HINT: AutoPatrol 已禁用。global.json 改为 true 启用");

            // P0-6：在玩家首次能打开网页面板之前先 seed 一次 settlements snapshot,避免
            // daily tick 来之前 UI 调 /api/settlements 拿到空 list。Refresh 自带 try/catch。
            SovereignTowns.WebConfig.SettlementsSnapshot.Refresh();

            // B7.5: 提示玩家打开网页面板 — 但 URL 含 session token，不能写日志 / 聊天面板（玩家分享
            // ModLogs 截图就会泄漏，攻击者可在 session 期间写任意配置）。改为只提示从城镇菜单进入。
            try
            {
                if (SovereignTowns.WebConfig.WebConfigServer.IsRunning)
                {
                    int port = SovereignTowns.WebConfig.WebConfigServer.Port;
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=ST_Msg_WebConfig_Started}[Sovereign Towns] Web control panel started. Open any town/castle menu and choose 'Open web control panel'.").ToString(),
                        Colors.Green));
                    Logger.Info($"WebConfigServer listening on 127.0.0.1:{port} (token withheld from logs/UI)");
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=ST_Msg_WebConfig_StartFailed}[Sovereign Towns] Web control panel did not start (port conflict or sandbox refusal). See the logs.").ToString(),
                        Colors.Yellow));
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

    /// <summary>
    /// 高频 campaign tick（vanilla <c>CampaignEvents.TickEvent</c>，签名 <c>Action&lt;float&gt;</c>）。
    /// 仅用于懒初始化大地图控制面板按钮：MapScreen.Instance 在会话早期为 null，需等其就绪后再建 MapView。
    /// 幂等 —— _mapButtonView 非空后不再重建（镜像 IG UIManager.TryInitializeImprovedGarrisonsUI）。
    /// </summary>
    private void OnCampaignTick(float dt)
    {
        try
        {
            if (_mapButtonView == null && SandBox.View.Map.MapScreen.Instance != null)
            {
                _mapButtonView = new SovereignTowns.Ui.ControlPanel.ControlPanelMapButtonView();
                Logger.Info("ControlPanel: map button view created");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnCampaignTick map-button bootstrap failed", ex);
        }
    }

    private void OnDailyTick()
    {
        try
        {
            DrainWebConfigSync();

            // B17.4 A2：每日活动汇总(IG GarrisonDailyBehavior.cs:50-66 借鉴)
            // 顺序固定:read snapshot → display → reset(IG 5 年沉淀,顺序错就重复弹窗 / 漏弹)
            if (ConfigurationManager.Current?.EnabledFeatures?.ShowDailySummary == true)
            {
                try
                {
                    var snap = SovereignTowns.Audit.DailyActivityCounters.Snapshot();
                    int total = snap.recruited + snap.transferred + snap.patrols + snap.sallies + snap.prisoners;
                    if (total > 0)
                    {
                        var template = new TextObject(
                            "{=ST_Msg_DailySummary}[Sovereign Towns] Today: recruited {RECRUITED}, transferred {TRANSFERRED}, patrols {PATROLS}, sallies {SALLIES}, prisoner-recruits {PRISONERS}.");
                        template.SetTextVariable("RECRUITED", snap.recruited);
                        template.SetTextVariable("TRANSFERRED", snap.transferred);
                        template.SetTextVariable("PATROLS", snap.patrols);
                        template.SetTextVariable("SALLIES", snap.sallies);
                        template.SetTextVariable("PRISONERS", snap.prisoners);
                        InformationManager.DisplayMessage(new InformationMessage(template.ToString(), Colors.Green));
                    }
                }
                catch (Exception sumEx) { Logger.Warn($"daily summary display failed: {sumEx.Message}"); }
                finally
                {
                    // ★ 严格在 display 之后(包括异常路径)清零,避免次日重复计数
                    SovereignTowns.Audit.DailyActivityCounters.ResetAll();
                }
            }
            else
            {
                // feature off — 仍然清零,免得后续打开时一次性涌出累计值
                SovereignTowns.Audit.DailyActivityCounters.ResetAll();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DailyTick failed", ex);
        }
    }

    /// <summary>
    /// 首府后勤评估入口。从 vanilla HourlyTickEvent(Action,无参)进入,用无状态间隔门控:
    /// CampaignTime 总小时数能被 CapitalLogisticsTickHours 整除时才评估。无状态 →
    /// 存档 / 读档相位自动对齐,无需持久化计数器。
    /// </summary>
    private void OnHourlyTick()
    {
        try
        {
            int interval = ConfigurationManager.Current?.FiscalAutonomy?.CapitalLogisticsTickHours ?? 6;
            if (interval < 1) interval = 1;
            if (interval > 24) interval = 24;
            if ((long)CampaignTime.Now.ToHours % interval != 0) return;
            DrainWebConfigSync();
            _capitalLogisticsManager?.EvaluateAll();
        }
        catch (Exception ex)
        {
            Logger.Error("OnHourlyTick failed", ex);
        }
    }

    private void OnHourlyTickParty(MobileParty party)
    {
        try
        {
            DrainWebConfigSync();
            // B16.4: 所有 4 种 StPartyComponent 子类（patrol / transfer / sally / recruiter）
            // 由 PartyLifecycleManager.OnHourlyTickParty 单点路由到 StPartyComponent.OnHourlyTick。
            // 本方法体保留 DrainWebConfigSync — 网页配置编辑需要 game thread 同步。
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
            // 巡逻队不再每小时启发式创建 —— 由 CapitalLogisticsManager 经时间展开调度器派发。
            _sallyDispatcher?.OnHourlyTickSettlement(settlement);
        }
        catch (Exception ex)
        {
            Logger.Error("OnHourlyTickSettlement failed", ex);
        }
    }

    /// <summary>
    /// 兜底：vanilla HourlyTickSettlement 在城内停留时可能跳 tick（已知现象），
    /// daily 用同一评估逻辑重新检查一次。SallyDispatcher 内部已有上限/冷却保护，重复触发无副作用。
    /// </summary>
    private void OnDailyTickSettlement(Settlement settlement)
    {
        try
        {
            DrainWebConfigSync();
            _sallyDispatcher?.OnHourlyTickSettlement(settlement);
            // 用户明确：XP 注入 + 俘虏招募仅在首府进行；招兵/调拨/巡逻由 CapitalLogisticsManager 在 DailyTick 统一调度。
            // B7.15 multi-clan：以"该 settlement 的 ownerClan 是否把它当首府"为准 — 玩家或 AI 都按各自首府走。
            var mgr = _capitalRegistry?.GetForSettlement(settlement);
            var capitalSettlement = _capitalRegistry?.GetCapitalForClan(mgr?.OwnerClan);
            // B7.20：诊断日志 — 放在 Debug，避免长期存档每个日 tick 写入高频噪声。
            if (settlement != null && (settlement.IsTown || settlement.IsCastle) && settlement.OwnerClan == Clan.PlayerClan)
            {
                Logger.Debug($"OnDailyTickSettlement '{settlement.Name}' (ownerClan={settlement.OwnerClan?.StringId}): " +
                             $"registry hasMgr={mgr != null} capital={capitalSettlement?.Name?.ToString() ?? "<none>"} matches={(capitalSettlement == settlement)}");
            }
            if (capitalSettlement != null && settlement == capitalSettlement)
            {
                Upgrades.GarrisonXpInjector.GiveDailyXpToGarrison(settlement);
                _prisonerRecruitmentManager?.OnDailyTickSettlement(settlement);
            }

            // P0-6 修复：刷新 SettlementsSnapshot,供 HTTP /api/settlements 端点读取(HTTP 线程
            // 不能直接 touch vanilla 对象)。在 settlement-tick 末尾刷新,频率 ~ 1× 每城每日,
            // 对一个玩家氏族 < 20 城来说总开销可忽略;UI 5 秒轮询不要求实时,daily 粒度足够。
            SettlementsSnapshot.Refresh();
        }
        catch (Exception ex)
        {
            Logger.Error("OnDailyTickSettlement failed", ex);
        }
    }

    /// <summary>
    /// 战斗结束回调（vanilla <c>CampaignEvents.MapEventEnded</c>，签名 <c>Action&lt;MapEvent&gt;</c>）。
    /// T2 之后：所有 4 种 StPartyComponent 子类（patrol / transfer / sally / recruiter）由 lifecycle 单点路由
    /// （StPartyComponent.OnMapEventEnded）。战利品集中处理（旧 BattleLootManager）已删除，物品由基类 TryEconomicMaintenance
    /// 在下次到达 settlement.Town 时统一卖入 _teamFunds。
    /// try-catch 包裹避免影响 vanilla 事件链。
    /// </summary>
    private void OnMapEventEnded(MapEvent mapEvent)
    {
        try
        {
            _lifecycle?.OnMapEventEnded(mapEvent);   // B16.1-B16.4：单点路由到 StPartyComponent
        }
        catch (Exception ex)
        {
            Logger.Error("OnMapEventEnded forwarding failed", ex);
        }
    }

    /// <summary>
    /// P0-3：宣战回调。仅处理玩家氏族的征兵队：若 _assignedTarget 村庄所属势力已与玩家氏族敌对，
    /// 立即将该征兵队的阶段设为 Returning 并导航回 home，避免征兵队被敌军拦截。
    /// </summary>
    private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
    {
        try
        {
            var playerFaction = Clan.PlayerClan?.MapFaction;
            if (playerFaction == null) return;

            // 只有当玩家阵营是宣战的当事方之一才需处理
            if (faction1 != playerFaction && faction2 != playerFaction) return;

            foreach (var party in MobileParty.AllCustomParties)
            {
                try
                {
                    if (party?.PartyComponent is not SovereignTowns.Parties.StRecruiterPartyComponent recruiter) continue;
                    if (recruiter.HomeSettlementOrNull?.OwnerClan != Clan.PlayerClan) continue;

                    var target = recruiter.AssignedTarget;
                    if (target == null) continue;

                    // 目标村庄所属势力现在是否与玩家敌对
                    if (target.MapFaction?.IsAtWarWith(playerFaction) != true) continue;

                    var home = recruiter.HomeSettlementOrNull;
                    if (home == null) continue;

                    Logger.Info($"OnWarDeclared: recruiter '{PartyNameFormatter.SafeName(party)}' target '{target.Name}' is now hostile, retreating to '{home.Name}'");
                    recruiter.SetAssignedTarget(home);
                    recruiter.TransitionTo(SovereignTowns.Parties.StRecruiterPartyComponent.RecruiterPhase.Returning);
                    // 2026-05-18 修复 v2：用 GoToWithLeave，因 DoNotMakeNewDecisions(true) 下需显式 LeaveSettlement。
                    SovereignTowns.Common.SafeMoveHelper.GoToWithLeave(party, home, "OnWarDeclared retreat recruiter");
                }
                catch (Exception partyEx)
                {
                    Logger.Error("OnWarDeclared inner party loop threw", partyEx);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnWarDeclared failed", ex);
        }
    }

    /// <summary>
    /// P0-4：英雄换氏族回调。当玩家（MainHero）换氏族时，解散旧氏族所有在途 ST 队伍，
    /// 并为新氏族引导 EnsureForClan（通过 CapitalRegistry.HandlePlayerClanSwap）。
    /// </summary>
    private void OnHeroChangedClan(Hero hero, Clan oldClan)
    {
        try
        {
            if (hero != Hero.MainHero) return;
            var newClan = hero.Clan;
            Logger.Info($"OnHeroChangedClan: player switched from '{oldClan?.StringId}' to '{newClan?.StringId}'");
            _capitalRegistry?.HandlePlayerClanSwap(oldClan, newClan);
        }
        catch (Exception ex)
        {
            Logger.Error("OnHeroChangedClan failed", ex);
        }
    }

    /// <summary>
    /// B17.4 B1：ConfigurationManager.ReplaceAndSave 后被回调。
    /// 把所有 in-flight StRecruiterPartyComponent 切回 Dispatching 阶段，让 PlanNextHop 用新规则重选目标。
    /// settlementStringId == null → 影响所有 home town。否则仅影响 home 匹配的队伍。
    /// 不限于 PlayerClan — 任意 managed clan 的 recruiter 都按其各自首府的规则刷新。
    /// </summary>
    private void OnConfigChangedHandler(string? settlementStringId)
    {
        try
        {
            // P1-B4 修复：迭代前先做快照，避免 foreach 过程中其它线程修改集合导致枚举器失效。
            var parties = MobileParty.AllCustomParties.ToList();
            foreach (var party in parties)
            {
                try
                {
                    if (party?.PartyComponent is not SovereignTowns.Parties.StRecruiterPartyComponent recruiter) continue;
                    var home = recruiter.HomeSettlementOrNull;
                    if (home == null) continue;
                    if (settlementStringId != null && home.StringId != settlementStringId) continue;

                    recruiter.SetAssignedTarget(null);
                    recruiter.TransitionTo(SovereignTowns.Parties.StRecruiterPartyComponent.RecruiterPhase.Dispatching);
                    Logger.Info($"OnConfigChanged: '{PartyNameFormatter.SafeName(party)}' transitioned to Dispatching for re-planning under new rule");
                }
                catch (Exception innerEx) { Logger.Warn($"OnConfigChanged per-party refresh failed: {innerEx.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnConfigChangedHandler failed", ex);
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

    /// <summary>
    /// 反注册路径（由 SovereignTownsSubModule.OnSubModuleUnloaded 调用）。
    /// CampaignBehaviorBase 没有 OnRemovedBehavior virtual（反射确认），故走 SubModule 进程级 hook。
    ///
    /// MbEvent 没有 RemoveListener API；ClearListeners(owner) 删除 owner==this 的所有订阅，
    /// 包括 OnSessionLaunched 里那段 lambda 订阅 MobilePartyDestroyed（其 owner 也是 this）。
    /// 故无需把 lambda 改成命名字段。
    ///
    /// 顺序：先反注册自身订阅 → 再链式 Uninstall 内部 manager（manager 内部各自再反注册 + 清 Instance）。
    /// 多次调用安全：每个 manager.Uninstall 都自带幂等门控。
    /// 全部 try-catch 包裹，确保 Module 卸载流程不被任一异常中断。
    /// </summary>
    public void Uninstall()
    {
        try
        {
            if (_instance != this)
            {
                // 早已被新 behavior 替换或已清空 — 跳过避免误清 listeners。
                return;
            }

            // ── 1) 本 behavior 自身 RegisterEvents 内 + OnSessionLaunched 内的所有 this-owned 订阅
            //    用 ClearListeners(this) 一次性删除（MbEvent 没有 RemoveListener，按 owner 反注册）。
            try { CampaignEvents.OnSessionLaunchedEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall OnSessionLaunchedEvent.ClearListeners failed", ex); }
            try { CampaignEvents.OnGameLoadedEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall OnGameLoadedEvent.ClearListeners failed", ex); }
            try { CampaignEvents.DailyTickEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall DailyTickEvent.ClearListeners failed", ex); }
            try { CampaignEvents.HourlyTickEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall HourlyTickEvent.ClearListeners failed", ex); }
            try { CampaignEvents.HourlyTickPartyEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall HourlyTickPartyEvent.ClearListeners failed", ex); }
            try { CampaignEvents.HourlyTickSettlementEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall HourlyTickSettlementEvent.ClearListeners failed", ex); }
            try { CampaignEvents.DailyTickSettlementEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall DailyTickSettlementEvent.ClearListeners failed", ex); }
            try { CampaignEvents.OnSettlementOwnerChangedEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall OnSettlementOwnerChangedEvent.ClearListeners failed", ex); }
            try { CampaignEvents.MapEventEnded.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall MapEventEnded.ClearListeners failed", ex); }
            try { CampaignEvents.WarDeclared.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall WarDeclared.ClearListeners failed", ex); }
            try { CampaignEvents.OnHeroChangedClanEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall OnHeroChangedClanEvent.ClearListeners failed", ex); }
            try { CampaignEvents.TickEvent.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall TickEvent.ClearListeners failed", ex); }
            // MobilePartyDestroyed：覆盖 OnSessionLaunched 内的 lambda 订阅（owner=this）。
            try { CampaignEvents.MobilePartyDestroyed.ClearListeners(this); } catch (Exception ex) { Logger.Warn("Uninstall MobilePartyDestroyed.ClearListeners failed", ex); }

            // ConfigurationManager.OnConfigChanged 是 plain static C# event，不是 MbEvent；保留现有 -= 路径，
            // 它自带跨存档去重逻辑（_registeredConfigChangedHandler）。
            try
            {
                if (_registeredConfigChangedHandler != null)
                {
                    ConfigurationManager.OnConfigChanged -= _registeredConfigChangedHandler;
                    _registeredConfigChangedHandler = null;
                }
            }
            catch (Exception ex) { Logger.Warn("Uninstall OnConfigChanged unsubscribe failed", ex); }

            // ── 2) 链式反注册内部 manager（顺序：依赖端先 uninstall，registry 最后）
            try { _vanillaPatrolSuppressor?.Uninstall(); } catch (Exception ex) { Logger.Warn("Uninstall VanillaPatrolSuppressor failed", ex); }
            try { _vanillaSuppression?.Uninstall(); } catch (Exception ex) { Logger.Warn("Uninstall VanillaSuppressionManager failed", ex); }
            try { _capitalRegistry?.Uninstall(); } catch (Exception ex) { Logger.Warn("Uninstall CapitalRegistry failed", ex); }
            try { _lifecycle?.Uninstall(); } catch (Exception ex) { Logger.Warn("Uninstall PartyLifecycleManager failed", ex); }

            _staticSallyDispatcher = null;
            _instance = null;
            Logger.Info("SovereignTownsCampaignBehavior: uninstalled (event listeners cleared + managers unhooked)");
        }
        catch (Exception ex)
        {
            Logger.Error("SovereignTownsCampaignBehavior.Uninstall failed", ex);
        }
    }

}
