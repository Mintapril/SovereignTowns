# Central Garrison Dispatcher — Unified Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

> **This plan supersedes `2026-05-21-fiscal-autonomy.md`** — it merges the fiscal-autonomy treasury work with the central garrison dispatcher into one task sequence. Do not execute the old plan.

**Goal:** A per-clan central dispatcher decides every managed settlement's garrison size (capital + towns + castles) by maximizing defensive value under the clan's affordable wage budget. Money flows through a per-clan treasury so the town military self-funds. Players cannot set garrison targets by default; an opt-in manual mode lets them, with a dispatcher assessment.

**Architecture:** `ClanTreasury` (L1) holds gold + a 7-day expense ring. `STClanFinanceModel : DefaultClanFinanceModel` (L1 GameModel, player clan only) reroutes managed-settlement income + garrison wage into the treasury. The dispatcher runs two single-commodity MCMF passes per clan per day: **Pass A** (`GarrisonAllocationSolver`, new) allocates the wage budget across settlements by a convex per-settlement value function → per-settlement target headcount; **Pass B** (`SupplyDemandGraph`, existing routing MCMF) fills those targets. `CapitalLogisticsManager` orchestrates both. A peacetime disband-excess step sheds unaffordable garrison.

**Reference specs:**
- `SovereignTowns/docs/superpowers/specs/2026-05-21-fiscal-autonomy-design.md` (treasury, finance model, war buffer, loop-closure §2)
- `SovereignTowns/docs/superpowers/specs/2026-05-21-central-garrison-dispatcher-design.md` (the two-pass dispatcher, value function, manual mode)

**Tech Stack:** C# net472, Bannerlord v1.3.15 API, Newtonsoft.Json, Gauntlet UI, Alpine.js. No Harmony — `STClanFinanceModel` is a GameModel registered via `AddModel`.

**Verification model:** No unit tests (`CLAUDE.md`). Every code task ends with `dotnet build` (0 errors). Runtime verified once at the end — Task 10.

**Build command (from workspace root `C:\Users\rangt\Desktop\workspace`):**
```
dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug
```

**Line references** are anchor-based ("locate X, insert after"); each task's first step on an existing file is to read it and find the anchor.

**Persistence note:** ST custom state persists as a JSON string in `SovereignTownsCampaignBehavior.SyncData` (`st_capitals_json`), NOT via the Saveable-class system. The treasury follows that pattern (`st_treasuries_json`) — no `SovereignTownsTypeDefiner` change, no new `LocalSaveId`.

---

## Task 1: `FiscalAutonomyConfig` + config plumbing

Holds both the treasury knobs and the dispatcher/value-function knobs. Mirrors the `BuildingBonusConfig` pattern.

**Files:** Create `src/Configuration/FiscalAutonomyConfig.cs`; modify `GlobalConfig.cs`, `ConfigurationManager.cs`, `WebConfig/WebConfigEndpoints.cs`.

- [ ] **Step 1: Create `FiscalAutonomyConfig.cs`**

```csharp
namespace SovereignTowns.Configuration;

/// <summary>财政自治 + 中央驻军调度器配置。详见两份设计文档。</summary>
public sealed class FiscalAutonomyConfig
{
    // ── 金库 / 预算 ──
    public float GarrisonWageBudgetRatio { get; set; } = 0.55f;   // 驻军工资上限占可持续收入比例
    public int   TreasuryBufferDays      { get; set; } = 30;      // 缓冲金上限 = 本值 × 近7日均开销
    public bool  PlayerClanSubsidyWhenTreasuryEmpty { get; set; } = true;

    // ── 遣散超额 ──
    public int   MinGarrisonFloor        { get; set; } = 40;
    public float DisbandExcessThreshold  { get; set; } = 1.2f;
    public bool  DisbandUnaffordableExcess { get; set; } = true;

    // ── 手动模式 ──
    public bool  AllowManualGarrisonTargets { get; set; } = false;

    // ── 分配 MCMF 价值函数 (设计文档 central-garrison-dispatcher §4) ──
    public int   ValueFloorBase          { get; set; } = 1000;
    public int   ValueCoreBase           { get; set; } = 100;
    public int   SurplusEdgeCost         { get; set; } = 1;       // 必须严格 > 0
    public int   AdequateBase            { get; set; } = 60;
    public int   AdequateProsperityDivisor { get; set; } = 80;
    public int   AdequateThreatWeight    { get; set; } = 8;
    public int   CoreTierCount           { get; set; } = 5;       // core 段离散子层数
    public int   MaxGarrisonHardCap      { get; set; } = 400;
}
```

