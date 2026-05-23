# Sovereign Towns

[English](README.md) | [简体中文](README.zh-CN.md)

An end-to-end **clan-level town governance** mod for
**Mount & Blade II: Bannerlord v1.3.15**. Each managed clan elects one of
its settlements as a **capital**, and the mod takes over garrison
composition, volunteer recruitment, prisoner conversion, cross-settlement
troop logistics, patrolling, sally-forth, treasury, and per-branch
composition templates — for the player clan *and* for every AI clan that
owns a capital. A min-cost-flow (MCMF) solver plans daily troop movement
across the clan's settlement network; vanilla volunteer-recruitment is
suppressed for AI clans and routed exclusively through the mod's channels.

> **Compatibility**: SovereignTowns governs the same vanilla surfaces —
> garrison composition, patrol/sally parties, AI-clan recruitment — that
> `ImprovedGarrisons` (IG) and `GarrisonDoSomething` (GDS) also modify.
> Running either alongside this mod would race for the same state, so both
> are listed in `<IncompatibleModules>` and the launcher refuses to start
> with either enabled. Compatible with **RBM** (troop classification is
> not `stringId`-dependent).

> **Status**: pre-release rapid iteration (v0.0.1). Save-format and
> `global.json` schema may break between commits — no backwards-compatibility
> is guaranteed yet.

## What it does

Each managed clan picks one of its settlements as a **capital**. A daily
cadence then automatically:

- Recruits volunteers in the capital and dispatches **Recruiter parties** to
  villages farther afield.
- Converts prisoners into the capital garrison.
- Dispatches **Transfer parties** to rebalance troops between the capital and
  branch towns/castles, driven by a min-cost-flow solver.
- Dispatches **Patrol parties** around each owned settlement to keep bandits
  and small raiders off the road.
- Dispatches **Sally parties** when a hostile force threatens a settlement.
- Pays party wages from a per-capital treasury and reconciles the books with
  the clan finance tooltip.
- Honours per-branch composition templates (tier-range / culture filter /
  troop-type ratios) when topping up garrisons.

Configuration: an in-game **Control Panel** (a persistent vertical button
on the left edge of the campaign map, plus an entry on every owned
town/castle menu) and a separately-served **web control panel** at
`http://127.0.0.1:<port>/` (default `41763`, auto-increments on conflict)
for richer editing.

UI is fully localised in **English** and **Simplified Chinese**.

## Repository layout

```
.
├── src/                         # C# source + csproj
│   └── SovereignTowns.csproj
├── Module/                      # Bannerlord-side module assets
│   ├── SubModule.xml
│   ├── GUI/Prefabs/             # Gauntlet UI prefabs (control panel)
│   ├── ModuleData/Languages/    # EN + CNs localization
│   └── WebUI/                   # web control panel bundle (HTML/JS/CSS)
├── Directory.Build.props
├── LICENSE
├── README.md
└── README.zh-CN.md
```

The following directories may exist locally but are excluded from git
(third-party copyright, repo bloat, or internal-only documentation):

- `_research/` — decompiled vanilla + reference mods (kept locally as the
  authoritative source when looking up TaleWorlds API signatures).
- `audits/` — design specs, refactor handoffs, backlog notes.
- `docs/` — behaviour guide + planning notes.
- `.claude/` — AI-tooling scratch state.

## Build

Requires the .NET Framework 4.7.2 dev pack and a local Bannerlord install.

```powershell
dotnet build src\SovereignTowns.csproj -c Debug
# or -c Release
```

By default the `DeployToGame` MSBuild target (AfterTargets="Build")
**automatically copies** the DLL/PDB + `SubModule.xml` + GUI prefabs + WebUI
bundle + language XMLs into your live Bannerlord install at
`D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\SovereignTowns`.

Override the install path:

```powershell
dotnet build src\SovereignTowns.csproj -c Debug `
  -p:BannerlordPath="C:\Games\Mount & Blade II Bannerlord"
