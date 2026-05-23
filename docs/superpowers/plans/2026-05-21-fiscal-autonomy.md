# Fiscal Autonomy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make a managed clan's town/castle military (vanilla garrison wage + ST recruiter/upgrade/seed spend) self-funded from that clan's own settlement income, via a per-clan treasury. Size garrisons to what the income affords. Keep the `SovereignTowns` name true in the fiscal-autonomy dimension.

**Architecture:** A per-clan `ClanTreasury` (Layer 1) holds a gold balance + a trailing-7-day expense ring. `STClanFinanceModel : DefaultClanFinanceModel` (Layer 1 GameModel, **player clan only**) reroutes managed-settlement income and garrison wage out of clan gold and into the treasury, returning only the buffered overflow. `AffordabilityPlanner` (Layer 2 evaluator) runs a priority waterfall that converts sustainable income into a per-settlement affordable garrison headcount, which replaces the flat `TargetTotalCount` feeding the MCMF supply/demand graph. `CapitalLogisticsManager` gains a peacetime disband-excess step so over-sized garrisons actually shed wage. Task 7 first removes the dual-surface config-spec duplication so the 6 new knobs are added once.

**Tech Stack:** C# net472, Mount & Blade II Bannerlord v1.3.15 modding API, Newtonsoft.Json, Gauntlet UI, Alpine.js (WebUI). No Harmony needed — `STClanFinanceModel` is a GameModel override registered via `AddModel`.

**Verification model:** No unit-test framework (per `CLAUDE.md`). Every code task ends with `dotnet build` (must report 0 errors) as the automated gate. Runtime behaviour is verified once at the end — see Task 9.

**Reference spec:** `SovereignTowns/docs/superpowers/specs/2026-05-21-fiscal-autonomy-design.md`

> **⚠ FULLY SUPERSEDED (2026-05-21) — DO NOT EXECUTE.** This plan is replaced by the unified plan `2026-05-21-central-garrison-dispatcher.md`, which merges the treasury work here with the central garrison dispatcher into one task sequence. Kept only for diff history.

**Build command (run from workspace root `C:\Users\rangt\Desktop\workspace`):**
```
dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug
```
Expected on success: `Build succeeded`, `0 Error(s)` (pre-existing nullable warnings are fine).

**Line references:** This plan uses **anchor-based** edit instructions ("locate the X block, insert after it") rather than absolute line numbers, because the files drift. Every task's first step on an existing file is to read it and locate the anchor.

**Spec deviation (persistence):** Spec §5 said the treasury balance would be a new `Saveable` field with a new `LocalSaveId`. The actual project pattern (verified in `SovereignTownsCampaignBehavior.SyncData` / `CapitalRegistry.ExportCapitals`) persists ST custom state as a **JSON string in `SyncData`** (`st_capitals_json`), not via the Saveable-class system. This plan persists the treasury the same way — a second `SyncData` key `st_treasuries_json`. No `SovereignTownsTypeDefiner` change, no new `LocalSaveId`.

---

## Task 1: `FiscalAutonomyConfig` + config plumbing

Mirrors the `BuildingBonusConfig` pattern exactly (see the building-level-party-bonuses plan, Task 2).

**Files:**
- Create: `SovereignTowns/src/Configuration/FiscalAutonomyConfig.cs`
- Modify: `SovereignTowns/src/Configuration/GlobalConfig.cs`
- Modify: `SovereignTowns/src/Configuration/ConfigurationManager.cs`
- Modify: `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs`

- [ ] **Step 1: Create `FiscalAutonomyConfig.cs`**

```csharp
namespace SovereignTowns.Configuration;

/// <summary>
/// 城镇财政自治配置。GarrisonWageBudgetRatio 控制驻军工资最多占可持续收入的比例;
/// 其余字段控制金库缓冲金、超额遣散与金库见底兜底。详见设计文档 §3.6。
/// </summary>
public sealed class FiscalAutonomyConfig
{
    /// <summary>驻军工资最多占可持续收入(税+关税)的比例。</summary>
    public float GarrisonWageBudgetRatio { get; set; } = 0.55f;

    /// <summary>可负担再低也保留的驻军人数。</summary>
    public int MinGarrisonFloor { get; set; } = 40;

    /// <summary>金库缓冲金上限 = 本字段 × 近 7 日均开销。</summary>
    public int TreasuryBufferDays { get; set; } = 30;

    /// <summary>当前驻军超可负担目标此倍数 → 和平期遣散超额。</summary>
    public float DisbandExcessThreshold { get; set; } = 1.2f;

    /// <summary>是否启用遣散超额杠杆。</summary>
    public bool DisbandUnaffordableExcess { get; set; } = true;

    /// <summary>金库见底时,自主开销(征兵/升级)是否由玩家个人金币兜底。</summary>
    public bool PlayerClanSubsidyWhenTreasuryEmpty { get; set; } = true;
}
```

- [ ] **Step 2: Add `FiscalAutonomy` to `GlobalConfig`**

