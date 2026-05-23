# Sovereign Towns

[English](README.md) | [简体中文](README.zh-CN.md)

**Hand the clan's day-to-day busywork — garrison, recruitment, patrols, sallies, books — to its capital.** A min-cost-flow dispatcher decides what moves where; you decide the policy.

> **Bannerlord v1.3.15** · requires **Bannerlord.Harmony** · incompatible with **ImprovedGarrisons** and **GarrisonDoSomething** · plays nice with **RBM** · pre-release (v0.0.1, save and config formats may break between commits)

---

## What it does

Your clan picks one settlement as a **capital**. From there the mod runs everything else.

### Capital-led automation

- **Garrison composition** — every owned town and castle held to your per-branch template (tier range, culture filter, troop-type ratios).
- **Recruitment** — in-place at the capital, Recruiter parties out to villages, prisoners converted on the spot.
- **Logistics** — Transfer parties shuttle troops across the clan's settlement network, planned by a min-cost-flow solver against a capital-level snapshot.
- **Defence** — Patrol parties orbit each holding to keep bandits off the road; Sally parties go out when a real threat shows up.
- **Capital recovery** — if the capital falls, in-flight parties either migrate to the new capital or evaporate cleanly.

### Clan economy

Vanilla has no separate "clan treasury" — `Clan.Gold` is a computed property `=> Leader?.Gold ?? 0`. For your clan that's `Hero.MainHero.Gold`. The mod treats it as the single source of truth.

- Vanilla pours all `clan.Fiefs` income into `Clan.Gold` (no mod interception).
- Mod outflows — party seed funds, recruit-per-head wages, equipment upgrades — debit `Clan.Gold` (= `Hero.MainHero.Gold`) directly via `Hero.ChangeHeroGold`.
- Each dispatched party (Recruiter / Patrol / Sally / Transfer) carries vanilla `MobileParty.PartyTradeGold` as its operating budget: buys food and stock mounts en route, sells loot back into it, returns whatever's left to the clan leader on disband. The economy stays closed against vanilla `Settlement.Gold` — same path vanilla caravans use.
- "Pause when broke" guard rail (default on) holds mod spending when the clan would go negative.
- Workshops and caravans keep flowing to your `Hero.Gold` per vanilla — same account, no separate ledger.
- Each food purchase posts a bottom-left log: `[Sovereign Towns] {Party} bought {N} {item} at {Where} (-{N}d)` (player-clan parties only).

### Knobs and observability

- **Configurable cadence** — logistics tick anywhere from 1 hour to 24 hours (default 6h).
- **In-game Control Panel** — persistent button on the left edge of the campaign map plus an entry on every owned town/castle menu.
- **Web Control Panel** — separately served at `http://127.0.0.1:41763/` (auto-increments on port conflict) for richer editing.
- **Activity feed** — every dispatch, recruit, transfer and sally logged, queryable from either panel.
- **Localisation** — English and Simplified Chinese.

---

## Scope

Currently **player clan only**. AI-clan management is implemented end-to-end (symmetric `CapitalRegistry` / `VanillaSuppressionManager` paths) but disabled by default — `EnabledFeatures.ApplyToAiSettlementsToo` defaults to `false` and is intentionally hidden from the panels until balance testing wraps. Flipping it puts every capital-holding clan under the same regime and routes their recruitment exclusively through the mod.

---

## Install

