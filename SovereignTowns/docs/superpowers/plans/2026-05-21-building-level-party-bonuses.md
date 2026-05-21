# Building-level Party Bonuses Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make settlement building levels (军营 Barracks / 哨所 Guard House) correctly drive each mod party type's concurrent cap and garrison XP, with all coefficients adjustable in both control-panel surfaces, and disable vanilla patrol parties for mod-managed clans.

**Architecture:** A new `BuildingLevelReader` infrastructure helper centralises the correct vanilla `BuildingType.StringId`s (fixing a silent ID-mismatch bug). A new `BuildingBonusConfig` holds per-level coefficients consumed by `PartyLifecycleManager.GetMaxFor`, `GarrisonXpInjector` and `PatrolDispatcher`. A Harmony prefix on `PatrolPartiesCampaignBehavior.CanSettlementSpawnNewPartyCurrently` plus a `VanillaPatrolSuppressor` class suppress and dissolve vanilla patrols for managed clans.

**Tech Stack:** C# net472, Mount & Blade II Bannerlord v1.3.15 modding API, Harmony 2.4.x (`HarmonyLib`), Newtonsoft.Json, Gauntlet UI, Alpine.js (WebUI).

**Verification model:** This project has NO unit-test framework (per `CLAUDE.md`). Every code task ends with `dotnet build` (must report 0 errors) as the automated gate. Runtime behaviour is verified once at the end by launching the game and reading logs — see Task 8.

**Reference spec:** `SovereignTowns/docs/superpowers/specs/2026-05-21-building-level-party-bonuses-design.md`

**Build command (run from workspace root `C:\Users\rangt\Desktop\workspace`):**
```
dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug
```
Expected on success: `Build succeeded` with `0 Error(s)` (pre-existing nullable warnings are fine).

---

## Task 1: `BuildingLevelReader` infrastructure helper

**Files:**
- Create: `SovereignTowns/src/Common/BuildingLevelReader.cs`

- [ ] **Step 1: Create `BuildingLevelReader.cs`**

```csharp
using System;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common;

/// <summary>本 Mod 关心的 vanilla 建筑。Barracks=军营,GuardHouse=哨所。</summary>
public enum StBuilding
{
    Barracks,
    GuardHouse,
}

/// <summary>
/// 全 Mod 唯一的 vanilla 建筑等级读取入口。集中持有正确的 BuildingType.StringId
/// (反编译 DefaultBuildingTypes 核实,v1.3.15):
///   军营   Town  = building_settlement_barracks    Castle = building_castle_barracks
///   哨所   Town  = building_settlement_guard_house  Castle = building_castle_guard_house
/// 旧代码误用 settlement_garrison / castle_barracks(无 building_ 前缀且名字错),
/// 导致建筑等级恒为 0。本类取代那些重复查找。
/// </summary>
public static class BuildingLevelReader
{
    /// <summary>返回建筑当前等级,钳制 [0,3]。town==null / 未建造 / 任何异常 → 0。绝不抛。</summary>
    public static int GetLevel(Settlement? settlement, StBuilding building)
    {
        try
        {
            var town = settlement?.Town;
            if (town?.Buildings == null) return 0;
            bool isCastle = settlement!.IsCastle;
            string targetId = building switch
            {
                StBuilding.Barracks   => isCastle ? "building_castle_barracks"    : "building_settlement_barracks",
                StBuilding.GuardHouse => isCastle ? "building_castle_guard_house" : "building_settlement_guard_house",
                _ => "",
            };
            if (targetId.Length == 0) return 0;

            foreach (var b in town.Buildings)
            {
                if (b?.BuildingType == null) continue;
                string id;
                try { id = b.BuildingType.StringId ?? ""; }
                catch { continue; }
                if (string.Equals(id, targetId, StringComparison.Ordinal))
                {
                    int level;
                    try { level = b.CurrentLevel; }
                    catch { level = 0; }
                    if (level < 0) level = 0;
                    if (level > 3) level = 3;
                    return level;
                }
            }
            return 0; // 该建筑尚未建造(建造槽空着)
        }
        catch (Exception ex)
        {
            Logger.Error($"BuildingLevelReader.GetLevel failed ({building})", ex);
            return 0;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/Common/BuildingLevelReader.cs
git commit -m "feat(buildings): add BuildingLevelReader with verified vanilla building IDs"
```

---

## Task 2: `BuildingBonusConfig` + config plumbing

**Files:**
- Create: `SovereignTowns/src/Configuration/BuildingBonusConfig.cs`
- Modify: `SovereignTowns/src/Configuration/GlobalConfig.cs`
- Modify: `SovereignTowns/src/Configuration/ConfigurationManager.cs`
- Modify: `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs`

- [ ] **Step 1: Create `BuildingBonusConfig.cs`**

```csharp
namespace SovereignTowns.Configuration;

/// <summary>
/// 建筑等级 → 队伍加成系数。军营(Barracks)驱动征兵 / 调拨 / 出击队并发上限与驻军每日 XP;
/// 哨所(Guard House)驱动巡逻队并发上限。
/// 并发上限公式:cap = Base + 建筑等级 × PerLevel,结果钳制 ≥ 1。
/// 默认值令 0 级建筑下 = 旧的固定行为(各类队伍上限 1、驻军 XP 5),3 级下 = 上限 4 / XP 20。
/// </summary>
public sealed class BuildingBonusConfig
{
    public int RecruiterBaseCap { get; set; } = 1;
    public int RecruiterCapPerBarracksLevel { get; set; } = 1;

    public int TransferBaseCap { get; set; } = 1;
    public int TransferCapPerBarracksLevel { get; set; } = 1;

    public int SallyBaseCap { get; set; } = 1;
    public int SallyCapPerBarracksLevel { get; set; } = 1;

    public int PatrolBaseCap { get; set; } = 1;
    public int PatrolCapPerGuardHouseLevel { get; set; } = 1;

    public int GarrisonXpBasePerDay { get; set; } = 5;
    public int GarrisonXpPerBarracksLevel { get; set; } = 5;
}
```