Read `GlobalConfig.cs`. Locate the `BuildingBonus` property block (added by the building-bonus feature; if `BuildingBonus` is absent, use the `Thresholds` block as the anchor). After it, add:
```csharp

    /// <summary>城镇财政自治:金库供养、可负担驻军目标、缓冲金。</summary>
    public FiscalAutonomyConfig FiscalAutonomy { get; set; } = new FiscalAutonomyConfig();
```
In `GlobalConfig.CreateDefault()`, after the `BuildingBonus = new BuildingBonusConfig(),` line add:
```csharp
        FiscalAutonomy = new FiscalAutonomyConfig(),
```
If `GlobalConfig` has a `Clone()` that deep-copies sub-objects, add a `FiscalAutonomy` clone line mirroring how `BuildingBonus` is cloned.

- [ ] **Step 3: Bump `CurrentConfigVersion` + default the sub-object in `ConfigurationManager`**

Read `ConfigurationManager.cs`. Locate `CurrentConfigVersion` and increment it by 1 (expected `20 → 21`). In `TryLoadFromDisk`, after the `parsed.BuildingBonus ??= new BuildingBonusConfig();` line add:
```csharp
            parsed.FiscalAutonomy ??= new FiscalAutonomyConfig();
```

- [ ] **Step 4: Add `ValidateFiscalAutonomy` and call it from `ValidateConfig`**

In `ValidateConfig`, after the `ValidateBuildingBonus` call block, insert:
```csharp
        if (config.FiscalAutonomy != null && !ValidateFiscalAutonomy(config.FiscalAutonomy, out reason))
        {
            return false;
        }
```
After the `ValidateBuildingBonus` method, add:
```csharp
    private static bool ValidateFiscalAutonomy(FiscalAutonomyConfig f, out string reason)
    {
        if (f.GarrisonWageBudgetRatio < 0.1f || f.GarrisonWageBudgetRatio > 1.0f)
        { reason = $"FiscalAutonomy.GarrisonWageBudgetRatio invalid ({f.GarrisonWageBudgetRatio}); [0.1, 1.0]"; return false; }
        if (f.MinGarrisonFloor < 0 || f.MinGarrisonFloor > 500)
        { reason = $"FiscalAutonomy.MinGarrisonFloor invalid ({f.MinGarrisonFloor}); [0, 500]"; return false; }
        if (f.TreasuryBufferDays < 0 || f.TreasuryBufferDays > 120)
        { reason = $"FiscalAutonomy.TreasuryBufferDays invalid ({f.TreasuryBufferDays}); [0, 120]"; return false; }
        if (f.DisbandExcessThreshold < 1.0f || f.DisbandExcessThreshold > 3.0f)
        { reason = $"FiscalAutonomy.DisbandExcessThreshold invalid ({f.DisbandExcessThreshold}); [1.0, 3.0]"; return false; }
        reason = "";
        return true;
    }
```

- [ ] **Step 5: Default the sub-object in `WebConfigEndpoints.PutConfig`**

Read `WebConfigEndpoints.cs`. Locate `PutConfig`, after `parsed.BuildingBonus ??= new BuildingBonusConfig();` add:
```csharp
            parsed.FiscalAutonomy ??= new FiscalAutonomyConfig();
```

- [ ] **Step 6: Build.** `dotnet build "SovereignTowns/src/SovereignTowns.csproj" -c Debug` → 0 errors.

- [ ] **Step 7: Commit.**
```bash
git add SovereignTowns/src/Configuration/FiscalAutonomyConfig.cs SovereignTowns/src/Configuration/GlobalConfig.cs SovereignTowns/src/Configuration/ConfigurationManager.cs SovereignTowns/src/WebConfig/WebConfigEndpoints.cs
git commit -m "feat(config): add FiscalAutonomyConfig with validation, bump ConfigVersion"
```

---

## Task 2: `ClanTreasury` + `CapitalManager.Treasury` + persistence

**Files:**
- Create: `SovereignTowns/src/Economy/ClanTreasury.cs`
- Modify: `SovereignTowns/src/Capital/CapitalManager.cs`
- Modify: `SovereignTowns/src/Capital/CapitalRegistry.cs`
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`

- [ ] **Step 1: Create `ClanTreasury.cs`**

```csharp
using System;

namespace SovereignTowns.Economy;

/// <summary>
/// 单个受管氏族的金库。瞬态对象;余额 + 近 7 日实际开销环通过
/// SovereignTownsCampaignBehavior.SyncData 的 st_treasuries_json 持久化。
/// 余额钳制 ≥ 0 —— 赤字由调用方(STClanFinanceModel / ModTreasury)按 §3.5 兜底。
/// </summary>
public sealed class ClanTreasury
{
    private long _balance;
    private readonly long[] _expenseByDay = new long[7];
    private int _dayCursor;

    public long Balance => _balance;

    /// <summary>进账(收入归集)。负值忽略。</summary>
    public void Credit(long amount)
    {
        if (amount > 0) _balance += amount;
    }

    /// <summary>
    /// 扣款。实际开销全额计入近 7 日环;余额不足时只扣到 0,返回欠款(shortfall)。
    /// 调用方据 shortfall 决定兜底(玩家金币 / 暂停自主开销)。
    /// </summary>
    public long Debit(long amount)
    {
        if (amount <= 0) return 0;
        _expenseByDay[_dayCursor] += amount;
        long shortfall = amount > _balance ? amount - _balance : 0;
        _balance -= (amount - shortfall);
        return shortfall;
    }

    /// <summary>能否全额承担 amount(不扣款)。</summary>
    public bool CanAfford(long amount) => amount <= 0 || _balance >= amount;

