# Supply Demand MCMF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the MCMF supply/demand algorithm and wire it into SovereignTowns logistics execution with conservative source eligibility.

**Architecture:** Phase 1 creates focused algorithm units under `SovereignTowns/src/Algorithm`. Phase 2 builds a daily logistics graph from managed settlements. Phase 3 executes decoded instructions through existing safety-checked dispatchers. The execution graph only includes source kinds that current dispatchers can actually consume: local in-place notable recruits, capital village recruiter dispatch, and garrison transfers. Prisoner conversion and non-capital recruiter sources remain outside the execution graph until they have instruction-scoped executors.

**Tech Stack:** C# net472, nullable enabled, TaleWorlds Campaign APIs, Newtonsoft.Json config, existing `Logger` and `DecisionAuditLogger`.

---

### Task 1: Baseline And Branch Hygiene

**Files:**
- Read: `SovereignTowns/src/SovereignTowns.csproj`
- Read: `docs/superpowers/specs/2026-05-18-supply-demand-mcmf-design.md`

- [ ] **Step 1: Confirm branch isolation**

Run: `git branch --show-current`
Expected: `codex/supply-demand-mcmf`

- [ ] **Step 2: Run baseline build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: build succeeds. Existing baseline nullable warnings in unrelated files are acceptable and must not be mixed into this change.

### Task 2: MinCostFlow Red Test And Implementation

**Files:**
- Create: `SovereignTowns/src/Algorithm/MinCostFlow.cs`

- [ ] **Step 1: Write the failing self-test reference**

Create `MinCostFlow.SelfTest()` usages before the solver implementation is complete. The expected transportation case is:

```csharp
// source capacities: A=2, B=1; demands X=2, Y=1
// costs: A->X=1, A->Y=5, B->X=2, B->Y=1
// optimal: A->X 2, B->Y 1, total cost 3, flow 3
```

- [ ] **Step 2: Run build to verify RED**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: fail while `MinCostFlow` is incomplete or self-test assertions cannot pass.

- [ ] **Step 3: Implement `MinCostFlow`**

Implement:

```csharp
public sealed class MinCostFlow
{
    public void AddNode(int id);
    public void AddEdge(int from, int to, int capacity, int cost);
    public MinCostFlowResult Solve(int source, int sink);
    public static bool SelfTest(out string message);
}
```

Use SPFA over residual edges, non-negative public costs, reverse residual edges, and expose per-original-edge flow.

- [ ] **Step 4: Run build and self-test**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: build succeeds with no new warnings from algorithm files.

### Task 3: Match Policy And Dispatch Instructions

**Files:**
- Create: `SovereignTowns/src/Algorithm/DispatchInstruction.cs`
- Create: `SovereignTowns/src/Algorithm/MatchPolicy.cs`

- [ ] **Step 1: Add instruction records**

Define sealed instruction types:

```csharp
public abstract class DispatchInstruction
public sealed class InPlaceRecruitInstruction
public sealed class RecruiterPartyInstruction
public sealed class PrisonerConvertInstruction
public sealed class TransferPartyInstruction
```

`RecruiterPartyInstruction` must use `ReturnSettlement`, not arbitrary destination delivery.

- [ ] **Step 2: Add match policy**

Implement bucketization over existing `GenericTroopRole` values: `Infantry`, `Ranged`, `Cavalry`, `HorseArcher`. Match penalties must clamp leniency to `[0, 1]` and keep costs non-negative.

- [ ] **Step 3: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: build succeeds with no new warnings from new files.

### Task 4: Supply Demand Graph Solver

**Files:**
- Create: `SovereignTowns/src/Algorithm/SupplyDemandGraph.cs`
- Modify: `SovereignTowns/src/Managers/CapitalLogisticsManager.cs`

- [ ] **Step 1: Build graph snapshot**