```

(See `Directory.Build.props` for the default.)

## Runtime logs

Logs are written to:

```
%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\
```

(*not* the module directory — Steam-on-`C:` hits UAC.)

## Hard invariants

These are bugs already paid for; they are not negotiable.

1. **`TargetFramework = net472`**. Bannerlord v1.3.15 CLR cannot resolve
   `netstandard 2.1.0.0` → MonoMod/Harmony chain crash at boot.
2. **`SaveBaseId = 1_900_000_000`** in `src/SaveSystem/SovereignTownsTypeDefiner.cs`.
   The earlier value `100_000_000` lives in a low 8-digit band shared by other
   mods → save corruption. Stays below ButterLib's `2_002_018_000`.
3. **`LocalSaveId` per `Saveable` type: never reuse, never reorder.** When
   deleting a field, keep the ID and mark `[Obsolete]` with type `object` so
   vanilla skips it.
4. **GameModels must be added in `OnGameStart(Game, IGameStarter)`** —
   *not* `OnSessionLaunched`. By session-launched time the Campaign is
   finalised and `AddModel` corrupts the internal model list.
5. **Every event handler entry point wraps its body in `try { ... } catch
   { Logger.Error(...) }`.** Never let our exceptions escape into vanilla.
6. **`HourlyTickPartyEvent` callbacks first-line-filter by `PartyComponent`
   type.** Players have hundreds of parties per hour; touching non-ST parties
   is unsafe and a perf budget killer.
7. **The save becomes hard-dependent on this mod** the moment an
   `StPartyComponent` subclass is persisted. There is no in-mod removal flow.
8. **JSON: `Newtonsoft.Json`** (bundled in
   `$(GameBinPath)\Newtonsoft.Json.dll`, `Private=false`). Don't reintroduce
   hand-rolled regex/MiniJson parsers.

## Architecture in one screen

Four-layer dependency stack, top-down, no upward references; same-layer
Manager-to-Manager coupling goes through `SovereignTownsCampaignBehavior`
(the single event dispatcher).

```
Layer 4  UI                 : DiagnosticGameMenu, STPartyDialogRegistration,
                              ControlPanel (Gauntlet, src/Ui/),
                              WebConfig (HTTP, src/WebConfig/)
Layer 3  Dispatchers        : CapitalManager ★ (src/Capital/),
                              CapitalLogisticsManager (src/Managers/),
                              RecruitmentDispatcher + PrisonerRecruitmentManager
                              + CapitalInPlaceRecruiter (src/Recruitment/),
                              PatrolDispatcher (src/Patrol/),
                              TransferDispatcher (src/Transfer/),
                              SallyDispatcher (src/SallyForth/),
                              PartyLifecycleManager (src/Lifecycle/)
Layer 3b Component instances: StPartyComponent (abstract base, src/Parties/),
                              StPatrolPartyComponent / StRecruiterPartyComponent /
                              StTransferPartyComponent / StSallyPartyComponent
Layer 2  Evaluators         : RiskAssessmentService, TroopCompositionEvaluator,
                              TroopClassifier, TroopTemplateMatcher,
                              GenericTroopMatcher, HostilePartyScanner,
                              GarrisonPowerEvaluator, HorizonForecast
                              (src/Evaluators/)
Layer 2.5 Algorithm kernels : MinCostFlow, UnifiedGarrisonSolver,
                              GarrisonAllocationSolver, DispatchInstruction,
                              RecruitmentTopology (src/Algorithm/)
                              — consumed by CapitalLogisticsManager to plan
                              recruitment + cross-settlement transfers.
Layer 1  Infrastructure     : SovereignTownsSubModule, SovereignTownsCampaignBehavior,
                              SovereignTownsTypeDefiner, ConfigurationManager,
                              Logger, DecisionAuditLogger + ActivityNarrator
                              + ActivityFeed (src/Audit/)
Supporting                  : src/Models/ (GameModel overrides — speed, wage,
                              size-limit, volunteer, ClanFinance);
                              src/Economy/ (ClanTreasury, ModTreasury,
                              ModExpenseLedger, TreasuryUserActions);
                              src/Settlement/ (VanillaSuppressionManager,
                              VanillaPatrolSuppressor);
                              src/Templates/ (TroopTemplateModeService);
                              src/Upgrades/ (TroopUpgradeService,
                              GarrisonXpInjector);
                              src/Patches/ (Harmony patches);
                              src/Coordination/, src/Common/ (helpers).
```

★ **CapitalManager** is central: each managed clan has at most one "capital"
town. **CapitalLogisticsManager** is the daily decision point for capital
in-place recruitment, capital recruiter dispatch, and cross-settlement
transfers — driven by min-cost-flow against the capital-level snapshot.
When the capital falls, `PartyLifecycleManager.MigrateAllOrDisband` rescues
in-flight parties to the new capital or evaporates them.

## Tests

There are **no unit tests**. Verification = launch the game, watch the logs.

## License

MIT — see [LICENSE](LICENSE).