    /// <summary>每日推进 7 日环。由 STClanFinanceModel 的 applyWithdrawals 路径每日调一次。</summary>
    public void RollDay()
    {
        _dayCursor = (_dayCursor + 1) % 7;
        _expenseByDay[_dayCursor] = 0;
    }

    /// <summary>近 7 日平均日开销。</summary>
    public long TrailingDailyExpense()
    {
        long sum = 0;
        foreach (var d in _expenseByDay) sum += d;
        return sum / 7;
    }

    /// <summary>缓冲金上限 = bufferDays × 近 7 日均开销。</summary>
    public long BufferCap(int bufferDays)
        => Math.Max(0, bufferDays) * Math.Max(0L, TrailingDailyExpense());

    /// <summary>余额超缓冲金上限的部分 skim 出来(回流氏族金币),余额降到上限。</summary>
    public long SkimAboveBufferCap(int bufferDays)
    {
        long cap = BufferCap(bufferDays);
        if (_balance <= cap) return 0;
        long overflow = _balance - cap;
        _balance = cap;
        return overflow;
    }

    // ── 持久化 (经 SyncData JSON) ──
    // 形式: "balance;d0,d1,...,d6;cursor"
    public string Serialize()
        => $"{_balance};{string.Join(",", _expenseByDay)};{_dayCursor}";

    public static ClanTreasury Deserialize(string? s)
    {
        var t = new ClanTreasury();
        try
        {
            if (string.IsNullOrEmpty(s)) return t;
            var parts = s!.Split(';');
            if (parts.Length >= 1) long.TryParse(parts[0], out t._balance);
            if (parts.Length >= 2)
            {
                var days = parts[1].Split(',');
                for (int i = 0; i < 7 && i < days.Length; i++)
                    long.TryParse(days[i], out t._expenseByDay[i]);
            }
            if (parts.Length >= 3) int.TryParse(parts[2], out t._dayCursor);
            if (t._dayCursor < 0 || t._dayCursor > 6) t._dayCursor = 0;
        }
        catch { /* 解析失败 → 空金库 */ }
        return t;
    }
}
```

- [ ] **Step 2: Add `Treasury` to `CapitalManager`**

Read `CapitalManager.cs`. Mirror the `_patrolScheduler` / `_recruiterScheduler` field+property pattern. Add a field initialized in the constructor:
```csharp
    private ClanTreasury _treasury = new ClanTreasury();
    /// <summary>本氏族金库(财政自治)。永不为 null。</summary>
    public ClanTreasury Treasury => _treasury;
```
Add `using SovereignTowns.Economy;` to the using block. Add a restore hook (called by `CapitalRegistry.RestoreTreasuries` before `Initialize`):
```csharp
    /// <summary>SyncData(load) 路径:用持久化串重建金库。</summary>
    public void RestoreTreasuryFrom(string? serialized)
        => _treasury = ClanTreasury.Deserialize(serialized);
```

- [ ] **Step 3: Add export/restore to `CapitalRegistry`**

In `CapitalRegistry.cs`, mirror `ExportCapitals` / `RestoreCapitals`:
```csharp
    /// <summary>导出 clanStringId → 金库序列化串,供 SyncData 写盘。</summary>
    public Dictionary<string, string> ExportTreasuries()
    {
        var dict = new Dictionary<string, string>();
        try
        {
            foreach (var kv in _managers)
            {
                if (kv.Key == null || kv.Value == null) continue;
                dict[kv.Key.StringId] = kv.Value.Treasury.Serialize();
            }
        }
        catch (Exception ex) { Logger.Error("CapitalRegistry.ExportTreasuries failed", ex); }
        return dict;
    }

    /// <summary>SyncData(load) 暂存的金库串。Initialize 后由本方法消费。</summary>
    private Dictionary<string, string>? _pendingTreasuries;
    public void RestoreTreasuries(Dictionary<string, string>? dict) => _pendingTreasuries = dict;
```
In `EnsureForClan`, after `mgr.Initialize();` (or right before, mirroring `RestoreFromStringId`), inject the treasury restore:
```csharp
        if (_pendingTreasuries != null
            && _pendingTreasuries.TryGetValue(clan.StringId, out var tser))
        {
            mgr.RestoreTreasuryFrom(tser);
        }
```
At the end of `Initialize` (next to `_pendingCapitals = null;`), add `_pendingTreasuries = null;`.

- [ ] **Step 4: Persist via `SovereignTownsCampaignBehavior.SyncData`**

In `SyncData`, mirror the `st_capitals_json` block with a second key:
```csharp
            string treasuriesJson = string.Empty;
            if (dataStore.IsSaving)
            {
                try
                {
                    var tdict = _capitalRegistry?.ExportTreasuries() ?? new Dictionary<string, string>();
                    treasuriesJson = JsonConvert.SerializeObject(tdict);
                }
                catch (Exception exT) { Logger.Error("SovereignTowns: SyncData treasuries export failed", exT); treasuriesJson = string.Empty; }
            }
            dataStore.SyncData("st_treasuries_json", ref treasuriesJson);
            if (dataStore.IsLoading)
            {
                try
                {
                    _pendingTreasuries = string.IsNullOrEmpty(treasuriesJson)
                        ? null
                        : JsonConvert.DeserializeObject<Dictionary<string, string>>(treasuriesJson);
                }
                catch (Exception exTl) { Logger.Error("SovereignTowns: parse st_treasuries_json failed", exTl); _pendingTreasuries = null; }
            }
