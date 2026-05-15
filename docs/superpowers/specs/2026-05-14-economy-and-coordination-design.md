# 经济统一 + 协调升级设计文档

**Date**: 2026-05-14
**Scope**: SovereignTowns mod — 5 项行为变更打包
**Status**: 设计阶段（待用户审阅）

## 1. 背景与动机

用户对当前 mod 提出 5 项变更需求：

1. **去除"城镇金库"概念**：所有 mod 引发的开销应从玩家个人第纳尔扣，不再走城金库（`GiveGoldAction.ApplyForPartyToSettlement` 等路径）
2. **巡逻与出击可同时开**：当前 `SallyForthManager` 有"无巡逻队"的前置门控，需删除
3. **巡逻队支援出击战斗**：出击队进入战斗后，所有巡逻队评估"能否在战斗结束前抵达"，能则赶去支援
4. **征兵队池化**：征兵队数量由首府兵营等级控制（与巡逻队同公式），多支征兵队应像巡逻队一样不重复访问同一村庄
5. **第纳尔支出可视化**：网页控制面板新增"财务"页，按"今日/本周/全部"显示 mod 引发的支出（按类别拆分）。游戏内显示**不做**。

## 2. 已对齐的决策（brainstorming 阶段）

| # | 议题 | 决定 |
|---|---|---|
| 1 | 支援判定算法 | **简单距离阈值**：ETA = 距离 / 巡逻队速度，ETA < `SupportEtaThresholdHours`（默认 2.0f）则去支援 |
| 2 | 征兵队村庄去重机制 | **新增独立 `ClanRecruiterScheduler`**（与 `ClanPatrolScheduler` 同模式）。保留现有 `VillageCooldownHours = 72h` 作硬性冷却 |
| 3 | 游戏内简略显示 | **不做**（vanilla 右下角不动） |
| 4 | 控制面板财务页 | **三个颗粒度**：今日 / 本周 / 全部，按类别拆分 + 近期流水 |

## 3. 架构总览

5 项变更虽触及不同子系统但同属"经济与协调"主题：

```
①去城金库 ──── 影响每个花钱的子系统
              │
              ↓ 提供统一的"扣钱入口"
              │
⑤第纳尔可视化 ←─── 在统一入口里加 ledger 记录 → Web 控制面板新增"财务"页

②出击+巡逻共存 → 删除 SallyForth 内的"无巡逻队"前置检查
              ↓
③巡逻支援出击战斗 → PatrolManager 优先链里新增一个"敌战中"分支

④征兵队池化 → 新增 ClanRecruiterScheduler，与 ClanPatrolScheduler 完全平行的设计
```

**新增文件**：
- `SovereignTowns/src/Economy/ModTreasury.cs` —— 统一扣钱门面
- `SovereignTowns/src/Economy/ModExpenseLedger.cs` —— 支出记账（含 Category 枚举、Snapshot DTO）
- `SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs` —— 征兵调度器

**修改文件**：
- `SovereignTowns/src/Recruitment/RecruitmentManager.cs` —— 调度走 scheduler；扣钱走 ModTreasury
- `SovereignTowns/src/Recruitment/CapitalInPlaceRecruiter.cs` —— 每招 1 人也扣 5 denar（与外派对齐）
- `SovereignTowns/src/Upgrades/TroopUpgradeService.cs` —— 升级金币走 ModTreasury
- `SovereignTowns/src/SallyForth/SallyForthManager.cs` —— 删除"无巡逻队"门控；新增 `GetActiveCombatSallyParties` 公开方法；100 金本钱走 ModTreasury
- `SovereignTowns/src/Patrol/PatrolManager.cs` —— 优先链新增"support sally battle"分支
- `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs` —— `GetCapFor(KindRecruiter)` 改为读取首府兵营等级
- `SovereignTowns/src/Capital/CapitalManager.cs` —— 新增 `RecruiterScheduler` 字段
- `SovereignTowns/src/Capital/CapitalRegistry.cs` —— 增 Recruiter scheduler 的 Export/Restore 方法
- `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs` —— SyncData 新增两个 JSON 字段（recruiter scheduler + ledger historical totals）
- `SovereignTowns/src/Configuration/GlobalConfig.cs` —— 新增 `ClanRecruiterConfig` 子对象 + `ClanPatrolConfig.SupportEtaThresholdHours` + `EnabledFeatures.PauseSpendingWhenBroke`
- `SovereignTowns/src/Configuration/ConfigurationManager.cs` —— `??=` 兜底 `ClanRecruiter`
- `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs` —— 新增 `GET /api/finance`
- `Modules/SovereignTowns/WebUI/...` —— 新增 "财务" 标签页（HTML/JS/CSS）

