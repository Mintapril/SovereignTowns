# Unified MCMF handoff (current workspace)

This note replaces the older two-pass / shadow-mode handoff. The current
workspace has deleted the legacy `SupplyDemandGraph` path and `CapitalLogisticsManager`
now uses the unified time-expanded solver as the authoritative dispatcher.

## Current architecture

Entry:

- `SovereignTownsCampaignBehavior.OnHourlyTick`
- `CapitalLogisticsManager.EvaluateAll`
- `CapitalLogisticsManager.RunUnifiedDispatch`
- `UnifiedGarrisonSolver.SolveCoroutine`

Solver:

- Builds a time-expanded min-cost-flow graph over `T = FiscalAutonomy.HorizonTicks`.
- Uses `MinCostFlow.SolveStepwise` so SSP augmentation can yield every
  `FiscalAutonomy.SspYieldEvery` augmentations.
- Decodes only tick-0 decisions: target garrison, recruitment, transfers,
  patrol dispatch, and disband counts.
- Uses soft budget pressure through holding-edge wage cost; `BudgetTroopCap`
  is diagnostic only.

Execution:

- `CapitalLogisticsManager.ExecuteMergedInstructions` consumes solver output.
- Individual dispatchers still validate live constraints immediately before
  mutating game state.
- Feature flags are now reflected in planning as well as execution:
  disabled recruitment / transfer channels are omitted from the graph, and
  disabled patrols pass zero patrol headroom.

## Core files

| File | Role |
| --- | --- |
| `src/Algorithm/UnifiedGarrisonSolver.cs` | Time-expanded graph builder, solve driver, tick-0 decoder. |
| `src/Algorithm/MinCostFlow.cs` | SSP + Dijkstra + Johnson potentials; exposes stepwise solving and self-test. |
| `src/Algorithm/RecruitmentTopology.cs` | Volunteer enumeration, rule-aware role bucketing, village candidates. |
| `src/Evaluators/HorizonForecast.cs` | Flat and threat-projected forecast sources. |
| `src/Evaluators/HostilePartyScanner.cs` | Route risk and converging-hostile scanning. |
| `src/Managers/CapitalLogisticsManager.cs` | Schedules solver coroutines, validates stale results, executes instructions. |
| `src/Patrol/PatrolDispatcher.cs` | Computes patrol headroom and creates patrol parties from solver output. |

## Current graph contract

Important node families:

- `G[settlement, role, tau]`: troops at a settlement boundary.
- `Transit[role, tau]`: recruited troops arriving at the capital before optional forwarding.
- `disbandGate[settlement, tau]`: disband exit with daily-rate cap plus overflow.
- `superSource` / `superSink`.

Important edge families:

- Initial garrison and in-flight arrivals enter `G`.
- Holding edges `G[tau] -> G[tau+1]` carry tier value and wage cost.
- Transfer edges are only built when `TroopTransfers` is enabled.
- Recruitment origins are only built when `AutoRecruitment` is enabled.
- Patrol sink is only built when `AutoPatrol` is enabled and headroom is positive.
- Disband gates always exist for feasibility; protected settlements have zero normal disband cap and can only use overflow.

The K offset is balanced by representing each edge's share of the `[0,T)`
life span. Any source-to-sink path should have the same total K component
for a troop. Debug builds count and assert reduced-cost clamp hits in
`MinCostFlow`.

## Known design tradeoffs

- Soft budget means poor clans can still over-commit if value constants are too high.
  This is a tuning / design risk, not a compile bug.
- In-flight arrivals are role-blind and enter the infantry track.
- Exact-template recruitment uses a deterministic primary service role: the
  highest-tier exact-template target the troop can upgrade into.
- `HorizonTicks = 1` is a degenerate horizon, not a fallback to old behavior.

## Validation checklist

Build:

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug /p:ModuleDeployPath=C:\Users\rangt\Desktop\workspace\.build_deploy\SovereignTowns
```

In game:

- Confirm hourly logistics fires at `CapitalLogisticsTickHours`.
- With `AutoRecruitment=false`, no recruiter or in-place recruitment instruction should be produced.
- With `TroopTransfers=false`, no transfer instruction should be produced.
- With `AutoPatrol=false`, solver logs should report `patrol=0`.
- With nearby enemies above `DispatchRiskVetoThreshold`, dispatchers should soft-skip recruiter / transfer routes.
- Watch `MERGED-TIMING` for build / solve / decode cost and any reduced-cost warnings.

## Removed stale concepts

These are no longer current in this workspace:

- `MergedSolverMode`
- `ShadowMerged`
- `LegacyOnly`
- `SupplyDemandGraph`
- legacy Pass A / Pass B orchestration
- `MERGED-SHADOW` / `MERGED-DIFF` as the primary safety model

Historical plans under `docs/superpowers/` and older audit files are design history,
not the current implementation contract.