```
Add a `private Dictionary<string, string>? _pendingTreasuries;` field next to `_pendingCapitals`. In `OnSessionLaunched`, right after `_capitalRegistry.RestoreCapitals(_pendingCapitals);`, add `_capitalRegistry.RestoreTreasuries(_pendingTreasuries); _pendingTreasuries = null;`.

- [ ] **Step 5: Build.** → 0 errors.

- [ ] **Step 6: Commit.**
```bash
git add SovereignTowns/src/Economy/ClanTreasury.cs SovereignTowns/src/Capital/CapitalManager.cs SovereignTowns/src/Capital/CapitalRegistry.cs SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs
git commit -m "feat(economy): add per-clan ClanTreasury with SyncData persistence"
```

---

## Task 3: `STClanFinanceModel` — reroute income + garrison wage

**Files:**
- Create: `SovereignTowns/src/Models/STClanFinanceModel.cs`
- Modify: `SovereignTowns/src/SovereignTownsSubModule.cs`

- [ ] **Step 1: Create `STClanFinanceModel.cs`**

Design: subclass `DefaultClanFinanceModel`. For the **player clan only**, recompute managed-settlement income + garrison wage read-only (`applyWithdrawals=false`, so no double side-effects — `base` already mutated `TradeTaxAccumulated`/`PartyTradeGold`), route them into the treasury, and adjust the clan gold delta so only the buffered overflow reaches clan gold. See spec §3.2.

```csharp
using System;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Economy;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Models;

/// <summary>
/// 财政自治金币改道模型。仅对玩家氏族生效:把受管领地的税/关税/村庄收入与驻军工资
/// 从氏族金币里抵消、改记入 ClanTreasury,只让金库缓冲金溢出回流氏族金币。
/// AI / 非受管氏族走 base 原行为。异常 → 退回 base 结果(fail-safe)。详见设计文档 §3.2。
/// </summary>
public sealed class STClanFinanceModel : DefaultClanFinanceModel
{
    private static readonly TextObject _treasuryLine = new TextObject("主权城镇金库结算");

    public override ExplainedNumber CalculateClanGoldChange(
        Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
    {
        var en = base.CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);
        try
        {
            if (clan == null || clan != Clan.PlayerClan) return en;            // 仅玩家氏族
            var registry = CapitalRegistry.Instance;
            if (registry == null || !registry.IsManagedClan(clan)) return en;
            var treasury = registry.GetForClan(clan)?.Treasury;
            if (treasury == null) return en;

            long income = 0, wage = 0;
            foreach (var fief in clan.Fiefs)                                    // 受管领地 = clan.Fiefs
            {
                if (fief?.Settlement == null) continue;
                income += SafeTownIncome(clan, fief);
                var gp = fief.GarrisonParty;
                if (gp != null && gp.IsActive) wage += Math.Max(0, gp.TotalWage);
            }

            int bufferDays = ConfigurationManager.Current?.FiscalAutonomy?.TreasuryBufferDays ?? 30;
            long overflow, shortfall;
            if (applyWithdrawals)                                               // 每日财政 tick 唯一一次
            {
                treasury.RollDay();
                treasury.Credit(income);
                shortfall = treasury.Debit(wage);                               // ST 增量开销当天已即时 Debit
                overflow = treasury.SkimAboveBufferCap(bufferDays);
            }
            else
            {
                // 预览:不动金库。近似 —— 净额并入余额后估算溢出/欠款。
                long projected = treasury.Balance + income - wage;
                long cap = treasury.BufferCap(bufferDays);
                overflow = projected > cap ? projected - cap : 0;
                shortfall = projected < 0 ? -projected : 0;
            }

            // 氏族金币应得 = base − income(改入金库) + wage(金库代付) + overflow − shortfall
            en.Add(-income + wage + overflow - shortfall, _treasuryLine);
        }
        catch (Exception ex)
        {
            Logger.Error("STClanFinanceModel.CalculateClanGoldChange failed; fell back to base", ex);
        }
        return en;
    }

    /// <summary>受管领地的总收入(税+关税+村庄+项目),只读重算,绝不抛。</summary>
    private long SafeTownIncome(Clan clan, Town fief)
    {
        try
        {
            long sum = 0;
            sum += (long)Campaign.Current.Models.SettlementTaxModel.CalculateTownTax(fief).ResultNumber;
            sum += (long)CalculateTownIncomeFromTariffs(clan, fief, false).ResultNumber;
            sum += CalculateTownIncomeFromProjects(fief);
            if (fief.Villages != null)
                foreach (var v in fief.Villages)
                    sum += CalculateVillageIncome(clan, v, false);
            return Math.Max(0, sum);
        }
        catch (Exception ex)
        {
            Logger.Error($"STClanFinanceModel.SafeTownIncome failed for '{fief?.Settlement?.StringId}'", ex);
            return 0;
        }
    }
}
```

> **Implementer note — review-critical.** This is the trickiest file. Two things to verify against the decompiled `DefaultClanFinanceModel` (`_research/Vanilla/`): (a) `CalculateTownIncomeFromTariffs` / `CalculateVillageIncome` / `CalculateTownIncomeFromProjects` are `public override` — call them directly; (b) `base.CalculateClanGoldChange` with `applyWithdrawals=true` already decremented `TradeTaxAccumulated`, so the recompute MUST pass `false`. Confirm `ExplainedNumber.Add(float, TextObject)` is the right overload (it is — used throughout the decompiled model).

- [ ] **Step 2: Register the model in `OnGameStart`**

In `SovereignTownsSubModule.cs`, in the `AddModel` block, add after `STVolunteerModel`:
```csharp
                campaignStarter.AddModel(new SovereignTowns.Models.STClanFinanceModel());