- [ ] **Step 2: Add `FiscalAutonomy` to `GlobalConfig`** — read `GlobalConfig.cs`, after the `BuildingBonus` property add `public FiscalAutonomyConfig FiscalAutonomy { get; set; } = new FiscalAutonomyConfig();`; add `FiscalAutonomy = new FiscalAutonomyConfig(),` to `CreateDefault()`; mirror the `BuildingBonus` line in `Clone()` if present.

- [ ] **Step 3: `ConfigurationManager`** — bump `CurrentConfigVersion` by 1 (expected 20→21); in `TryLoadFromDisk` after `parsed.BuildingBonus ??= ...` add `parsed.FiscalAutonomy ??= new FiscalAutonomyConfig();`.

- [ ] **Step 4: `ValidateFiscalAutonomy`** — call it from `ValidateConfig` after the `ValidateBuildingBonus` call; add the method:

```csharp
    private static bool ValidateFiscalAutonomy(FiscalAutonomyConfig f, out string reason)
    {
        if (f.GarrisonWageBudgetRatio < 0.1f || f.GarrisonWageBudgetRatio > 1.0f)
        { reason = $"FiscalAutonomy.GarrisonWageBudgetRatio invalid ({f.GarrisonWageBudgetRatio}); [0.1,1.0]"; return false; }
        if (f.TreasuryBufferDays < 0 || f.TreasuryBufferDays > 120)
        { reason = $"FiscalAutonomy.TreasuryBufferDays invalid ({f.TreasuryBufferDays}); [0,120]"; return false; }
        if (f.MinGarrisonFloor < 0 || f.MinGarrisonFloor > 500)
        { reason = $"FiscalAutonomy.MinGarrisonFloor invalid ({f.MinGarrisonFloor}); [0,500]"; return false; }
        if (f.DisbandExcessThreshold < 1.0f || f.DisbandExcessThreshold > 3.0f)
        { reason = $"FiscalAutonomy.DisbandExcessThreshold invalid ({f.DisbandExcessThreshold}); [1.0,3.0]"; return false; }
        if (f.SurplusEdgeCost < 1 || f.SurplusEdgeCost > 1000)
        { reason = $"FiscalAutonomy.SurplusEdgeCost invalid ({f.SurplusEdgeCost}); [1,1000]"; return false; }
        if (f.CoreTierCount < 1 || f.CoreTierCount > 20)
        { reason = $"FiscalAutonomy.CoreTierCount invalid ({f.CoreTierCount}); [1,20]"; return false; }
        if (f.MaxGarrisonHardCap < f.MinGarrisonFloor || f.MaxGarrisonHardCap > 2000)
        { reason = $"FiscalAutonomy.MaxGarrisonHardCap invalid ({f.MaxGarrisonHardCap}); [MinGarrisonFloor,2000]"; return false; }
        if (f.AdequateBase < f.MinGarrisonFloor || f.AdequateBase > f.MaxGarrisonHardCap)
        { reason = $"FiscalAutonomy.AdequateBase must be in [MinGarrisonFloor,MaxGarrisonHardCap]"; return false; }
        if (f.ValueFloorBase <= f.ValueCoreBase)
        { reason = $"FiscalAutonomy.ValueFloorBase must exceed ValueCoreBase (floor must dominate core)"; return false; }
        reason = "";
        return true;
    }
```

- [ ] **Step 5: `WebConfigEndpoints.PutConfig`** — after `parsed.BuildingBonus ??= ...` add `parsed.FiscalAutonomy ??= new FiscalAutonomyConfig();`.

- [ ] **Step 6: Build** → 0 errors. **Step 7: Commit** `feat(config): add FiscalAutonomyConfig (treasury + dispatcher knobs), bump ConfigVersion`.

---

## Task 2: `ClanTreasury` + `CapitalManager.Treasury` + persistence

**Files:** Create `src/Economy/ClanTreasury.cs`; modify `Capital/CapitalManager.cs`, `Capital/CapitalRegistry.cs`, `Campaign/SovereignTownsCampaignBehavior.cs`.

- [ ] **Step 1: Create `ClanTreasury.cs`**

