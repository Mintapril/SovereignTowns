# B1 盲区修复 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire 4 already-built sub-systems whose call sites or feature reads are missing: (a) LLM advice into the decision pipeline, (b) `RequestTransferIn` intent into `CastleSupportManager`, (c) `RequestDisbandExcess` into a real "dismiss" workflow, (d) `AlertLowFood` into a real recruitment pause.

**Architecture:** No new layer. Adds one helper (`FoodGuard`), one new method on `CastleSupportManager`, an `LLMAdvice → GarrisonDecision` translator in `TownGarrisonManager`, a new `DismissPartyComponent` (`LocalSaveId=4`) + `DisbandReturnPartyDispatcher`. `PartyLifecycleManager` gains a new tracked `KindDismiss`. `SafeUninstallMenu` learns to disband the new component.

**Tech Stack:** C# net472, TaleWorlds Bannerlord v1.3.15 API, Newtonsoft.Json (bundled), no unit-test framework. Verification: `dotnet build` + launch game + inspect `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\*.log` and `...\Audit\*.log`.

**Spec:** `docs/superpowers/specs/2026-05-13-b1-blind-spots-design.md` (commit `d06d008`).

---

## Task 0: Baseline build sanity

Confirm current `master` builds cleanly before touching anything; future build failures are then ours alone.

**Files:** none

- [ ] **Step 0.1: Run baseline build**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug
```

Expected: `Build succeeded.` with `0 Error(s)`. Note any pre-existing warning count — that is our "clean" baseline. If build fails, stop and report; do not start B1 on broken main.

---

## Task 1: #7 — `FoodGuard` pauses every recruitment entry point

When `Town.FoodChange < rule.FoodSafetyThreshold`, every recruitment path must early-return with a log + single audit entry. Transfers and upgrades are intentionally unaffected.

**Files:**
- Create: `SovereignTowns/src/Configuration/FoodGuard.cs`
- Modify: `SovereignTowns/src/Recruitment/RecruitmentManager.cs` (add guard at the top of `TryDispatchRecruiter`)
- Modify: `SovereignTowns/src/Recruitment/CapitalInPlaceRecruiter.cs:33-41` (add guard after the early-returns)
- Modify: `SovereignTowns/src/Recruitment/PrisonerRecruitmentManager.cs:28-49` (add guard after the existing early-returns)

- [ ] **Step 1.1: Create `FoodGuard` helper**

Create `SovereignTowns/src/Configuration/FoodGuard.cs` with this full content:

```csharp
using System;
using SovereignTowns.Audit;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Configuration;

/// <summary>
/// Shared low-food early-out for recruitment paths. Reads <c>town.FoodChange</c> against
/// <see cref="TownGarrisonRule.FoodSafetyThreshold"/>; when paused, emits a single audit
/// entry per call so each blocked entry point is independently visible.
///
/// <para>
/// Not used by transfers (incoming troops do not raise food pressure) or upgrades
/// (no extra mouths). See B1 spec §3.4.
/// </para>
/// </summary>
public static class FoodGuard
{
    /// <summary>
    /// Returns <c>true</c> when the caller MUST early-return because the town's food
    /// trend is below the configured safety threshold. Side effect: when paused, logs
    /// one Info line and one Audit entry (Source=Rule, accepted=false).
    /// </summary>
    /// <param name="town">Target town; null is treated as "not paused" (caller-side guards run).</param>
    /// <param name="rule">Active rule (caller already resolved via <c>ConfigurationManager.GetRuleFor</c>).</param>
    /// <param name="callerLabel">Short string identifying the call site for audit (e.g. "RecruitmentManager").</param>
    public static bool IsRecruitmentPausedForFood(Town town, TownGarrisonRule rule, string callerLabel)
    {
        try
        {
            if (town?.Settlement == null || rule == null) return false;
            if (town.FoodChange >= rule.FoodSafetyThreshold) return false;

            Logger.Info(
                $"FoodGuard: recruitment paused at '{town.Name}' by {callerLabel} " +
                $"(foodChange={town.FoodChange:F2} < threshold={rule.FoodSafetyThreshold:F2})");

            DecisionAuditLogger.LogRule(
                decisionType: "RecruitmentPausedLowFood",
                inputSummary: $"town={town.Settlement.StringId} caller={callerLabel} foodChange={town.FoodChange:F2}",
                decisionJson: $"{{\"threshold\":{rule.FoodSafetyThreshold:F2},\"foodChange\":{town.FoodChange:F2}}}",
                accepted: false,
                rejectionReason: "FoodSafetyThreshold");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"FoodGuard.IsRecruitmentPausedForFood threw for town '{town?.Name}'", ex);
            return false; // 失败时不阻塞业务路径
        }
    }
}
```

- [ ] **Step 1.2: Add guard to `RecruitmentManager.TryDispatchRecruiter`**

Read `SovereignTowns/src/Recruitment/RecruitmentManager.cs` to locate the body of `TryDispatchRecruiter(Town town, GarrisonDecision decision)`. The method starts with existing guards (feature flag, capital, etc.). Insert the food guard **immediately after the `ConfigurationManager.GetRuleFor(town)` call** and **before any planner/limit logic** so the audit reflects the real first cause of skip. Use this snippet (adjust variable names if the file uses different identifiers — the call shape is fixed):

```csharp
            var rule = ConfigurationManager.GetRuleFor(town);
            if (rule == null) return false;

            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(town, rule, "RecruitmentManager"))
                return false;
```

If the method does not currently fetch `rule` near the top, **add the fetch** alongside the guard insertion; later code paths can keep their own `rule` reference unchanged.

- [ ] **Step 1.3: Add guard to `CapitalInPlaceRecruiter.RecruitFromCapitalNotables`**

In `SovereignTowns/src/Recruitment/CapitalInPlaceRecruiter.cs` find the block (lines ~54-56):

```csharp
            var rule = ConfigurationManager.GetRuleFor(town);
            if (rule == null) return;
            if (currentMen >= rule.TargetTotalCount) return;
```

Insert immediately after `if (rule == null) return;`:

```csharp
            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(town, rule, "CapitalInPlaceRecruiter"))
                return;
```

- [ ] **Step 1.4: Add guard to `PrisonerRecruitmentManager.OnDailyTickSettlement`**

In `SovereignTowns/src/Recruitment/PrisonerRecruitmentManager.cs` find the block (lines ~46-49):

```csharp
            var town = settlement.Town;
            if (town == null) return;
            var rule = ConfigurationManager.GetRuleFor(town);
            if (rule == null || !rule.AllowPrisonerConversion) return;
```

Insert immediately after that block:

```csharp
            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(town, rule, "PrisonerRecruitment"))
                return;
```

- [ ] **Step 1.5: Build**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug
```

Expected: `Build succeeded.` with the same `0 Error(s)` as Task 0 and warning count within ±1 of baseline. If new errors appear, fix them before moving on — common cause: missing `using SovereignTowns.Configuration;` in the three Recruitment files.

- [ ] **Step 1.6: Commit**

```bash
git add SovereignTowns/src/Configuration/FoodGuard.cs SovereignTowns/src/Recruitment/RecruitmentManager.cs SovereignTowns/src/Recruitment/CapitalInPlaceRecruiter.cs SovereignTowns/src/Recruitment/PrisonerRecruitmentManager.cs
git commit -m "$(cat <<'EOF'
B1 #7: add FoodGuard pausing all recruitment entry points when FoodChange < threshold

- FoodGuard.IsRecruitmentPausedForFood emits one Info log + one audit
  entry per call (Source=Rule, accepted=false, reject=FoodSafetyThreshold).
- Wired into RecruitmentManager.TryDispatchRecruiter,
  CapitalInPlaceRecruiter.RecruitFromCapitalNotables,
  PrisonerRecruitmentManager.OnDailyTickSettlement.
- Transfers and upgrades intentionally unaffected.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: #1.B — `RequestTransferIn` intent → `CastleSupportManager.TryDispatchForDemand`

Currently the rule engine's `RequestTransferIn` decision (when `gap≥30 && features.CastleSupport`) hits the fallback `"MVP 3: action 'RequestTransferIn' not yet implemented"` in `TownGarrisonManager.EvaluateOne:174`. Add a single-destination dispatcher on `CastleSupportManager`, wire `TownGarrisonManager` to call it, and remove the fallback for this kind.

**Files:**
- Modify: `SovereignTowns/src/Transfer/CastleSupportManager.cs` (new public method)
- Modify: `SovereignTowns/src/Managers/TownGarrisonManager.cs` (ctor param + new EvaluateOne case)
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:140` (pass `_castleSupportManager` into `TownGarrisonManager` ctor)