```
Update the adjacent log line `Registered 4 ST GameModels (...)` → `Registered 5 ST GameModels (PartySize/Speed/Wage/Volunteer/ClanFinance)`.

- [ ] **Step 3: Build.** → 0 errors.

- [ ] **Step 4: Commit.**
```bash
git add SovereignTowns/src/Models/STClanFinanceModel.cs SovereignTowns/src/SovereignTownsSubModule.cs
git commit -m "feat(economy): STClanFinanceModel reroutes player-clan town income + garrison wage to treasury"
```

---

## Task 4: `ModTreasury` reroute to `ClanTreasury`

`ModTreasury` currently charges `Hero.MainHero.Gold`. Reroute it to the clan's `ClanTreasury`. The 5 call sites pass a `Clan` (or `Settlement`) so the treasury can be resolved.

**Files:**
- Modify: `SovereignTowns/src/Economy/ModTreasury.cs`
- Modify: `SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs`
- Modify: `SovereignTowns/src/Recruitment/CapitalInPlaceRecruiter.cs`
- Modify: `SovereignTowns/src/Recruitment/BranchInPlaceRecruiter.cs`
- Modify: `SovereignTowns/src/Upgrades/TroopUpgradeService.cs`
- Modify: `SovereignTowns/src/Parties/StPartyComponent.cs`

- [ ] **Step 1: Add a `Clan` parameter to `ModTreasury.CanAfford / Charge / Refund`**

Change the three method signatures to take `Clan? clan` as the first parameter. New behaviour:
- Resolve `treasury = CapitalRegistry.Instance?.GetForClan(clan)?.Treasury`.
- `CanAfford(clan, amount)` → `treasury?.CanAfford(amount) ?? false`.
- `Charge(clan, category, amount, note)`:
  - If `treasury == null` → keep the current Hero.MainHero fallback (defensive — pre-capital-init).
  - Else `treasury.CanAfford(amount)`? If not and `PlayerClanSubsidyWhenTreasuryEmpty == false` → refuse (return false), as today.
  - If subsidy on and treasury short → `treasury.Debit(amount)` returns `shortfall`; charge `shortfall` to `Hero.MainHero` (`hero.ChangeHeroGold(-shortfall)`); full `amount` still goes to the ledger.
  - Else `treasury.Debit(amount)` (shortfall 0).
- `Refund(clan, category, amount, note)` → `treasury?.Credit(amount)`; ledger records `-amount`.
- Keep `ExpenseCategory`, `ModExpenseLedger` calls, audit logging unchanged.

> The exact body is mechanical given `ClanTreasury` (Task 2) and `FiscalAutonomyConfig` (Task 1). Keep every method's outer `try/catch` returning `false` / no-op as today.

- [ ] **Step 2: Update the 5 call sites**

For each, the clan is already in hand (`town.OwnerClan` / `homeTown.OwnerClan` / `home.OwnerClan`). Pass it as the new first arg:
- `RecruitmentDispatcher.TryDispatchRecruiter` — recruiter seed charge.
- `CapitalInPlaceRecruiter.RecruitFromCapitalNotables` — per-troop recruit wage charge.
- `BranchInPlaceRecruiter.RecruitFromBranchNotables` — per-troop recruit wage charge.
- `TroopUpgradeService.TryUpgradeGarrison` — upgrade gold charge + the failure-path `Refund`.
- `StPartyComponent.TrySeedAndBuyInitialFood` (and its `Refund` on rollback / `TryRefundOnDestroy`) — the 4-party seed.

Grep `ModTreasury.Charge`, `ModTreasury.CanAfford`, `ModTreasury.Refund` across `src/` to confirm exactly these sites; update every match.

- [ ] **Step 3: Build.** → 0 errors. The compiler will flag any missed call site (arg-count mismatch) — fix each.

- [ ] **Step 4: Commit.**
```bash
git add -A SovereignTowns/src/Economy/ModTreasury.cs SovereignTowns/src/Recruitment SovereignTowns/src/Upgrades/TroopUpgradeService.cs SovereignTowns/src/Parties/StPartyComponent.cs
git commit -m "feat(economy): ModTreasury charges the clan treasury instead of player gold"
```

---

## Task 5: `AffordabilityPlanner` + MCMF integration

**Files:**
- Create: `SovereignTowns/src/Evaluators/AffordabilityPlanner.cs`
- Modify: `SovereignTowns/src/Algorithm/SupplyDemandGraph.cs`

- [ ] **Step 1: Create `AffordabilityPlanner.cs`**

The priority waterfall from spec §3.3. Stateless static service (类比 `RiskAssessmentService`). Returns per-settlement affordable headcount.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Evaluators;

/// <summary>
/// 财政自治可负担瀑布(设计文档 §3.3)。把氏族可持续收入换算成每领地可负担驻军头数。
/// 无状态;每次现算收入,只读 treasury.Balance。对所有受管氏族生效(只塑形、不碰钱)。
/// </summary>
public static class AffordabilityPlanner
{
    /// <summary>每受管领地的可负担驻军头数。键 = settlement。</summary>
    public static Dictionary<Settlement, int> PlanFor(CapitalManager manager)
    {
        var plan = new Dictionary<Settlement, int>();
        try
        {
            var clan = manager?.OwnerClan;
            if (clan == null) return plan;
            var cfg = ConfigurationManager.Current?.FiscalAutonomy ?? new FiscalAutonomyConfig();

            // 受管领地节点
            var towns = clan.Fiefs.Where(t => t?.Settlement != null && t.Settlement.IsActive).ToList();
            if (towns.Count == 0) return plan;

            // 1. 可持续收入(税 + 关税;排除易被劫的村庄收入)
            long sustainableIncome = 0;
            foreach (var t in towns)
            {
                sustainableIncome += (long)Campaign.Current.Models.SettlementTaxModel.CalculateTownTax(t).ResultNumber;
                sustainableIncome += (long)Campaign.Current.Models.ClanFinanceModel
                    .CalculateTownIncomeFromTariffs(clan, t, false).ResultNumber;
            }
            long clanGarrisonBudget = (long)(cfg.GarrisonWageBudgetRatio * sustainableIncome);

            // 2. 单兵工资:保守取满级(假设升级最终到位)。走 vanilla 模型,RBM 安全。
            int wage = WagePerTroopAtMaxTier(towns);
            if (wage <= 0) wage = 5;

            // 3. 战时:有缓冲金 → 按全额配置供养(见 §3.5)
            bool atWar = AnyAtWar(clan, towns);
            long treasuryBalance = manager.Treasury?.Balance ?? 0;
            long configWageBill = towns.Sum(t => (long)ConfigTargetHeads(t) * wage);
            long effectiveBudget = (atWar && treasuryBalance > 0)
                ? Math.Max(clanGarrisonBudget, configWageBill)
                : clanGarrisonBudget;

            // 4. 优先级瀑布:围攻 > High > 首府 > Medium > Low > Safe
            var ordered = towns
                .OrderByDescending(t => PriorityOf(t, manager))
                .ToList();
            long budget = effectiveBudget;
            foreach (var t in ordered)
            {
                int wantHeads = ScaledTargetHeads(t, manager);          // configTarget × multiplier
                long wantWage = (long)wantHeads * wage;
                long grant = Math.Min(wantWage, Math.Max(0, budget));
                int heads = (int)(grant / wage);
                heads = Math.Max(cfg.MinGarrisonFloor, Math.Min(heads, wantHeads));
                plan[t.Settlement] = heads;
                budget -= (long)heads * wage;                            // floor 可让 budget 转负 = 已接受补贴
            }
            return plan;
        }
        catch (Exception ex)
        {
            Logger.Error("AffordabilityPlanner.PlanFor failed", ex);
            return plan;
        }
    }

    // —— 下列 helper 的具体实现见实现说明 ——
    // ConfigTargetHeads(town)      : 首府 = TownGarrisonRule.TargetTotalCount;
    //                                分支 = BranchRule.TargetPower 经 GarrisonPowerEvaluator
    //                                       的单兵 power 换算成头数。
    // ScaledTargetHeads(t, mgr)    : ConfigTargetHeads × (risk≥High ? WartimeMultiplier : PeacetimeMultiplier)。
    // PriorityOf(t, mgr)           : 围攻 5 / High 4 / 首府 3 / Medium 2 / Low 1 / Safe 0。
    // WagePerTroopAtMaxTier(towns) : Campaign.Models.PartyWageModel.GetCharacterWage(满级代表兵种)。
    //                                代表兵种用 GarrisonPowerEvaluator.MakeStubTroop 同套路按 Tier 找。
    // AnyAtWar(clan, towns)        : clan.MapFaction 处于战争 OR 任一领地 RiskAssessmentService≥High。
}
```