1. Install [Bannerlord.Harmony](https://www.nexusmods.com/mountandblade2bannerlord/mods/2006) (and any other usual prerequisites).
2. Drop this module's `SovereignTowns/` folder next to `Native/` under `Modules/`.
3. Enable **Sovereign Towns** in the launcher.
4. Start a campaign and open the map button.

Logs are written to

```
%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\
```

— never the module directory, to avoid UAC writes on Steam-on-`C:` installs.

---

## Build from source

```powershell
dotnet build src\SovereignTowns.csproj -c Debug
```

`Directory.Build.props` defaults `BannerlordPath` to the standard Steam install. Override via, in precedence order:

1. **CLI flag** — `dotnet build … -p:BannerlordPath="D:\Games\Mount & Blade II Bannerlord"`
2. **`Directory.Build.props.user`** — gitignored, one line of XML
3. **Env var** — `$env:BannerlordPath = "..."`

The `DeployToGame` MSBuild target runs `AfterTargets="Build"` and copies the DLL, GUI prefabs, WebUI bundle and language XMLs into `$(BannerlordPath)\Modules\SovereignTowns`.

There are **no unit tests** — verification is "launch the game, watch the logs."

---

## Repository layout

```
.
├── src/                     # C# source + csproj
├── Module/                  # SubModule.xml + Gauntlet prefabs + ModuleData + WebUI
├── Directory.Build.props
├── README.md  README.zh-CN.md  LICENSE
```

Local-only and gitignored: `_research/` (decompiled vanilla + reference mods), `audits/` (design notes), `docs/` (planning), `.claude/` (AI-tooling state).

---

## For modders

<details>
<summary><strong>Architecture overview</strong></summary>

Four-layer dependency stack, top-down, no upward references; same-layer wiring routes through the single `SovereignTownsCampaignBehavior` event dispatcher.

```
Layer 4   UI                 DiagnosticGameMenu, STPartyDialogRegistration,
                             ControlPanel (Gauntlet), WebConfig (HTTP)
Layer 3   Dispatchers        CapitalManager ★, CapitalLogisticsManager,
                             RecruitmentDispatcher, PrisonerRecruitmentManager,
                             PatrolDispatcher, TransferDispatcher, SallyDispatcher,
                             PartyLifecycleManager
Layer 3b  Components         StPartyComponent + Patrol/Recruiter/Transfer/Sally
Layer 2   Evaluators         Risk, TroopClassifier, TemplateMatcher,
                             HostilePartyScanner, GarrisonPowerEvaluator, ...
Layer 2.5 Algorithm kernels  MinCostFlow, UnifiedGarrisonSolver,
                             GarrisonAllocationSolver, RecruitmentTopology
Layer 1   Infrastructure     SubModule, CampaignBehavior, TypeDefiner,
                             ConfigurationManager, Logger, DecisionAuditLogger
Supporting                   Models/ (vanilla GameModel overrides),
                             Economy/ (ModTreasury wrapper over Hero.ChangeHeroGold +
                                       ledger / audit; ClanGoldAccess thin facade),
                             Settlement/ (vanilla suppression),
                             Templates/, Upgrades/, Patches/, Coordination/, Common/
```

★ `CapitalManager` is per-clan: at most one capital town per managed clan.
`CapitalLogisticsManager` runs the per-tick decision (recruitment + cross-settlement transfers) by handing a snapshot to the MCMF solver and decoding the flow into dispatch instructions. The tick interval is `FiscalAutonomy.CapitalLogisticsTickHours` (default 6, range 1–24) — the same value also sets one unit in the time-expanded MCMF horizon.

</details>

<details>
<summary><strong>Hard invariants — bugs already paid for, don't undo</strong></summary>

1. **`TargetFramework = net472`**. v1.3.15's CLR can't resolve `netstandard 2.1.0.0` — anything else chain-crashes MonoMod/Harmony at boot.
2. **`SaveBaseId = 1_900_000_000`** (`src/SaveSystem/SovereignTownsTypeDefiner.cs`). The original `100_000_000` collided with other mods in a low band → save corruption. Stays below ButterLib's `2_002_018_000`.
3. **`LocalSaveId` per `Saveable` type: never reuse, never reorder.** When deleting a field, keep the ID and mark `[Obsolete]` with type `object` so vanilla skips it.
4. **Register GameModels in `OnGameStart`, not `OnSessionLaunched`.** By session-launched time the Campaign is finalised and `AddModel` corrupts the internal model list.
5. **Every event-handler entry point wraps its body in `try { ... } catch { Logger.Error(...) }`.** Our exceptions must not leak into vanilla.
6. **`HourlyTickPartyEvent` callbacks first-line filter by `PartyComponent` type.** Players have hundreds of parties per hour — touching non-ST ones is both unsafe and a perf budget killer.
7. **Save becomes hard-dependent on the mod** the moment an `StPartyComponent` subclass is persisted. There is no in-mod removal path.
8. **JSON via Newtonsoft.Json** (bundled with the game at `$(GameBinPath)\Newtonsoft.Json.dll`, `Private=false`). No hand-rolled regex/MiniJson parsers.

</details>

---

## License

MIT — see [LICENSE](LICENSE).