```csharp
using System;

namespace SovereignTowns.Economy;

/// <summary>
/// 单个受管氏族的金库。瞬态;余额 + 近 7 日实际开销环经 SyncData 的 st_treasuries_json 持久化。
/// 余额钳制 ≥ 0 —— 赤字由调用方按设计 §3.5 兜底。
/// </summary>
public sealed class ClanTreasury
{
    private long _balance;
    private readonly long[] _expenseByDay = new long[7];
    private int _dayCursor;

    public long Balance => _balance;

    public void Credit(long amount) { if (amount > 0) _balance += amount; }

    /// <summary>扣款。开销全额计入 7 日环;余额不足只扣到 0,返回欠款。</summary>
    public long Debit(long amount)
    {
        if (amount <= 0) return 0;
        _expenseByDay[_dayCursor] += amount;
        long shortfall = amount > _balance ? amount - _balance : 0;
        _balance -= (amount - shortfall);
        return shortfall;
    }

    public bool CanAfford(long amount) => amount <= 0 || _balance >= amount;

    public void RollDay() { _dayCursor = (_dayCursor + 1) % 7; _expenseByDay[_dayCursor] = 0; }

    public long TrailingDailyExpense()
    {
        long sum = 0; foreach (var d in _expenseByDay) sum += d; return sum / 7;
    }

    public long BufferCap(int bufferDays)
        => Math.Max(0, bufferDays) * Math.Max(0L, TrailingDailyExpense());

    public long SkimAboveBufferCap(int bufferDays)
    {
        long cap = BufferCap(bufferDays);
        if (_balance <= cap) return 0;
        long overflow = _balance - cap; _balance = cap; return overflow;
    }

    // 持久化形式: "balance;d0,..,d6;cursor"
    public string Serialize() => $"{_balance};{string.Join(",", _expenseByDay)};{_dayCursor}";

    public static ClanTreasury Deserialize(string? s)
    {
        var t = new ClanTreasury();
        try
        {
            if (string.IsNullOrEmpty(s)) return t;
            var p = s!.Split(';');
            if (p.Length >= 1) long.TryParse(p[0], out t._balance);
            if (p.Length >= 2)
            {
                var d = p[1].Split(',');
                for (int i = 0; i < 7 && i < d.Length; i++) long.TryParse(d[i], out t._expenseByDay[i]);
            }
            if (p.Length >= 3) int.TryParse(p[2], out t._dayCursor);
            if (t._dayCursor < 0 || t._dayCursor > 6) t._dayCursor = 0;
        }
        catch { /* → 空金库 */ }
        return t;
    }
}
```

- [ ] **Step 2: `CapitalManager.Treasury`** — mirror the `_patrolScheduler` field+property pattern. Add `using SovereignTowns.Economy;`, a `private ClanTreasury _treasury = new ClanTreasury();`, `public ClanTreasury Treasury => _treasury;`, and `public void RestoreTreasuryFrom(string? s) => _treasury = ClanTreasury.Deserialize(s);`.

- [ ] **Step 3: `CapitalRegistry`** — add `ExportTreasuries()` (mirror `ExportCapitals`, value = `mgr.Treasury.Serialize()`), a `private Dictionary<string,string>? _pendingTreasuries;` field, `RestoreTreasuries(dict)`. In `EnsureForClan`, before `mgr.Initialize()` inject `if (_pendingTreasuries != null && _pendingTreasuries.TryGetValue(clan.StringId, out var ts)) mgr.RestoreTreasuryFrom(ts);`. Clear `_pendingTreasuries = null;` at end of `Initialize`.

- [ ] **Step 4: `SovereignTownsCampaignBehavior.SyncData`** — add a `st_treasuries_json` block mirroring `st_capitals_json` (export `ExportTreasuries()`, parse into a `_pendingTreasuries` field). In `OnSessionLaunched`, after `_capitalRegistry.RestoreCapitals(...)`, add `_capitalRegistry.RestoreTreasuries(_pendingTreasuries); _pendingTreasuries = null;`.

- [ ] **Step 5: Build** → 0 errors. **Step 6: Commit** `feat(economy): per-clan ClanTreasury with SyncData persistence`.

---

## Task 3: `STClanFinanceModel` — reroute income + garrison wage

**Files:** Create `src/Models/STClanFinanceModel.cs`; modify `SovereignTownsSubModule.cs`.

- [ ] **Step 1: Create `STClanFinanceModel.cs`** — subclass `DefaultClanFinanceModel`, override `CalculateClanGoldChange`. Player clan only. Let `base` run, then recompute managed-fief income + garrison wage **read-only** (`applyWithdrawals=false` — `base` already mutated `TradeTaxAccumulated`/`PartyTradeGold`), route into the treasury, return only buffered overflow to clan gold.