- [ ] **Step 2: Add `BuildingBonus` property to `GlobalConfig`**

In `SovereignTowns/src/Configuration/GlobalConfig.cs`, after the `Thresholds` property block (ends at line 55 `public PartyThresholds Thresholds { get; set; } = new PartyThresholds();`), insert:

```csharp

    /// <summary>建筑等级 → 队伍加成系数。军营 / 哨所等级派生各类队伍并发上限与驻军 XP。</summary>
    public BuildingBonusConfig BuildingBonus { get; set; } = new BuildingBonusConfig();
```

Then in `GlobalConfig.CreateDefault()`, after the line `Thresholds = new PartyThresholds(),` add:

```csharp
        BuildingBonus = new BuildingBonusConfig(),
```

- [ ] **Step 3: Bump `CurrentConfigVersion` and default the new sub-object in `ConfigurationManager`**

In `SovereignTowns/src/Configuration/ConfigurationManager.cs`:

Change line 27 from:
```csharp
    public const int CurrentConfigVersion = 19;
```
to:
```csharp
    public const int CurrentConfigVersion = 20;
```

In `TryLoadFromDisk`, after line 548 `parsed.Thresholds ??= new PartyThresholds();` add:
```csharp
            parsed.BuildingBonus ??= new BuildingBonusConfig();
```

- [ ] **Step 4: Add `ValidateBuildingBonus` and call it from `ValidateConfig`**

In `ConfigurationManager.cs`, inside `ValidateConfig`, after the `ClanRecruiter` validation block (ends line 711 `}`) and before `reason = "";` (line 713), insert:

```csharp
        if (config.BuildingBonus != null && !ValidateBuildingBonus(config.BuildingBonus, out reason))
        {
            return false;
        }
```

Then add this method immediately after `ValidateClanRecruiter` (after its closing `}` at line 743):

```csharp
    private static bool ValidateBuildingBonus(BuildingBonusConfig b, out string reason)
    {
        foreach (var (name, val, lo, hi) in new (string, int, int, int)[]
        {
            ("RecruiterBaseCap",             b.RecruiterBaseCap,             1, 10),
            ("RecruiterCapPerBarracksLevel", b.RecruiterCapPerBarracksLevel, 0, 5),
            ("TransferBaseCap",              b.TransferBaseCap,              1, 10),
            ("TransferCapPerBarracksLevel",  b.TransferCapPerBarracksLevel,  0, 5),
            ("SallyBaseCap",                 b.SallyBaseCap,                 1, 10),
            ("SallyCapPerBarracksLevel",     b.SallyCapPerBarracksLevel,     0, 5),
            ("PatrolBaseCap",                b.PatrolBaseCap,                1, 10),
            ("PatrolCapPerGuardHouseLevel",  b.PatrolCapPerGuardHouseLevel,  0, 5),
            ("GarrisonXpBasePerDay",         b.GarrisonXpBasePerDay,         0, 50),
            ("GarrisonXpPerBarracksLevel",   b.GarrisonXpPerBarracksLevel,   0, 50),
        })
        {
            if (val < lo || val > hi)
            {
                reason = $"BuildingBonus.{name} invalid ({val}); [{lo}, {hi}]";
                return false;
            }
        }
        reason = "";
        return true;
    }
```

(`ConfigurationManager` and `BuildingBonusConfig` share namespace `SovereignTowns.Configuration` — no `using` needed.)

- [ ] **Step 5: Default the new sub-object in `WebConfigEndpoints.PutConfig`**

In `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs`, after line 117 `parsed.Thresholds ??= new PartyThresholds();` add:
```csharp
            parsed.BuildingBonus ??= new BuildingBonusConfig();
```

- [ ] **Step 6: Build**

Run: `dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add SovereignTowns/src/Configuration/BuildingBonusConfig.cs SovereignTowns/src/Configuration/GlobalConfig.cs SovereignTowns/src/Configuration/ConfigurationManager.cs SovereignTowns/src/WebConfig/WebConfigEndpoints.cs
git commit -m "feat(config): add BuildingBonusConfig with validation, bump ConfigVersion to 20"
```

---

## Task 3: Wire building bonuses into the dispatchers

**Files:**
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs` (constants ~30-33, `GetMaxFor` 602-611, `ComputePatrolCapForHome` doc+method 592-646)
- Modify: `SovereignTowns/src/Upgrades/GarrisonXpInjector.cs` (usings, comment 83-85, `ComputeXpFromBarracks` 199-234)
- Modify: `SovereignTowns/src/Patrol/PatrolDispatcher.cs` (`TryFindPatrolTemplate` barracks block 280-302)

- [ ] **Step 1: Verify the old cap constants are not referenced elsewhere**

Run a grep for `MaxRecruitersPerTown`, `MaxTransfersPerTown`, `MaxSallyForthPerTown`, `ComputePatrolCapForHome` across `SovereignTowns/src`. Expected: matches ONLY inside `PartyLifecycleManager.cs`. If any other file references them, stop and report — the plan assumes they are private/internal to that file.

- [ ] **Step 2: Replace the cap constants in `PartyLifecycleManager.cs`**

Replace lines 29-33 (the `上限（按城镇 × kind）` comment block and the three `Max*PerTown` constants):
```csharp
    // ────────── 上限（按城镇 × kind） ──────────
    /// <summary>B7.27 起：KindRecruiter 不再使用此常量，改用 ComputePatrolCapForHome。常量保留作为 fallback / 历史参考。</summary>
    public const int MaxRecruitersPerTown = 1;
    public const int MaxTransfersPerTown  = 1;
    public const int MaxSallyForthPerTown = 1;