**不动**：
- `STPartyWageModel`（mod 自有小队工资仍为 0）
- `GarrisonTransferManager`（调拨队本身无金钱开销）
- `BattleLootHandler`（战利品/卖战俘是收入流，与支出分开看更清晰）

## 4. 详细设计

### 4.1 `ModTreasury` 统一扣钱门面

新文件：`src/Economy/ModTreasury.cs`

**公开 API**：

```csharp
public static class ModTreasury
{
    /// <summary>
    /// 从玩家个人金币扣 amount，记 ledger 一条，写 Audit 一条。
    /// </summary>
    /// <returns>true = 扣款成功（金币足）；false = "PauseSpendingWhenBroke" 开启且金额不足 → 拒绝扣款</returns>
    public static bool Charge(ExpenseCategory category, int amount, string note);

    /// <summary>
    /// 仅查询能否承担（不扣款）。给"派征兵队前先检查初始金"这类预检查用。
    /// </summary>
    public static bool CanAfford(int amount);
}

public enum ExpenseCategory
{
    RecruiterWage,    // 每招 1 人 5 denar
    RecruiterSeed,    // 派出征兵队的 1000 初始金
    Upgrade,          // 驻军升级单兵金币
    SallySeed,        // 出击队 100 金本钱
    Other
}
```

**内部行为**：

```csharp
public static bool Charge(ExpenseCategory category, int amount, string note)
{
    if (amount <= 0) return true;

    // 软门控（默认开启）
    var feat = ConfigurationManager.Current.EnabledFeatures;
    if (feat.PauseSpendingWhenBroke && !CanAfford(amount))
    {
        Logger.Info($"ModTreasury: 拒绝 {category} -{amount}d 因玩家金币不足且 PauseSpendingWhenBroke=true");
        return false;
    }

    // 真扣钱（vanilla 标准 API；允许负余额，与玩家自身行为一致）
    try
    {
        GiveGoldAction.ApplyBetweenCharacters(
            giverHero: Hero.MainHero,
            recipientHero: null,
            amount: amount,
            disableNotification: true);
    }
    catch (Exception ex)
    {
        Logger.Error($"ModTreasury: ChangeHeroGold failed for {category} -{amount}d", ex);
        return false;
    }

    // 记账
    ModExpenseLedger.Record(category, amount, note);

    // 审计
    DecisionAuditLogger.LogRule(
        decisionType: "mod_expense",
        inputSummary: $"category={category} amount={amount} note={note}",
        decisionJson: $"{{\"category\":\"{category}\",\"amount\":{amount},\"note\":\"{EscapeJson(note)}\"}}",
        accepted: true);

    return true;
}

public static bool CanAfford(int amount) =>
    Hero.MainHero?.Gold >= amount;
```

**注**：vanilla API 选择 `GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, amount)` —— 这是 vanilla 标准的"玩家给空"扣款路径，会触发 vanilla 内部记账事件。备选 `Hero.MainHero.ChangeHeroGold(-amount, true)` 也可（更轻），实现时验证哪个有副作用。

### 4.2 改造点：所有现有花钱路径走 ModTreasury