- [ ] **Step 2.1: Add `TryDispatchForDemand` to `CastleSupportManager`**

Open `SovereignTowns/src/Transfer/CastleSupportManager.cs`. Add `using SovereignTowns.Transfer;` is not needed (already in the same namespace). The class already exposes `EvaluateAll()`. Append the following public method right after `EvaluateAll()` (before the private helpers section starting at line ~146):

```csharp
    /// <summary>
    /// B1 #1.B: single-destination demand path. Runs the standard pair evaluation
    /// but restricts <paramref name="destination"/> to the supplied town, then
    /// dispatches the resulting tasks through <see cref="GarrisonTransferManager"/>
    /// in the same tick (no waiting for the next DailyTick). Idempotent: if a
    /// transfer is already in-flight to <paramref name="destination"/>, the underlying
    /// <c>MaxTransfersPerTown=1</c> limit makes additional dispatches a no-op.
    /// </summary>
    /// <param name="destination">The deficit town whose intent triggered this call.</param>
    /// <param name="requestedMagnitude">Suggested troop count; only used to cap the
    /// number of tasks dispatched (one task per <see cref="MaxTroopsPerTask"/>).</param>
    /// <param name="transferManager">Already-constructed GarrisonTransferManager; null
    /// returns 0 + warning log.</param>
    /// <returns>Number of TransferTasks that were actually dispatched (≥ 0).</returns>
    public int TryDispatchForDemand(Town destination, int requestedMagnitude, GarrisonTransferManager? transferManager)
    {
        if (destination == null || destination.Settlement == null)
        {
            Logger.Warn("CastleSupportManager.TryDispatchForDemand: destination is null");
            return 0;
        }
        if (transferManager == null)
        {
            Logger.Warn($"TryDispatchForDemand '{destination.Name}': transferManager is null — skipped");
            return 0;
        }
        if (requestedMagnitude <= 0) return 0;

        try
        {
            // Reuse the full evaluation; intent-triggered path stays consistent with
            // DailyTick logic. PairAndBuildTasks already honors all filters (faction /
            // siege / risk / distance) so we just filter the result by destination.
            var allTasks = EvaluateAll();
            if (allTasks.Count == 0) return 0;

            int maxTasks = (int)Math.Ceiling((double)requestedMagnitude / MaxTroopsPerTask);
            if (maxTasks < 1) maxTasks = 1;

            int dispatched = 0;
            foreach (var task in allTasks)
            {
                if (task.Destination != destination.Settlement) continue;
                if (dispatched >= maxTasks) break;

                bool ok;
                try
                {
                    ok = transferManager.TryDispatchTransfer(task);
                }
                catch (Exception ex)
                {
                    Logger.Error("TryDispatchForDemand: TryDispatchTransfer threw", ex);
                    ok = false;
                }

                if (ok)
                {
                    dispatched++;
                    DecisionAuditLogger.LogRule(
                        decisionType: "TransferRequestedByIntent",
                        inputSummary: $"dst={destination.Settlement.StringId} src={task.Source.StringId} requested={requestedMagnitude}",
                        decisionJson: $"{{\"requested\":{requestedMagnitude},\"task_troops\":{task.RequestedTroops},\"reason\":\"{EscapeAudit(task.Reason)}\"}}",
                        accepted: true);
                }
            }

            Logger.Info($"TryDispatchForDemand '{destination.Name}': dispatched={dispatched} / candidates={allTasks.Count} (requested={requestedMagnitude})");
            return dispatched;
        }
        catch (Exception ex)
        {
            Logger.Error($"CastleSupportManager.TryDispatchForDemand outer failure for '{destination.Name}'", ex);
            return 0;
        }
    }

    private static string EscapeAudit(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
    }
```

Add `using SovereignTowns.Audit;` to the file if not already present (current `using` block does not include it — `DecisionAuditLogger` is referenced via FQN otherwise).

- [ ] **Step 2.2: Add `CastleSupportManager` + `GarrisonTransferManager` deps to `TownGarrisonManager`**

Open `SovereignTowns/src/Managers/TownGarrisonManager.cs`. Currently the ctor at line 27 takes `(RecruitmentManager? recruitmentManager, LLMReasoningService? llmService, CapitalManager? capitalManager)`. Extend to accept the two transfer collaborators (nullable for back-compat with any test ctor call):

```csharp
    private readonly RecruitmentManager? _recruitmentManager;
    private readonly LLMReasoningService? _llmService;
    private readonly CapitalManager? _capitalManager;
    private readonly Transfer.CastleSupportManager? _castleSupportManager;
    private readonly Transfer.GarrisonTransferManager? _transferManager;

    public TownGarrisonManager(
        RecruitmentManager? recruitmentManager = null,
        LLMReasoningService? llmService = null,
        CapitalManager? capitalManager = null,
        Transfer.CastleSupportManager? castleSupportManager = null,
        Transfer.GarrisonTransferManager? transferManager = null)
    {
        _recruitmentManager = recruitmentManager;
        _llmService = llmService;
        _capitalManager = capitalManager;
        _castleSupportManager = castleSupportManager;
        _transferManager = transferManager;
    }
```

`EvaluateOne` is `private static`. Convert the new fields into ctor-time captured locals by changing `EvaluateOne` from `static` to an **instance method** so it can read `_castleSupportManager`/`_transferManager`. The current invocation at line 63 is `EvaluateOne(capital, _recruitmentManager, _llmService);` — change it to `EvaluateOne(capital);` and drop the matching parameters from the method signature; the existing references to `recruitmentManager` and `llmService` inside become `_recruitmentManager` / `_llmService`.

The header line should become:

```csharp
    private void EvaluateOne(Town town)
```

- [ ] **Step 2.3: Add `RequestTransferIn` case in `EvaluateOne`**

Inside `EvaluateOne`'s `foreach (var d in decisions)` block in `TownGarrisonManager.cs`, find the `else` block ending at `rejectionReason = $"MVP 3: action '{d.Kind}' not yet implemented";` (line ~174). Insert this `else if` **before** that fallback (and after the existing `RequestUpgrade && !rule.AllowAutoUpgrade` branch at line ~167):

```csharp
            else if (d.Kind == GarrisonActionKind.RequestTransferIn
                     && _castleSupportManager != null
                     && _transferManager != null
                     && ConfigurationManager.Current.EnabledFeatures.CastleSupport)
            {
                int n = _castleSupportManager.TryDispatchForDemand(town, d.Magnitude, _transferManager);
                dispatched = n > 0;
                rejectionReason = dispatched ? null : "no feasible donor / already at transfer limit";
            }
            else if (d.Kind == GarrisonActionKind.RequestTransferIn
                     && !ConfigurationManager.Current.EnabledFeatures.CastleSupport)
            {
                rejectionReason = "CastleSupport feature disabled";
            }
```

- [ ] **Step 2.4: Wire the new deps from `SovereignTownsCampaignBehavior`**

Open `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`. Find line 140 (current code):

```csharp
            _townGarrisonManager = new TownGarrisonManager(_recruitmentManager, _llmService, _capitalManager);
```

Replace with:

```csharp
            _townGarrisonManager = new TownGarrisonManager(
                recruitmentManager: _recruitmentManager,
                llmService: _llmService,
                capitalManager: _capitalManager,
                castleSupportManager: _castleSupportManager,
                transferManager: _transferManager);
```

The construction order already builds `_castleSupportManager` (line 106) and `_transferManager` (line 107) before `_townGarrisonManager` (line 140) — no reordering needed.