```
with:
```csharp
    // ────────── 上限（按城镇 × kind） ──────────
    // 各类队伍并发上限改为建筑等级派生，见 GetMaxFor / CapFrom。
```

- [ ] **Step 3: Rewrite `GetMaxFor` and replace `ComputePatrolCapForHome` with `CapFrom` in `PartyLifecycleManager.cs`**

Replace the `GetMaxFor` method (lines 602-611) AND the `ComputePatrolCapForHome` xml-doc + method (lines 592-646) — i.e. delete the whole region from the `// ─ 辅助 ─` helpers' `GetMaxFor` doc comment down through the end of `ComputePatrolCapForHome` — with:

```csharp
    /// <summary>
    /// 按 (home, kind) 返回并发硬上限。
    /// 征兵 / 调拨 / 出击 ← 军营(Barracks)等级;巡逻 ← 哨所(Guard House)等级。
    /// 公式:cap = Base + 建筑等级 × PerLevel,系数取自 GlobalConfig.BuildingBonus。
    /// </summary>
    private static int GetMaxFor(Settlement home, string kind)
    {
        var cfg = ConfigurationManager.Current?.BuildingBonus;
        if (kind == KindRecruiter)
            return CapFrom(home, StBuilding.Barracks,
                cfg?.RecruiterBaseCap ?? 1, cfg?.RecruiterCapPerBarracksLevel ?? 1);
        if (kind == KindTransfer)
            return CapFrom(home, StBuilding.Barracks,
                cfg?.TransferBaseCap ?? 1, cfg?.TransferCapPerBarracksLevel ?? 1);
        if (kind == KindSallyForth)
            return CapFrom(home, StBuilding.Barracks,
                cfg?.SallyBaseCap ?? 1, cfg?.SallyCapPerBarracksLevel ?? 1);
        if (kind == KindPatrol)
            return CapFrom(home, StBuilding.GuardHouse,
                cfg?.PatrolBaseCap ?? 1, cfg?.PatrolCapPerGuardHouseLevel ?? 1);
        // 未知 kind：保守上限 1，避免失控创建
        return 1;
    }

    /// <summary>cap = baseCap + 建筑等级 × perLevel，钳制 ≥ 1。读不到建筑按 0 级处理。绝不抛。</summary>
    private static int CapFrom(Settlement? home, StBuilding building, int baseCap, int perLevel)
    {
        try
        {
            int level = BuildingLevelReader.GetLevel(home, building);
            int cap = baseCap + level * perLevel;
            return cap < 1 ? 1 : cap;
        }
        catch (Exception ex)
        {
            Logger.Error($"CapFrom failed for '{home?.Name}' ({building})", ex);
            return 1;
        }
    }
```

(`PartyLifecycleManager.cs` already has `using SovereignTowns.Common;` (line 4) and `using System;` — `StBuilding` / `BuildingLevelReader` resolve directly. `ConfigurationManager` is referenced elsewhere in the file already.)

- [ ] **Step 4: Confirm `ConfigurationManager` is imported in `PartyLifecycleManager.cs`**

If the build in Step 7 reports `ConfigurationManager` not found, add `using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;` to the using block of `PartyLifecycleManager.cs`. (Other files like `GarrisonXpInjector.cs` use exactly this alias.)

- [ ] **Step 5: Rewrite `ComputeXpFromBarracks` in `GarrisonXpInjector.cs`**

In `SovereignTowns/src/Upgrades/GarrisonXpInjector.cs`, add to the using block (after line 7 `using ConfigurationManager = ...;`):
```csharp
using SovereignTowns.Common;
```

Replace the comment at lines 83-85:
```csharp
            // B7.19：每日 XP 奖励改为按兵营建筑等级派生（不再可手动调整）。
            // settlement_garrison（Town）/ castle_barracks（Castle）等级 → (level + 1) × 5
            // 即 lvl 0→5, lvl 1→10, lvl 2→15, lvl 3→20。
```
with:
```csharp
            // 每日 XP = GarrisonXpBasePerDay + 军营(Barracks)等级 × GarrisonXpPerBarracksLevel。
```

Replace the entire `ComputeXpFromBarracks` method (xml-doc lines 199-203 + body 204-234) with:
```csharp
    /// <summary>
    /// 按军营(Barracks)建筑等级派生每日 XP:GarrisonXpBasePerDay + level × GarrisonXpPerBarracksLevel。
    /// 系数取自 GlobalConfig.BuildingBonus;读不到建筑按 0 级。
    /// </summary>
    private static int ComputeXpFromBarracks(Settlement? settlement)
    {
        var cfg = ConfigurationManager.Current?.BuildingBonus;
        int xpBase = cfg?.GarrisonXpBasePerDay ?? 5;
        int xpPerLevel = cfg?.GarrisonXpPerBarracksLevel ?? 5;
        int level = BuildingLevelReader.GetLevel(settlement, StBuilding.Barracks);
        return xpBase + level * xpPerLevel;
    }
```

- [ ] **Step 6: Point `TryFindPatrolTemplate` at the Guard House in `PatrolDispatcher.cs`**

In `SovereignTowns/src/Patrol/PatrolDispatcher.cs`, replace the barracks-reading block (lines 280-302 — from `int barracksLevel = 0;` through `if (barracksLevel > 3) barracksLevel = 3;`) with:

