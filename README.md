# Sovereign Towns

[English](README.md) | [简体中文](README.zh-CN.md)

An end-to-end **clan-level town governance** mod for
**Mount & Blade II: Bannerlord v1.3.15**. The player clan elects one of
its settlements as a **capital**, and the mod takes over the clan's
day-to-day automation around it: garrison composition, volunteer
recruitment, prisoner conversion, cross-settlement troop logistics,
patrolling, sally-forth, clan-gold-backed treasury management (party
seed funds, recruit wages, equipment upgrades, plus a Hero↔Clan.Gold
transfer UI vanilla doesn't ship), and per-branch composition
templates. A min-cost-flow (MCMF) solver plans troop movement across
the player's settlement network on a configurable **logistics tick**
(default every 6 game hours, configurable 1–24h via
`FiscalAutonomy.CapitalLogisticsTickHours`).

> **Scope**: currently **player-clan only**. AI-clan management is
> code-complete (CapitalRegistry / VanillaSuppressionManager have
> symmetric AI paths) but disabled by default — gated behind
> `GlobalConfig.ApplyToAiSettlementsToo`, which defaults to `false` and
> is intentionally not exposed in the in-game / web control panels
> pending further balance testing. Flipping the flag enables full
> AI-clan takeover (capital election, garrison rebalancing, recruitment,
> patrols, sallies, prisoner conversion) and suppresses vanilla
> volunteer-recruitment for those clans.

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

The managed clan picks one of its settlements as a **capital**. On every
logistics tick (default 6 game hours, configurable 1–24h) the mod
automatically:

- Recruits volunteers in the capital and dispatches **Recruiter parties** to
  villages farther afield.
- Converts prisoners into the capital garrison.
- Dispatches **Transfer parties** to rebalance troops between the capital and
  branch towns/castles, driven by a min-cost-flow solver.
- Dispatches **Patrol parties** around each owned settlement to keep bandits
  and small raiders off the road.
- Dispatches **Sally parties** when a hostile force threatens a settlement.
- Honours per-branch composition templates (tier-range / culture filter /
  troop-type ratios) when topping up garrisons.

### Clan economy

Mod-owned economic decisions also run from the capital. The treasury **is**
the vanilla `Clan.Gold` — there is no separate ledger:

- Vanilla aggregates income from every fief the clan owns (daily taxes,
  trade income, village income — for every `clan.Fiefs` entry regardless of
  which family member holds it) into `Clan.Gold` via the standard
  `DefaultClanFinanceModel`. The mod does not intercept this.
- Mod outflows — party seed funds, recruiter-per-head wages, equipment
  upgrade costs — are debited straight from `Clan.Gold` (through a
  reflection helper, since vanilla declares the setter internal).
- A "pause when broke" feature flag (default on) holds back mod-initiated
  spending when `Clan.Gold` would go negative; turning it off lets the mod
  spend the clan into the red just like vanilla policies can.
- The control panel and web panel both expose a deposit/withdraw page that
  moves funds between **Hero.MainHero.Gold** and **Clan.Gold** — vanilla
  has no UI for this, so the mod adds it. Workshop and caravan profits keep
  going to your personal `Hero.Gold` per vanilla; they do not enter clan
  gold.
- The vanilla Clan Finance tab and the mod's treasury page show the same
  number (because they are the same number).

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

The `DeployToGame` MSBuild target (`AfterTargets="Build"`) automatically
copies the DLL/PDB + `SubModule.xml` + GUI prefabs + WebUI bundle + language
XMLs into `$(BannerlordPath)\Modules\SovereignTowns`.

`BannerlordPath` defaults to the standard Steam install location:

```
C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord
```

If your install lives elsewhere (custom Steam library, Epic, GOG, a
different drive), override it in any of these ways — they take precedence
in this order:

1. **Per-build CLI flag** — one-shot:
   ```powershell
   dotnet build src\SovereignTowns.csproj -c Debug `
     -p:BannerlordPath="D:\Games\Mount & Blade II Bannerlord"
   ```
2. **`Directory.Build.props.user`** — local persistent override
   (gitignored; recommended for contributors):
   ```xml
   <Project>
     <PropertyGroup>
       <BannerlordPath>D:\Games\Mount &amp; Blade II Bannerlord</BannerlordPath>
     </PropertyGroup>
   </Project>
   ```
3. **Environment variable** `BannerlordPath`:
   ```powershell
   $env:BannerlordPath = "D:\Games\Mount & Blade II Bannerlord"
   dotnet build src\SovereignTowns.csproj -c Debug
   ```

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
                              size-limit, volunteer; ClanFinance is a
                              read-only SafeTownIncome helper, no longer
                              registered);
                              src/Economy/ (ModTreasury — debits Clan.Gold;
                              TreasuryUserActions — Hero↔Clan.Gold transfer;
                              ClanGoldAccess — reflection setter;
                              ModExpenseLedger);
                              src/Settlement/ (VanillaSuppressionManager,
                              VanillaPatrolSuppressor);
                              src/Templates/ (TroopTemplateModeService);
                              src/Upgrades/ (TroopUpgradeService,
                              GarrisonXpInjector);
                              src/Patches/ (Harmony patches);
                              src/Coordination/, src/Common/ (helpers).
```

★ **CapitalManager** is central: each managed clan has at most one "capital"
town. **CapitalLogisticsManager** is the per-tick decision point for
capital in-place recruitment, recruiter-party dispatch, and cross-settlement
transfers — driven by min-cost-flow against the capital-level snapshot.
The tick fires every `FiscalAutonomy.CapitalLogisticsTickHours` game hours
(default 6, configurable [1, 24]; this same value also sets the unit length
of one tick in the time-expanded MCMF solver). When the capital falls,
`PartyLifecycleManager.MigrateAllOrDisband` rescues in-flight parties to
the new capital or evaporates them.

## Tests

There are **no unit tests**. Verification = launch the game, watch the logs.

## License

MIT — see [LICENSE](LICENSE).