| 文件 | 旧路径 | 新路径 |
|---|---|---|
| `RecruitmentManager.RecruitFromTargetVillage` | `ownerHero.ChangeHeroGold(-cost)` | `ModTreasury.Charge(RecruiterWage, cost, "village=...")` |
| `RecruitmentManager.TryDispatchRecruiter` | 城金库初始拨款 1000 | 派出前 `if (!ModTreasury.CanAfford(1000)) return;`，派出时 `ModTreasury.Charge(RecruiterSeed, 1000, "town=...")` |
| `CapitalInPlaceRecruiter` | 不花钱 | 每招 1 人 `ModTreasury.Charge(RecruiterWage, 5, "capital_inplace village=...")` —— 与外派对齐 |
| `TroopUpgradeService.TryUpgradeGarrison` | `GiveGoldAction.ApplyForPartyToSettlement` | `ModTreasury.Charge(Upgrade, goldCost, "char=...")` |
| `SallyForthManager.CreateSallyParty`（或同等位置） | 从源城扣 100 金 | 派出前 `if (!ModTreasury.CanAfford(100)) return;`，派出时 `ModTreasury.Charge(SallySeed, 100, "town=...")` |

每个改造点的代码层面：
- 删除原 vanilla API 调用
- 添加 ModTreasury 调用
- 派出新小队前 `CanAfford` 预检（防止扣不到钱却派了队）

### 4.3 `ModExpenseLedger` 支出记账

新文件：`src/Economy/ModExpenseLedger.cs`

**内存结构**：

```csharp
public static class ModExpenseLedger
{
    private static readonly object _gate = new();
    private static readonly List<ExpenseEntry> _entries = new();
    private static readonly Dictionary<ExpenseCategory, long> _historicalRolledOver = new();
    private const int MaxInMemoryDays = 30;

    public static void Record(ExpenseCategory category, int amount, string note);
    public static FinanceReport BuildReport();      // 给 /api/finance 用
    public static FinanceSnapshot CreateSnapshot(); // 给 SyncData 用
    public static void RestoreFromSnapshot(FinanceSnapshot s);
    public static void Clear();                     // 给 SafeUninstall 用
}

public sealed class ExpenseEntry
{
    public long TimestampMs { get; set; }   // CampaignTime.ToMilliseconds
    public ExpenseCategory Category { get; set; }
    public int Amount { get; set; }         // 正数（扣多少）
    public string Note { get; set; }
}

public sealed class FinanceReport
{
    public Dictionary<string, long> Today { get; set; }      // category name → amount
    public Dictionary<string, long> Week { get; set; }
    public Dictionary<string, long> AllTime { get; set; }
    public long TodayTotal { get; set; }
    public long WeekTotal { get; set; }
    public long AllTimeTotal { get; set; }
    public List<ExpenseEntry> RecentEntries { get; set; }    // 最近 50 条
}

public sealed class FinanceSnapshot
{
    public Dictionary<string, long> HistoricalRolledOver { get; set; }  // category name → amount
    public List<ExpenseEntry> RecentEntries { get; set; }               // 内存里 30 天的
}
```

**轮转策略**（防止 ledger 无限膨胀）：

```csharp
public static void Record(ExpenseCategory category, int amount, string note)
{
    lock (_gate)
    {
        _entries.Add(new ExpenseEntry { ... });
        TrimAndRollOverIfNeeded();
    }
}

private static void TrimAndRollOverIfNeeded()
{
    var cutoff = CampaignTime.Now.ToMilliseconds - (MaxInMemoryDays * 24 * 3600 * 1000L);
    while (_entries.Count > 0 && _entries[0].TimestampMs < cutoff)
    {
        var old = _entries[0];
        _historicalRolledOver.TryGetValue(old.Category, out var sum);
        _historicalRolledOver[old.Category] = sum + old.Amount;
        _entries.RemoveAt(0);
    }
}
```

**报告构建**：

```csharp
public static FinanceReport BuildReport()
{
    lock (_gate)
    {
        var nowMs = CampaignTime.Now.ToMilliseconds;
        var todayStartMs = nowMs - (24 * 3600 * 1000L);
        var weekStartMs = nowMs - (7 * 24 * 3600 * 1000L);

        var today = AggregateBy(e => e.TimestampMs >= todayStartMs);
        var week = AggregateBy(e => e.TimestampMs >= weekStartMs);
        var allTime = AggregateAllIncludingHistorical();

        return new FinanceReport
        {
            Today = today.byCategory,
            TodayTotal = today.total,
            Week = week.byCategory,
            WeekTotal = week.total,
            AllTime = allTime.byCategory,
            AllTimeTotal = allTime.total,
            RecentEntries = _entries.TakeLast(50).Reverse().ToList()
        };
    }
}
```