```csharp
        // 巡逻模板等级 ← 哨所(Guard House)等级。读不到建筑按 0 级,下面 clamp 到 [1,3]。
        int guardHouseLevel = BuildingLevelReader.GetLevel(settlement, StBuilding.GuardHouse);
        if (guardHouseLevel < 1) guardHouseLevel = 1;
        if (guardHouseLevel > 3) guardHouseLevel = 3;
```

Then in the same method, in the candidate-building loop, change:
```csharp
        for (int lvl = barracksLevel; lvl >= 1; lvl--)
```
to:
```csharp
        for (int lvl = guardHouseLevel; lvl >= 1; lvl--)
```

(`PatrolDispatcher.cs` already has `using SovereignTowns.Common;` at line 4 — `BuildingLevelReader` / `StBuilding` resolve directly.)

- [ ] **Step 7: Build**

Run: `dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug`
Expected: `Build succeeded`, 0 errors. If `ConfigurationManager` is unresolved in `PartyLifecycleManager.cs`, apply Step 4 and rebuild.

- [ ] **Step 8: Commit**

```bash
git add SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs SovereignTowns/src/Upgrades/GarrisonXpInjector.cs SovereignTowns/src/Patrol/PatrolDispatcher.cs
git commit -m "feat(buildings): drive party caps + garrison XP from building levels, patrol from Guard House"
```

---

## Task 4: Gauntlet control-panel "Building bonuses" group

**Files:**
- Modify: `SovereignTowns/src/Ui/ControlPanel/ControlPanelSpecs.cs`

The panel is fully reflection-driven: a new `SpecGroup` with `Root="BuildingBonus"` is discovered automatically (`StrategyTabVM.RebuildGroups` iterates `ControlPanelSpecs.AllGroups`; `StrategyTabVM.RootObj` resolves the root by reflection on `GlobalConfig`; `GetD`/`SetD` reflect on the property name). No VM code changes are needed.

- [ ] **Step 1: Add the `building_bonus` SpecGroup**

In `SovereignTowns/src/Ui/ControlPanel/ControlPanelSpecs.cs`, inside `BuildGroups()`, after the closing `},` of the `lifecycle` group (the last group, ending at line 418) and before the list-closing `};` (line 419), insert:

```csharp

            // ── 7. building_bonus ─────────────────────────────────────────────────────
            new SpecGroup
            {
                Key = "building_bonus",
                LabelZh = "建筑加成", LabelEn = "Building bonuses",
                HintZh  = "军营(Barracks)等级派生征兵 / 调拨 / 出击队的并发上限与驻军每日 XP;哨所(Guard House)等级派生巡逻队并发上限。上限 = 基础值 + 建筑等级 × 每级增量。",
                HintEn  = "Barracks level drives recruiter / transfer / sally concurrent caps and daily garrison XP; Guard House level drives the patrol cap. Cap = base + building level × per-level increment.",
                Advanced = false,
                Specs = new List<SpecEntry>
                {
                    new SpecEntry { Root="BuildingBonus", Key="RecruiterBaseCap",
                        LabelZh="征兵队：上限基础值", LabelEn="Recruiter: base cap",
                        HintZh="征兵队并发上限的基础值（军营 0 级时的上限）",
                        HintEn="Base value of the recruiter concurrent cap (the cap at barracks level 0).",
                        Min=1, Max=10, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="RecruiterCapPerBarracksLevel",
                        LabelZh="征兵队：军营每级增量", LabelEn="Recruiter: cap per barracks level",
                        HintZh="军营每升 1 级，征兵队并发上限 +N",
                        HintEn="Each barracks level adds N to the recruiter concurrent cap.",
                        Min=0, Max=5, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="TransferBaseCap",
                        LabelZh="调拨队：上限基础值", LabelEn="Transfer: base cap",
                        HintZh="调拨队并发上限的基础值（军营 0 级时的上限）",
                        HintEn="Base value of the transfer concurrent cap (the cap at barracks level 0).",
                        Min=1, Max=10, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="TransferCapPerBarracksLevel",
                        LabelZh="调拨队：军营每级增量", LabelEn="Transfer: cap per barracks level",
                        HintZh="军营每升 1 级，调拨队并发上限 +N",
                        HintEn="Each barracks level adds N to the transfer concurrent cap.",
                        Min=0, Max=5, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="SallyBaseCap",
                        LabelZh="出击队：上限基础值", LabelEn="Sally: base cap",
                        HintZh="出击队并发上限的基础值（军营 0 级时的上限）",
                        HintEn="Base value of the sally concurrent cap (the cap at barracks level 0).",
                        Min=1, Max=10, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="SallyCapPerBarracksLevel",
                        LabelZh="出击队：军营每级增量", LabelEn="Sally: cap per barracks level",
                        HintZh="军营每升 1 级，出击队并发上限 +N",
                        HintEn="Each barracks level adds N to the sally concurrent cap.",
                        Min=0, Max=5, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="PatrolBaseCap",
                        LabelZh="巡逻队：上限基础值", LabelEn="Patrol: base cap",
                        HintZh="巡逻队并发上限的基础值（哨所 0 级时的上限）",
                        HintEn="Base value of the patrol concurrent cap (the cap at Guard House level 0).",
                        Min=1, Max=10, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="PatrolCapPerGuardHouseLevel",
                        LabelZh="巡逻队：哨所每级增量", LabelEn="Patrol: cap per Guard House level",
                        HintZh="哨所每升 1 级，巡逻队并发上限 +N",
                        HintEn="Each Guard House level adds N to the patrol concurrent cap.",
                        Min=0, Max=5, Discrete=true, Step=1, Def=1 },

                    new SpecEntry { Root="BuildingBonus", Key="GarrisonXpBasePerDay",
                        LabelZh="驻军：每日 XP 基础值", LabelEn="Garrison: base daily XP",
                        HintZh="驻军每兵每日 XP 的基础值（军营 0 级时的值）",
                        HintEn="Base per-troop daily garrison XP (the value at barracks level 0).",
                        Min=0, Max=50, Discrete=true, Step=1, Def=5 },

                    new SpecEntry { Root="BuildingBonus", Key="GarrisonXpPerBarracksLevel",
                        LabelZh="驻军：军营每级 XP 增量", LabelEn="Garrison: daily XP per barracks level",
                        HintZh="军营每升 1 级，驻军每兵每日 XP +N",
                        HintEn="Each barracks level adds N to per-troop daily garrison XP.",
                        Min=0, Max=50, Discrete=true, Step=1, Def=5 },
                },
            },
```

