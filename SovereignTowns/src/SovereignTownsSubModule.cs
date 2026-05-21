using SovereignTowns.Campaign;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using HarmonyLib;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns;

/// <summary>
/// Mod 入口。所有覆盖方法都用最外层 try-catch 包，绝不让我们的异常逃逸到 vanilla。
/// 2026-05-13 启动崩溃应急修复 — 早期阶段任何异常都吞掉，并用 Debug.Print 落地（Logger 可能还没初始化）。
/// </summary>
public sealed class SovereignTownsSubModule : MBSubModuleBase
{
    public const string ModuleId = "SovereignTowns";
    private const string Tag = "[SovereignTowns]";

    private bool _skipBehaviorRegistration;
    private bool _loggerInitialized;

    private static readonly string[] IncompatibleModuleIds =
    {
        "ImprovedGarrisons",
        "GarrisonDoSomething"
    };

    protected override void OnSubModuleLoad()
    {
        try { base.OnSubModuleLoad(); }
        catch (System.Exception ex) { TrySafeDebugPrint($"{Tag} base.OnSubModuleLoad threw: {ex}"); }

        try { TrySafeDebugPrint($"{Tag} OnSubModuleLoad enter"); } catch { }

        try
        {
            Logger.Initialize(ModuleId);
            _loggerInitialized = true;
            Logger.Info($"OnSubModuleLoad — SovereignTowns v0.0.1 booting");
        }
        catch (System.Exception ex)
        {
            TrySafeDebugPrint($"{Tag} Logger.Initialize threw: {ex.Message}");
        }

        try
        {
            foreach (var modId in IncompatibleModuleIds)
            {
                try
                {
                    if (ModuleHelper.IsModuleActive(modId))
                    {
                        _skipBehaviorRegistration = true;
                        if (_loggerInitialized) Logger.Error($"CRITICAL: 互斥模块 '{modId}' 已激活。SovereignTowns 进入退化模式（不注册任何 CampaignBehavior）。");
                        TrySafeDebugPrint($"{Tag} CRITICAL: 互斥模块 '{modId}' 已激活。退化模式。");
                    }
                }
                catch (System.Exception ex)
                {
                    TrySafeDebugPrint($"{Tag} IsModuleActive('{modId}') threw: {ex.Message}");
                }
            }

            if (!_skipBehaviorRegistration)
            {
                if (_loggerInitialized)
                    Logger.Info($"互斥检测通过：未发现冲突模块 ({string.Join(", ", IncompatibleModuleIds)})");

                try
                {
                    new Harmony("sovereigntowns.patches").PatchAll();
                    if (_loggerInitialized) Logger.Info("Harmony patches applied");
                }
                catch (System.Exception hex)
                {
                    if (_loggerInitialized) Logger.Error("Harmony PatchAll failed — patrol suppression disabled", hex);
                    TrySafeDebugPrint($"{Tag} Harmony PatchAll threw: {hex.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            TrySafeDebugPrint($"{Tag} OnSubModuleLoad outer body threw: {ex.Message}");
        }
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        try { base.OnBeforeInitialModuleScreenSetAsRoot(); }
        catch (System.Exception ex) { TrySafeDebugPrint($"{Tag} base.OnBeforeInitialModuleScreenSetAsRoot threw: {ex}"); }

        try
        {
            if (_skipBehaviorRegistration)
            {
                var template = new TextObject(
                    "{=ST_Msg_IncompatibleMods}Sovereign Towns: incompatible modules detected ({MODS}). The mod is inactive. Disable the conflicting modules in the launcher and restart.");
                template.SetTextVariable("MODS", string.Join(", ", IncompatibleModuleIds));
                var msg = template.ToString();
                try { InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Red)); } catch { }
                if (_loggerInitialized) Logger.Warn("Displayed incompatibility warning to user");
                // 不再 throw — 应急修复期间，宁可让 mod 在退化模式下静默，也不让游戏崩。
                // 玩家可以在主菜单看到红字警告。
            }
        }
        catch (System.Exception ex)
        {
            TrySafeDebugPrint($"{Tag} OnBeforeInitialModuleScreenSetAsRoot body threw: {ex.Message}");
        }
    }

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        // 会话级 evaluator 缓存清空：跨局后旧 CharacterObject 引用必须丢弃。
        // 必须放在所有 AddModel/AddBehavior 之前，且不能挪动后续注册顺序（CLAUDE.md 不变量 #4）。
        try { SovereignTowns.Evaluators.EvaluatorCache.Reset(); }
        catch (System.Exception ex) { TrySafeDebugPrint($"{Tag} EvaluatorCache.Reset threw: {ex.Message}"); }

        try { base.OnGameStart(game, gameStarterObject); }
        catch (System.Exception ex) { TrySafeDebugPrint($"{Tag} base.OnGameStart threw: {ex}"); }

        try
        {
            if (_loggerInitialized) Logger.Info($"OnGameStart — game.GameType={game?.GameType?.GetType().Name ?? "<null>"}");

            if (_skipBehaviorRegistration)
            {
                if (_loggerInitialized) Logger.Warn("Skipping CampaignBehavior registration due to incompatibility flag");
                return;
            }

            if (gameStarterObject is not CampaignGameStarter campaignStarter)
            {
                if (_loggerInitialized) Logger.Info("Non-Campaign mode (e.g., CustomBattle). Skipping CampaignBehavior registration.");
                return;
            }

            // 注册自定义 GameModel — 必须在 OnGameStart 内（CLAUDE.md 不变量 #4）。
            try
            {
                campaignStarter.AddModel(new SovereignTowns.Models.STPartySizeLimitModel());
                campaignStarter.AddModel(new SovereignTowns.Models.STPartySpeedModel());
                campaignStarter.AddModel(new SovereignTowns.Models.STPartyWageModel());
                campaignStarter.AddModel(new SovereignTowns.Models.STVolunteerModel());
                campaignStarter.AddModel(new SovereignTowns.Models.STClanFinanceModel());
                if (_loggerInitialized) Logger.Info("Registered 5 ST GameModels (PartySize/Speed/Wage/Volunteer/ClanFinance)");
            }
            catch (System.Exception ex)
            {
                if (_loggerInitialized) Logger.Error("AddModel failed in OnGameStart", ex);
                TrySafeDebugPrint($"{Tag} AddModel threw: {ex.Message}");
            }

            try
            {
                campaignStarter.AddBehavior(new SovereignTownsCampaignBehavior());
                if (_loggerInitialized) Logger.Info("SovereignTownsCampaignBehavior registered");
            }
            catch (System.Exception ex)
            {
                if (_loggerInitialized) Logger.Error("Behavior registration failed", ex);
                TrySafeDebugPrint($"{Tag} AddBehavior threw: {ex.Message}");
            }

            // B7.1: dump troops.json for the web frontend's ExactTroop picker.
            // NOTE: actual dump call moved to SovereignTownsCampaignBehavior.OnSessionLaunched —
            // at OnGameStart the CharacterObject pool (spnpccharacters.xml etc.) is not yet
            // registered with MBObjectManager, so dumping here yields count=0. See troops.json
            // empty-payload bug 2026-05-14.

            // B7.3: start local HTTP server so the player can edit config in a browser.
            // Idempotent — already-running call is a no-op.
            try { SovereignTowns.WebConfig.WebConfigServer.Start(); }
            catch (System.Exception ex)
            {
                if (_loggerInitialized) Logger.Error("WebConfigServer.Start failed (swallowed)", ex);
            }
        }
        catch (System.Exception ex)
        {
            TrySafeDebugPrint($"{Tag} OnGameStart body threw: {ex.Message}");
        }
    }