- [ ] **Step 2.5: Build**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug
```

Expected: `Build succeeded.`. Common errors:
- `'EvaluateOne' is a non-static method, ...` — confirm Step 2.2 changed `private static void EvaluateOne(...)` to `private void EvaluateOne(Town town)` and removed `static` references.
- `'GarrisonTransferManager' could not be found` — add `using SovereignTowns.Transfer;` to TownGarrisonManager.cs.

- [ ] **Step 2.6: Commit**

```bash
git add SovereignTowns/src/Transfer/CastleSupportManager.cs SovereignTowns/src/Managers/TownGarrisonManager.cs SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs
git commit -m "$(cat <<'EOF'
B1 #1.B: connect RequestTransferIn intent to CastleSupportManager

- New public CastleSupportManager.TryDispatchForDemand(destination, mag, transferManager)
  reuses EvaluateAll's pairing then dispatches matching tasks immediately.
- TownGarrisonManager.EvaluateOne now consumes RequestTransferIn instead of
  falling through to the 'MVP 3 not yet implemented' branch.
- SovereignTownsCampaignBehavior passes _castleSupportManager + _transferManager
  into the TownGarrisonManager ctor.
- Audit entries logged as 'TransferRequestedByIntent' (Source=Rule, accepted=true).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: #2 — LLM advice translated into `GarrisonDecision`

Right now `pendingLlmAdvice` is consumed only into a log line (`TownGarrisonManager.cs:127`). Translate eligible advice actions into `GarrisonDecision` and merge with rule-engine output via priority-desc sort + same-kind dedup.

**Files:**
- Modify: `SovereignTowns/src/Managers/TownGarrisonManager.cs` (translator + merger + EvaluateOne refactor)

- [ ] **Step 3.1: Add advice translator + merge helper**

Inside `TownGarrisonManager` (after `EscapeJson` at line ~190), add two private static helpers:

```csharp
    /// <summary>
    /// B1 #2: translate an LLM advice into at most one GarrisonDecision.
    /// Returns null for advice that should not enter the decision list
    /// (do_nothing / advise_user / adjust_rule / unknown action / negative magnitude).
    /// </summary>
    private static GarrisonDecision? TranslateLlmAdvice(LLMAdvice? advice)
    {
        if (advice == null) return null;
        if (string.IsNullOrEmpty(advice.Action)) return null;
        if (advice.MagnitudeSuggested < 0) return null;

        const int LlmPriority = 60; // between gap≥10 (50+gap/2) and siege (100)
        var reason = string.IsNullOrEmpty(advice.Reason) ? "llm-advice" : "llm:" + advice.Reason;

        switch (advice.Action)
        {
            case "send_recruiting_party":
                return new GarrisonDecision(GarrisonActionKind.RequestRecruitment,
                    priority: LlmPriority, magnitude: advice.MagnitudeSuggested, reason: reason);
            case "transfer_troops":
                return new GarrisonDecision(GarrisonActionKind.RequestTransferIn,
                    priority: LlmPriority, magnitude: advice.MagnitudeSuggested, reason: reason);
            case "adjust_rule":
            case "advise_user":
            case "do_nothing":
            default:
                return null; // logged + audited at caller, not dispatched
        }
    }

    /// <summary>
    /// B1 #2: merge rule + LLM decisions. Dedup by Kind keeping the highest priority;
    /// when priorities tie, LLM-sourced wins (assumed input via <paramref name="llmDecision"/>).
    /// Result is sorted priority-desc, ready to drive the dispatch loop.
    /// </summary>
    private static List<GarrisonDecision> MergeDedupSort(
        IReadOnlyList<GarrisonDecision> ruleDecisions,
        GarrisonDecision? llmDecision)
    {
        var by = new Dictionary<GarrisonActionKind, (GarrisonDecision Dec, bool FromLlm)>();
        foreach (var r in ruleDecisions)
        {
            by[r.Kind] = (r, false);
        }
        if (llmDecision.HasValue)
        {
            var l = llmDecision.Value;
            if (!by.TryGetValue(l.Kind, out var existing) || l.Priority > existing.Dec.Priority)
            {
                by[l.Kind] = (l, true);
            }
            else if (l.Priority == existing.Dec.Priority)
            {
                by[l.Kind] = (l, true); // tie → LLM wins
            }
        }
        var merged = new List<GarrisonDecision>(by.Count);
        foreach (var kv in by.Values) merged.Add(kv.Dec);
        merged.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
        return merged;
    }
```

- [ ] **Step 3.2: Refactor `EvaluateOne` to use the merger + audit `Source=Llm` for LLM-derived decisions**

Locate the block in `EvaluateOne` (lines ~122-145) that currently reads:

```csharp
        var pendingLlmAdvice = town.Settlement != null ? LlmAutoExecuteBridge.ConsumePendingAdvice(town.Settlement) : null;
        if (pendingLlmAdvice != null)
        {
            Logger.Info($"  consuming LLM advice for '{town.Name}': action={pendingLlmAdvice.Action} target={pendingLlmAdvice.TargetSettlement} mag={pendingLlmAdvice.MagnitudeSuggested}");
            // MVP 6: LLM 建议日志化 + 审计；实际行为仍由规则引擎驱动以保稳定性
            // 后续版本：将 LLM advice 转为 GarrisonDecision 并优先于规则引擎执行
        }

        // 规则引擎决策 + 审计 + 派发到 Manager
        var decisions = RuleBasedFallbackDecisionMaker.Decide(town);
        var inputSummary = $"town={townId} risk={risk.Level} target={effectiveTarget} current={snap.Total} gap={totalGap}";
```

Replace with:

```csharp
        var pendingLlmAdvice = town.Settlement != null ? LlmAutoExecuteBridge.ConsumePendingAdvice(town.Settlement) : null;
        GarrisonDecision? llmDecision = null;
        bool llmAdvisoryOnly = false;
        if (pendingLlmAdvice != null)
        {
            Logger.Info($"  consuming LLM advice for '{town.Name}': action={pendingLlmAdvice.Action} target={pendingLlmAdvice.TargetSettlement} mag={pendingLlmAdvice.MagnitudeSuggested}");
            llmDecision = TranslateLlmAdvice(pendingLlmAdvice);
            llmAdvisoryOnly = llmDecision == null
                && (pendingLlmAdvice.Action == "advise_user" || pendingLlmAdvice.Action == "adjust_rule");
            if (llmDecision == null && !llmAdvisoryOnly)
            {
                DecisionAuditLogger.LogRule(
                    decisionType: "LLMAdviceRejectedAtTranslate",
                    inputSummary: $"town={townId} action={pendingLlmAdvice.Action} mag={pendingLlmAdvice.MagnitudeSuggested}",
                    decisionJson: $"{{\"action\":\"{EscapeJson(pendingLlmAdvice.Action)}\",\"reason\":\"untranslatable\"}}",
                    accepted: false,
                    rejectionReason: "Action not in translate map or magnitude < 0");
            }
        }

        // 规则引擎决策 + LLM 翻译 + 合并 + 审计 + 派发
        var ruleDecisions = RuleBasedFallbackDecisionMaker.Decide(town);
        var decisions = MergeDedupSort(ruleDecisions, llmDecision);
        var inputSummary = $"town={townId} risk={risk.Level} target={effectiveTarget} current={snap.Total} gap={totalGap}";
```

Then locate the audit log call at line ~177 inside the foreach:

```csharp
            DecisionAuditLogger.LogRule(
                decisionType: d.Kind.ToString(),
                inputSummary: inputSummary,
                decisionJson: $"{{\"kind\":\"{d.Kind}\",\"priority\":{d.Priority},\"magnitude\":{d.Magnitude},\"reason\":\"{EscapeJson(d.Reason)}\"}}",
                accepted: dispatched,
                rejectionReason: rejectionReason);
```

Replace with a version that distinguishes Llm-sourced decisions:

