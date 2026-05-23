# Sovereign Towns

Automated clan town garrison / recruitment / patrol / cross-town transfer /
sally-forth management for **Mount & Blade II: Bannerlord v1.3.15**.

Designed as a **complete replacement** for `ImprovedGarrisons` (IG) and
`GarrisonDoSomething` (GDS) — both are listed in `<IncompatibleModules>` and
detected at boot. Compatible with **RBM** (troop classification is not
`stringId`-dependent).

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

Configuration: an in-game **Control Panel** (Esc-menu button on the campaign
map) plus a separately-launched **web control panel** at
`http://127.0.0.1:<port>/` for richer editing.

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
├── docs/                        # behaviour guide + planning notes
├── audits/                      # design specs, refactor handoffs, backlogs
├── Directory.Build.props
├── LICENSE
└── README.md
```

`_research/` (decompiled vanilla + reference mods) is local-only and excluded
from git — third-party copyright + repository bloat.

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
                              ControlPanel (Gauntlet), WebConfig (HTTP)
Layer 3  Dispatchers        : CapitalManager ★, CapitalLogisticsManager,
                              RecruitmentDispatcher, PrisonerRecruitmentManager,
                              PatrolDispatcher, TransferDispatcher,
                              SallyDispatcher, PartyLifecycleManager
Layer 3b Component instances: StPartyComponent (abstract base),
                              StPatrolPartyComponent / StRecruiterPartyComponent /
                              StTransferPartyComponent / StSallyPartyComponent
Layer 2  Evaluators         : RiskAssessmentService, TroopCompositionEvaluator,
                              TroopClassifier, TroopTemplateMatcher,
                              GenericTroopMatcher, HostilePartyScanner
Layer 1  Infrastructure     : SovereignTownsSubModule, SovereignTownsCampaignBehavior,
                              SovereignTownsTypeDefiner, ConfigurationManager,
                              Logger, DecisionAuditLogger
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