### 4.4 `SyncData` 持久化（一次性扩展）

`SovereignTownsCampaignBehavior.SyncData` 在现有的 `st_capital_stringid` / `st_ai_capitals_json` / `st_patrol_schedulers_json` 之后新增**两个**字段：

```
"st_recruiter_schedulers_json" : string?  // 与 patrol scheduler 同结构
"st_finance_snapshot_json"     : string?  // FinanceSnapshot JSON
```

OnSessionLaunched 中相应：
- `_capitalRegistry.RestoreRecruiterSchedulers(_pendingRecruiterSchedulers)`
- `ModExpenseLedger.RestoreFromSnapshot(_pendingFinanceSnapshot)`

`_pending*` 字段同款暂存机制。

### 4.5 `ClanRecruiterScheduler` 设计（完全平行于 ClanPatrolScheduler）

新文件：`src/Recruitment/ClanRecruiterScheduler.cs`

```csharp
public sealed class ClanRecruiterScheduler
{
    private readonly Clan _clan;
    private readonly Dictionary<string, CampaignTime> _lastRecruitedAt = new();   // key: Settlement.StringId, 持久化
    private readonly Dictionary<string, CampaignTime> _bookedUntil     = new();   // 瞬态
    private readonly Dictionary<MBGUID, CampaignTime> _lastStopChangedAt = new(); // 瞬态
    private readonly Dictionary<MBGUID, string> _lastSeenLocation = new();        // 瞬态

    public ClanRecruiterScheduler(Clan clan);
    public Clan OwnerClan => _clan;

    public Settlement? PickNextVillage(MobileParty recruiterParty);
    public void RecordVisit(Settlement village);
    public void PreemptiveBook(Settlement village, MobileParty party, float etaHours);
    public bool IsStuck(MobileParty party, float stuckTimeoutHours);
    public bool TryMarkArrival(MobileParty party, Settlement visited);
    public void NotifySettlementLost(Settlement settlement);
    public void NotifyAllLost();
    public void NotifyPartyDestroyed(MobileParty party);
    public ClanRecruiterSchedulerSnapshot CreateSnapshot();
    public void RestoreFromSnapshot(ClanRecruiterSchedulerSnapshot snapshot);
}

public sealed class ClanRecruiterSchedulerSnapshot
{
    public Dictionary<string, long> LastRecruitedAt { get; set; } = new();
}
```

### 4.6 PickNextVillage 算法

与 PickNextStop 类似但针对村庄：

```csharp
public Settlement? PickNextVillage(MobileParty recruiterParty)
{
    if (recruiterParty == null) return null;
    try
    {
        var config = ConfigurationManager.Current.ClanRecruiter;
        var globalCfg = ConfigurationManager.Current;
        var now = CampaignTime.Now;
        var partyPos = recruiterParty.GetPosition2D;

        // 候选筛选：复用现有 RecruitmentPlanner.RankCandidates 取 top-N 候选村
        // RankCandidates 已处理：候选兵种数、距离、风险、村庄状态（被劫 / 敌方）
        var capital = TryGetCapitalManager()?.GetCapitalSettlement()?.Town;
        if (capital == null) return null;
        var candidates = RecruitmentPlanner.RankCandidates(capital, _clan);
        // candidates 通常 ≤ 8

        Settlement? best = null;
        float bestScore = float.MaxValue;

        foreach (var v in candidates)
        {
            // 硬性 VillageCooldownHours 由 RecruitmentCooldown 旧机制保护 → 这里假定 candidates 已过滤
            // 新调度器只防"短期 + 多队抢点"

            if (_bookedUntil.TryGetValue(v.StringId, out var booked) && booked > now)
                continue;

            if (_lastRecruitedAt.TryGetValue(v.StringId, out var lva))
            {
                if (lva.ElapsedHoursUntilNow < config.MinVisitGapHours)
                    continue;
            }

            float hoursSinceVisit = _lastRecruitedAt.TryGetValue(v.StringId, out var l)
                ? (float)l.ElapsedHoursUntilNow
                : 1e6f;
            float distance = (partyPos - v.GetPosition2D).Length;
            float score = -hoursSinceVisit + config.DistanceWeightHoursPerTile * distance;

            if (score < bestScore)
            {
                bestScore = score;
                best = v;
            }
        }

        if (best != null)
        {
            float etaHours = ComputeEtaHours(recruiterParty, best);
            PreemptiveBook(best, recruiterParty, etaHours);
        }
        return best;
    }
    catch (Exception ex)
    {
        Logger.Error($"ClanRecruiterScheduler.PickNextVillage failed for clan '{_clan?.StringId}'", ex);
        return null;
    }
}
```