```csharp
using System;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Models;

public sealed class STClanFinanceModel : DefaultClanFinanceModel
{
    private static readonly TextObject _line = new TextObject("主权城镇金库结算");

    public override ExplainedNumber CalculateClanGoldChange(
        Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
    {
        var en = base.CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);
        try
        {
            if (clan == null || clan != Clan.PlayerClan) return en;
            var reg = CapitalRegistry.Instance;
            if (reg == null || !reg.IsManagedClan(clan)) return en;
            var treasury = reg.GetForClan(clan)?.Treasury;
            if (treasury == null) return en;

            long income = 0, wage = 0;
            foreach (var fief in clan.Fiefs)
            {
                if (fief?.Settlement == null) continue;
                income += SafeTownIncome(clan, fief);
                var gp = fief.GarrisonParty;
                if (gp != null && gp.IsActive) wage += Math.Max(0, gp.TotalWage);
            }
            int bufferDays = ConfigurationManager.Current?.FiscalAutonomy?.TreasuryBufferDays ?? 30;
            long overflow, shortfall;
            if (applyWithdrawals)
            {
                treasury.RollDay();
                treasury.Credit(income);
                shortfall = treasury.Debit(wage);
                overflow = treasury.SkimAboveBufferCap(bufferDays);
            }
            else
            {
                long projected = treasury.Balance + income - wage;
                long cap = treasury.BufferCap(bufferDays);
                overflow = projected > cap ? projected - cap : 0;
                shortfall = projected < 0 ? -projected : 0;
            }
            en.Add(-income + wage + overflow - shortfall, _line);
        }
        catch (Exception ex) { Logger.Error("STClanFinanceModel failed; fell back to base", ex); }
        return en;
    }

    /// <summary>受管领地总收入(税+关税+村庄+项目),只读重算,绝不抛。公开供 Finance 视图复用。</summary>
    public long SafeTownIncome(Clan clan, TaleWorlds.CampaignSystem.Settlements.Town fief)
    {
        try
        {
            long sum = (long)Campaign.Current.Models.SettlementTaxModel.CalculateTownTax(fief).ResultNumber;
            sum += (long)CalculateTownIncomeFromTariffs(clan, fief, false).ResultNumber;
            sum += CalculateTownIncomeFromProjects(fief);
            if (fief.Villages != null)
                foreach (var v in fief.Villages) sum += CalculateVillageIncome(clan, v, false);
            return Math.Max(0, sum);
        }
        catch (Exception ex) { Logger.Error($"SafeTownIncome failed '{fief?.Settlement?.StringId}'", ex); return 0; }
    }
}
```

> **Review-critical.** Verify against decompiled `DefaultClanFinanceModel` (`_research/Vanilla/`): `CalculateTownIncomeFromTariffs/VillageIncome/Projects` are `public override` — call directly; recompute MUST use `applyWithdrawals=false`; treasury mutated ONLY when `applyWithdrawals=true`.

- [ ] **Step 2: Register in `OnGameStart`** — in `SovereignTownsSubModule.cs` AddModel block, after `STVolunteerModel` add `campaignStarter.AddModel(new SovereignTowns.Models.STClanFinanceModel());`; update the log line to `Registered 5 ST GameModels`.

- [ ] **Step 3: Build** → 0 errors. **Step 4: Commit** `feat(economy): STClanFinanceModel reroutes player-clan income + garrison wage to treasury`.

---

## Task 4: `ModTreasury` reroute to `ClanTreasury`

`ModTreasury` charges `Hero.MainHero` today; reroute to the clan treasury.

**Files:** modify `Economy/ModTreasury.cs`, `Recruitment/RecruitmentDispatcher.cs`, `Recruitment/CapitalInPlaceRecruiter.cs`, `Recruitment/BranchInPlaceRecruiter.cs`, `Upgrades/TroopUpgradeService.cs`, `Parties/StPartyComponent.cs`.

- [ ] **Step 1: Add a `Clan` parameter to `ModTreasury.CanAfford/Charge/Refund`.**
  - Resolve `treasury = CapitalRegistry.Instance?.GetForClan(clan)?.Treasury`.
  - `CanAfford(clan, amount)` → `treasury?.CanAfford(amount) ?? false`.
  - `Charge(clan, cat, amount, note)`: if `treasury == null` keep the current `Hero.MainHero` fallback; else if treasury short AND `PlayerClanSubsidyWhenTreasuryEmpty == false` → refuse (return false); else `shortfall = treasury.Debit(amount)`, charge `shortfall` (if >0) to `Hero.MainHero`; full `amount` → `ModExpenseLedger` + audit as today.
  - `Refund(clan, cat, amount, note)` → `treasury?.Credit(amount)`; ledger records `-amount`.
  - Keep every method's outer try/catch and return-false semantics.