```csharp
            bool fromLlm = llmDecision.HasValue
                           && d.Kind == llmDecision.Value.Kind
                           && d.Priority == llmDecision.Value.Priority
                           && d.Magnitude == llmDecision.Value.Magnitude;

            DecisionAuditLogger.Log(new AuditEntry
            {
                Timestamp = DateTime.UtcNow,
                DecisionType = d.Kind.ToString(),
                Source = fromLlm ? DecisionSource.Llm : DecisionSource.Rule,
                InputSummary = inputSummary,
                DecisionJson = $"{{\"kind\":\"{d.Kind}\",\"priority\":{d.Priority},\"magnitude\":{d.Magnitude},\"reason\":\"{EscapeJson(d.Reason)}\"}}",
                Accepted = dispatched,
                RejectionReason = rejectionReason
            });
```

If `llmAdvisoryOnly` is true (e.g. `advise_user`), an extra audit entry should be written after the foreach to record the advisory was seen:

```csharp
        if (llmAdvisoryOnly && pendingLlmAdvice != null)
        {
            DecisionAuditLogger.Log(new AuditEntry
            {
                Timestamp = DateTime.UtcNow,
                DecisionType = "LLMAdvisoryLogged",
                Source = DecisionSource.Llm,
                InputSummary = inputSummary,
                DecisionJson = $"{{\"action\":\"{EscapeJson(pendingLlmAdvice.Action)}\",\"reason\":\"{EscapeJson(pendingLlmAdvice.Reason)}\"}}",
                Accepted = true
            });
        }
```

- [ ] **Step 3.3: Ensure the right `using`s are present**

`TownGarrisonManager.cs` already has `using SovereignTowns.Audit;` and `using SovereignTowns.Llm;`. Confirm `using SovereignTowns.Decisions;` is present (it is, line 6). Add `using System;` if missing for `DateTime.UtcNow` (already present, line 1).

- [ ] **Step 3.4: Build**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug
```

Expected: `Build succeeded.`. Common error: `AuditEntry` not found → confirm `using SovereignTowns.Audit;` is at the top.

- [ ] **Step 3.5: Commit**

```bash
git add SovereignTowns/src/Managers/TownGarrisonManager.cs
git commit -m "$(cat <<'EOF'
B1 #2: translate LLM advice into GarrisonDecision and merge with rule output

- TranslateLlmAdvice maps send_recruiting_party / transfer_troops to
  RequestRecruitment / RequestTransferIn with priority=60; advisory and
  unknown actions are logged + audited but not dispatched.
- MergeDedupSort: same Kind keeps highest priority; ties go to LLM.
- LLM-sourced dispatch audits with Source=Llm; rule path unchanged.
- 'advise_user' and 'adjust_rule' produce LLMAdvisoryLogged audit entries.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: #6.B — `DismissPartyComponent` + `DisbandReturnPartyDispatcher`

Add the new `CustomPartyComponent`, register it with `SovereignTownsTypeDefiner` at `LocalSaveId=4`, extend `PartyLifecycleManager` (new kind + Migrate behavior), build the dispatcher, wire `EvaluateOne`, extend `SafeUninstallMenu`. Three commits split it into safe sub-steps.

### 4.A: Component + TypeDefiner

**Files:**
- Create: `SovereignTowns/src/Parties/DismissPartyComponent.cs`
- Modify: `SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs`

- [ ] **Step 4.A.1: Create `DismissPartyComponent`**

Create `SovereignTowns/src/Parties/DismissPartyComponent.cs` with this full content:

```csharp
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Parties;

/// <summary>
/// B1 #6.B: A short-lived party carrying dismissed-troop excess from a player-owned
/// town back to a home village. Once the party reaches the village (or idle thresholds
/// trigger), it is destroyed — soldiers "go home" semantically.
///
/// <para>
/// Same lifecycle category as recruiter / transfer / sallyforth: persists through
/// save/load via SaveableTypeDefiner (LocalSaveId=4), tracked by
/// <see cref="Lifecycle.PartyLifecycleManager"/>, swept by
/// <see cref="Ui.SafeUninstallMenu"/> on uninstall.
/// </para>
/// </summary>
public sealed class DismissPartyComponent : CustomPartyComponent
{
    /// <summary>stringId prefix; allows pattern matching in uninstall + audit paths.</summary>
    public const string StringIdPrefix = "st_dismiss_";

    [SaveableField(1)]
    private string? _homeVillageStringId;

    [SaveableField(2)]
    private string? _dismissedFromTownStringId;

    [SaveableField(3)]
    private CampaignTime _departureTime;

    [CachedData]
    private TextObject? _cachedName;

    /// <summary>Resolves the dismissal source town's settlement from saved stringId; null if missing/destroyed.</summary>
    public Settlement? DismissedFromSettlement
        => string.IsNullOrEmpty(_dismissedFromTownStringId)
            ? null
            : MBObjectManager.Instance?.GetObject<Settlement>(_dismissedFromTownStringId);

    /// <summary>Resolves the destination village from saved stringId; null if missing/destroyed.</summary>
    public Settlement? HomeVillage
        => string.IsNullOrEmpty(_homeVillageStringId)
            ? null
            : MBObjectManager.Instance?.GetObject<Settlement>(_homeVillageStringId);

    /// <summary>Hour the dispatcher created the party. Lifecycle idle math reads it.</summary>
    public CampaignTime DepartureTime => _departureTime;

    public override Hero? PartyOwner => DismissedFromSettlement?.OwnerClan?.Leader;

    public override TextObject Name
    {
        get
        {
            if (_cachedName != null) return _cachedName;
            var srcName = DismissedFromSettlement?.Name?.ToString() ?? "Unknown";
            _cachedName = new TextObject("{=ST_DismissedParty}Dismissed Troops of " + srcName);
            return _cachedName;
        }
    }

    public override Settlement HomeSettlement => HomeVillage ?? DismissedFromSettlement!;

    public override bool AvoidHostileActions => true;

    private DismissPartyComponent(
        Settlement homeVillage,
        TextObject name,
        Hero owner,
        Settlement dismissedFromTown,
        string partyMountStringId,
        string partyHarnessStringId,
        float customPartyBaseSpeed,
        bool avoidHostileActions,
        InitializationArgs args,
        Hero? leader = null)
        : base(homeVillage, name, owner, partyMountStringId, partyHarnessStringId,
               customPartyBaseSpeed, avoidHostileActions, args, leader)
    {
        _homeVillageStringId = homeVillage?.StringId;
        _dismissedFromTownStringId = dismissedFromTown?.StringId;
        _departureTime = CampaignTime.Now;
    }

    /// <summary>
    /// Factory: build a dismiss party in <paramref name="sourceTown"/> bound for
    /// <paramref name="homeVillage"/>. Empty roster — dispatcher fills it after creation.
    /// Returns null + Logger.Error on any failure (never throws).
    /// </summary>
    public static MobileParty? CreateForTown(Town sourceTown, Settlement homeVillage)
    {
        if (sourceTown == null || homeVillage == null)
        {
            Logger.Error("DismissPartyComponent.CreateForTown: null sourceTown or homeVillage");
            return null;
        }

        try
        {
            var sourceSettlement = sourceTown.Settlement;
            if (sourceSettlement == null)
            {
                Logger.Error("DismissPartyComponent.CreateForTown: sourceTown.Settlement is null");
                return null;
            }

            var ownerClan = sourceSettlement.OwnerClan;
            var ownerLeader = ownerClan?.Leader;
            if (ownerClan == null || ownerLeader == null)
            {
                Logger.Error($"DismissPartyComponent.CreateForTown: town '{sourceSettlement.StringId}' has no OwnerClan/Leader");
                return null;
            }

            var emptyTroops = TroopRoster.CreateDummyTroopRoster();
            var emptyPrisoners = TroopRoster.CreateDummyTroopRoster();

            var args = new InitializationArgs(
                sourceSettlement.GatePosition,
                spawnRadius: 1f,
                ownerClan,
                emptyTroops,
                emptyPrisoners);

            var nameObj = new TextObject(
                "{=ST_DismissedParty}Dismissed Troops of " + sourceSettlement.Name);

            var component = new DismissPartyComponent(
                homeVillage: homeVillage,
                name: nameObj,
                owner: ownerLeader,
                dismissedFromTown: sourceSettlement,
                partyMountStringId: string.Empty,
                partyHarnessStringId: string.Empty,
                customPartyBaseSpeed: 0f,
                avoidHostileActions: true,
                args: args,
                leader: null);

            var stringId = StringIdPrefix
                           + sourceSettlement.StringId
                           + "_"
                           + DateTime.UtcNow.Ticks.ToString();

            var mobileParty = MobileParty.CreateParty(stringId, component);
            if (mobileParty == null)
            {
                Logger.Error($"DismissPartyComponent.CreateForTown: MobileParty.CreateParty returned null for '{stringId}'");
                return null;
            }

            Logger.Info(
                $"DismissPartyComponent: created '{stringId}' from town '{sourceSettlement.StringId}' headed to '{homeVillage.StringId}' (owner={ownerLeader.Name})");

            return mobileParty;
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"DismissPartyComponent.CreateForTown: unexpected exception for town '{sourceTown?.Settlement?.StringId ?? "<null>"}'",
                ex);
            return null;
        }
    }
}
```