### 4.7 池大小动态化

`PartyLifecycleManager.GetCapFor(settlement, KindRecruiter)` 改造：

```csharp
public int GetCapFor(Settlement settlement, string kind)
{
    if (kind == KindRecruiter)
    {
        try
        {
            var capitalMgr = CapitalRegistry.Instance?.GetForSettlement(settlement);
            var capital = capitalMgr?.GetCapital();
            if (capital == null) return 1;
            int barracksLevel = GetBarracksLevel(capital);  // 复用 PatrolManager 的私有方法或抽公共
            return Math.Max(1, barracksLevel + 1);
        }
        catch { return 1; }
    }
    // ... 其他 kind 不变
}
```

`PatrolManager.TryFindPatrolTemplate` 内的 `GetBarracksLevel` 抽到一个共享 helper（`Capital/CapitalHelpers.cs` 或类似），让 patrol 和 recruiter 都用。

### 4.8 SallyForth 改造

**§4.8.1 删除"无巡逻队"前置门控**

在 `SallyForthManager.OnHourlyTickSettlement` 找到检查"该 settlement 是否有 active patrol"的 if，删除。

**§4.8.2 新增 GetActiveCombatSallyParties 公开方法**

```csharp
/// <summary>
/// 返回该氏族当前正在 MapEvent 中战斗的出击队列表。
/// 供 PatrolManager 评估是否需要赶去支援。
/// </summary>
public List<MobileParty> GetActiveCombatSallyParties(Clan clan)
{
    var result = new List<MobileParty>();
    try
    {
        foreach (var party in MobileParty.AllCustomParties)
        {
            if (party == null || !party.IsActive) continue;
            if (party.PartyComponent is not SallyForthPartyComponent sc) continue;
            if (sc.HomeSettlement?.OwnerClan != clan) continue;
            if (party.MapEvent == null) continue;  // 不在战斗中
            result.Add(party);
        }
    }
    catch (Exception ex)
    {
        Logger.Error("GetActiveCombatSallyParties failed", ex);
    }
    return result;
}
```

### 4.9 PatrolManager 优先链改造

在 `OnHourlyTickParty` 的优先链里插入新分支（位置：在 Defense 之后、Heal 之前）：

```csharp
// 1) 自动 merge
if (members < MinPartyMembersBeforeMerge) { ... return; }

// 2) 防御响应
var defenseTarget = scheduler.GetDefenseTarget(party);
if (defenseTarget != null) { ... return; }

// ★ 3) 支援出击战斗（新增）
if (_sallyForthManager != null)
{
    var supportSally = FindSupportableSallyBattle(party, capitalMgr);
    if (supportSally != null)
    {
        Logger.Info($"PatrolManager: '{SafeName(party)}' supporting sally battle at '{SafeName(supportSally.MapEvent?.Position2D)}'");
        SafeSetMoveEngageParty(party, supportSally);
        return;
    }
}

// 4) Heal
// 5) 抵达侦测
// 6) 卡死保护
```

**FindSupportableSallyBattle 实现**：