- [ ] **Step 2: Update the 5 call sites** — pass the clan (already in hand: `town.OwnerClan` / `homeTown.OwnerClan` / `home.OwnerClan`): `RecruitmentDispatcher.TryDispatchRecruiter`, `CapitalInPlaceRecruiter`, `BranchInPlaceRecruiter`, `TroopUpgradeService.TryUpgradeGarrison` (charge + failure-path `Refund`), `StPartyComponent.TrySeedAndBuyInitialFood` (+ rollback `Refund` / `TryRefundOnDestroy`). Grep `ModTreasury.Charge|CanAfford|Refund` to confirm every site.

- [ ] **Step 3: Build** → 0 errors (compiler flags missed sites via arg-count). **Step 4: Commit** `feat(economy): ModTreasury charges the clan treasury instead of player gold`.

---

## Task 5: `GarrisonAllocationSolver` — Pass A allocation MCMF

The central decision. Per clan: allocate the wage budget across settlements by a convex per-settlement value function → per-settlement target headcount. Spec: `central-garrison-dispatcher-design.md` §3-§4.

**Files:** Create `src/Algorithm/GarrisonAllocationSolver.cs`.

- [ ] **Step 1: Create `GarrisonAllocationSolver.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Evaluators;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Algorithm;

/// <summary>Pass A 分配结果:每定居点目标头数 + 价值分解(日志/评估用)。</summary>
public sealed class GarrisonAllocationResult
{
    public Dictionary<Settlement, int> Target { get; } = new();
    public Dictionary<Settlement, string> Breakdown { get; } = new();
}

/// <summary>
/// 中央驻军调度器 Pass A:分配 MCMF。把氏族工资预算按"防御价值"分配到各城/堡。
/// 单商品(头数)、凸费用层、复用 MinCostFlow。设计文档 central-garrison-dispatcher §3-§4。
/// </summary>
public static class GarrisonAllocationSolver
{
    public static GarrisonAllocationResult Solve(CapitalManager manager)
    {
        var result = new GarrisonAllocationResult();
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null) return result;
            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();

            var towns = clan.Fiefs.Where(t => t?.Settlement != null && t.Settlement.IsActive).ToList();
            if (towns.Count == 0) return result;

            // 预算(头数)。clanWageBudget 见 helper;wagePerTroop 取保守满级。
            int wagePerTroop = Math.Max(1, WagePerTroopAtMaxTier(towns));
            long clanWageBudget = ClanWageBudget(manager, towns, cfg);
            int budgetCap = (int)Math.Min(int.MaxValue, clanWageBudget / wagePerTroop);
            if (budgetCap <= 0) return result;

            var graph = new MinCostFlow();
            int next = 1;
            int superSource = next++, budgetNode = next++, unspentNode = next++, superSink = next++;
            graph.AddEdge(superSource, budgetNode, budgetCap, 0);
            graph.AddEdge(budgetNode, unspentNode, budgetCap, 0);   // 未花预算,零损失
            graph.AddEdge(unspentNode, superSink, budgetCap, 0);

            // 每定居点价值层 → 记录 (settlement, tierNode, headcountSpan)
            var tierOwner = new Dictionary<int, Settlement>();
            foreach (var t in towns)
            {
                var s = t.Settlement;
                int floor = Math.Max(0, cfg.MinGarrisonFloor);
                int adequate = AdequateFor(t, cfg);
                int hardCap = HardCapFor(t, cfg);
                float threat = ThreatWeight(s);
                float strat = StrategicWeight(s);

                // floor 层
                if (floor > 0)
                {
                    int n = next++; tierOwner[n] = s;
                    int cost = -(int)Math.Round(cfg.ValueFloorBase * threat * strat);
                    graph.AddEdge(budgetNode, n, floor, cost);
                    graph.AddEdge(n, superSink, floor, 0);
                }
                // core 层:离散成 K 子层,凸递减
                int coreSpan = Math.Max(0, adequate - floor);
                if (coreSpan > 0)
                {
                    int K = Math.Max(1, cfg.CoreTierCount);
                    for (int k = 0; k < K; k++)
                    {
                        int cap = (coreSpan * (k + 1) / K) - (coreSpan * k / K);
                        if (cap <= 0) continue;
                        float dim = 1.0f - 0.8f * ((k + 0.5f) / K);   // 1.0→0.2
                        int cost = -(int)Math.Round(cfg.ValueCoreBase * dim * threat * strat);
                        int n = next++; tierOwner[n] = s;
                        graph.AddEdge(budgetNode, n, cap, cost);
                        graph.AddEdge(n, superSink, cap, 0);
                    }
                }
                // surplus 层:严格正费用 → MCMF 宁可不花
                int surplusSpan = Math.Max(0, hardCap - adequate);
                if (surplusSpan > 0)
                {
                    int n = next++; tierOwner[n] = s;
                    graph.AddEdge(budgetNode, n, surplusSpan, Math.Max(1, cfg.SurplusEdgeCost));
                    graph.AddEdge(n, superSink, surplusSpan, 0);
                }
                result.Breakdown[s] = $"threat={threat:F1} strat={strat:F2} floor={floor} adequate={adequate} hardCap={hardCap}";
            }

            var flow = graph.Solve(superSource, superSink);
            foreach (var t in towns) result.Target[t.Settlement] = 0;
            foreach (var kv in flow.EdgeFlows)
            {
                if (kv.Value <= 0) continue;
                if (tierOwner.TryGetValue(kv.Key.To, out var s))
                    result.Target[s] = result.Target.TryGetValue(s, out var c) ? c + kv.Value : kv.Value;
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("GarrisonAllocationSolver.Solve failed", ex);
            return result;
        }
    }

    // —— helper 契约(实现见说明) ——
    // ClanWageBudget(mgr,towns,cfg): GarrisonWageBudgetRatio × Σ(税+关税);
    //   atWar && treasury.Balance>0 → max(常规, Σ configWageBill)。复用 STClanFinanceModel 收入口径。
    // WagePerTroopAtMaxTier(towns) : Campaign.Models.PartyWageModel.GetCharacterWage(满级代表兵种)。
    // AdequateFor(t,cfg)           : clamp(AdequateBase + Prosperity/AdequateProsperityDivisor
    //                                + round(NearbyLandThreatIntensity × AdequateThreatWeight),
    //                                MinGarrisonFloor, HardCapFor(t)).
    // HardCapFor(t,cfg)            : vanilla GarrisonParty.PartySizeLimit,取不到 → MaxGarrisonHardCap。
    // ThreatWeight(s)              : RiskAssessmentService.Assess(s).Level → Safe .5/Low 1/Med 2/High 4/Crit 8。
    // StrategicWeight(s)           : (s.IsTown&&isCapital?1.3:1.0) × clamp(Prosperity/4000,0.5,1.5)。
}
```