- [ ] **Step 2: Build**

Run: `dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add SovereignTowns/src/Ui/ControlPanel/ControlPanelSpecs.cs
git commit -m "feat(ui): control panel Building bonuses group (Gauntlet)"
```

---

## Task 5: WebUI "Building bonuses" group

**Files:**
- Modify: `SovereignTowns/SovereignTowns/WebUI/index.html`

The WebUI renders groups generically (tab nav + a single spec loop). A new group needs: a `buildingBonusSpecs` getter, inclusion in the `all` array, a group object in `settingsGroups`, and one line in `fetchConfig` to seed the empty root. There is NO build step — `index.html` is copied to the game by the `DeployToGame` MSBuild target; it is verified at runtime in Task 8.

- [ ] **Step 1: Add the `buildingBonusSpecs` getter**

In `SovereignTowns/SovereignTowns/WebUI/index.html`, the `thresholdSpecs` getter ends at line 1230 (`];`) followed by `},` on line 1231. Immediately after line 1231 (before the blank line 1232 and the `// ============ computed ============` comment on line 1233), insert:

```javascript
        get buildingBonusSpecs() {
          const t = this.tr.bind(this);
          return [
          { key: 'RecruiterBaseCap', root: 'BuildingBonus', label: t('征兵队：上限基础值', 'Recruiter: base cap'), hint: t('征兵队并发上限的基础值（军营 0 级时的上限）', 'Base value of the recruiter concurrent cap (the cap at barracks level 0).'), min: 1, max: 10, discrete: true, step: 1, def: 1 },
          { key: 'RecruiterCapPerBarracksLevel', root: 'BuildingBonus', label: t('征兵队：军营每级增量', 'Recruiter: cap per barracks level'), hint: t('军营每升 1 级，征兵队并发上限 +N', 'Each barracks level adds N to the recruiter concurrent cap.'), min: 0, max: 5, discrete: true, step: 1, def: 1 },
          { key: 'TransferBaseCap', root: 'BuildingBonus', label: t('调拨队：上限基础值', 'Transfer: base cap'), hint: t('调拨队并发上限的基础值（军营 0 级时的上限）', 'Base value of the transfer concurrent cap (the cap at barracks level 0).'), min: 1, max: 10, discrete: true, step: 1, def: 1 },
          { key: 'TransferCapPerBarracksLevel', root: 'BuildingBonus', label: t('调拨队：军营每级增量', 'Transfer: cap per barracks level'), hint: t('军营每升 1 级，调拨队并发上限 +N', 'Each barracks level adds N to the transfer concurrent cap.'), min: 0, max: 5, discrete: true, step: 1, def: 1 },
          { key: 'SallyBaseCap', root: 'BuildingBonus', label: t('出击队：上限基础值', 'Sally: base cap'), hint: t('出击队并发上限的基础值（军营 0 级时的上限）', 'Base value of the sally concurrent cap (the cap at barracks level 0).'), min: 1, max: 10, discrete: true, step: 1, def: 1 },
          { key: 'SallyCapPerBarracksLevel', root: 'BuildingBonus', label: t('出击队：军营每级增量', 'Sally: cap per barracks level'), hint: t('军营每升 1 级，出击队并发上限 +N', 'Each barracks level adds N to the sally concurrent cap.'), min: 0, max: 5, discrete: true, step: 1, def: 1 },
          { key: 'PatrolBaseCap', root: 'BuildingBonus', label: t('巡逻队：上限基础值', 'Patrol: base cap'), hint: t('巡逻队并发上限的基础值（哨所 0 级时的上限）', 'Base value of the patrol concurrent cap (the cap at Guard House level 0).'), min: 1, max: 10, discrete: true, step: 1, def: 1 },
          { key: 'PatrolCapPerGuardHouseLevel', root: 'BuildingBonus', label: t('巡逻队：哨所每级增量', 'Patrol: cap per Guard House level'), hint: t('哨所每升 1 级，巡逻队并发上限 +N', 'Each Guard House level adds N to the patrol concurrent cap.'), min: 0, max: 5, discrete: true, step: 1, def: 1 },
          { key: 'GarrisonXpBasePerDay', root: 'BuildingBonus', label: t('驻军：每日 XP 基础值', 'Garrison: base daily XP'), hint: t('驻军每兵每日 XP 的基础值（军营 0 级时的值）', 'Base per-troop daily garrison XP (the value at barracks level 0).'), min: 0, max: 50, discrete: true, step: 1, def: 5 },
          { key: 'GarrisonXpPerBarracksLevel', root: 'BuildingBonus', label: t('驻军：军营每级 XP 增量', 'Garrison: daily XP per barracks level'), hint: t('军营每升 1 级，驻军每兵每日 XP +N', 'Each barracks level adds N to per-troop daily garrison XP.'), min: 0, max: 50, discrete: true, step: 1, def: 5 },
          ];
        },
```

- [ ] **Step 2: Add `buildingBonusSpecs` to the `all` lookup array**