```csharp
private MobileParty? FindSupportableSallyBattle(MobileParty patrol, Capital.CapitalManager capitalMgr)
{
    try
    {
        var threshold = ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours;
        var sallies = _sallyForthManager.GetActiveCombatSallyParties(capitalMgr.OwnerClan);
        if (sallies.Count == 0) return null;

        var partyPos = patrol.GetPosition2D;
        var partySpeed = Math.Max(patrol.Speed, 0.1f);

        MobileParty? best = null;
        float bestEta = float.MaxValue;
        foreach (var sally in sallies)
        {
            try
            {
                var mapEvent = sally.MapEvent;
                if (mapEvent == null) continue;  // 双重保险
                var combatPos = sally.GetPosition2D;  // sally 的位置 ≈ MapEvent 位置
                float distance = (partyPos - combatPos).Length;
                float eta = distance / partySpeed;
                if (eta < threshold && eta < bestEta)
                {
                    bestEta = eta;
                    best = sally;
                }
            }
            catch { /* 单个 sally 失败不影响其他 */ }
        }
        return best;
    }
    catch (Exception ex)
    {
        Logger.Error("FindSupportableSallyBattle failed", ex);
        return null;
    }
}
```

**新增 SafeSetMoveEngageParty 包装**（与现有 SafeSetMoveGoToSettlement 同款）：

```csharp
private static void SafeSetMoveEngageParty(MobileParty party, MobileParty target)
{
    try
    {
        party.SetMoveEngageParty(target);
    }
    catch (Exception ex)
    {
        Logger.Error($"SetMoveEngageParty failed for '{SafeName(party)}' -> '{SafeName(target)}'", ex);
    }
}
```

**支援结束自然过渡**：vanilla MapEvent 结束后，patrol 的 `TargetParty` 会被清空。下个 hourly tick：
- 步骤 3 找不到 active combat sally → 跳过
- 步骤 5（抵达侦测）也许触发（patrol 可能停在战场附近），也许不（取决于 vanilla 行为）
- 步骤 6 IsStuck 兜底 → 强制重新 PickNextStop

完整支援→恢复路径不需要额外代码。

### 4.10 RecruitmentManager 接入 scheduler

在现有 `RecruitmentManager.OnHourlyTickParty` 里，将"招到人后规划下一站"的路径换成 scheduler 驱动：

```csharp
// 旧
var next = RecruitmentPlanner.PickNext(party);  // 内部用 cooldown + 距离评分

// 新
var capitalMgr = _capitalRegistry?.GetForSettlement(party.PartyComponent.HomeSettlement);
if (capitalMgr == null) { /* 老路径回退 */ return; }
var next = capitalMgr.RecruiterScheduler.PickNextVillage(party);
```

招到人后调 `scheduler.RecordVisit(village)`：

```csharp
// 在 RecruitFromTargetVillage 内 recruitCount > 0 之后
capitalMgr.RecruiterScheduler.RecordVisit(targetVillage);
```

派遣新征兵队时（TryDispatchRecruiter 末尾）调首站：

```csharp
capitalMgr.RecruiterScheduler.RecordVisit(capital);  // 标记首府"刚出门"
var firstStop = capitalMgr.RecruiterScheduler.PickNextVillage(newParty);
if (firstStop != null) SafeSetMoveGoToSettlement(newParty, firstStop);
```

### 4.11 WebConfig 财务页

**新 endpoint**：`GET /api/finance`

```csharp
// WebConfigEndpoints.cs 新增方法
public static string GetFinance()
{
    try
    {
        var report = ModExpenseLedger.BuildReport();
        return JsonConvert.SerializeObject(report);
    }
    catch (Exception ex)
    {
        Logger.Error("GetFinance failed", ex);
        return "{\"error\":\"" + ex.Message + "\"}";
    }
}
```

在 `WebConfigServer.Handle` 路由表里注册：

```csharp
"/api/finance" => WriteJson(response, 200, GetFinance()),
```

**Web UI 新增标签页**（`Modules/SovereignTowns/WebUI/finance.html` + JS）：

```html
<!-- 三列汇总 -->
<section>
  <div class="col">
    <h3>今日</h3>
    <table>
      <tr><th>类别</th><th>金额</th></tr>
      <tr><td>招兵</td><td id="today-recruiter"></td></tr>
      <tr><td>升级</td><td id="today-upgrade"></td></tr>
      <tr><td>出击本钱</td><td id="today-sally"></td></tr>
      <tr class="total"><td>总</td><td id="today-total"></td></tr>
    </table>
  </div>
  <div class="col">...本周... </div>
  <div class="col">...全部... </div>
</section>
<!-- 流水 -->
<section>
  <h3>近期流水（最近 50 条）</h3>
  <table id="recent-table">
    <thead><tr><th>时间</th><th>类别</th><th>金额</th><th>备注</th></tr></thead>
    <tbody></tbody>
  </table>
</section>
```

