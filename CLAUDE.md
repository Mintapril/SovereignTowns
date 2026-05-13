# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**SovereignTowns** — a Mount & Blade II: Bannerlord **v1.3.15** Campaign mod that automates player-owned town garrison / recruitment / patrol / cross-town transfer / sally-forth. Designed as a **complete replacement** for `ImprovedGarrisons` (IG) and `GarrisonDoSomething` (GDS) — those mods are listed in `<IncompatibleModules>` and detected at boot. Must remain compatible with **RBM** (兵种判定不依赖 stringId).

Layout:
- `SovereignTowns/` — the actual mod project (csproj, src, SubModule.xml, GUI prefabs, decompilation evidence in `_research/`)
- Top-level `*.md` design docs (see "Documentation map" below) — **read these before substantive work**; they encode rationale that the code does not.

## Build & deploy

There is only one project: `SovereignTowns/src/SovereignTowns.csproj`.

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
# or -c Release
```

The `DeployToGame` MSBuild target (AfterTargets="Build") **automatically copies** the DLL/PDB + SubModule.xml + GUI prefabs into the live Bannerlord install. Override the install path with `-p:BannerlordPath="..."` — default in `Directory.Build.props` is `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`.

There are **no unit tests**. Verification = launch the game, watch logs at `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\` (NOT the module directory — Steam-on-C: hits UAC).

## Hard invariants (do not violate)

These are bugs we have already paid for. They are not negotiable.

1. **TargetFramework = `net472`**. Do not change to netstandard2.1; Bannerlord v1.3.15 CLR cannot resolve netstandard 2.1.0.0 → MonoMod/Harmony chain crash at boot.
2. **`SaveBaseId = 1_900_000_000`** (`src/SaveSystem/SovereignTownsTypeDefiner.cs`). The earlier value `100_000_000` is in a low 8-digit band shared by other mods → save corruption. Stay below ButterLib's `2_002_018_000`.
3. **`LocalSaveId` per Saveable type: never reuse, never reorder.** When deleting a field, keep the ID and mark `[Obsolete]` with type `object` so vanilla skips it.
4. **GameModels must be added in `OnGameStart(Game, IGameStarter)`** — *not* `OnSessionLaunched`. By session-launched time the Campaign is finalized and `AddModel` corrupts the internal model list. See `SovereignTownsSubModule.OnGameStart`.
5. **LLM is forbidden on any real-time path** — `HourlyTick*`, risk eval, patrol order switch, lifecycle obstacle resolution, sally-forth triggers, `CanMoveToSettlementEvent`, siege/emergency, all defense evaluation. LLM is allowed only on `DailyTick` long-term review, user-initiated "advisor" menus, and template generation. The hard rule is in `ARCHITECTURE.md §4.0`; enforce when adding new code paths.
6. **Every event handler entry point wraps its body in `try { ... } catch { Logger.Error(...) }`.** Never let our exceptions escape into vanilla — IG/GDS users have already seen the crash modes. The pattern is everywhere in `SovereignTownsCampaignBehavior` and the `*Manager.OnHourly*` callbacks.
7. **`HourlyTickPartyEvent` callbacks must first-line-filter by PartyComponent type.** Players have 100s of parties per hour; touching non-ST parties is both unsafe and a perf budget killer (see `ARCHITECTURE.md §7`).
8. **The save becomes hard-dependent on this mod** the moment a `CustomPartyComponent` subclass is persisted (`RecruitingPartyComponent` / `TransferPartyComponent` / `SallyForthPartyComponent`). Uninstall path is `src/Ui/SafeUninstallMenu.cs` — it must transfer all custom-party roster back to garrison and `DestroyParty` before the user can safely remove the mod.
9. **JSON: use `Newtonsoft.Json`** (bundled in `$(GameBinPath)\Newtonsoft.Json.dll`, `Private=false`). Do not reintroduce hand-written regex/MiniJson parsers — they were removed in 2026-05-12 cross-validation.

## Architecture in one screen

5-layer dependency stack, top-down, no upward references; same-layer Manager-to-Manager coupling goes through `SovereignTownsCampaignBehavior` (the single event dispatcher). Full diagram + per-component contract in `SovereignTowns/_research`-adjacent `ARCHITECTURE.md` at the workspace root.

```
Layer 5  UI / integration  : DiagnosticGameMenu, SafeUninstallMenu, MapRibbon, ConfigScreen, MCMIntegration (soft dep)
Layer 4  LLM (optional)    : ILLMProvider, LLMReasoningService, LLMDecisionValidator, RuleBasedFallbackDecisionMaker
Layer 3  Managers          : CapitalManager (★), TownGarrisonManager, RecruitmentManager, PrisonerRecruitmentManager,
                             PatrolManager, CastleSupportManager, GarrisonTransferManager, SallyForthManager,
                             PartyLifecycleManager