> **Review-critical.** Confirm `MinCostFlow` API matches `SupplyDemandGraph`'s usage (`AddEdge(from,to,cap,cost)`, `Solve(src,sink)`, `flow.EdgeFlows` keyed by `{From,To}`). `MakeStubTroop` is `private` in `GarrisonPowerEvaluator` — make it `internal` or duplicate a 5-line tier→`CharacterObject` lookup. `NearbyLandThreatIntensity` is on the settlement/risk path used by `RiskAssessmentService` — reuse it. Keep every helper exception-safe.

- [ ] **Step 2: Build** → 0 errors. **Step 3: Commit** `feat(dispatcher): GarrisonAllocationSolver — Pass A allocation MCMF`.

---

## Task 6: Pass A → Pass B integration + manual mode

**Files:** modify `Algorithm/SupplyDemandGraph.cs`, `Managers/CapitalLogisticsManager.cs`; create `src/Algorithm/GarrisonAssessment.cs`.

- [ ] **Step 1: Create `GarrisonAssessment.cs`** — a small DTO:
```csharp
namespace SovereignTowns.Algorithm;
public sealed class GarrisonAssessment
{
    public string SettlementId = "";
    public int PlayerTarget;
    public int RecommendedTarget;
    public int DailyWageDelta;          // (player - recommended) × wagePerTroop
    public bool LoopClosesAtPlayerTarget;
}
```

- [ ] **Step 2: Wire Pass A into `CapitalLogisticsManager.EvaluateClan`** — read it; before `RunMcmf`, call `GarrisonAllocationSolver.Solve(manager)` once. Pass the result into the graph build. If `AllowManualGarrisonTargets == false` → the result's per-settlement target is authoritative. If `true` → build `GarrisonAssessment` per settlement (player target from `TownGarrisonRule.TargetTotalCount` / `BranchRule.TargetPower`, recommended from Pass A), stash for the UI (a static `LatestAssessments` dict keyed by clan, like `SettlementsSnapshot`), and let routing use the player targets.