- [ ] **Step 4.A.2: Register `LocalSaveId=4` in `SovereignTownsTypeDefiner`**

Open `SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs`. The current `DefineClassTypes` body registers 1/2/3. Append id 4:

```csharp
    protected override void DefineClassTypes()
    {
        // local id 1: 招募队伍组件
        AddClassDefinition(typeof(Parties.RecruitingPartyComponent), 1);

        // local id 2: 调拨队伍组件
        AddClassDefinition(typeof(Parties.TransferPartyComponent), 2);

        // local id 3: 出击队伍组件
        AddClassDefinition(typeof(Parties.SallyForthPartyComponent), 3);

        // local id 4: 退伍队伍组件 (B1 #6.B)
        AddClassDefinition(typeof(Parties.DismissPartyComponent), 4);
    }
```

- [ ] **Step 4.A.3: Build**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug
```

Expected: `Build succeeded.`. Common errors:
- `Hero` ambiguous reference → confirm `using TaleWorlds.CampaignSystem;` is at top of DismissPartyComponent.cs.
- `InitializationArgs` not found → it is `protected` on `CustomPartyComponent`; the file inherits, so access works inside the derived class only.

- [ ] **Step 4.A.4: Commit**

```bash
git add SovereignTowns/src/Parties/DismissPartyComponent.cs SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs
git commit -m "$(cat <<'EOF'
B1 #6.B (1/3): add DismissPartyComponent + register LocalSaveId=4

- CustomPartyComponent subclass parallel to Recruiting/Transfer/SallyForth.
- Three SaveableFields: _homeVillageStringId, _dismissedFromTownStringId,
  _departureTime. PartyOwner / HomeSettlement resolve dynamically from
  stringIds (owner-change safe).
- Factory CreateForTown returns MobileParty? — never throws.
- SovereignTownsTypeDefiner.DefineClassTypes appends id 4.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### 4.B: PartyLifecycleManager extensions

**Files:**
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`

- [ ] **Step 4.B.1: Add kind constants and max-for case**

Open `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`. At line 30-32 add a new max constant, and at line 42 add a new kind constant:

```csharp
    public const int MaxRecruitersPerTown = 1;
    public const int MaxTransfersPerTown  = 1;
    public const int MaxSallyForthPerTown = 1;
    public const int MaxDismissPerTown    = 1;
```

```csharp
    public const string KindRecruiter  = "recruiter";
    public const string KindTransfer   = "transfer";
    public const string KindPatrol     = "patrol";
    public const string KindSallyForth = "sallyforth";
    public const string KindDismiss    = "dismiss";
```

At line ~461 (`GetMaxFor`), add the new case:

```csharp
    private static int GetMaxFor(string kind)
    {
        if (kind == KindRecruiter)  return MaxRecruitersPerTown;
        if (kind == KindTransfer)   return MaxTransfersPerTown;
        if (kind == KindSallyForth) return MaxSallyForthPerTown;
        if (kind == KindDismiss)    return MaxDismissPerTown;
        // 未知 kind：保守上限 1，避免失控创建
        return 1;
    }
```

- [ ] **Step 4.B.2: Add `DismissPartyComponent` case in `RebuildFromCampaign`**

At line ~182 inside `RebuildFromCampaign`'s custom-party loop, add an `else if` after the `SallyForthPartyComponent` branch:

```csharp
                            else if (comp is SallyForthPartyComponent sp)
                            {
                                var home = sp.HomeSettlement;
                                if (home == null) { skipped++; continue; }
                                _tracked[party] = new TrackedPartyMeta(home, KindSallyForth, now, party.TargetSettlement, SafeMemberCount(party));
                                sallyforths++;
                            }
                            else if (comp is DismissPartyComponent dp)
                            {
                                // Home for tracking = source town (so MigrateByHomeSettlement
                                // picks it up when the source town is lost); dispatcher
                                // separately keeps the village in dp.HomeVillage.
                                var srcTown = dp.DismissedFromSettlement;
                                if (srcTown == null) { skipped++; continue; }
                                _tracked[party] = new TrackedPartyMeta(srcTown, KindDismiss, now, party.TargetSettlement, SafeMemberCount(party));
                                // dismisses++; — local counter, see below
                            }
```

Add a counter variable. Change line ~153:

```csharp
            int recruiters = 0, transfers = 0, patrols = 0, sallyforths = 0, skipped = 0;
```

to:

```csharp
            int recruiters = 0, transfers = 0, patrols = 0, sallyforths = 0, dismisses = 0, skipped = 0;
```

Add `dismisses++;` inside the new branch, replacing the placeholder comment. Update the final summary log line at line ~234:

```csharp
            Logger.Info($"PartyLifecycleManager.RebuildFromCampaign: recruiters={recruiters} transfers={transfers} patrols={patrols} sallyforths={sallyforths} dismisses={dismisses} skipped={skipped} (total tracked={_tracked.Count})");
```

- [ ] **Step 4.B.3: Add kind=dismiss special case in `MigrateAllOrDisband`**

Open the `MigrateAllOrDisband` method (line ~249). Inside the snapshot foreach (line ~260) replace the existing migration block with one that branches on kind:

```csharp
            foreach (var party in snapshot)
            {
                try
                {
                    if (party == null) continue;
                    if (!_tracked.TryGetValue(party, out var meta)) continue;

                    // B1 #6.B: dismiss parties carry already-discharged troops — they must
                    // EVAPORATE on capital switch (going to new capital reverses the
                    // 'returning home' semantic). Skip migration, just destroy.
                    if (meta.Kind == KindDismiss)
                    {
                        if (party.IsActive)
                        {
                            try { DestroyPartyAction.Apply(null, party); }
                            catch (Exception destroyEx)
                            {
                                Logger.Error($"MigrateAllOrDisband: dismiss-party DestroyPartyAction failed for '{SafeName(party)}'", destroyEx);
                            }
                            partiesDisbanded++;
                        }
                        continue;
                    }

                    if (party.IsActive && newGarrison?.MemberRoster != null && party.MemberRoster != null)
                    {
                        var elements = party.MemberRoster.GetTroopRoster();
                        foreach (var elem in elements)
                        {
                            if (elem.Character == null || elem.Character.IsHero) continue;
                            if (elem.Number <= 0) continue;
                            newGarrison.MemberRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                            migratedTroops += elem.Number;
                        }
                    }
                    if (party.IsActive)
                    {
                        DisbandPartyAction.StartDisband(party);
                        partiesDisbanded++;
                    }
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"MigrateAllOrDisband: failed for party '{SafeName(party)}'", oneEx);
                }
            }