JS 每 5 秒 fetch `/api/finance` 一次刷新。

## 5. 配置变更

### 5.1 GlobalConfig 新增

```csharp
public sealed class GlobalConfig
{
    // ...
    public ClanRecruiterConfig ClanRecruiter { get; set; } = new();  // 新
    // ClanPatrol 已有，但加一个 SupportEtaThresholdHours 字段
}

public sealed class ClanPatrolConfig
{
    // 已有字段...
    public float SupportEtaThresholdHours { get; set; } = 2.0f;  // 新
}

public sealed class ClanRecruiterConfig
{
    public float EtaBufferHours { get; set; } = 1.0f;
    public float StuckTimeoutHours { get; set; } = 12.0f;
    public float MinVisitGapHours { get; set; } = 4.0f;
    public float DistanceWeightHoursPerTile { get; set; } = 0.5f;
}

public sealed class EnabledFeatures
{
    // ...
    public bool PauseSpendingWhenBroke { get; set; } = true;  // 新
}
```

### 5.2 ConfigurationManager.TryLoadFromDisk 兜底新字段

```csharp
parsed.ClanPatrol ??= new ClanPatrolConfig();
parsed.ClanRecruiter ??= new ClanRecruiterConfig();  // 新
// SupportEtaThresholdHours 是 ClanPatrol 子字段，无需单独兜底（new ClanPatrolConfig() 自带默认）
```

**不升 ConfigVersion**（与之前的策略一致：新增字段、null-coalesce 兜底）。

## 6. 边界情况

| 情况 | 处理 |
|---|---|
| 玩家金币不足且 PauseSpendingWhenBroke=true | ModTreasury.Charge 返回 false，调用方应做 fallback（不派遣 / 不升级），日志记 Info |
| 玩家金币不足但 PauseSpendingWhenBroke=false | 仍扣，允许负余额（与 vanilla 玩家自身行为一致） |
| Hero.MainHero == null（极端 / 战役未启动） | ModTreasury.Charge 返回 false，记 Warn |
| RecruiterScheduler 在玩家无首府时 | 没有 RecruiterScheduler 可用 → RecruitmentManager 走老路径（保持 mod 继续可用） |
| Sally 支援判定时 patrol 距离过远（> Threshold） | 不去；patrol 继续按 scheduler 巡逻 |
| Sally 战斗结束时 patrol 还在赶路 | vanilla 战斗完后 sally 的 MapEvent 清空，patrol 的 TargetParty 也被 vanilla 清；下个 hourly tick patrol 走 stuck protection 重新选下一站 |
| 多支 patrol 都判定能支援同一 sally | 都会去 —— vanilla MapEvent 系统会自然把多个友方 party 合并到同一战斗。无 cap |
| Ledger 内存超 30 天 | TrimAndRollOverIfNeeded 把超期 entry 累加进 `_historicalRolledOver` 字典后丢弃 |
| 读档时 ledger 数据丢失 | snapshot 读不到 → 内存为空 + historical 字典为空。视为新游戏；不会崩 |
| SafeUninstall | 新增一行 `ModExpenseLedger.Clear()` |

## 7. 风险与回退路径

| 风险 | 缓解 |
|---|---|
| 改成扣玩家钱后玩家"感觉变穷" | 默认开启 `PauseSpendingWhenBroke`；不足时跳过非必需开销；提供 Audit 日志可查 |
| ModTreasury 失败导致小队没派出 | 调用方先 `CanAfford` 预检，确认能付才派；派出后扣费失败仅记 Warn，已派的小队照常执行 |
| Recruiter 池突然增到 4 支 → 招兵速度过快 | 玩家可在网页面板里手动改 VillageCooldownHours 拉长冷却；也可关 AutoRecruitment 总开关 |
| Sally + Patrol 协同导致大量 patrol 都跑去同一战斗 | 已存在 vanilla MapEvent 容量上限；最多影响"巡逻覆盖"暂时变差，1-2 小时内自然恢复 |
| 财务页 fetch 失败 | 前端 fallback 显示 "—"；不影响游戏 |
| 网页面板 schemaVersion 没升导致 UI 看不到新字段 | 后端 endpoint 总是返回最新字段；前端按字段存在性渲染（缺字段则隐藏对应列） |