In the `settingsGroups` getter, change line 1238 from:
```javascript
          const all = [...budget, ...this.resourceSpecs, ...this.thresholdSpecs];
```
to:
```javascript
          const all = [...budget, ...this.resourceSpecs, ...this.thresholdSpecs, ...this.buildingBonusSpecs];
```

- [ ] **Step 3: Add the `building_bonus` group object**

In the `settingsGroups` getter's `return [ ... ]`, after the `lifecycle` group object (its closing `},` is on line 1343) and before the array-closing `]` on line 1344, insert:

```javascript
            {
              key: 'building_bonus',
              label: t('建筑加成', 'Building bonuses'),
              hint: t('军营等级派生征兵 / 调拨 / 出击队并发上限与驻军每日 XP；哨所等级派生巡逻队并发上限。上限 = 基础值 + 建筑等级 × 每级增量。', 'Barracks level drives recruiter / transfer / sally caps and daily garrison XP; Guard House level drives the patrol cap. Cap = base + building level × per-level increment.'),
              specs: clean([
                spec('BuildingBonus', 'RecruiterBaseCap'),
                spec('BuildingBonus', 'RecruiterCapPerBarracksLevel'),
                spec('BuildingBonus', 'TransferBaseCap'),
                spec('BuildingBonus', 'TransferCapPerBarracksLevel'),
                spec('BuildingBonus', 'SallyBaseCap'),
                spec('BuildingBonus', 'SallyCapPerBarracksLevel'),
                spec('BuildingBonus', 'PatrolBaseCap'),
                spec('BuildingBonus', 'PatrolCapPerGuardHouseLevel'),
                spec('BuildingBonus', 'GarrisonXpBasePerDay'),
                spec('BuildingBonus', 'GarrisonXpPerBarracksLevel'),
              ]),
            },
```

- [ ] **Step 4: Seed the empty `BuildingBonus` root in `fetchConfig`**

In `fetchConfig()`, after line 1495 `r.body.ClanRecruiter = r.body.ClanRecruiter || {};` add:
```javascript
            r.body.BuildingBonus = r.body.BuildingBonus || {};
```

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/SovereignTowns/WebUI/index.html
git commit -m "feat(ui): control panel Building bonuses group (WebUI)"
```

---

## Task 6: Harmony infrastructure

**Files:**
- Modify: `SovereignTowns/src/SovereignTowns.csproj`
- Modify: `SovereignTowns/SubModule.xml`
- Modify: `SovereignTowns/src/SovereignTownsSubModule.cs`

The mod currently uses no Harmony. This task adds the `0Harmony` reference, declares the `Bannerlord.Harmony` module dependency, and applies patches at boot. `Bannerlord.Harmony` v2.4.2.225 is already installed in the game.

- [ ] **Step 1: Add the `0Harmony` reference to the csproj**

In `SovereignTowns/src/SovereignTowns.csproj`, inside the TaleWorlds `<ItemGroup>` (the one ending at line 92, after the `SandBox.View` reference block at lines 88-91), add before the `</ItemGroup>`:

```xml
    <Reference Include="0Harmony">
      <HintPath>$(BannerlordPath)\Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
```

- [ ] **Step 2: Declare the `Bannerlord.Harmony` dependency in SubModule.xml**

In `SovereignTowns/SubModule.xml`, inside `<DependedModules>` (lines 10-14), add as the FIRST entry (Harmony must load before us):

```xml
    <DependedModule Id="Bannerlord.Harmony" DependentVersion="v2.4.2.225" Optional="false" />
```

So the block becomes:
```xml
  <DependedModules>
    <DependedModule Id="Bannerlord.Harmony" DependentVersion="v2.4.2.225" Optional="false" />
    <DependedModule Id="Native"      DependentVersion="v1.3.15" Optional="false" />
    <DependedModule Id="SandBoxCore" DependentVersion="v1.3.15" Optional="false" />
    <DependedModule Id="Sandbox"     DependentVersion="v1.3.15" Optional="false" />
  </DependedModules>
```

- [ ] **Step 3: Apply Harmony patches in `OnSubModuleLoad`**

In `SovereignTowns/src/SovereignTownsSubModule.cs`, add to the using block (after line 8 `using Logger = SovereignTowns.Logging.Logger;`):
```csharp
using HarmonyLib;
```

In `OnSubModuleLoad`, inside the existing outer `try` block, replace the final `if`-block (lines 67-70):
```csharp
            if (!_skipBehaviorRegistration && _loggerInitialized)
            {
                Logger.Info($"互斥检测通过：未发现冲突模块 ({string.Join(", ", IncompatibleModuleIds)})");
            }
```
with:
```csharp
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
```

- [ ] **Step 4: Build**

Run: `dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug`
Expected: `Build succeeded`, 0 errors. (`PatchAll()` with no `[HarmonyPatch]` classes yet is a valid no-op; the patch class arrives in Task 7.)

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/src/SovereignTowns.csproj SovereignTowns/SubModule.xml SovereignTowns/src/SovereignTownsSubModule.cs
git commit -m "feat(harmony): add Harmony reference, dependency and PatchAll at boot"
```

---

## Task 7: `VanillaPatrolSuppressor` + Harmony patch

**Files:**
- Create: `SovereignTowns/src/Settlement/VanillaPatrolSuppressor.cs`
- Create: `SovereignTowns/src/Patches/PatrolSpawnSuppressionPatch.cs`
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`

- [ ] **Step 1: Create `VanillaPatrolSuppressor.cs`**

```csharp
using System;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;

namespace SovereignTowns.SettlementManagement;