- [ ] **Step 3: `SupplyDemandGraph.BuildSettlementStates`** — accept the Pass A result. Replace `ComputeDesiredTarget(rule, risk)`:
  - auto mode: capital `DesiredTotal` = Pass A target; branch `DesiredPower` = `headsToPower(Pass A target)`.
  - manual mode: capital `DesiredTotal` = `min(TargetTotalCount × multiplier, hardCap)`; branch `DesiredPower` = `min(BranchRule.TargetPower, headsToPower(hardCap))`.
  - `IsOtherOwnedBranch` nodes unchanged.
  `headsToPower` = heads × `GarrisonPowerEvaluator` reference per-troop power.

- [ ] **Step 4: Build** → 0 errors. **Step 5: Commit** `feat(dispatcher): wire Pass A allocation into routing; manual-mode assessment`.

---

## Task 7: Disband-excess + war buffer/downgrade

**Files:** modify `Managers/CapitalLogisticsManager.cs`, `Upgrades/TroopUpgradeService.cs` (or its caller).

- [ ] **Step 1: Peacetime disband-excess** — after `ExecuteMcmfInstructions` in `EvaluateClan`, for each managed settlement: skip if `DisbandUnaffordableExcess == false`, OR `AllowManualGarrisonTargets == true` (manual mode disables disband — the player chose to over-garrison), OR under siege, OR risk ≥ High (peacetime only). Else if `current > affordableTarget × DisbandExcessThreshold` (affordableTarget = Pass A result), remove `current - affordableTarget` lowest-Tier non-hero troops from `GarrisonParty.MemberRoster` — reuse the existing low-Tier extraction routine (recruiter-escort extraction). Log + audit.

- [ ] **Step 2: Pause upgrades when buffer dry + at war** — in the daily path calling `TroopUpgradeService.TryUpgradeGarrison`, gate: if clan at war AND `treasury.Balance <= 0`, skip the upgrade call (spec §3.5 —降级优先于裁员). Put the `IsBufferExhaustedAtWar(manager)` check as a shared helper on `GarrisonAllocationSolver` or a small static.

- [ ] **Step 3: Build** → 0 errors. **Step 4: Commit** `feat(dispatcher): peacetime disband-excess; pause upgrades when war buffer dry`.

---

## Task 8: Single config-spec source (architecture cleanup #7)

Done **before** Task 9 so the new config group is added once, not twice.

**Files:** modify `WebConfig/WebConfigEndpoints.cs` (+ `WebConfigServer.cs` routing), `Ui/ControlPanel/ControlPanelSpecs.cs`, `SovereignTowns/WebUI/index.html`.

- [ ] **Step 1: Audit** — list every knob in `ControlPanelSpecs.cs` and in `index.html`'s spec getters; produce the union. `ControlPanelSpecs.cs` must end as the superset (single source). Known drift: `PatrolMaxLifetimeHours` is WebUI-only — fold into `ControlPanelSpecs.cs`.
- [ ] **Step 2: `/api/specs` endpoint** — `WebConfigEndpoints` adds a token-gated `GET /api/specs` serializing `ControlPanelSpecs.AllGroups` (key/labels/hints/specs root/key/min/max/step/discrete/def/advanced) to JSON; register the route in `WebConfigServer.cs`.
- [ ] **Step 3: `index.html` consumes `/api/specs`** — replace the hard-coded spec getters + `settingsGroups` group list with a `fetch('/api/specs?t=...')` on load → `specGroups` data field; the existing generic group/spec render loop iterates it. Keep the ratio-normalization JS (behaviour, not spec data).
- [ ] **Step 4: Build** → 0 errors (WebUI has no build step). **Step 5: Commit** `refactor(ui): single config-spec source via /api/specs`.

---

## Task 9: UI — config group, hidden manual knobs, assessment, Finance tab

`ControlPanelSpecs.cs` is now the single source (Task 8), so the group is added once.

**Files:** modify `Ui/ControlPanel/ControlPanelSpecs.cs`, `Ui/ControlPanel/Tabs/StrategyTabVM.cs` (or wherever `TargetTotalCount` renders), `Ui/ControlPanel/Tabs/FinanceTabVM.cs`, `WebConfig/WebConfigEndpoints.cs`.