## 8. 测试方式

无单元测试（项目约定）。验收路径：

1. **编译**：Debug + Release 双模式零错零警告
2. **游戏内观察**：
   - 启动游戏，看 ModLogs：
     - 应出现 `mod_expense` 类型审计行（每次扣费一条）
     - 应**不再出现** `GiveGoldAction.ApplyForPartyToSettlement` 相关的城金库扣款
   - 派征兵队：玩家金额减少 1000 立即可见
   - 升级驻军：玩家金额减少（按升级费）可见
   - 出击：玩家金额减少 100 可见
   - 多支征兵队同时出发 → 目标村庄不同
   - 巡逻队 + 出击队并存：能同时看到地图上有这两类小队
   - 出击战斗时：观察附近 patrol 是否在距离阈值内自动靠拢（看日志 `supporting sally battle`）
3. **网页面板**：
   - 打开网页面板访问 "财务" tab
   - 应看到今日 / 本周 / 全部三栏
   - 触发几次扣费后刷新，金额应增长
   - 流水表应显示最近几条
4. **存档**：
   - 存档 → 退出 → 读档：财务数据应保留（历史累计 + 内存 30 天 entries）
5. **边界**：
   - 玩家金额 = 0，PauseSpendingWhenBroke=true：mod 应停止派征兵队 / 不升级 / 不出击；日志 Info 行记录拒绝原因
   - 卸载流程：所有 mod 创建小队解散、ledger 清空

## 9. 实施大致顺序（供 writing-plans 阶段细化）

1. ModTreasury + ExpenseCategory 枚举 + ModExpenseLedger 骨架
2. 改造所有花钱路径走 ModTreasury（5 个改动点）
3. ModExpenseLedger 报告/snapshot 实现 + SyncData 接入
4. ClanRecruiterScheduler 新文件 + CapitalManager 持有 + Registry 导出/恢复
5. PartyLifecycleManager.GetCapFor(Recruiter) 改为动态
6. RecruitmentManager.OnHourlyTickParty 接入 scheduler；TryDispatchRecruiter 首站接入
7. SallyForthManager 删除巡逻互斥；新增 GetActiveCombatSallyParties
8. PatrolManager 优先链插入支援分支 + SafeSetMoveEngageParty 包装
9. GlobalConfig 新增 ClanRecruiterConfig + SupportEtaThresholdHours + PauseSpendingWhenBroke
10. WebConfigEndpoints + WebConfigServer 路由表新增 /api/finance
11. WebUI 新增财务标签页（HTML/JS/CSS）
12. SafeUninstallMenu 增加 ModExpenseLedger.Clear()
13. 编译 + 游戏内验证

## 10. 不在本次范围

显式排除：
- 游戏内大地图右下角的简略支出显示（用户明确说先不做）
- mod 收入流（卖战俘/卖战利品的钱）也进 ledger —— 当前仅记录支出
- 投资类操作（如自动建筑、自动招贵族）的预算控制
- 玩家在面板上手动转账给某个城/小队
- 财务报告的图表 / 趋势线（先做表格够用）

---

## 自审摘要

| 检查项 | 状态 |
|---|---|
| 5 项变更全有对应设计章节 | ✓ |
| 无 "TBD"/"TODO" | ✓ |
| 字段名 / 类名跨节一致（ModTreasury / ModExpenseLedger / ClanRecruiterScheduler / ExpenseCategory）| ✓ |
| 项目硬约束不违反 | ✓ — 不动 csproj、不增 Saveable 类型（用 SyncData JSON）、所有公开方法 try-catch |
| 用户配置不被清空（不升 ConfigVersion） | ✓ |
| 与 README.md 描述的当前功能不冲突 | 需在实施后同步更新 README |
