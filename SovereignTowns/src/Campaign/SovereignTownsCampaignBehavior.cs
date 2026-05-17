using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SovereignTowns.Audit;
using SovereignTowns.Battle;
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
    private BattleLootManager? _battleLootManager;
    private TransferDispatcher? _transferDispatcher;
    private PatrolDispatcher? _patrolDispatcher;
    private SallyDispatcher? _sallyDispatcher;

    // B16.2: 静态 accessor — StSallyPartyComponent.NotifyDispatcherEnded 通过它通知 cooldown 重置。
    private static SallyDispatcher? _staticSallyDispatcher;
    public static SallyDispatcher? SallyDispatcher => _staticSallyDispatcher;
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
            // P0-3：宣战事件 — 玩家氏族征兵队目标村庄成为敌对时立即撤退
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            // P0-4：英雄换氏族事件 — 玩家换氏族时迁移所有在途队伍
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);
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
            _battleLootManager = new BattleLootManager(_capitalRegistry);
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
                _transferDispatcher);

            // B7.14：抑制 vanilla 在我们接管的城镇/城堡上的 GarrisonAutoRecruitment。
            // 时序：必须在 RecruitmentDispatcher 构造之后；否则 vanilla 在 Settlement.All 初次扫描前 hook 上来可能错过。
            _vanillaSuppression = new SovereignTowns.SettlementManagement.VanillaSuppressionManager();
            _vanillaSuppression.Initialize();

            Logger.Info($"OnSessionLaunched: 全部 Manager 就绪 (含 Capital + SallyDispatcher + VanillaSuppression) ConfigVersion={ConfigurationManager.Current.ConfigVersion}");
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
            _patrolDispatcher?.OnHourlyTickSettlement(settlement);
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
            // Round-1 P0-1：玩家驻自家首府时 vanilla 跳 HourlyTickSettlement → patrol 永远不新派。
            // PatrolDispatcher 内部已有 cap 检查（CountExistingPatrolsAtHome），daily 多调一次幂等。
            _patrolDispatcher?.OnHourlyTickSettlement(settlement);
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
    /// B16.4 起所有 4 种 StPartyComponent 子类（patrol / transfer / sally / recruiter）由 lifecycle 单点路由
    /// （StPartyComponent.OnMapEventEnded）。仅保留 _battleLootManager.OnMapEventEnded（战利品集中处理）。
    /// try-catch 包裹避免影响 vanilla 事件链。
    /// </summary>
    private void OnMapEventEnded(MapEvent mapEvent)
    {
        try
        {
            _battleLootManager?.OnMapEventEnded(mapEvent);
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
                    if (!target.MapFaction.IsAtWarWith(playerFaction)) continue;

                    var home = recruiter.HomeSettlementOrNull;
                    if (home == null) continue;

                    Logger.Info($"OnWarDeclared: recruiter '{PartyNameFormatter.SafeName(party)}' target '{target.Name}' is now hostile, retreating to '{home.Name}'");
                    recruiter.SetAssignedTarget(home);
                    recruiter.TransitionTo(SovereignTowns.Parties.StRecruiterPartyComponent.RecruiterPhase.Returning);
                    try { party.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false); }
                    catch (Exception navEx) { Logger.Error($"OnWarDeclared SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(party)}'", navEx); }
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
