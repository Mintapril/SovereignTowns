# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**SovereignTowns** — a Mount & Blade II: Bannerlord **v1.3.15** Campaign mod that automates managed-clan town garrison / recruitment / patrol / cross-town transfer / sally-forth. Designed as a **complete replacement** for `ImprovedGarrisons` (IG) and `GarrisonDoSomething` (GDS) — those mods are listed in `<IncompatibleModules>` and detected at boot. Must remain compatible with **RBM** (兵种判定不依赖 stringId).

Layout:
- `SovereignTowns/` — the actual mod project (csproj, src, SubModule.xml, GUI prefabs)
- `SovereignTowns/_research/` — decompilations of IG, GDS, and selected vanilla classes (ilspycmd 8.1). Local-only reference, **not tracked in git**. Treat as the primary source when looking up vanilla API signatures — do not invent.

## Build & deploy

There is only one project: `SovereignTowns/src/SovereignTowns.csproj`.

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
# or -c Release
```

The `DeployToGame` MSBuild target (AfterTargets="Build") **automatically copies** the DLL/PDB + SubModule.xml + GUI prefabs into the live Bannerlord install. Override the install path with `-p:BannerlordPath="..."` — default in `Directory.Build.props` is `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`.

There are **no unit tests**. Verification = launch the game, watch logs at `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\` (NOT the module directory — Steam-on-C: hits UAC).

## Project status: pre-release rapid iteration

**No backwards compatibility is required** — neither for save files nor for `global.json` config. The project is in active design iteration; we break and re-create as needed:

- Save IDs (`SaveBaseId`, `LocalSaveId`, type-definer class IDs) may be **renumbered or dropped** at will. No need to leave `[Obsolete]` placeholder slots, no need to reserve enum values for "old audit logs", no need to keep dead code paths around for legacy compat. Just delete it cleanly.
- `ConfigVersion` mismatch already resets to defaults (`ConfigurationManager.TryLoadFromDisk` → returns null on version drift). No migration code needed when fields change.
- "Removed feature reserved enum slot" comments (e.g. `// 4 (formerly RequestDisbandExcess) intentionally left unused`) are **not** required — feel free to renumber.
- Existing playtest saves can be regenerated. Telling the user "delete `global.json` and restart" is an acceptable answer.