/// <summary>
/// 禁用受管氏族定居点的 vanilla 巡逻队(哨所自带巡逻)。
/// 创建端由 <see cref="SovereignTowns.Patches.PatrolSpawnSuppressionPatch"/>(Harmony 前缀)拦截;
/// 本类负责存量清理 + 易主时清理,并提供 <see cref="ShouldSuppressPatrolFor"/> 供该前缀判定。
/// 范围与 VanillaSuppressionManager 一致(受管 clan + 有可用首府),额外要求 EnabledFeatures.AutoPatrol。
/// 全部方法 try-catch,绝不抛回 vanilla。
/// </summary>
public sealed class VanillaPatrolSuppressor
{
    private bool _initialized;

    /// <summary>全局单例,供 WebConfig 热应用时调 <see cref="DissolveAllManagedVanillaPatrols"/>。</summary>
    public static VanillaPatrolSuppressor? Instance { get; private set; }

    /// <summary>OnSessionLaunched 时由 CampaignBehavior 调用。</summary>
    public void Initialize()
    {
        try
        {
            if (_initialized) return;
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            DissolveAllManagedVanillaPatrols();
            Instance = this;
            _initialized = true;
            Logger.Info("VanillaPatrolSuppressor: initialized");
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaPatrolSuppressor.Initialize failed", ex);
        }
    }

    /// <summary>
    /// Harmony 前缀调用:该 settlement 是否应禁止 vanilla 生成新巡逻队。
    /// 跑在 vanilla 热路径上 — 保持轻量,绝不抛。
    /// </summary>
    public static bool ShouldSuppressPatrolFor(Settlement? settlement)
    {
        try
        {
            if (settlement == null) return false;
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat == null || !feat.AutoPatrol) return false;
            return IsManagedSettlement(settlement);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>遍历所有 vanilla 巡逻队,解散归属受管定居点的。供 Initialize + 配置热应用用。</summary>
    public void DissolveAllManagedVanillaPatrols()
    {
        try
        {
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat == null || !feat.AutoPatrol) return;

            int dissolved = 0;
            foreach (var mp in MobileParty.AllPatrolParties.ToList())
            {
                try
                {
                    var home = mp?.HomeSettlement;
                    if (home == null || !IsManagedSettlement(home)) continue;
                    DissolveParty(mp);
                    dissolved++;
                }
                catch (Exception inner)
                {
                    Logger.Error("VanillaPatrolSuppressor: dissolving one patrol failed", inner);
                }
            }
            if (dissolved > 0)
                Logger.Info($"VanillaPatrolSuppressor: dissolved {dissolved} vanilla patrol parties on managed settlements");
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaPatrolSuppressor.DissolveAllManagedVanillaPatrols failed", ex);
        }
    }

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
            if (settlement == null) return;
            if (!ShouldSuppressPatrolFor(settlement)) return;
            // 易主进入受管范围:解散该定居点现存的 vanilla 巡逻队。
            foreach (var mp in MobileParty.AllPatrolParties.ToList())
            {
                try
                {
                    if (mp?.HomeSettlement == settlement) DissolveParty(mp);
                }
                catch (Exception inner)
                {
                    Logger.Error("VanillaPatrolSuppressor: post-ownership dissolve failed", inner);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("VanillaPatrolSuppressor.OnSettlementOwnerChanged failed", ex);
        }
    }

    /// <summary>仿 vanilla PatrolPartiesCampaignBehavior.RemoveSettlementParties 的安全解散。</summary>
    private static void DissolveParty(MobileParty mp)
    {
        if (mp == null) return;
        mp.MapEventSide = null;
        if (mp.IsActive)
            DestroyPartyAction.Apply(null, mp);
    }