    protected override void OnSubModuleUnloaded()
    {
        try { base.OnSubModuleUnloaded(); }
        catch (System.Exception ex) { TrySafeDebugPrint($"{Tag} base.OnSubModuleUnloaded threw: {ex.Message}"); }

        try
        {
            if (_loggerInitialized) Logger.Info("OnSubModuleUnloaded");
        }
        catch { }

        try { SovereignTowns.WebConfig.WebConfigServer.Stop(); }
        catch (System.Exception ex) { TrySafeDebugPrint($"{Tag} WebConfigServer.Stop threw: {ex.Message}"); }

        try { SovereignTowns.Audit.DecisionAuditLogger.Shutdown(); }
        catch (System.Exception ex) { TrySafeDebugPrint($"{Tag} AuditLogger.Shutdown threw: {ex.Message}"); }

        try { if (_loggerInitialized) Logger.Shutdown(); }
        catch { }

        try { TrySafeDebugPrint($"{Tag} OnSubModuleUnloaded done"); } catch { }
    }

    /// <summary>
    /// 不依赖 Logger 的紧急打印。即使 vanilla Debug 类失效也不抛。
    /// </summary>
    private static void TrySafeDebugPrint(string s)
    {
        try { Debug.Print(s); }
        catch { /* 无能为力 */ }
    }
}