- [ ] **Step 1: `fiscal_autonomy` SpecGroup** — add a `SpecGroup` with `Root="FiscalAutonomy"` and `SpecEntry` for the user-facing knobs (`GarrisonWageBudgetRatio`, `MinGarrisonFloor`, `TreasuryBufferDays`, `DisbandExcessThreshold`, `DisbandUnaffordableExcess`, `PlayerClanSubsidyWhenTreasuryEmpty`, `AllowManualGarrisonTargets`). The value-function tunables (`ValueCoreBase` etc.) go in an `Advanced=true` group.
- [ ] **Step 2: Gate the manual-target knobs** — `TargetTotalCount` / `BranchRule.TargetPower` rows: when `AllowManualGarrisonTargets == false`, hide or disable them and show the dispatcher's computed target instead. When `true`, show them editable.
- [ ] **Step 3: Assessment view** — when `AllowManualGarrisonTargets == true`, surface `GarrisonAssessment` per settlement (player vs recommended target, daily wage delta, loop-closes verdict) in the Strategy or Finance tab + WebUI.
- [ ] **Step 4: Finance tab + `/api/finance`** — add treasury balance, buffer cap, trailing daily expense, per-settlement P&L (income vs garrison wage, recommended vs current). Source from `ClanTreasury` + `GarrisonAllocationSolver` + `STClanFinanceModel.SafeTownIncome`.
- [ ] **Step 5: Build** → 0 errors. **Step 6: Commit** `feat(ui): fiscal autonomy + dispatcher config, assessment view, Finance P&L`.

---

## Task 10: In-game verification

No automated tests — verify at runtime per `CLAUDE.md`. Logs at `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\`.

- [ ] **1.** Confirm `DeployToGame` copied the build. Launch a managed-clan campaign owning ≥1 town + ≥1 castle.
- [ ] **2. Treasury** — daily log shows Credit (income) / Debit (wage) / `SkimAboveBufferCap` overflow; balance never negative.
- [ ] **3. Pass A allocation** — log prints per-settlement value breakdown (threat/strategic/floor/adequate) + target; a low-prosperity town gets a smaller target; a besieged town keeps its target (high threat → high value).
- [ ] **4. Castle funding** — a border castle (high threat) draws a real garrison from the clan budget despite ~0 own income; a safe interior castle settles near `MinGarrisonFloor`.
- [ ] **5. Disband-excess** — a conquered over-garrisoned town in peacetime sheds low-Tier troops; in war it does NOT; in manual mode it does NOT.
- [ ] **6. War** — village income → 0 under raid; buffer drains; once empty, upgrades pause, headcount holds.
- [ ] **7. Manual mode** — `AllowManualGarrisonTargets` off: `TargetTotalCount` hidden, dispatcher target shown. On: knobs editable, assessment shows "player vs recommended, +X/day".
- [ ] **8. AI clans** — AI managed clan finance unchanged (vanilla); garrison targets still Pass-A-shaped.
- [ ] **9. Dual surface** — `/api/specs` drives the WebUI; the config group renders identically both surfaces; out-of-range `global.json` rejected by `ValidateFiscalAutonomy`.
- [ ] **10. Persistence** — save/reload; treasury balance + 7-day ring restored.
- [ ] **11.** Commit any `fix(...)` and re-run until all checks pass.

---

## Self-Review

**Spec coverage:**
- fiscal-autonomy §3.1/§3.2/§3.5 (treasury, finance model, war buffer) → Tasks 2,3,7. ✓
- fiscal-autonomy §2 loop-closure → holds by construction (budget = ratio × income); verified Task 10.3. ✓
- central-dispatcher §2 two-pass MCMF → Tasks 5 (Pass A), 6 (Pass B integration). ✓
- central-dispatcher §3-§4 allocation graph + value function → Task 5. ✓
- central-dispatcher §5 state inputs → Task 5 helpers (`ClanWageBudget`, `ThreatWeight`, `AdequateFor`). ✓
- central-dispatcher §7 manual mode + assessment + disband gating → Tasks 6,7,9. ✓
- central-dispatcher §9 (this plan replaces the old fiscal-autonomy plan) → header note. ✓
- architecture-review #7 single spec source → Task 8, before Task 9. ✓

**Ordering rationale:** config (1) → treasury data (2) → finance reroute (3) → spend reroute (4) → allocation solver (5) → routing integration + manual mode (6) → disband/war levers (7) → UI dedup (8) → UI knobs/assessment (9) → verify (10). Each task builds clean alone.

**Open contracts (flagged, not copy-paste code):**
- `STClanFinanceModel` recompute (Task 3) — review-critical, verify vs decompiled model.
- `GarrisonAllocationSolver` helper bodies (Task 5) — small, need exact `RiskAssessmentService` / `PartyWageModel` / `GarrisonParty.PartySizeLimit` APIs.
- `ModTreasury` reroute body (Task 4) — mechanical given Tasks 1+2.
- Task 8 `/api/specs` — a refactor; Step 1 audit produces the spec union first.

**Superseded:** `2026-05-21-fiscal-autonomy.md` — do not execute; this plan replaces it.