    private static bool IsManagedSettlement(Settlement settlement)
    {
        try
        {
            if (!(settlement.IsTown || settlement.IsCastle)) return false;
            var ownerClan = settlement.OwnerClan;
            if (ownerClan == null) return false;

            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat != null && !feat.ApplyToAiSettlementsToo && ownerClan != Clan.PlayerClan)
                return false;

            var registry = CapitalRegistry.Instance;
            if (registry != null)
                return registry.IsManagedClanWithCapital(ownerClan);
            return ownerClan == Clan.PlayerClan;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 2: Create `PatrolSpawnSuppressionPatch.cs`**

```csharp
using HarmonyLib;
using SovereignTowns.SettlementManagement;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Patches;

/// <summary>
/// Harmony 前缀:受管氏族定居点禁止 vanilla 生成新巡逻队。
/// 目标 = PatrolPartiesCampaignBehavior.CanSettlementSpawnNewPartyCurrently —— 所有 vanilla
/// 巡逻 spawn 路径(DailyTickSettlement / OnNewGameCreated / OnBuildingLevelChanged)的总闸。
/// 前缀返回 false 表示跳过原方法;此时必须给出 __result 与 out 参数 reason。
/// </summary>
[HarmonyPatch(typeof(PatrolPartiesCampaignBehavior), "CanSettlementSpawnNewPartyCurrently")]
internal static class PatrolSpawnSuppressionPatch
{
    private static bool Prefix(Settlement settlement, ref bool __result, ref TextObject reason)
    {
        try
        {
            if (VanillaPatrolSuppressor.ShouldSuppressPatrolFor(settlement))
            {
                __result = false;
                reason = TextObject.Empty;
                return false; // 跳过 vanilla 原方法
            }
        }
        catch (System.Exception ex)
        {
            Logger.Error("PatrolSpawnSuppressionPatch.Prefix failed", ex);
        }
        return true; // 继续执行 vanilla 原方法
    }
}
```

Note: if the build reports `PatrolPartiesCampaignBehavior` is inaccessible (the type is `internal`, not `public`), replace the `[HarmonyPatch(...)]` attribute with a bare `[HarmonyPatch]` and add inside the class:
```csharp
    private static System.Reflection.MethodBase TargetMethod()
        => AccessTools.Method(
            AccessTools.TypeByName("TaleWorlds.CampaignSystem.CampaignBehaviors.PatrolPartiesCampaignBehavior"),
            "CanSettlementSpawnNewPartyCurrently");
```
and remove the `using TaleWorlds.CampaignSystem.CampaignBehaviors;` line.

- [ ] **Step 3: Wire `VanillaPatrolSuppressor` into `SovereignTownsCampaignBehavior`**

In `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`, after the `_vanillaSuppression` field declaration (line 56 `private SovereignTowns.SettlementManagement.VanillaSuppressionManager? _vanillaSuppression;`), add:
```csharp
    private SovereignTowns.SettlementManagement.VanillaPatrolSuppressor? _vanillaPatrolSuppressor;
```

In `OnSessionLaunched`, after the `_vanillaSuppression.Initialize();` call (line 249), add:
```csharp

            // 禁用受管氏族定居点的 vanilla 巡逻队（哨所自带巡逻）。
            _vanillaPatrolSuppressor = new SovereignTowns.SettlementManagement.VanillaPatrolSuppressor();
            _vanillaPatrolSuppressor.Initialize();
```

- [ ] **Step 4: Build**

Run: `dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug`
Expected: `Build succeeded`, 0 errors. If `PatrolPartiesCampaignBehavior` is inaccessible, apply the Step 2 note and rebuild.

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/src/Settlement/VanillaPatrolSuppressor.cs SovereignTowns/src/Patches/PatrolSpawnSuppressionPatch.cs SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs
git commit -m "feat(patrol): suppress and dissolve vanilla patrol parties for managed clans"
```

---

## Task 8: In-game verification

No automated tests exist. Verify at runtime per `CLAUDE.md`.

- [ ] **Step 1: Confirm the deployed build**

The `DeployToGame` MSBuild target ran after the last build and copied the DLL + `SubModule.xml` + `WebUI/` into the live install. Confirm the build output reported the `[SovereignTowns] Deployed to ...` message.

- [ ] **Step 2: Launch the game with a managed-clan campaign**

Enable `SovereignTowns` + `Bannerlord.Harmony` in the launcher. Load a campaign where the player clan owns at least one town with a Guard House (哨所) and a Barracks (军营). Watch logs at `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\`.

- [ ] **Step 3: Verify building-level scaling**

- Log line `Harmony patches applied` appears at boot.
- `VanillaPatrolSuppressor: initialized` appears at session launch.
- In a managed town, raise the Guard House level; confirm the patrol cap log in `PatrolDispatcher` (`st-patrols=x/cap`) reports `cap = PatrolBaseCap + guardHouseLevel × PatrolCapPerGuardHouseLevel`.
- Confirm garrison daily XP scales with barracks level (`GarrisonXpInjector` debug logs).

- [ ] **Step 4: Verify vanilla patrol suppression**

- Managed-clan settlements spawn no new vanilla patrol parties.
- Any vanilla patrol parties present at load on managed settlements are dissolved (`dissolved N vanilla patrol parties` log line).
- AI (non-managed) settlements still spawn vanilla patrols normally.

- [ ] **Step 5: Verify both control-panel surfaces**

- Open the in-game Gauntlet control panel → a "建筑加成 / Building bonuses" group shows 10 sliders; change one, Save, reopen — value persisted.
- Open the WebUI → same group renders; change a value, Save, reload — persisted.
- Set a slider out of range via a hand-edited `global.json` and reload — `ValidateBuildingBonus` rejects it and the config resets to defaults with the version/validation message.

- [ ] **Step 6: Commit any fixes**

If runtime verification surfaces bugs, fix them, rebuild, and commit with a `fix(...)` message. Re-run Steps 2-5 until all checks pass.

---

## Self-Review

**Spec coverage** (against `2026-05-21-building-level-party-bonuses-design.md`):
- §1.2 building-ID bug fix → Task 1 (`BuildingLevelReader` with correct IDs) + Task 3 (call sites switched to it). ✓
- §2.1 `BuildingLevelReader` → Task 1. ✓
- §2.2 `BuildingBonusConfig` + ConfigVersion bump + `??=` defaulting + validation → Task 2. ✓
- §2.3 control panel dual surface + `ValidateBuildingBonus` → Task 2 (validator), Task 4 (Gauntlet), Task 5 (WebUI). ✓
- §2.4 哨所 takeover (`GetMaxFor`, patrol template, `ComputeXpFromBarracks`) → Task 3. ✓
- §2.5 `VanillaPatrolSuppressor` + Harmony → Task 6 (infra) + Task 7. ✓
- §7 `MaxTransfersPerTown` / `MaxSallyForthPerTown` deletion → Task 3 Step 2. ✓
- §8 out-of-scope dungeon bug — intentionally NOT addressed; no task. ✓

**Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N". Every code step has complete code. The single conditional (Task 7 Step 2 note for an inaccessible vanilla type) is a build-time-detectable contingency with full replacement code, not a placeholder.

**Type consistency:** `BuildingBonusConfig` property names are identical across Task 2 (definition), Task 3 (`cfg?.RecruiterBaseCap` etc.), Task 4 (`Key=`), Task 5 (`key:`). `StBuilding.Barracks` / `StBuilding.GuardHouse` and `BuildingLevelReader.GetLevel` match between Task 1 and Task 3. `VanillaPatrolSuppressor.ShouldSuppressPatrolFor` signature matches between Task 7 Step 1 (definition) and Step 2 (call site). `Root="BuildingBonus"` (Task 4) matches `root: 'BuildingBonus'` (Task 5) and the `GlobalConfig.BuildingBonus` property (Task 2).