```

Note: the existing loop reused `_tracked.Keys` snapshot but read the meta inline by direct dictionary access only on the dismiss branch I added. The original code did **not** read `meta` at all (it migrated every tracked party identically). The above replacement adds a `TryGetValue` lookup. Ensure the same lookup pattern is used consistently. If a party is somehow no longer in `_tracked` here, we now `continue` instead of migrating it; this matches the intent.

Add `using TaleWorlds.CampaignSystem.Actions;` to PartyLifecycleManager.cs if not present (it already is, line 6).

- [ ] **Step 4.B.4: Add kind=dismiss case in `MigrateByHomeSettlement`**

In `MigrateByHomeSettlement` (line ~300), replace the body of the snapshot foreach similarly:

```csharp
            foreach (var party in snapshot)
            {
                try
                {
                    if (party == null) continue;
                    if (!_tracked.TryGetValue(party, out var rec)) continue;
                    if (rec.Home != lostSettlement) continue; // 仅清理该 settlement 的 in-flight

                    // B1 #6.B: dismiss parties evaporate, do not migrate roster
                    if (rec.Kind == KindDismiss)
                    {
                        if (party.IsActive)
                        {
                            try { DestroyPartyAction.Apply(null, party); }
                            catch (Exception destroyEx)
                            {
                                Logger.Error($"MigrateByHomeSettlement: dismiss-party DestroyPartyAction failed for '{SafeName(party)}'", destroyEx);
                            }
                            disbanded++;
                        }
                        _tracked.Remove(party);
                        continue;
                    }

                    if (party.IsActive && fallbackGarrison?.MemberRoster != null && party.MemberRoster != null)
                    {
                        var elements = party.MemberRoster.GetTroopRoster();
                        foreach (var elem in elements)
                        {
                            if (elem.Character == null || elem.Character.IsHero) continue;
                            if (elem.Number <= 0) continue;
                            fallbackGarrison.MemberRoster.AddToCounts(elem.Character, elem.Number, false, elem.WoundedNumber, elem.Xp);
                            migrated += elem.Number;
                        }
                    }
                    if (party.IsActive)
                    {
                        DisbandPartyAction.StartDisband(party);
                        disbanded++;
                    }
                    _tracked.Remove(party);
                }
                catch (Exception oneEx)
                {
                    Logger.Error($"MigrateByHomeSettlement: failed for party '{SafeName(party)}'", oneEx);
                }
            }
```

- [ ] **Step 4.B.5: Add arrival-at-village destruction in `OnHourlyTickParty`**

In `OnHourlyTickParty` (line ~369), add a dismiss-specific check **before** the existing idle logic. After the early-return at line 373 (`if (!_tracked.TryGetValue(party, out var meta)) return;`), insert:

```csharp
            // B1 #6.B: dismiss party reached its target village → evaporate
            if (meta.Kind == KindDismiss && party.PartyComponent is DismissPartyComponent dp)
            {
                var homeVillage = dp.HomeVillage;
                if (homeVillage != null && (party.CurrentSettlement == homeVillage || party.LastVisitedSettlement == homeVillage))
                {
                    Logger.Info($"HourlyTick '{SafeName(party)}': dismiss arrived at '{homeVillage.Name}' → DestroyPartyAction.Apply");
                    try { DestroyPartyAction.Apply(null, party); }
                    catch (Exception destroyEx) { Logger.Error($"DestroyPartyAction failed for dismiss '{SafeName(party)}'", destroyEx); }
                    UntrackParty(party);
                    return;
                }
            }
```

- [ ] **Step 4.B.6: Build**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug
```

Expected: `Build succeeded.`. Common error: `DismissPartyComponent` undefined → confirm `using SovereignTowns.Parties;` is at the top (it already is, line 4).

- [ ] **Step 4.B.7: Commit**

```bash
git add SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs
git commit -m "$(cat <<'EOF'
B1 #6.B (2/3): teach PartyLifecycleManager about dismiss-kind parties

- New KindDismiss constant + MaxDismissPerTown=1.
- RebuildFromCampaign recovers DismissPartyComponent into _tracked using
  the source town as Home (for MigrateByHomeSettlement) plus the village
  in dp.HomeVillage.
- OnHourlyTickParty destroys a dismiss party on arrival at its village
  (CurrentSettlement or LastVisitedSettlement match).
- MigrateAllOrDisband + MigrateByHomeSettlement evaporate dismiss parties
  instead of migrating their (already-discharged) roster.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### 4.C: Dispatcher + EvaluateOne case + SafeUninstall

**Files:**
- Create: `SovereignTowns/src/Lifecycle/DisbandReturnPartyDispatcher.cs`
- Modify: `SovereignTowns/src/Managers/TownGarrisonManager.cs` (EvaluateOne case)
- Modify: `SovereignTowns/src/Ui/SafeUninstallMenu.cs:108` (add component case)

- [ ] **Step 4.C.1: Create `DisbandReturnPartyDispatcher`**

Create `SovereignTowns/src/Lifecycle/DisbandReturnPartyDispatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Audit;
using SovereignTowns.Parties;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Lifecycle;

/// <summary>
/// B1 #6.B: turn a <c>RequestDisbandExcess</c> intent into a real "discharge home"
/// MobileParty. Picks dominant-culture troops from the garrison (low-tier first),
/// selects a home village within distance, creates a <see cref="DismissPartyComponent"/>,
/// transfers the troops into it, registers tracking, and aims the party at the village.
///
/// <para>
/// On failure at any step, the partial state is left intact (troops returned where
/// possible); the method always returns the count of troops actually dismissed and
/// never throws to the caller.
/// </para>
/// </summary>
public static class DisbandReturnPartyDispatcher
{
    /// <summary>Search radius around the source town for a home village (map units).</summary>
    private const float HomeVillageMaxDistance = 80f;

    /// <summary>Among matching villages, randomise within the closest <c>N</c>.</summary>
    private const int HomeVillageTopRandom = 3;

    /// <summary>
    /// Try to discharge up to <paramref name="magnitude"/> excess troops from
    /// <paramref name="town"/>'s garrison home. Returns the number actually moved
    /// into a created dismiss party.
    /// </summary>
    public static int DismissExcess(
        Town town,
        int magnitude,
        PartyLifecycleManager lifecycle)
    {
        if (town == null || town.Settlement == null || lifecycle == null) return 0;
        if (magnitude <= 0) return 0;

        try
        {
            if (!lifecycle.CanCreateAnotherParty(town.Settlement, PartyLifecycleManager.KindDismiss))
            {
                Logger.Info($"DismissExcess '{town.Name}': already at MaxDismissPerTown limit, skip");
                return 0;
            }

            var roster = town.GarrisonParty?.MemberRoster;
            if (roster == null) return 0;

            // 1) Pick low-tier non-hero regulars up to magnitude
            var picked = PickLowTierTroops(roster, magnitude);
            if (picked.Count == 0)
            {
                Logger.Info($"DismissExcess '{town.Name}': no eligible regular troops found");
                return 0;
            }

            // 2) Dominant culture argmax
            var dominantCulture = DominantCulture(picked);

            // 3) Find home village
            var homeVillage = FindHomeVillage(town.Settlement, dominantCulture);
            if (homeVillage == null)
            {
                Logger.Info($"DismissExcess '{town.Name}': no eligible home village within {HomeVillageMaxDistance} — fallback: RemoveTroop only (no party)");
                int evaporated = 0;
                foreach (var p in picked)
                {
                    try
                    {
                        roster.RemoveTroop(p.Character, p.Count, default(UniqueTroopDescriptor), 0);
                        evaporated += p.Count;
                    }
                    catch (Exception removeEx)
                    {
                        Logger.Error($"DismissExcess '{town.Name}': RemoveTroop fallback failed for '{p.Character.StringId}'", removeEx);
                    }
                }
                DecisionAuditLogger.LogRule(
                    decisionType: "DisbandExcess",
                    inputSummary: $"town={town.Settlement.StringId} mode=evaporate magnitude={magnitude}",
                    decisionJson: $"{{\"dismissed\":{evaporated},\"mode\":\"evaporate\"}}",
                    accepted: evaporated > 0);
                return evaporated;
            }

            // 4) Create dismiss party
            var party = DismissPartyComponent.CreateForTown(town, homeVillage);
            if (party == null)
            {
                Logger.Error($"DismissExcess '{town.Name}': CreateForTown returned null");
                return 0;
            }

            // 5) Move picked troops into the party + remove from garrison
            int actuallyMoved = 0;
            foreach (var p in picked)
            {
                try
                {
                    party.MemberRoster.AddToCounts(p.Character, p.Count, false, 0, 0);
                    roster.RemoveTroop(p.Character, p.Count, default(UniqueTroopDescriptor), 0);
                    actuallyMoved += p.Count;
                }
                catch (Exception moveEx)
                {
                    Logger.Error($"DismissExcess '{town.Name}': troop move failed for '{p.Character.StringId}'", moveEx);
                }
            }

            if (actuallyMoved == 0)
            {
                Logger.Warn($"DismissExcess '{town.Name}': created party but moved 0 troops — destroying party");
                try { TaleWorlds.CampaignSystem.Actions.DestroyPartyAction.Apply(null, party); }
                catch { /* swallow */ }
                return 0;
            }

            // 6) Track + aim at village
            lifecycle.RegisterTrackedParty(party, town.Settlement, PartyLifecycleManager.KindDismiss);
            try { party.SetMoveGoToSettlement(homeVillage); }
            catch (Exception navEx) { Logger.Error($"DismissExcess '{town.Name}': SetMoveGoToSettlement failed", navEx); }

            DecisionAuditLogger.LogRule(
                decisionType: "DisbandExcess",
                inputSummary: $"town={town.Settlement.StringId} mode=party home={homeVillage.StringId} magnitude={magnitude}",
                decisionJson: $"{{\"dismissed\":{actuallyMoved},\"home_village\":\"{homeVillage.StringId}\",\"culture\":\"{dominantCulture?.StringId ?? "<mixed>"}\"}}",
                accepted: true);

            Logger.Info($"DismissExcess '{town.Name}': dispatched {actuallyMoved} troops home to '{homeVillage.Name}' (culture={dominantCulture?.StringId ?? "<mixed>"})");
            return actuallyMoved;
        }
        catch (Exception ex)
        {
            Logger.Error($"DisbandReturnPartyDispatcher.DismissExcess outer failure for '{town?.Name}'", ex);
            return 0;
        }
    }