> **Implementer note.** The helper bodies are small but need exact APIs: `TownGarrisonRule` for capital `TargetTotalCount` + multipliers (read via `ConfigurationManager.GetRuleFor`); `BranchRule.TargetPower`; `RiskAssessmentService.Assess`. `MakeStubTroop` is `private` in `GarrisonPowerEvaluator` — either make it `internal` or duplicate a 5-line tier→`CharacterObject` lookup here. Keep every helper exception-safe.

- [ ] **Step 2: Wire the plan into `SupplyDemandGraph.BuildSettlementStates`**

Read `SupplyDemandGraph.cs`. In `BuildSettlementStates`, call `AffordabilityPlanner.PlanFor(manager)` once at the top; pass the result down. Replace:
- Capital: the `ComputeDesiredTarget(rule, risk)` result with `plan[settlement]` (fall back to `ComputeDesiredTarget` if the key is missing).
- Branch (non-capital, not other-owned): `branch.TargetPower` with `min(branch.TargetPower, headsToPower(plan[settlement]))` — `headsToPower` = `heads × GarrisonPowerEvaluator` reference per-troop power.
- Leave `IsOtherOwnedBranch` nodes untouched (they keep the vanilla baseline — fiscal autonomy does not size other lords' garrisons).

- [ ] **Step 3: Build.** → 0 errors.

- [ ] **Step 4: Commit.**
```bash
git add SovereignTowns/src/Evaluators/AffordabilityPlanner.cs SovereignTowns/src/Algorithm/SupplyDemandGraph.cs
git commit -m "feat(economy): affordability waterfall sizes garrison targets from sustainable income"
```

---

## Task 6: Disband-excess lever + war downgrade

**Files:**
- Modify: `SovereignTowns/src/Managers/CapitalLogisticsManager.cs`
- Modify: `SovereignTowns/src/Upgrades/TroopUpgradeService.cs` (or its caller in `CapitalLogisticsManager`)

- [ ] **Step 1: Add a peacetime disband-excess step to `CapitalLogisticsManager.EvaluateClan`**

After `ExecuteMcmfInstructions`, for each managed settlement:
- Skip if `DisbandUnaffordableExcess == false`, or settlement under siege, or risk ≥ High (peacetime only).
- `affordable = AffordabilityPlanner` plan headcount for the settlement (re-run `PlanFor` once per clan and reuse; or have `RunMcmf` return it).
- `current = GarrisonThresholdMath.ActualGarrisonCount(settlement)`.
- If `current > affordable × DisbandExcessThreshold`: remove `current - affordable` lowest-Tier non-hero troops from `GarrisonParty.MemberRoster` (reuse the low-Tier extraction helper used by recruiter escort / transfer extraction — `TroopTransferHelper` or the existing low-tier picker). Log + audit.

> Reuse the existing low-Tier extraction routine rather than writing a new one — recruiter escort extraction already does "drain N lowest-Tier non-hero troops from a garrison roster". Locate it and call it.

- [ ] **Step 2: Pause upgrades when the treasury buffer is empty and at war**

In the daily logistics path that calls `TroopUpgradeService.TryUpgradeGarrison`, gate it: if the clan is at war AND `treasury.Balance <= 0`, skip the upgrade call (spec §3.5 —降级优先于裁员: stop pushing tier up). The `atWar` + balance check mirrors `AffordabilityPlanner.AnyAtWar`. A shared `FiscalState.IsBufferExhaustedAtWar(manager)` helper avoids duplicating the check — put it on `AffordabilityPlanner` or a small static.

- [ ] **Step 3: Build.** → 0 errors.

- [ ] **Step 4: Commit.**
```bash
git add SovereignTowns/src/Managers/CapitalLogisticsManager.cs SovereignTowns/src/Upgrades/TroopUpgradeService.cs
git commit -m "feat(economy): disband unaffordable garrison excess; pause upgrades when war buffer is dry"
```

---

## Task 7: Single config-spec source (architecture cleanup #7)

**Why first, before Task 8:** the dual-surface config specs are hand-maintained twice (`ControlPanelSpecs.cs` + `index.html`) and have already drifted. Adding the 6 fiscal-autonomy knobs to both would add 6 more duplicated entries. This task makes `ControlPanelSpecs.cs` the single source; the WebUI consumes it via a new endpoint.

**Files:**
- Modify: `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs` (+ routing in `WebConfigServer.cs`)
- Modify: `SovereignTowns/src/Ui/ControlPanel/ControlPanelSpecs.cs` (ensure it covers ALL groups, not just Strategy)
- Modify: `SovereignTowns/SovereignTowns/WebUI/index.html`

- [ ] **Step 1: Audit spec coverage**

Read `ControlPanelSpecs.cs` and the `index.html` spec getters (`thresholdSpecs`, `resourceSpecs`, `budget`, group definitions). List every knob in each. The architecture review found `PatrolMaxLifetimeHours` exists only in `index.html`. Produce the union; `ControlPanelSpecs.cs` must end this task as the **superset** (single source of truth).

- [ ] **Step 2: Add a `/api/specs` endpoint**

In `WebConfigEndpoints.cs`, add a `GET /api/specs` handler that serializes `ControlPanelSpecs.AllGroups` (key/labels/hints/specs with root/key/min/max/step/discrete/def/advanced) to JSON via Newtonsoft. Register the route in `WebConfigServer.cs` next to the existing `/api/*` routes. Token-gate it like the other `/api` endpoints.

- [ ] **Step 3: Convert `index.html` to consume `/api/specs`**

Replace the hard-coded `thresholdSpecs` / `resourceSpecs` / `budget` getters and the `settingsGroups` group list with: a `fetch('/api/specs?t=...')` on load that populates a `specGroups` data field; the render loop iterates `specGroups` generically (it already renders groups generically — only the *source* changes). Keep the ratio-normalization JS (that is behaviour, not spec data).

- [ ] **Step 4: Build + smoke-check**

`dotnet build` → 0 errors. The WebUI has no build step (`DeployToGame` copies it). Defer runtime check to Task 9.

- [ ] **Step 5: Commit.**
```bash
git add SovereignTowns/src/WebConfig SovereignTowns/src/Ui/ControlPanel/ControlPanelSpecs.cs SovereignTowns/SovereignTowns/WebUI/index.html
git commit -m "refactor(ui): single config-spec source via /api/specs, kill dual-surface duplication"
```

---

## Task 8: "财政自治" control-panel group + Finance tab extension

Because Task 7 made `ControlPanelSpecs.cs` the single source, the 6 knobs are added **once**.

**Files:**
- Modify: `SovereignTowns/src/Ui/ControlPanel/ControlPanelSpecs.cs`
- Modify: `SovereignTowns/src/Ui/ControlPanel/Tabs/FinanceTabVM.cs` (+ `Items/FinanceRowVM.cs` / `FinanceTableVM.cs` as needed)
- Modify: `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs` (the `/api/finance` payload)

- [ ] **Step 1: Add the `fiscal_autonomy` SpecGroup**

In `ControlPanelSpecs.cs`, add a `SpecGroup` with `Root="FiscalAutonomy"` and 6 `SpecEntry` (mirror the building-bonus group structure):
`GarrisonWageBudgetRatio` (0.1–1.0, step 0.05), `MinGarrisonFloor` (0–500 discrete), `TreasuryBufferDays` (0–120 discrete), `DisbandExcessThreshold` (1.0–3.0, step 0.1), `DisbandUnaffordableExcess` (toggle), `PlayerClanSubsidyWhenTreasuryEmpty` (toggle). Bilingual labels/hints. With Task 7 done, the WebUI picks this up automatically from `/api/specs`.

- [ ] **Step 2: Extend the Finance tab + `/api/finance`**

Add to the finance payload + `FinanceTabVM`: per-clan treasury balance, buffer cap, trailing daily expense, and per-settlement P&L rows (income vs garrison wage, affordable target vs current garrison). Source the numbers from `ClanTreasury` + `AffordabilityPlanner` + `STClanFinanceModel.SafeTownIncome` (extract that into a reusable read-only helper).

- [ ] **Step 3: Build.** → 0 errors.

- [ ] **Step 4: Commit.**
```bash
git add SovereignTowns/src/Ui/ControlPanel SovereignTowns/src/WebConfig/WebConfigEndpoints.cs
git commit -m "feat(ui): Fiscal Autonomy config group + Finance tab treasury/P&L view"
```

---

## Task 9: In-game verification

No automated tests. Verify at runtime per `CLAUDE.md` — logs at `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\`.

- [ ] **Step 1:** Confirm the `DeployToGame` target copied the build. Launch with a managed-clan campaign owning ≥1 town + ≥1 castle.
- [ ] **Step 2: Treasury accounting** — daily log shows treasury Credit (town income) / Debit (garrison wage), `SkimAboveBufferCap` overflow回流, balance never negative.
- [ ] **Step 3: Affordability sizing** — a low-prosperity town's garrison target drops below 150; a besieged town is NOT shrunk (waterfall priority); a castle settles at `MinGarrisonFloor`.
- [ ] **Step 4: Disband-excess** — a conquered over-garrisoned town in peacetime sheds low-Tier troops down toward affordable; in war it does NOT.
- [ ] **Step 5: War handling** — village income → 0 under raid; buffer drains; once empty, upgrades pause; garrison headcount holds.
- [ ] **Step 6: AI clans** — an AI managed clan's finance is unchanged (vanilla), but its garrison targets are still affordability-shaped.
- [ ] **Step 7: Dual surface** — `/api/specs` drives the WebUI; the "财政自治" group renders identically in the Gauntlet panel and WebUI; out-of-range `global.json` is rejected by `ValidateFiscalAutonomy`.
- [ ] **Step 8: Persistence** — save/reload; treasury balance + 7-day ring restored via `st_treasuries_json`.
- [ ] **Step 9:** Commit any `fix(...)` and re-run until all checks pass.

---

## Self-Review

**Spec coverage** (against `2026-05-21-fiscal-autonomy-design.md`):
- §3.1 component layout (`ClanTreasury` L1 / `STClanFinanceModel` L1 / `AffordabilityPlanner` L2) → Tasks 2,3,5. ✓
- §3.2 income+wage reroute, player-clan only, `applyWithdrawals` guard → Task 3. ✓
- §3.3 priority waterfall, MaxTier wage, branch power conversion → Task 5. ✓
- §3.4 MCMF integration, no budget node, disband-excess → Tasks 5,6. ✓
- §3.5 buffer (trailing-7d denominator), war全额供养, pause-upgrades, subsidy toggle → Tasks 2,3,6. ✓
- §3.6 control panel + `ValidateFiscalAutonomy` + Finance tab → Tasks 1,8. ✓
- §5 persistence → **deviated** (SyncData JSON, not Saveable class — see header). Task 2. ✓
- Architecture-review #7 (single spec source) → Task 7, before Task 8. ✓

**Ordering rationale:** config (1) → treasury data + persistence (2) → finance-model reroute (3) → spend reroute (4) → affordability sizing (5) → disband/war levers (6) → UI dedup (7) → UI knobs (8) → verify (9). Each task builds clean on its own.

**Open contracts** (intentionally not copy-paste code — flagged for the implementer):
- `STClanFinanceModel` recompute — review-critical, verify against decompiled `DefaultClanFinanceModel`.
- `AffordabilityPlanner` helper bodies — small, need exact `TownGarrisonRule` / `BranchRule` / `RiskAssessmentService` APIs.
- `ModTreasury` reroute body — mechanical given Tasks 1+2.
- Task 7 `/api/specs` — a refactor; Step 1 audit must produce the spec union first.