Layer 2  Evaluators        : RiskAssessmentService, TroopCompositionEvaluator, SettlementDefenseDemandEvaluator,
                             TroopClassifier, TroopTemplateMatcher, GenericTroopMatcher
Layer 1  Infrastructure    : SovereignTownsSubModule, SovereignTownsCampaignBehavior, SovereignTownsTypeDefiner,
                             ConfigurationManager (+ ConfigMigrator), Logger, DecisionAuditLogger
```

★ **CapitalManager** is central to the runtime semantics: the player has at most one "capital" town. **Automation (XP injection, prisoner recruitment, capital-notable recruiter) runs only on the capital**; non-capital owned towns receive support via `CastleSupportManager` → `GarrisonTransferManager` cross-town transfers. When the capital falls, `PartyLifecycleManager.MigrateAllOrDisband` rescues in-flight parties to the new capital or evaporates them.

## Lifecycle gotchas (read before changing init/save code)

- `SovereignTownsCampaignBehavior.SyncData` runs **before** `OnSessionLaunched`. The capital's `Settlement.StringId` is buffered into `_pendingCapitalStringId` and re-injected into `CapitalManager` after construction. Do not assume Manager instances exist inside `SyncData`.
- `OnGameLoadedEvent` triggers `PartyLifecycleManager.RebuildFromCampaign()`. Without it, the `_tracked` dict is empty after load → `CountActive` returns 0 → unlimited duplicate spawns + idle timers broken. This was C1 in the cross-validation; do not remove the OnGameLoaded subscription.
- `HourlyTickSettlement` is **skipped while the player is inside the settlement** (vanilla quirk). Sally-forth / capital-only-daily routines are double-subscribed via `OnDailyTickSettlement` as a fallback.
- LLM `llm.json` is read from `Modules/SovereignTowns/Configs/llm.json` at session launch; missing or unparseable → `NoOpLLMProvider`, mod continues fine.

## Documentation map

The design docs at the workspace root are load-bearing — they encode decisions that are not visible in the code. **When making architectural changes, update the relevant doc in the same change.**

| File | Purpose |
|---|---|
| `ARCHITECTURE.md` | Component contract (responsibility / deps / tick / data / save / MVP / vanilla API per module). Authoritative on layering rules and the LLM-not-realtime constraint. |
| `AUDIT_REPORT_2026-05-12.md` | CRITICAL/WARNING list from the 5-subagent audit. Open issues are tracked here. |
| `CROSS_VALIDATION_REPORT.md` | Community-source cross-checks (ButterLib SaveSystem, BUTR templates, IG/GDS/BannerKings decomps). Records resolved CRITICALs. |
| `FEASIBILITY_REPORT.md` / `RESEARCH_FINDINGS.md` | Phase-1/2 evidence base. Cite the file path under `_research/decompiled/...` when claiming a vanilla API exists. |
| `MOD_SURVEY.md` | Functional baseline that this mod must cover when replacing IG + GDS. |
| `RISK_REGISTER.md`, `UNCERTAINTY_LOG.md`, `PHASE1_RESEARCH_PLAN.md` | Risk tracking + open uncertainties + the original research plan. |
| `SovereignTowns/_research/` | Full decompilations of IG, GDS, and selected vanilla classes (ilspycmd 8.1). Treat this as the primary reference when looking up vanilla API signatures — do not invent. |

## Working norms

- Always cite the decompiled vanilla file under `_research/` when introducing a new TaleWorlds API call. The architecture doc uses 📂 markers for this.
- Prefer extending an existing Manager over adding a new one; the layering rules require that new same-layer wiring still go through `SovereignTownsCampaignBehavior`.
- Independent multi-file investigations should be dispatched to parallel subagents (per the user's standing instruction in memory).