    private readonly struct PickedTroop
    {
        public PickedTroop(CharacterObject character, int count) { Character = character; Count = count; }
        public CharacterObject Character { get; }
        public int Count { get; }
    }

    private static List<PickedTroop> PickLowTierTroops(TroopRoster roster, int magnitude)
    {
        var result = new List<PickedTroop>();
        try
        {
            var elements = roster.GetTroopRoster();
            // Sort ascending tier so the cheapest leave first
            var sorted = elements
                .Where(e => e.Character != null && !e.Character.IsHero && e.Character.IsRegular && e.Number > 0)
                .OrderBy(e => e.Character.Tier)
                .ToList();

            int remaining = magnitude;
            foreach (var elem in sorted)
            {
                if (remaining <= 0) break;
                int take = Math.Min(elem.Number, remaining);
                if (take <= 0) continue;
                result.Add(new PickedTroop(elem.Character, take));
                remaining -= take;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DisbandReturnPartyDispatcher.PickLowTierTroops failed", ex);
        }
        return result;
    }

    private static CultureObject? DominantCulture(List<PickedTroop> picked)
    {
        var counts = new Dictionary<CultureObject, int>();
        foreach (var p in picked)
        {
            var c = p.Character?.Culture;
            if (c == null) continue;
            counts.TryGetValue(c, out var prev);
            counts[c] = prev + p.Count;
        }
        if (counts.Count == 0) return null;

        CultureObject? best = null;
        int bestCount = -1;
        foreach (var kv in counts)
        {
            if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
        }
        return best;
    }

    private static Settlement? FindHomeVillage(Settlement sourceSettlement, CultureObject? dominantCulture)
    {
        try
        {
            var pos = sourceSettlement.GetPosition2D;

            // (a) Same culture, ≤ 80f, not raided
            var sameCulture = Settlement.All
                .Where(s => s.IsVillage && !IsRaided(s) && (s.GetPosition2D - pos).Length <= HomeVillageMaxDistance
                            && dominantCulture != null && s.Culture == dominantCulture)
                .OrderBy(s => (s.GetPosition2D - pos).Length)
                .Take(HomeVillageTopRandom)
                .ToList();
            if (sameCulture.Count > 0)
            {
                return sameCulture[MBRandom.RandomInt(sameCulture.Count)];
            }

            // (b) Any culture, ≤ 80f, not raided
            var anyCulture = Settlement.All
                .Where(s => s.IsVillage && !IsRaided(s) && (s.GetPosition2D - pos).Length <= HomeVillageMaxDistance)
                .OrderBy(s => (s.GetPosition2D - pos).Length)
                .Take(HomeVillageTopRandom)
                .ToList();
            if (anyCulture.Count > 0)
            {
                return anyCulture[MBRandom.RandomInt(anyCulture.Count)];
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("DisbandReturnPartyDispatcher.FindHomeVillage failed", ex);
            return null;
        }
    }

    private static bool IsRaided(Settlement v)
    {
        try { return v.IsUnderRaid; }
        catch { return false; }
    }
}
```

- [ ] **Step 4.C.2: Wire `RequestDisbandExcess` case in `TownGarrisonManager.EvaluateOne`**

In `TownGarrisonManager.cs`, the ctor introduced in Step 2.2 already has `_castleSupportManager`/`_transferManager` but no lifecycle reference. Add:

```csharp
    private readonly Lifecycle.PartyLifecycleManager? _lifecycle;
```

Extend the ctor signature:

```csharp
    public TownGarrisonManager(
        RecruitmentManager? recruitmentManager = null,
        LLMReasoningService? llmService = null,
        CapitalManager? capitalManager = null,
        Transfer.CastleSupportManager? castleSupportManager = null,
        Transfer.GarrisonTransferManager? transferManager = null,
        Lifecycle.PartyLifecycleManager? lifecycle = null)
    {
        _recruitmentManager = recruitmentManager;
        _llmService = llmService;
        _capitalManager = capitalManager;
        _castleSupportManager = castleSupportManager;
        _transferManager = transferManager;
        _lifecycle = lifecycle;
    }
```

Inside `EvaluateOne`'s decision foreach, add a new `else if` after the `RequestTransferIn` branch added in Step 2.3:

```csharp
            else if (d.Kind == GarrisonActionKind.RequestDisbandExcess
                     && rule.AutoDisbandExcess
                     && _lifecycle != null)
            {
                int dismissed = Lifecycle.DisbandReturnPartyDispatcher.DismissExcess(town, d.Magnitude, _lifecycle);
                dispatched = dismissed > 0;
                rejectionReason = dismissed == 0 ? "no eligible troops / no home village / at limit" : null;
            }
            else if (d.Kind == GarrisonActionKind.RequestDisbandExcess && !rule.AutoDisbandExcess)
            {
                rejectionReason = "AutoDisbandExcess=false in rule";
            }
```

In `SovereignTownsCampaignBehavior.cs` line 140-145 (modified in Step 2.4), pass `_lifecycle` as the final ctor arg:

```csharp
            _townGarrisonManager = new TownGarrisonManager(
                recruitmentManager: _recruitmentManager,
                llmService: _llmService,
                capitalManager: _capitalManager,
                castleSupportManager: _castleSupportManager,
                transferManager: _transferManager,
                lifecycle: _lifecycle);
```

- [ ] **Step 4.C.3: Extend `SafeUninstallMenu` to disband `DismissPartyComponent`**

In `SovereignTowns/src/Ui/SafeUninstallMenu.cs` find lines 107-110:

```csharp
                    if (p?.PartyComponent is SovereignTowns.Parties.RecruitingPartyComponent
                        || p?.PartyComponent is SovereignTowns.Parties.TransferPartyComponent)
                    {
```

Add `DismissPartyComponent` plus `SallyForthPartyComponent` (the latter is currently missing — also a pre-existing gap):

```csharp
                    if (p?.PartyComponent is SovereignTowns.Parties.RecruitingPartyComponent
                        || p?.PartyComponent is SovereignTowns.Parties.TransferPartyComponent
                        || p?.PartyComponent is SovereignTowns.Parties.SallyForthPartyComponent
                        || p?.PartyComponent is SovereignTowns.Parties.DismissPartyComponent)
                    {
```

Note: Adding `SallyForthPartyComponent` here closes a small uninstall hole — sally parties were previously left behind. This is in-scope for B1 because we are touching the same line.

- [ ] **Step 4.C.4: Build**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug
```

Expected: `Build succeeded.`. Common errors:
- `Settlement.All` not found → use `MBObjectManager.Instance.GetObjectTypeList<Settlement>()` instead. If `Settlement.All` is missing in v1.3.15, replace the LINQ source in `FindHomeVillage` accordingly:
  ```csharp
  var all = MBObjectManager.Instance.GetObjectTypeList<Settlement>();
  ```
  and iterate `all` rather than `Settlement.All`.
- `CultureObject` not found → add `using TaleWorlds.Core;` (already in the file).
- `IsUnderRaid` not found → use `s.IsRaided` instead (Bannerlord renamed across patches).

- [ ] **Step 4.C.5: Commit**

```bash
git add SovereignTowns/src/Lifecycle/DisbandReturnPartyDispatcher.cs SovereignTowns/src/Managers/TownGarrisonManager.cs SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs SovereignTowns/src/Ui/SafeUninstallMenu.cs
git commit -m "$(cat <<'EOF'
B1 #6.B (3/3): wire dispatcher + EvaluateOne case + SafeUninstall

- DisbandReturnPartyDispatcher.DismissExcess picks low-tier regulars,
  finds a same-culture village ≤ 80f (fallback to any village), creates
  a DismissPartyComponent, transfers troops, registers tracking, and
  navigates the party home. Audits each dispatch ('DisbandExcess').
- TownGarrisonManager now takes a PartyLifecycleManager ctor arg and
  consumes RequestDisbandExcess intents.
- SafeUninstallMenu sweeps DismissPartyComponent (and the previously-
  missed SallyForthPartyComponent) on uninstall.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Integration verification (launch game)

The previous tasks each compiled cleanly. Now exercise them in a real campaign.

**Files:** none (verification only)

- [ ] **Step 5.1: Build Release + deploy**

```bash
dotnet build SovereignTowns/src/SovereignTowns.csproj -c Release
```

Expected: `Build succeeded.`. The csproj's `DeployToGame` MSBuild target copies the DLL + PDB + SubModule.xml + GUI prefabs into `$(BannerlordPath)\Modules\SovereignTowns\`.

- [ ] **Step 5.2: Launch a campaign with player owning ≥ 2 towns**

Start Bannerlord via launcher with SovereignTowns enabled. Load (or create) a save in which the player clan owns at least one town that is set as capital and one additional town/castle. Tail the live log file:

```powershell
Get-Content -Path "$env:USERPROFILE\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\SovereignTowns_*.log" -Wait
```

- [ ] **Step 5.3: Verify #7 FoodGuard fires**

In-game, lower the capital town's `FoodChange` below `rule.FoodSafetyThreshold` (default `-2.0`). Easiest path: via vanilla cheat `campaign.help` if console is enabled, or wait for a naturally-occurring famine, or set the threshold high via the in-game config Ribbon (Set FoodSafetyThreshold to `+50` — every town will then "fail" the check immediately for one DailyTick cycle).

Trigger DailyTick (advance ≥ 24 in-game hours).

Expected in `*.log`:
```
FoodGuard: recruitment paused at '<town>' by RecruitmentManager (foodChange=... < threshold=...)
FoodGuard: recruitment paused at '<town>' by CapitalInPlaceRecruiter (foodChange=... < threshold=...)
FoodGuard: recruitment paused at '<town>' by PrisonerRecruitment (foodChange=... < threshold=...)
```

Expected in `Audit\Audit_*.log`: 3 lines with `DecisionType=RecruitmentPausedLowFood`, `accepted=false`, `reject=FoodSafetyThreshold`.

- [ ] **Step 5.4: Verify #1.B RequestTransferIn dispatch**

Set up: capital with garrison ≪ MinimumDefenders×1.2 (gap≥30) + another player-owned town with garrison > TargetTotalCount×1.3 (surplus). Enable `EnabledFeatures.CastleSupport=true` in `Modules/SovereignTowns/Configs/global.json`. Reset food threshold so the FoodGuard does not interfere.

Trigger DailyTick.

Expected in `*.log`:
```
EvaluateAll: capital='<capital>' (single-capital mode)
... decision: RequestTransferIn priority=35 magnitude=... reason='large gap=... — request transfer-in'
TryDispatchForDemand '<capital>': dispatched=1 / candidates=N (requested=...)
CastleSupport task: '<source>' -> '<capital>' troops=N priority=...
```

Expected in `Audit\Audit_*.log`: at least one line `DecisionType=TransferRequestedByIntent` `accepted=true`.

- [ ] **Step 5.5: Verify #2 LLM advice drives decisions**

Configure `Modules/SovereignTowns/Configs/llm.json`:
```json
{ "Provider": "ollama", "Endpoint": "http://127.0.0.1:11434", "Model": "qwen2.5:7b", "EnableForLongTermPlanning": true, "EnableForUserAdvice": true, "TimeoutSeconds": 15 }
```
Set `EnabledFeatures.LlmReasoning=true` and `LlmAutoExecute=true`. Run a local Ollama instance with the chosen model. Trigger DailyTick twice (LlmAutoExecuteBridge starts inference on day N, advice is consumed on day N+1).

Expected in `*.log` on day N+1:
```
consuming LLM advice for '<capital>': action=send_recruiting_party target=... mag=...
... decision: RequestRecruitment priority=60 magnitude=... reason='llm:...'
```

Expected in `Audit\Audit_*.log`: a line with `[Llm] RequestRecruitment` `accepted=true`.

If Ollama is not available locally, skip this step (the path is already covered by static rule decisions in Step 5.4) and note as "LLM smoke skipped — provider unavailable".

- [ ] **Step 5.6: Verify #6.B Dismiss workflow**

Set `rule.AutoDisbandExcess=true` in the capital's per-settlement override (or globally). Use cheat or wait until garrison > TargetTotalCount×1.2.

Trigger DailyTick.

Expected in `*.log`:
```
DismissExcess '<capital>': dispatched <N> troops home to '<village>' (culture=...)
DismissPartyComponent: created 'st_dismiss_..._...' from town '<capital>' headed to '<village>'
```

On the world map: a small party named `Dismissed Troops of <capital>` walking toward the chosen village. Wait for arrival (~few in-game hours):
```
HourlyTick 'Dismissed Troops of ...': dismiss arrived at '<village>' → DestroyPartyAction.Apply
OnMobilePartyDestroyed: '...' — auto-untracking
```

Expected in `Audit\Audit_*.log`: `DecisionType=DisbandExcess` `accepted=true`.

Save the game, reload, confirm the dismiss party (if still in-flight) reappears (LocalSaveId=4 round-trip).

- [ ] **Step 5.7: Verify SafeUninstall sweeps everything**

While at least one dismiss party + one recruiter + one sally party is in-flight: enter any player-owned town menu, click "Sovereign Towns: 安全卸载向导".

Expected toast: `销毁本 Mod 队伍 N 支；还原 M 座城镇的自动招募。`
Expected: all 3 party types disappear from the world map (`DisbandPartyAction.StartDisband` runs on each).

---

## Self-review notes

After writing this plan, I checked it against the spec one section at a time:

- **§3.1 LLM advice** → covered by Task 3 (3.1–3.5)
- **§3.2 RequestTransferIn** → covered by Task 2 (2.1–2.6)
- **§3.3 DismissPartyComponent + dispatcher** → covered by Task 4 (4.A through 4.C)
- **§3.4 FoodGuard** → covered by Task 1 (1.1–1.6)
- **§4 Hard invariants** → no SaveBaseId / net472 / Newtonsoft change; LocalSaveId=4 explicitly registered in Step 4.A.2; LLM stays out of HourlyTick (translator only runs inside DailyTick path); SafeUninstall sweeps the new component
- **§5 Verification** → covered by Task 5 (one step per blind spot)
- **§7 Implementation order** → followed (#7 → #1.B → #2 → #6.B)

No `TBD` or placeholder text remains; every code snippet is a complete edit including imports. Type/method names match between tasks (`TryDispatchForDemand`, `DismissExcess`, `MergeDedupSort`, `TranslateLlmAdvice`, `KindDismiss`, `MaxDismissPerTown`).