This relaxes the rules in the next section: invariants 2 / 3 / 7 are still listed because their underlying *vanilla-side* constraints still apply (vanilla's saveable system has fixed expectations), but you are free to redesign them in one shot rather than evolve incrementally. When you change save shape, just bump `CurrentConfigVersion` and / or `SaveBaseId` and move on.

## Hard invariants (do not violate)

These are bugs we have already paid for. They are not negotiable.

1. **TargetFramework = `net472`**. Do not change to netstandard2.1; Bannerlord v1.3.15 CLR cannot resolve netstandard 2.1.0.0 → MonoMod/Harmony chain crash at boot.
2. **`SaveBaseId = 1_900_000_000`** (`src/SaveSystem/SovereignTownsTypeDefiner.cs`). The earlier value `100_000_000` is in a low 8-digit band shared by other mods → save corruption. Stay below ButterLib's `2_002_018_000`.
3. **`LocalSaveId` per Saveable type: never reuse, never reorder.** When deleting a field, keep the ID and mark `[Obsolete]` with type `object` so vanilla skips it.
4. **GameModels must be added in `OnGameStart(Game, IGameStarter)`** — *not* `OnSessionLaunched`. By session-launched time the Campaign is finalized and `AddModel` corrupts the internal model list. See `SovereignTownsSubModule.OnGameStart`.
5. **Every event handler entry point wraps its body in `try { ... } catch { Logger.Error(...) }`.** Never let our exceptions escape into vanilla — IG/GDS users have already seen the crash modes. The pattern is everywhere in `SovereignTownsCampaignBehavior` and the `*Manager.OnHourly*` callbacks.
6. **`HourlyTickPartyEvent` callbacks must first-line-filter by PartyComponent type.** Players have 100s of parties per hour; touching non-ST parties is both unsafe and a perf budget killer.
7. **The save becomes hard-dependent on this mod** the moment a `StPartyComponent` subclass is persisted (`StRecruiterPartyComponent` / `StTransferPartyComponent` / `StSallyPartyComponent` / `StPatrolPartyComponent`). There is no in-mod removal flow.
8. **JSON: use `Newtonsoft.Json`** (bundled in `$(GameBinPath)\Newtonsoft.Json.dll`, `Private=false`). Do not reintroduce hand-written regex/MiniJson parsers — they were removed in 2026-05-12 cross-validation.

## Architecture in one screen

4-layer dependency stack, top-down, no upward references; same-layer Manager-to-Manager coupling goes through `SovereignTownsCampaignBehavior` (the single event dispatcher).

```
Layer 4  UI                 : DiagnosticGameMenu, STPartyDialogRegistration,
                              WebConfig (WebConfigServer + WebConfigEndpoints + TroopDumper)
Layer 3  Dispatchers        : CapitalManager (★), CapitalLogisticsManager, RecruitmentDispatcher, PrisonerRecruitmentManager,
                              PatrolDispatcher, TransferDispatcher, SallyDispatcher,
                              PartyLifecycleManager
Layer 3b Component instances: StPartyComponent (abstract base),
                              StPatrolPartyComponent / StRecruiterPartyComponent /
                              StTransferPartyComponent / StSallyPartyComponent
                              (each instance owns its own state machine; see B16.4)
Layer 2  Evaluators         : RiskAssessmentService, TroopCompositionEvaluator,
                              TroopClassifier, TroopTemplateMatcher, GenericTroopMatcher
Layer 1  Infrastructure     : SovereignTownsSubModule, SovereignTownsCampaignBehavior, SovereignTownsTypeDefiner,
                              ConfigurationManager, Logger, DecisionAuditLogger
```

★ **CapitalManager** is central to the runtime semantics: each managed clan has at most one "capital" town. **CapitalLogisticsManager** is the daily decision point for capital in-place recruitment, capital recruiter dispatch, and cross-settlement troop transfers. Non-capital town/castle transfers may still route branch-to-branch, but they are planned from the capital-level snapshot so the capital can coordinate against in-flight movement. When the capital falls, `PartyLifecycleManager.MigrateAllOrDisband` rescues in-flight parties to the new capital or evaporates them.

### Configurable thresholds

All "how many men / what fraction" knobs that gate party creation live in `GlobalConfig.Thresholds` (type `PartyThresholds`). Examples: `PatrolMinCapitalGarrison` (default 40), `PatrolTroopBatchSize` (15), `RecruiterEscortRatio` (0.10), `TransferMaxTroopsPerTask` (100), `TransferRatio` (0.30), `RecruitmentMinDemand` (10). All defaults match the historical hard-coded constants so saves carry over without behavioural drift. When you add a new gate of this kind, put the constant in `PartyThresholds` first, expose it via `thresholdSpecs` in `WebUI/index.html`, and read it as `ConfigurationManager.Current?.Thresholds?.X ?? <default>` inside the Manager (the `??` fallback is mandatory — config can be null during early init).

## Lifecycle gotchas (read before changing init/save code)

- `SovereignTownsCampaignBehavior.SyncData` runs **before** `OnSessionLaunched`. The capital's `Settlement.StringId` is buffered into `_pendingCapitalStringId` and re-injected into `CapitalManager` after construction. Do not assume Manager instances exist inside `SyncData`.
- `OnGameLoadedEvent` triggers `PartyLifecycleManager.RebuildFromCampaign()`. Without it, the `_tracked` dict is empty after load → `CountActive` returns 0 → unlimited duplicate spawns + idle timers broken. Do not remove the OnGameLoaded subscription.
- `HourlyTickSettlement` is **skipped while the player is inside the settlement** (vanilla quirk). Sally-forth / capital-only-daily routines are double-subscribed via `OnDailyTickSettlement` as a fallback.
## Working norms

- Always cite the decompiled vanilla file under `SovereignTowns/_research/` when introducing a new TaleWorlds API call.
- Prefer extending an existing Manager over adding a new one; the layering rules require that new same-layer wiring still go through `SovereignTownsCampaignBehavior`.
- Independent multi-file investigations should be dispatched to parallel subagents (per the user's standing instruction in memory).