Collect managed clan town/castle settlements, compute per-role demand from `TownGarrisonRule`, add garrison surplus sources, local notable sources, prisoner sources, village notable sources, and unmet bypass.

- [ ] **Step 2: Decode dispatch instructions**

Decode flow into instruction objects and log source kind, settlement, role, count, and cost breakdown.

- [ ] **Step 3: Wire solver behind execution cutover**

At the start of `CapitalLogisticsManager.RunDaily`, call the solver inside try/catch. Execution cutover is handled in Task 7; old heuristic decisions should not also run after cutover.

- [ ] **Step 4: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: build succeeds with no new warnings from the graph integration.

### Task 5: Config And Web UI Thresholds

**Files:**
- Modify: `SovereignTowns/src/Configuration/GlobalConfig.cs`
- Modify: `SovereignTowns/src/Configuration/ConfigurationManager.cs`
- Modify: `SovereignTowns/SovereignTowns/WebUI/index.html`

- [ ] **Step 1: Add thresholds**

Add `McmfHardPenalty`, `McmfTierPenalty`, `McmfLeniency`, `McmfUnmetCost`, `McmfRecruiterOverhead`, and `McmfTransferOverhead` to `PartyThresholds` with defaults from the spec.

- [ ] **Step 2: Validate thresholds**

Validate non-negative integer costs and leniency in `[0, 1]`.

- [ ] **Step 3: Expose sliders**

Add threshold specs near logistics/recruitment settings in `WebUI/index.html`.

- [ ] **Step 4: Build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: build succeeds.

### Task 6: Verification And Handoff

**Files:**
- Modify: `docs/superpowers/specs/2026-05-18-supply-demand-mcmf-design.md` if implementation findings require clarifying notes.

- [ ] **Step 1: Run final build**

Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
Expected: build succeeds. Report any pre-existing warnings separately from new warnings.

- [ ] **Step 2: Review diff**

Run: `git diff --stat` and `git diff --check`
Expected: only intentional files changed; no whitespace errors.

- [ ] **Step 3: Report execution-layer limitation**

Final response must state that MCMF is now wired into execution, while prisoner conversion and non-capital recruiter sources remain on the existing paths until their dispatchers become instruction-scoped executors.

### Task 7: Execution Layer Cutover

**Files:**
- Modify: `SovereignTowns/src/Managers/CapitalLogisticsManager.cs`
- Modify: `SovereignTowns/src/Transfer/TransferTask.cs`
- Modify: `SovereignTowns/src/Transfer/TransferDispatcher.cs`
- Modify: `SovereignTowns/src/Algorithm/SupplyDemandGraph.cs`

- [ ] **Step 1: Account for in-flight parties by role**

Transfer parties should reduce destination demand through inbound projected counts. Do not subtract transfer outbound from source surplus because those troops have already been removed from the source garrison when the party was created.

- [ ] **Step 2: Add role-aware transfer execution**

Extend `TransferTask` with an optional `GenericTroopRole` and have `TransferDispatcher` filter extracted troops by that role.

- [ ] **Step 3: Preserve branch-demand recruitment behavior**

When branches are short and the capital itself is full, create a capital stockpile demand that only capital in-place/village recruitment sources may satisfy. This keeps recruiter dispatch from disappearing during the cutover.

- [ ] **Step 4: Replace old daily logistics decisions**

`CapitalLogisticsManager.EvaluateClan` should call `SupplyDemandGraph.Run` and execute returned instructions. Existing dispatcher safety checks remain authoritative.

- [ ] **Step 5: Normalize execution order**

Execute transfer instructions before recruitment, then aggregate in-place and recruiter instructions by settlement/town. This avoids same-day transshipment of freshly recruited troops and repeated recruiter dispatch from per-role flow edges.

- [ ] **Step 6: Remove distance hard limits**

Delete transfer max-distance and recruitment search-distance config paths. Distance remains a cost / scoring input, not a hard eligibility rule.
