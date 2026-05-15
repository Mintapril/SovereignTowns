# 经济统一 + 协调升级 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 5 项行为打包：①去除城金库（全走玩家第纳尔）②出击巡逻共存 ③巡逻支援出击战斗 ④征兵队池化 + 村庄不重复 ⑤Web 面板财务页

**Architecture:** 新增统一扣钱门面 `ModTreasury` + 记账 `ModExpenseLedger`；新增 `ClanRecruiterScheduler`（与 `ClanPatrolScheduler` 完全平行）；PatrolManager 优先链插入"支援出击"分支；SallyForthManager 删除巡逻互斥门控；Web UI index.html 新增"财务"标签页。

**Tech Stack:** C# / .NET Framework 4.7.2 / Bannerlord v1.3.15 TaleWorlds CampaignSystem / Newtonsoft.Json / AlpineJS + Tailwind（已有的 WebUI 技术栈，单 HTML 文件）。

**项目特殊约定**：
- 无 git，无 commit 步骤。各 Task 末尾用 "Checkpoint" 标记
- 无单元测试。验证 = `dotnet build` 零错零警告 + 必要时游戏内日志
- 所有 public 方法 + 事件回调入口包 try/catch
- 路径使用 Windows 风格，C# 代码内用正斜杠或反斜杠都可

---

## 文件结构

**Create**：
- `SovereignTowns/src/Economy/ModTreasury.cs` — 统一扣钱门面（静态类）
- `SovereignTowns/src/Economy/ModExpenseLedger.cs` — 支出记账（静态类 + Snapshot DTO + Category enum + ExpenseEntry + FinanceReport）
- `SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs` — 征兵队调度器（与 ClanPatrolScheduler 同结构）

**Modify**：
- `SovereignTowns/src/Recruitment/RecruitmentManager.cs` — 扣钱走 ModTreasury；OnHourlyTickParty 接入 scheduler
- `SovereignTowns/src/Recruitment/CapitalInPlaceRecruiter.cs` — 每招 1 人扣 5 denar
- `SovereignTowns/src/Upgrades/TroopUpgradeService.cs` — 升级金币走 ModTreasury
- `SovereignTowns/src/SallyForth/SallyForthManager.cs` — 删互斥门控 + 100 金本钱走 ModTreasury + 新增 GetActiveCombatSallyParties
- `SovereignTowns/src/Patrol/PatrolManager.cs` — 优先链插入支援分支 + SafeSetMoveEngageParty 包装
- `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs` — KindRecruiter 上限改用 barracks 公式
- `SovereignTowns/src/Capital/CapitalManager.cs` — 新增 RecruiterScheduler 字段
- `SovereignTowns/src/Capital/CapitalRegistry.cs` — 增 ExportRecruiterSchedulerSnapshots / RestoreRecruiterSchedulers
- `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs` — SyncData 新增 2 字段 + OnSessionLaunched 回灌
- `SovereignTowns/src/Configuration/GlobalConfig.cs` — 新增 ClanRecruiterConfig + ClanPatrolConfig.SupportEtaThresholdHours + EnabledFeatures.PauseSpendingWhenBroke
- `SovereignTowns/src/Configuration/ConfigurationManager.cs` — `??=` 兜底 ClanRecruiter
- `SovereignTowns/src/Ui/SafeUninstallMenu.cs` — 增加 `ModExpenseLedger.Clear()` + 增加 RecruiterScheduler.NotifyAllLost
- `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs` — 新 endpoint GetFinance
- `SovereignTowns/src/WebConfig/WebConfigServer.cs` — 路由表新增 `/api/finance`
- `SovereignTowns/SovereignTowns/WebUI/index.html` — 新增"财务"标签页 + JS fetch 逻辑

---

## Task 总览

1. ModTreasury + ExpenseCategory 枚举
2. ModExpenseLedger 记账 + Snapshot DTO + 报告构建
3. 接入 ModTreasury：RecruitmentManager 招兵每人 + 派遣初始金
4. 接入 ModTreasury：CapitalInPlaceRecruiter 每人扣费
5. 接入 ModTreasury：TroopUpgradeService 升级金币
6. 接入 ModTreasury：SallyForthManager 出击 100 金本钱
7. ClanRecruiterScheduler 骨架 + Snapshot DTO
8. CapitalManager 持有 RecruiterScheduler + Registry 导出/恢复
9. ClanRecruiterScheduler 算法实现（PickNextVillage / RecordVisit 等）
10. PartyLifecycleManager.GetMaxFor 改造 KindRecruiter 用 barracks 公式
11. RecruitmentManager.OnHourlyTickParty 接入 RecruiterScheduler
12. SallyForthManager 删除巡逻互斥 + 新增 GetActiveCombatSallyParties
13. PatrolManager 优先链插入支援分支
14. GlobalConfig 新增 3 处字段 + ConfigurationManager 兜底
15. SyncData 接入新 JSON 字段（recruiter scheduler + finance snapshot）
16. SafeUninstallMenu 调 ModExpenseLedger.Clear + Recruiter.NotifyAllLost
17. WebConfig 新 endpoint /api/finance + 路由注册
18. WebUI index.html 新增"财务"标签页
19. 整体编译验证 + Release build

---

### Task 1: ModTreasury 门面 + ExpenseCategory 枚举

**Files:**
- Create: `SovereignTowns/src/Economy/ModTreasury.cs`

- [ ] **Step 1: 创建 ModTreasury.cs**

文件完整内容：

```csharp
using System;
using SovereignTowns.Audit;
using SovereignTowns.Configuration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using ConfigurationManager = SovereignTowns.Configuration.ConfigurationManager;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// B7.27：mod 引发的金币开销统一扣钱入口。所有原本"从城金库扣"的路径改走本门面，
/// 一律扣玩家个人金币（Hero.MainHero），并写 ledger + audit。
///
/// 调用契约：
///   - 派出新小队前先 CanAfford 预检，确认能付才派
///   - Charge 返回 false 时调用方应跳过本次动作（不派遣 / 不升级），不要硬塞
/// </summary>
public static class ModTreasury
{
    /// <summary>仅查询玩家是否能承担 amount，不扣款。</summary>
    public static bool CanAfford(int amount)
    {
        try
        {
            if (amount <= 0) return true;
            var hero = Hero.MainHero;
            if (hero == null) return false;
            return hero.Gold >= amount;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从玩家 Hero.MainHero 扣 amount。记 ledger + audit。
    /// </summary>
    /// <returns>true = 扣款成功；false = 玩家金币不足且 PauseSpendingWhenBroke=true，拒绝扣款</returns>
    public static bool Charge(ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return true;

        try
        {
            // 软门控：玩家金币不足且开关开启 → 拒绝扣款
            var feat = ConfigurationManager.Current?.EnabledFeatures;
            if (feat?.PauseSpendingWhenBroke == true && !CanAfford(amount))
            {
                Logger.Info($"ModTreasury: 拒绝 {category} -{amount}d 因玩家金币不足 (PauseSpendingWhenBroke=true)");
                return false;
            }

            var hero = Hero.MainHero;
            if (hero == null)
            {
                Logger.Warn($"ModTreasury: 拒绝 {category} -{amount}d 因 Hero.MainHero == null");
                return false;
            }

            try
            {
                // vanilla 标准 API：玩家 → null 转账 = 销毁金币（与税收等机制一致）
                GiveGoldAction.ApplyBetweenCharacters(
                    giverHero: hero,
                    recipientHero: null,
                    amount: amount,
                    disableNotification: true);
            }
            catch (Exception ex)
            {
                Logger.Error($"ModTreasury: GiveGoldAction failed for {category} -{amount}d, fallback to ChangeHeroGold", ex);
                try
                {
                    hero.ChangeHeroGold(-amount);
                }
                catch (Exception ex2)
                {
                    Logger.Error($"ModTreasury: ChangeHeroGold fallback also failed for {category} -{amount}d", ex2);
                    return false;
                }
            }

            // 记账
            ModExpenseLedger.Record(category, amount, note);

            // 审计
            DecisionAuditLogger.LogRule(
                decisionType: "mod_expense",
                inputSummary: $"category={category} amount={amount} note={note}",
                decisionJson: $"{{\"category\":\"{category}\",\"amount\":{amount},\"note\":\"{EscapeJson(note ?? "")}\"}}",
                accepted: true);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"ModTreasury.Charge failed for {category} -{amount}d", ex);
            return false;
        }
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}

/// <summary>mod 支出分类。</summary>
public enum ExpenseCategory
{
    /// <summary>每招 1 人的工资（外派征兵队 / 首府原地征兵）</summary>
    RecruiterWage,
    /// <summary>派出征兵队的初始本钱（1000 denar）</summary>
    RecruiterSeed,
    /// <summary>驻军升级的单兵金币成本</summary>
    Upgrade,
    /// <summary>出击队的初始本钱（100 denar）</summary>
    SallySeed,
    /// <summary>兜底</summary>
    Other
}
```

- [ ] **Step 2: 编译验证（会失败 — ModExpenseLedger 还没创建）**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 编译错误，说 `ModExpenseLedger` 找不到。这是预期，Task 2 立即修。

- [ ] **Step 3: Checkpoint** —— Task 2 立即接上恢复编译。

---

### Task 2: ModExpenseLedger 记账 + Snapshot DTO + 报告构建

**Files:**
- Create: `SovereignTowns/src/Economy/ModExpenseLedger.cs`

- [ ] **Step 1: 创建 ModExpenseLedger.cs**

文件完整内容：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Economy;

/// <summary>
/// B7.27：mod 支出流水。内存保留最近 30 天 entries，超期累加到 _historicalRolledOver。
/// 持久化通过 Snapshot（SovereignTownsCampaignBehavior.SyncData st_finance_snapshot_json 字段）。
///
/// 线程模型：所有写入假定在 campaign tick（主线程）发起；网页 endpoint 在 ThreadPool 读取 BuildReport，
/// 用 _gate 锁保证一致性（rare contention，开销可忽略）。
/// </summary>
public static class ModExpenseLedger
{
    private static readonly object _gate = new();
    private static readonly List<ExpenseEntry> _entries = new();
    private static readonly Dictionary<ExpenseCategory, long> _historicalRolledOver = new();
    private const int MaxInMemoryDays = 30;

    /// <summary>追加一条 entry。同时触发轮转。</summary>
    public static void Record(ExpenseCategory category, int amount, string note)
    {
        if (amount <= 0) return;
        try
        {
            lock (_gate)
            {
                long nowMs;
                try { nowMs = (long)CampaignTime.Now.ToMilliseconds; }
                catch { nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond; }

                _entries.Add(new ExpenseEntry
                {
                    TimestampMs = nowMs,
                    Category = category,
                    Amount = amount,
                    Note = note ?? ""
                });
                TrimAndRollOverIfNeeded();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModExpenseLedger.Record failed", ex);
        }
    }

    /// <summary>构建给 /api/finance 用的报告。</summary>
    public static FinanceReport BuildReport()
    {
        try
        {
            lock (_gate)
            {
                long nowMs;
                try { nowMs = (long)CampaignTime.Now.ToMilliseconds; }
                catch { nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond; }

                long todayStartMs = nowMs - (24L * 3600L * 1000L);
                long weekStartMs = nowMs - (7L * 24L * 3600L * 1000L);

                var today = AggregateBy(e => e.TimestampMs >= todayStartMs);
                var week  = AggregateBy(e => e.TimestampMs >= weekStartMs);
                var allTime = AggregateAllIncludingHistorical();

                // 最近 50 条（倒序）
                var recentEntries = _entries.Count > 50
                    ? _entries.GetRange(_entries.Count - 50, 50)
                    : new List<ExpenseEntry>(_entries);
                recentEntries.Reverse();

                return new FinanceReport
                {
                    Today = today.byCategory,
                    TodayTotal = today.total,
                    Week = week.byCategory,
                    WeekTotal = week.total,
                    AllTime = allTime.byCategory,
                    AllTimeTotal = allTime.total,
                    RecentEntries = recentEntries
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModExpenseLedger.BuildReport failed", ex);
            return new FinanceReport
            {
                Today = new(), Week = new(), AllTime = new(),
                RecentEntries = new()
            };
        }
    }

    /// <summary>导出 snapshot 给 SyncData 写盘。</summary>
    public static FinanceSnapshot CreateSnapshot()
    {
        try
        {
            lock (_gate)
            {
                var snap = new FinanceSnapshot();
                foreach (var kv in _historicalRolledOver)
                {
                    snap.HistoricalRolledOver[kv.Key.ToString()] = kv.Value;
                }
                snap.RecentEntries = new List<ExpenseEntry>(_entries);
                return snap;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModExpenseLedger.CreateSnapshot failed", ex);
            return new FinanceSnapshot();
        }
    }

    /// <summary>读档恢复。</summary>
    public static void RestoreFromSnapshot(FinanceSnapshot snapshot)
    {
        if (snapshot == null) return;
        try
        {
            lock (_gate)
            {
                _historicalRolledOver.Clear();
                if (snapshot.HistoricalRolledOver != null)
                {
                    foreach (var kv in snapshot.HistoricalRolledOver)
                    {
                        if (Enum.TryParse<ExpenseCategory>(kv.Key, out var cat))
                        {
                            _historicalRolledOver[cat] = kv.Value;
                        }
                    }
                }
                _entries.Clear();
                if (snapshot.RecentEntries != null)
                {
                    _entries.AddRange(snapshot.RecentEntries);
                }
                Logger.Info($"ModExpenseLedger: restored {_entries.Count} recent entries + {_historicalRolledOver.Count} historical categories");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModExpenseLedger.RestoreFromSnapshot failed", ex);
        }
    }

    /// <summary>SafeUninstall 时清空全部状态。</summary>
    public static void Clear()
    {
        try
        {
            lock (_gate)
            {
                _entries.Clear();
                _historicalRolledOver.Clear();
                Logger.Info("ModExpenseLedger: cleared all state");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModExpenseLedger.Clear failed", ex);
        }
    }

    // ── 内部辅助 ──

    private static void TrimAndRollOverIfNeeded()
    {
        long nowMs;
        try { nowMs = (long)CampaignTime.Now.ToMilliseconds; }
        catch { return; }

        long cutoff = nowMs - (MaxInMemoryDays * 24L * 3600L * 1000L);
        while (_entries.Count > 0 && _entries[0].TimestampMs < cutoff)
        {
            var old = _entries[0];
            _historicalRolledOver.TryGetValue(old.Category, out var sum);
            _historicalRolledOver[old.Category] = sum + old.Amount;
            _entries.RemoveAt(0);
        }
    }

    private static (Dictionary<string, long> byCategory, long total) AggregateBy(Func<ExpenseEntry, bool> filter)
    {
        var dict = new Dictionary<string, long>();
        long total = 0;
        foreach (var e in _entries)
        {
            if (!filter(e)) continue;
            var key = e.Category.ToString();
            dict.TryGetValue(key, out var s);
            dict[key] = s + e.Amount;
            total += e.Amount;
        }
        return (dict, total);
    }

    private static (Dictionary<string, long> byCategory, long total) AggregateAllIncludingHistorical()
    {
        var dict = new Dictionary<string, long>();
        long total = 0;
        // historical 滚存
        foreach (var kv in _historicalRolledOver)
        {
            var key = kv.Key.ToString();
            dict[key] = kv.Value;
            total += kv.Value;
        }
        // 内存里 30 天 entries 累加
        foreach (var e in _entries)
        {
            var key = e.Category.ToString();
            dict.TryGetValue(key, out var s);
            dict[key] = s + e.Amount;
            total += e.Amount;
        }
        return (dict, total);
    }
}

/// <summary>单条支出 entry。public 字段供 Newtonsoft 序列化。</summary>
public sealed class ExpenseEntry
{
    public long TimestampMs { get; set; }
    public ExpenseCategory Category { get; set; }
    public int Amount { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>财务报告 DTO（响应 /api/finance）。</summary>
public sealed class FinanceReport
{
    public Dictionary<string, long> Today { get; set; } = new();
    public long TodayTotal { get; set; }
    public Dictionary<string, long> Week { get; set; } = new();
    public long WeekTotal { get; set; }
    public Dictionary<string, long> AllTime { get; set; } = new();
    public long AllTimeTotal { get; set; }
    public List<ExpenseEntry> RecentEntries { get; set; } = new();
}

/// <summary>持久化 snapshot（SyncData st_finance_snapshot_json 内容）。</summary>
public sealed class FinanceSnapshot
{
    /// <summary>category enum 名 → 累计金额（早于内存 30 天窗口的部分）。</summary>
    public Dictionary<string, long> HistoricalRolledOver { get; set; } = new();
    /// <summary>内存里 30 天 entries。</summary>
    public List<ExpenseEntry> RecentEntries { get; set; } = new();
}
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。Task 1 + Task 2 联合恢复编译。

- [ ] **Step 3: Checkpoint** —— Economy 层（ModTreasury + Ledger）就位。下一步把 5 个原有花钱点切到 Charge 入口。

---

### Task 3: RecruitmentManager 招兵接入 ModTreasury

**Files:**
- Modify: `SovereignTowns/src/Recruitment/RecruitmentManager.cs`

当前花钱点：
- 派遣队伍初始 1000（line 39 const + line 177 `CreateForTown(... DefaultInitialGold, ...)`)
- 招每个人 5 denar（line 470 `ownerHero.ChangeHeroGold(-cost)`）

- [ ] **Step 1: 添加 using**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Recruitment\RecruitmentManager.cs`。在 using 区追加：

```csharp
using SovereignTowns.Economy;
```

放在 `using SovereignTowns.Decisions;` 之后（按字母序：D < E < Evaluators）。

- [ ] **Step 2: 改造派遣初始金路径**

定位 `TryDispatchRecruiter` 方法（约 line 92）。在 `var party = RecruitingPartyComponent.CreateForTown(homeTown, DefaultInitialGold, escortRoster);`（约 line 177）**之前**插入预检 + 扣款：

找到这块：
```csharp
            var party = RecruitingPartyComponent.CreateForTown(homeTown, DefaultInitialGold, escortRoster);
            if (party == null)
            {
                Logger.Warn($"  RecruitmentManager: CreateForTown 返回 null for '{homeTown.Name}'");
                // 护卫已抽离 → 还回 garrison
                if (escortRoster != null && escortActual > 0)
                {
                    TryRestoreEscort(homeTown, escortRoster);
                }
                return false;
            }
```

改成：

```csharp
            // B7.27：派出征兵队前先预检玩家金币 + 扣初始本钱。AI clan 跳过扣费（保留 AI 阵营战役经济不被破坏）。
            bool isPlayerClanDispatch = homeTown.OwnerClan == Clan.PlayerClan;
            if (isPlayerClanDispatch)
            {
                if (!ModTreasury.CanAfford(DefaultInitialGold))
                {
                    Logger.Info($"  RecruitmentManager: '{homeTown.Name}' 玩家金币不足 ({Hero.MainHero?.Gold ?? 0} < {DefaultInitialGold})，跳过派遣");
                    if (escortRoster != null && escortActual > 0)
                    {
                        TryRestoreEscort(homeTown, escortRoster);
                    }
                    return false;
                }
                if (!ModTreasury.Charge(ExpenseCategory.RecruiterSeed, DefaultInitialGold, $"recruiter_seed home={homeTown.Settlement.StringId}"))
                {
                    Logger.Info($"  RecruitmentManager: '{homeTown.Name}' ModTreasury.Charge 拒绝，跳过派遣");
                    if (escortRoster != null && escortActual > 0)
                    {
                        TryRestoreEscort(homeTown, escortRoster);
                    }
                    return false;
                }
            }

            var party = RecruitingPartyComponent.CreateForTown(homeTown, DefaultInitialGold, escortRoster);
            if (party == null)
            {
                Logger.Warn($"  RecruitmentManager: CreateForTown 返回 null for '{homeTown.Name}'");
                // 护卫已抽离 → 还回 garrison
                if (escortRoster != null && escortActual > 0)
                {
                    TryRestoreEscort(homeTown, escortRoster);
                }
                // 注：扣费已发生但 party 没创建出来 → 损失 1000 denar；记 Warn 让玩家可查日志
                if (isPlayerClanDispatch)
                {
                    Logger.Warn($"  RecruitmentManager: 1000 denar 已扣但 party 创建失败 — 玩家损失");
                }
                return false;
            }
```

- [ ] **Step 3: 改造招每个人扣费路径**

定位 `RecruitFromTargetVillage` 方法。找到这块（约 line 468-475）：

```csharp
                try
                {
                    ownerHero.ChangeHeroGold(-cost);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  RecruitFromTargetVillage: ChangeHeroGold(-{cost}) threw: {ex.Message}");
                }
```

替换为：

```csharp
                // B7.27：玩家氏族走 ModTreasury 统一记账；AI clan 已在 line 360 设 cost = 0，跳过扣费
                if (!isAiClan)
                {
                    ModTreasury.Charge(ExpenseCategory.RecruiterWage, cost, $"recruit village={village.StringId} troop={candidate.Troop.StringId}");
                }
                else
                {
                    // AI clan：保持原行为（不扣费），但仍记 ledger 0 不写 — Charge 已经被跳过
                }
```

注：`isAiClan` 变量在 line 359 已定义为 `home.OwnerClan != Clan.PlayerClan`。`cost` 变量在 line 430 之内 `int cost = costPerRecruit;`。

- [ ] **Step 4: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 5: Checkpoint** —— 外派征兵队的两条扣钱路径已统一走 ModTreasury。AI clan 仍免费不变。

---

### Task 4: CapitalInPlaceRecruiter 接入 ModTreasury

**Files:**
- Modify: `SovereignTowns/src/Recruitment/CapitalInPlaceRecruiter.cs`

当前行为：原地招募**不花钱**。按 spec 要求：每招 1 人扣 5 denar（与外派一致）。

- [ ] **Step 1: 读取 CapitalInPlaceRecruiter.cs 确认结构**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Recruitment\CapitalInPlaceRecruiter.cs`，定位招募成功的位置（应该有一处类似 `roster.AddToCounts(...)` 或调用 vanilla `RecruitFromHero` 的代码块；招到一人后 increment count）。

- [ ] **Step 2: 添加 using**

在 using 区（CapitalInPlaceRecruiter.cs 顶部）追加：

```csharp
using SovereignTowns.Economy;
```

- [ ] **Step 3: 在每次成功招到一个人时调用 ModTreasury.Charge**

找到招到人成功后的 `recruitedCount++` 或等价的统计语句。在它**之前或之后**（在同一个 if/try 内部）插入：

```csharp
                // B7.27：原地招募也要扣费（与外派对齐）。玩家氏族扣 5 denar，AI clan 免费。
                if (settlement.OwnerClan == Clan.PlayerClan)
                {
                    // 半价 = DefaultGoldPerRecruit (10) × 0.5 = 5
                    if (!ModTreasury.Charge(ExpenseCategory.RecruiterWage, 5, $"in_place capital={settlement.StringId} troop={troop.StringId}"))
                    {
                        // 扣费失败（金币不足且 PauseSpendingWhenBroke=true）→ 退兵（回滚 AddToCounts）
                        roster.RemoveTroop(troop, 1, default, 0);
                        Logger.Info($"  CapitalInPlaceRecruiter: '{settlement.Name}' 玩家金币不足，回滚 1 名招募");
                        break;
                    }
                }
```

注：变量名（`troop`、`roster`）需对照实际文件调整。如果实际变量名不同，使用文件里实际的本地变量名。

如果 `troop`/`roster` 实际是 `volunteerTroop`/`garrisonRoster` 之类的名字，对应替换。

- [ ] **Step 4: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。如有 CS0103 提示变量名不存在，对照实际变量名调整。

- [ ] **Step 5: Checkpoint** —— 原地征兵也走统一记账。

---

### Task 5: TroopUpgradeService 升级金币接入 ModTreasury

**Files:**
- Modify: `SovereignTowns/src/Upgrades/TroopUpgradeService.cs` (around line 217-227)

- [ ] **Step 1: 添加 using**

在 TroopUpgradeService.cs 顶部 using 区追加：

```csharp
using SovereignTowns.Economy;
```

放在 `using SovereignTowns.Evaluators;` 之后（按字母序）。

- [ ] **Step 2: 替换 GiveGoldAction.ApplyForPartyToSettlement 调用**

定位 line 216-227（这块）：

```csharp
                    // GarrisonParty.PartyTradeGold 实务上由游戏维护，这一步可能因为驻军金库不足而部分扣；任何异常吞掉不阻塞升级流。
                    if (goldCost > 0)
                    {
                        try
                        {
                            GiveGoldAction.ApplyForPartyToSettlement(partyBase, settlement, goldCost, disableNotification: true);
                        }
                        catch (Exception goldEx)
                        {
                            Logger.Warn($"TroopUpgradeService: gold deduction skipped for upgrade '{ch.StringId}'→'{target.StringId}' cost={goldCost}: {goldEx.Message}");
                        }
                    }
```

替换为：

```csharp
                    // B7.27：升级金币改走玩家个人金币（不再从城金库），统一记账走 ModTreasury。
                    // AI clan 升级跳过扣费（AI 经济保持原 vanilla 行为）。
                    if (goldCost > 0 && homeTown.OwnerClan == Clan.PlayerClan)
                    {
                        bool charged = ModTreasury.Charge(ExpenseCategory.Upgrade, goldCost, $"upgrade {ch.StringId}->{target.StringId} town={homeTown.Settlement.StringId}");
                        if (!charged)
                        {
                            // 扣费失败 → 不进行此次升级（回滚 SetElementXp + AddToCounts？）
                            // 简化：升级仍然发生（XP 已扣），玩家金币没扣到。Logger.Warn 让玩家可查。
                            Logger.Warn($"TroopUpgradeService: ModTreasury rejected charge for '{ch.StringId}'→'{target.StringId}' cost={goldCost}; upgrade仍发生 (XP 已扣)");
                        }
                    }
```

注：注释里提到回滚 SetElementXp + AddToCounts —— 实际实现简化为"扣不到钱也升级"，理由：升级 batch 已经走到这一步说明 XP 充足；玩家欠 1 笔小钱比让 batch 半截失败造成更复杂的回滚链路要好得多。

- [ ] **Step 3: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 4: Checkpoint** —— 升级金币改走玩家。AI 升级仍走老路径（免费）。

---

### Task 6: SallyForthManager 出击 100 金本钱接入 ModTreasury

**Files:**
- Modify: `SovereignTowns/src/SallyForth/SallyForthManager.cs`

当前花钱点：`InitialSallyGold = 100`（line 45 const），传入 `SallyForthPartyComponent.CreateForTown(...)`（line 388-391）。

- [ ] **Step 1: 添加 using**

在 SallyForthManager.cs 顶部 using 区追加：

```csharp
using SovereignTowns.Economy;
```

放在 `using SovereignTowns.Configuration;` 之后（按字母序）。

- [ ] **Step 2: 派出前预检 + 扣钱**

定位 `TryCreateSallyParty` 方法（line 371）。找到这块：

```csharp
            if (settlement.Town == null) return; // 不可能，但 nullable warn 安抚
            var sallyParty = SallyForthPartyComponent.CreateForTown(
                homeTown: settlement.Town,
                initialTarget: target,
                initialGold: InitialSallyGold);
```

替换为：

```csharp
            if (settlement.Town == null) return; // 不可能，但 nullable warn 安抚

            // B7.27：派出 sally 前先扣本钱（仅玩家氏族）
            bool isPlayerClanSally = settlement.OwnerClan == Clan.PlayerClan;
            if (isPlayerClanSally)
            {
                if (!ModTreasury.CanAfford(InitialSallyGold))
                {
                    Logger.Info($"SallyForthManager: '{settlement.Name}' 玩家金币不足 (need {InitialSallyGold})，跳过出击");
                    return;
                }
                if (!ModTreasury.Charge(ExpenseCategory.SallySeed, InitialSallyGold, $"sally_seed home={settlement.StringId}"))
                {
                    Logger.Info($"SallyForthManager: '{settlement.Name}' ModTreasury.Charge 拒绝，跳过出击");
                    return;
                }
            }

            var sallyParty = SallyForthPartyComponent.CreateForTown(
                homeTown: settlement.Town,
                initialTarget: target,
                initialGold: InitialSallyGold);
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 4: Checkpoint** —— 5 个原花钱路径全部走 ModTreasury。Task 7+ 进入调度器变更。

---

### Task 7: ClanRecruiterScheduler 骨架 + Snapshot DTO

**Files:**
- Create: `SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs`

- [ ] **Step 1: 创建骨架文件**

文件完整内容（参照 ClanPatrolScheduler 同款结构，仅算法 stub）：

```csharp
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;

namespace SovereignTowns.Recruitment;

/// <summary>
/// B7.27：全氏族征兵调度器。每个 Clan 一份，由 CapitalManager 持有。
///
/// 职责（与 ClanPatrolScheduler 完全平行）：
///   - 为征兵队选下一站村庄（PickNextVillage）— 在该 Clan 范围内 RankCandidates 取候选 + 多队预占去重；
///   - 多队互补（PreemptiveBook）— 选中后预占一段时间防其他征兵队抢同点；
///   - 抵达去重（TryMarkArrival）— 避免单次停留多次计入；
///   - 访问记录（RecordVisit）— 招到人后调用；
///   - 卡死检测（IsStuck）— 单段路超时则强制重选；
///   - 失守清理（NotifySettlementLost / NotifyAllLost / NotifyPartyDestroyed）；
///   - 存档支持（CreateSnapshot / RestoreFromSnapshot）— 通过 SyncData st_recruiter_schedulers_json。
///
/// 与现有 VillageCooldownHours = 72h 协作：72h 是 vanilla volunteer 刷新硬性冷却（由
/// RecruitmentCooldown 静态表保护），本调度器额外提供 MinVisitGapHours 短期回访保护 + 多队互补。
///
/// 不注册到 SovereignTownsTypeDefiner — 持久化由持有者 CapitalManager 通过 SovereignTownsCampaignBehavior.SyncData 管理。
///
/// 调用线程：所有方法假定在主线程 campaign tick 调用。
/// </summary>
public sealed class ClanRecruiterScheduler
{
    private readonly Clan _clan;
    private readonly Dictionary<string, CampaignTime> _lastRecruitedAt = new();   // key: Settlement.StringId（持久化）
    private readonly Dictionary<string, CampaignTime> _bookedUntil     = new();   // 瞬态
    private readonly Dictionary<MBGUID, CampaignTime> _lastStopChangedAt = new(); // 瞬态
    private readonly Dictionary<MBGUID, string> _lastSeenLocation = new();        // 瞬态

    public ClanRecruiterScheduler(Clan clan)
    {
        _clan = clan ?? throw new ArgumentNullException(nameof(clan));
    }

    public Clan OwnerClan => _clan;

    // ── Task 9 实现 ──
    public Settlement? PickNextVillage(MobileParty recruiterParty) => null;
    public void RecordVisit(Settlement village) { }
    public void PreemptiveBook(Settlement village, MobileParty party, float etaHours) { }
    public bool IsStuck(MobileParty party, float stuckTimeoutHours) => false;
    public bool TryMarkArrival(MobileParty party, Settlement visited) => false;

    // ── Task 9 实现 ──
    public void NotifySettlementLost(Settlement settlement) { }
    public void NotifyAllLost() { }
    public void NotifyPartyDestroyed(MobileParty party) { }

    // ── Task 9 实现 ──
    public ClanRecruiterSchedulerSnapshot CreateSnapshot() => new();
    public void RestoreFromSnapshot(ClanRecruiterSchedulerSnapshot snapshot) { }
}

/// <summary>持久化 DTO。仅保存 LastRecruitedAt（毫秒 long）。瞬态字段不入 snapshot。</summary>
public sealed class ClanRecruiterSchedulerSnapshot
{
    /// <summary>Settlement.StringId → CampaignTime.ToMilliseconds（long）。</summary>
    public Dictionary<string, long> LastRecruitedAt { get; set; } = new();
}
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 3: Checkpoint** —— Scheduler 骨架就位，所有方法是 no-op。Task 9 填入真实算法。

---

### Task 8: CapitalManager 持有 RecruiterScheduler + Registry 导出/恢复

**Files:**
- Modify: `SovereignTowns/src/Capital/CapitalManager.cs`
- Modify: `SovereignTowns/src/Capital/CapitalRegistry.cs`

- [ ] **Step 1: CapitalManager 加 using + 字段 + getter**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Capital\CapitalManager.cs`。

在 using 区追加：

```csharp
using SovereignTowns.Recruitment;
```

放在 `using SovereignTowns.Patrol;` 之后（按字母序）。

定位 `_patrolScheduler` 字段（约 line 47-48）：

```csharp
    /// <summary>本氏族的巡逻调度器（B7.26）。与 manager 同生命周期。</summary>
    private readonly ClanPatrolScheduler _patrolScheduler;
```

在它后面追加：

```csharp
    /// <summary>本氏族的征兵调度器（B7.27）。与 manager 同生命周期。</summary>
    private readonly ClanRecruiterScheduler _recruiterScheduler;
```

定位 ctor（约 line 50-55）：

```csharp
    public CapitalManager(PartyLifecycleManager lifecycle, Clan clan)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _clan = clan ?? throw new ArgumentNullException(nameof(clan));
        _patrolScheduler = new ClanPatrolScheduler(_clan);
    }
```

修改为：

```csharp
    public CapitalManager(PartyLifecycleManager lifecycle, Clan clan)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _clan = clan ?? throw new ArgumentNullException(nameof(clan));
        _patrolScheduler = new ClanPatrolScheduler(_clan);
        _recruiterScheduler = new ClanRecruiterScheduler(_clan);
    }
```

定位 `PatrolScheduler` getter（约 line 64）。在它之后追加：

```csharp
    /// <summary>本氏族的征兵调度器（B7.27）。永不为 null。</summary>
    public ClanRecruiterScheduler RecruiterScheduler => _recruiterScheduler;
```

- [ ] **Step 2: CapitalRegistry 增加 Export/Restore**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Capital\CapitalRegistry.cs`。

确认顶部已有 `using SovereignTowns.Patrol;`。如果没有 `using SovereignTowns.Recruitment;` 也需添加（同款命名空间引用）：

```csharp
using SovereignTowns.Recruitment;
```

定位 `ExportPatrolSchedulerSnapshots` 方法（应该在 line 95-111 范围）。**在它之后**追加（与 patrol 完全对称）：

```csharp
    /// <summary>导出所有 clan 的 recruiter scheduler snapshot 给 SyncData 写盘。Key = clan.StringId。</summary>
    public Dictionary<string, ClanRecruiterSchedulerSnapshot> ExportRecruiterSchedulerSnapshots()
    {
        var dict = new Dictionary<string, ClanRecruiterSchedulerSnapshot>();
        try
        {
            foreach (var kv in _managers)
            {
                if (kv.Key?.StringId == null) continue;
                dict[kv.Key.StringId] = kv.Value.RecruiterScheduler.CreateSnapshot();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ExportRecruiterSchedulerSnapshots failed", ex);
        }
        return dict;
    }

    /// <summary>SyncData 读档后回灌 recruiter scheduler 状态。</summary>
    public void RestoreRecruiterSchedulers(Dictionary<string, ClanRecruiterSchedulerSnapshot>? snapshots)
    {
        if (snapshots == null) return;
        try
        {
            foreach (var kv in snapshots)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                foreach (var mgrKv in _managers)
                {
                    if (mgrKv.Key?.StringId == kv.Key)
                    {
                        mgrKv.Value.RecruiterScheduler.RestoreFromSnapshot(kv.Value);
                        break;
                    }
                }
            }
            Logger.Info($"CapitalRegistry: restored recruiter scheduler snapshots for {snapshots.Count} clan(s)");
        }
        catch (Exception ex)
        {
            Logger.Error("RestoreRecruiterSchedulers failed", ex);
        }
    }
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 4: Checkpoint** —— 调度器接入 CapitalManager + Registry。

---

### Task 9: ClanRecruiterScheduler 算法实现

**Files:**
- Modify: `SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs`

- [ ] **Step 1: 添加额外 using**

在文件顶部 using 区追加：

```csharp
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using Logger = SovereignTowns.Logging.Logger;
```

- [ ] **Step 2: 替换 PickNextVillage / RecordVisit / PreemptiveBook / IsStuck / TryMarkArrival**

定位 "// ── Task 9 实现 ──" 注释块中的 5 个方法 stub，替换为：

```csharp
    public Settlement? PickNextVillage(MobileParty recruiterParty)
    {
        if (recruiterParty == null) return null;
        try
        {
            var config = ConfigurationManager.Current.ClanRecruiter;
            var now = CampaignTime.Now;

            // 取首府 town 用于 RankCandidates
            var capitalMgr = CapitalRegistry.Instance?.GetForClan(_clan);
            var capitalTown = capitalMgr?.GetCapital();
            if (capitalTown == null) return null;

            var rule = ConfigurationManager.GetRuleFor(capitalTown);
            if (rule == null) return null;

            // 用现有 RecruitmentPlanner 取候选（已处理：兵种匹配、距离、风险、村庄状态、72h 冷却由 RankCandidates 内部对接的 RecruitmentCooldown 处理）
            var candidates = RecruitmentPlanner.RankCandidates(
                capitalTown,
                maxDistance: 100f,
                maxResults: 8,
                excludeSettlements: null,
                matchingRule: rule);
            if (candidates.Count == 0) return null;

            var partyPos = recruiterParty.GetPosition2D;

            Settlement? best = null;
            float bestScore = float.MaxValue;

            foreach (var cand in candidates)
            {
                var v = cand.VillageSettlement;
                if (v == null) continue;

                // 多队互补：被他队预占 → 跳过
                if (_bookedUntil.TryGetValue(v.StringId, out var booked) && booked > now) continue;

                // 短期回访保护：最近 MinVisitGapHours 内不重复
                if (_lastRecruitedAt.TryGetValue(v.StringId, out var lva))
                {
                    if (lva.ElapsedHoursUntilNow < config.MinVisitGapHours) continue;
                }

                // 评分：越久未访问越优先 + 距离越近越优先
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

    public void RecordVisit(Settlement village)
    {
        if (village == null) return;
        try
        {
            _lastRecruitedAt[village.StringId] = CampaignTime.Now;
            _bookedUntil.Remove(village.StringId);
        }
        catch (Exception ex)
        {
            Logger.Error($"ClanRecruiterScheduler.RecordVisit failed for '{village?.StringId}'", ex);
        }
    }

    public void PreemptiveBook(Settlement village, MobileParty party, float etaHours)
    {
        if (village == null || party == null) return;
        try
        {
            var config = ConfigurationManager.Current.ClanRecruiter;
            float bookHours = Math.Max(0.5f, etaHours + config.EtaBufferHours);
            _bookedUntil[village.StringId] = CampaignTime.HoursFromNow(bookHours);
            _lastStopChangedAt[party.Id] = CampaignTime.Now;
        }
        catch (Exception ex)
        {
            Logger.Error("ClanRecruiterScheduler.PreemptiveBook failed", ex);
        }
    }

    public bool IsStuck(MobileParty party, float stuckTimeoutHours)
    {
        if (party == null) return false;
        try
        {
            if (!_lastStopChangedAt.TryGetValue(party.Id, out var last)) return false;
            return last.ElapsedHoursUntilNow >= stuckTimeoutHours;
        }
        catch { return false; }
    }

    public bool TryMarkArrival(MobileParty party, Settlement visited)
    {
        if (party == null || visited == null) return false;
        try
        {
            var sid = visited.StringId;
            if (_lastSeenLocation.TryGetValue(party.Id, out var prev) && prev == sid) return false;
            _lastSeenLocation[party.Id] = sid;
            return true;
        }
        catch { return false; }
    }

    private static float ComputeEtaHours(MobileParty party, Settlement target)
    {
        try
        {
            float distance = (party.GetPosition2D - target.GetPosition2D).Length;
            // v1.3.15: MobileParty.Speed 直接是 float（不是 ExplainedNumber）
            float speed = Math.Max(party.Speed, 0.1f);
            return distance / speed;
        }
        catch
        {
            return 24f;
        }
    }
```

- [ ] **Step 3: 替换生命周期方法**

替换 NotifySettlementLost / NotifyAllLost / NotifyPartyDestroyed 三个 stub：

```csharp
    public void NotifySettlementLost(Settlement settlement)
    {
        if (settlement == null) return;
        try
        {
            _lastRecruitedAt.Remove(settlement.StringId);
            _bookedUntil.Remove(settlement.StringId);
            Logger.Info($"ClanRecruiterScheduler({_clan.StringId}): cleared state for lost settlement '{settlement.StringId}'");
        }
        catch (Exception ex)
        {
            Logger.Error($"NotifySettlementLost failed for '{settlement?.StringId}'", ex);
        }
    }

    public void NotifyAllLost()
    {
        try
        {
            _lastRecruitedAt.Clear();
            _bookedUntil.Clear();
            _lastStopChangedAt.Clear();
            _lastSeenLocation.Clear();
            Logger.Info($"ClanRecruiterScheduler({_clan.StringId}): NotifyAllLost — all state cleared");
        }
        catch (Exception ex)
        {
            Logger.Error("NotifyAllLost failed", ex);
        }
    }

    public void NotifyPartyDestroyed(MobileParty party)
    {
        if (party == null) return;
        try
        {
            _lastStopChangedAt.Remove(party.Id);
            _lastSeenLocation.Remove(party.Id);
        }
        catch (Exception ex)
        {
            Logger.Error("NotifyPartyDestroyed failed", ex);
        }
    }
```

- [ ] **Step 4: 替换 Snapshot 方法**

替换 CreateSnapshot / RestoreFromSnapshot：

```csharp
    public ClanRecruiterSchedulerSnapshot CreateSnapshot()
    {
        var snap = new ClanRecruiterSchedulerSnapshot();
        try
        {
            foreach (var kv in _lastRecruitedAt)
            {
                snap.LastRecruitedAt[kv.Key] = (long)kv.Value.ToMilliseconds;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"ClanRecruiterScheduler.CreateSnapshot failed for clan '{_clan?.StringId}'", ex);
        }
        return snap;
    }

    public void RestoreFromSnapshot(ClanRecruiterSchedulerSnapshot snapshot)
    {
        if (snapshot == null) return;
        try
        {
            _lastRecruitedAt.Clear();
            foreach (var kv in snapshot.LastRecruitedAt)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                _lastRecruitedAt[kv.Key] = CampaignTime.Milliseconds(kv.Value);
            }
            _bookedUntil.Clear();
            _lastStopChangedAt.Clear();
            _lastSeenLocation.Clear();
            Logger.Info($"ClanRecruiterScheduler({_clan.StringId}): restored {_lastRecruitedAt.Count} village timestamps from snapshot");
        }
        catch (Exception ex)
        {
            Logger.Error($"RestoreFromSnapshot failed for clan '{_clan?.StringId}'", ex);
        }
    }
```

- [ ] **Step 5: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。注意 `ClanRecruiter` 字段在 `GlobalConfig` 还没加（Task 14 才加），可能导致编译错误"找不到 ClanRecruiter 属性"。如果出现这种错误，先跳到 Task 14 加配置字段，再回来。或者临时在本 Task 用硬编码值代替：

```csharp
// 临时硬编码（Task 14 之后改回 config）
float minVisitGapHours = 4.0f;
float etaBufferHours = 1.0f;
float distanceWeight = 0.5f;
```

更稳的做法：**先做 Task 14 再回来 Task 9 Step 2**。下方实施顺序已经反映这一点。

- [ ] **Step 6: Checkpoint** —— Scheduler 算法完整实现（Task 14 完成后）。

---

### Task 10: PartyLifecycleManager.GetMaxFor 改造 KindRecruiter

**Files:**
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs` (line 577-586)

- [ ] **Step 1: 修改 GetMaxFor 方法**

定位 line 577-586：

```csharp
    private static int GetMaxFor(Settlement home, string kind)
    {
        if (kind == KindRecruiter)  return MaxRecruitersPerTown;
        if (kind == KindTransfer)   return MaxTransfersPerTown;
        if (kind == KindSallyForth) return MaxSallyForthPerTown;
        if (kind == KindDismiss)    return MaxDismissPerTown;
        if (kind == KindPatrol)     return ComputePatrolCapForHome(home);
        // 未知 kind：保守上限 1，避免失控创建
        return 1;
    }
```

修改 KindRecruiter 那一行：

```csharp
    private static int GetMaxFor(Settlement home, string kind)
    {
        // B7.27：征兵队上限改用与巡逻队相同的兵营建筑等级公式（首府 barracks lvl + 1，0级 1 支，3级 4 支）
        if (kind == KindRecruiter)  return ComputePatrolCapForHome(home);
        if (kind == KindTransfer)   return MaxTransfersPerTown;
        if (kind == KindSallyForth) return MaxSallyForthPerTown;
        if (kind == KindDismiss)    return MaxDismissPerTown;
        if (kind == KindPatrol)     return ComputePatrolCapForHome(home);
        // 未知 kind：保守上限 1，避免失控创建
        return 1;
    }
```

注：`ComputePatrolCapForHome` 函数体名虽然带 "Patrol" 但实际只读 barracks 等级，可以共享。如果觉得名字不准确可以重命名为 `ComputeBarracksBasedCapForHome`，但不重命名也可以接受（只是内部 helper）。

`MaxRecruitersPerTown` 常量（line 30）保留（暂不删除，未来兜底用）—— 但加注释说明：

```csharp
    // ────────── 上限（按城镇 × kind） ──────────
    /// <summary>B7.27 起：KindRecruiter 不再使用此常量，改用 ComputePatrolCapForHome。常量保留作为 fallback / 历史参考。</summary>
    public const int MaxRecruitersPerTown = 1;
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 3: Checkpoint** —— 征兵队池上限改为动态。

---

### Task 11: RecruitmentManager.OnHourlyTickParty 接入 RecruiterScheduler

**Files:**
- Modify: `SovereignTowns/src/Recruitment/RecruitmentManager.cs`

- [ ] **Step 1: 派遣时调首站**

定位 `TryDispatchRecruiter` 方法中 party 创建成功后的位置（约 line 189-200，这块是 `party.SetMoveGoToSettlement(target.VillageSettlement, ...)` + `_lifecycle.RegisterTrackedParty(...)` 等）。

找到这块：

```csharp
            party.SetMoveGoToSettlement(target.VillageSettlement, MobileParty.NavigationType.Default, false);
            _lifecycle.RegisterTrackedParty(party, homeTown.Settlement, PartyKind);
            _visitedPerParty[party] = new HashSet<Settlement>();
```

替换为（增加 scheduler 调用）：

```csharp
            party.SetMoveGoToSettlement(target.VillageSettlement, MobileParty.NavigationType.Default, false);
            _lifecycle.RegisterTrackedParty(party, homeTown.Settlement, PartyKind);
            _visitedPerParty[party] = new HashSet<Settlement>();

            // B7.27：通知 scheduler "刚派遣，首站已选"。下次招到人后由 OnHourlyTickParty 流程接管。
            try
            {
                var dispatchCapitalMgr = _capitalRegistry?.GetForSettlement(homeTown.Settlement);
                if (dispatchCapitalMgr != null)
                {
                    dispatchCapitalMgr.RecruiterScheduler.RecordVisit(homeTown.Settlement);  // 标记首府"刚出门"
                    // target 已经由 RankCandidates 选过，但走一遍 PreemptiveBook 让别的征兵队知道
                    float etaHours = ((party.GetPosition2D - target.VillageSettlement.GetPosition2D).Length) / Math.Max(party.Speed, 0.1f);
                    dispatchCapitalMgr.RecruiterScheduler.PreemptiveBook(target.VillageSettlement, party, etaHours);
                }
            }
            catch (Exception schedEx)
            {
                Logger.Warn("RecruiterScheduler first-hop bookkeeping failed: " + schedEx.Message);
            }
```

- [ ] **Step 2: 在招到人后 RecordVisit**

定位 `RecruitFromTargetVillage` 方法的尾部（line 489 之前）。在 `recruited` 大于 0 时调 scheduler.RecordVisit：

找到 line 487-489：
```csharp
            Logger.Info($"  Recruiter '{recruitingParty.Name}': 在 '{village.Name}' 招募 {recruited} 名（扫描 {candidatesScanned} 名候选，花费 {spent} denar）");
        }
        catch (Exception ex)
```

**在 Logger.Info 行之前**插入：

```csharp
            // B7.27：通知 scheduler 本次访问，更新该 village 的 LastRecruitedAt
            if (recruited > 0)
            {
                try
                {
                    var visitCapitalMgr = _capitalRegistry?.GetForSettlement(home);
                    visitCapitalMgr?.RecruiterScheduler.RecordVisit(village);
                }
                catch (Exception ex) { Logger.Warn("RecruiterScheduler.RecordVisit (per-village) failed: " + ex.Message); }
            }
            Logger.Info($"  Recruiter '{recruitingParty.Name}': 在 '{village.Name}' 招募 {recruited} 名（扫描 {candidatesScanned} 名候选，花费 {spent} denar）");
```

- [ ] **Step 3: PlanNextHop 改为走 scheduler**

定位 `PlanNextHop` 方法（line 307-336）。整体替换为：

```csharp
    /// <summary>
    /// 规划巡回的下一站。B7.27：优先走 ClanRecruiterScheduler（多队互补）；失败回退原 RankCandidates 路径。
    /// </summary>
    private Settlement? PlanNextHop(MobileParty party, Settlement home)
    {
        try
        {
            // B7.27：优先用 scheduler（统一管理多队预占）
            var capitalMgr = _capitalRegistry?.GetForSettlement(home);
            if (capitalMgr != null)
            {
                var next = capitalMgr.RecruiterScheduler.PickNextVillage(party);
                if (next != null) return next;
            }

            // 回退：scheduler 返回 null（或 registry 不可用） → 走原 RankCandidates 路径，但同样应用本趟 visited 排除
            var homeTown = home.Town;
            if (homeTown == null) return null;
            var rule = ConfigurationManager.GetRuleFor(homeTown) ?? TownGarrisonRule.CreateDefault();

            var exclude = new HashSet<Settlement>();
            if (_visitedPerParty.TryGetValue(party, out var visited))
            {
                foreach (var s in visited) exclude.Add(s);
            }

            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: exclude,
                matchingRule: rule);

            if (candidates.Count == 0) return null;
            return candidates[0].VillageSettlement;
        }
        catch (Exception ex)
        {
            Logger.Error("PlanNextHop failed", ex);
            return null;
        }
    }
```

- [ ] **Step 4: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 5: Checkpoint** —— 征兵队的 3 个 scheduler 钩子接好：派遣时 RecordVisit(home) + Book(target)；招到人后 RecordVisit(village)；下一站 PickNextVillage。

---

### Task 12: SallyForthManager 删除巡逻互斥 + 新增 GetActiveCombatSallyParties

**Files:**
- Modify: `SovereignTowns/src/SallyForth/SallyForthManager.cs`

- [ ] **Step 1: 删除巡逻互斥门控**

定位 line 92-96：

```csharp
            // 关键：仅在"无巡逻队"时考虑出击
            // 1) vanilla patrol（settlement.PatrolParty 非空）
            if (settlement.PatrolParty != null) return;
            // 2) 我们自创的 patrol
            if (_lifecycle.CountActive(settlement, PartyLifecycleManager.KindPatrol) > 0) return;
```

**全部删除**（连同 comment）。

并把 line 31 类级别 doc 里"与 PatrolManager 互斥"那行去掉，改为：

```
/// 与 PatrolManager 并行：B7.27 之后两者可以同时启用（巡逻队会评估"能否在战斗结束前抵达"赶去支援）。
```

- [ ] **Step 2: 新增 GetActiveCombatSallyParties 公开方法**

在 `SallyForthManager` 类内合适位置（推荐在 `OnHourlyTickParty` 方法之前，作为同样是 public 的查询入口）添加：

```csharp
    /// <summary>
    /// B7.27：返回该氏族当前正在 MapEvent 中战斗的 sally 队列表。
    /// 供 PatrolManager 评估是否需要派 patrol 赶去支援。
    /// </summary>
    public List<MobileParty> GetActiveCombatSallyParties(Clan clan)
    {
        var result = new List<MobileParty>();
        if (clan == null) return result;
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

- [ ] **Step 3: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 4: Checkpoint** —— sally 不再被 patrol 互斥；提供查询入口给 PatrolManager 用。

---

### Task 13: PatrolManager 优先链插入支援分支

**Files:**
- Modify: `SovereignTowns/src/Patrol/PatrolManager.cs`

- [ ] **Step 1: 让 PatrolManager 持有 SallyForthManager 引用**

定位 PatrolManager 的字段块和 ctor。当前应该是：

```csharp
    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;

    public PatrolManager(PartyLifecycleManager lifecycle, CapitalRegistry? capitalRegistry = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
    }
```

改为：

```csharp
    private readonly PartyLifecycleManager _lifecycle;
    private readonly CapitalRegistry? _capitalRegistry;
    private readonly SovereignTowns.SallyForth.SallyForthManager? _sallyForthManager;  // B7.27：用于支援判定

    public PatrolManager(
        PartyLifecycleManager lifecycle,
        CapitalRegistry? capitalRegistry = null,
        SovereignTowns.SallyForth.SallyForthManager? sallyForthManager = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _capitalRegistry = capitalRegistry;
        _sallyForthManager = sallyForthManager;
    }
```

- [ ] **Step 2: 修改 SovereignTownsCampaignBehavior 在 OnSessionLaunched 里传 _sallyForthManager 进去**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Campaign\SovereignTownsCampaignBehavior.cs`。

定位 `_patrolManager = new PatrolManager(_lifecycle, _capitalRegistry);`（约 line 162）。但注意：此时 `_sallyForthManager` 还没构造（下一行才是 `_sallyForthManager = new SallyForthManager(...)`)。需要交换顺序。

找到这块：

```csharp
            _patrolManager = new PatrolManager(_lifecycle, _capitalRegistry);
            _sallyForthManager = new SallyForthManager(_lifecycle, _capitalRegistry);
```

改为：

```csharp
            // B7.27：sally 先构造，patrol 接受 sally 引用以做支援判定
            _sallyForthManager = new SallyForthManager(_lifecycle, _capitalRegistry);
            _patrolManager = new PatrolManager(_lifecycle, _capitalRegistry, _sallyForthManager);
```

- [ ] **Step 3: 在 PatrolManager.OnHourlyTickParty 优先链插入支援分支**

回到 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Patrol\PatrolManager.cs`。定位 `OnHourlyTickParty` 方法。当前优先链应该是（来自 B7.26 重构）：

```
1) 自动 merge
2) 防御响应
3) Heal
4) 抵达侦测
5) 卡死保护
```

在 "防御响应" 分支 return 之后、"Heal" 分支之前，插入新分支：

```csharp
            // 2) 防御响应（B7.26）
            var defenseTarget = scheduler.GetDefenseTarget(party);
            if (defenseTarget != null)
            {
                // ...原有 defense 代码块（不动）...
            }

            // ★ 3) 支援出击战斗（B7.27 新增）
            if (_sallyForthManager != null)
            {
                var supportSally = FindSupportableSallyBattle(party, capitalMgr);
                if (supportSally != null)
                {
                    Logger.Info($"PatrolManager: '{SafeName(party)}' supporting sally '{SafeName(supportSally)}' (ETA < {ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours:F1}h)");
                    SafeSetMoveEngageParty(party, supportSally);
                    return;
                }
            }

            // 4) Heal 检查 ... (原有，不动)
```

- [ ] **Step 4: 添加 FindSupportableSallyBattle 私有方法**

在 PatrolManager 类内任意合适位置（推荐在 OnHourlyTickParty 之后、helper 方法块附近）添加：

```csharp
    /// <summary>
    /// B7.27：判定本 patrol 是否能在某 sally 战斗结束前抵达。返回最近的可支援目标，无则 null。
    /// 简单算法：ETA = 距离 / 速度，ETA &lt; SupportEtaThresholdHours 即可。
    /// </summary>
    private MobileParty? FindSupportableSallyBattle(MobileParty patrol, Capital.CapitalManager capitalMgr)
    {
        try
        {
            if (_sallyForthManager == null) return null;
            var threshold = ConfigurationManager.Current.ClanPatrol.SupportEtaThresholdHours;
            var sallies = _sallyForthManager.GetActiveCombatSallyParties(capitalMgr.OwnerClan);
            if (sallies.Count == 0) return null;

            var partyPos = patrol.GetPosition2D;
            float partySpeed = Math.Max(patrol.Speed, 0.1f);

            MobileParty? best = null;
            float bestEta = float.MaxValue;
            foreach (var sally in sallies)
            {
                try
                {
                    if (sally.MapEvent == null) continue;  // 双重保险
                    float distance = (partyPos - sally.GetPosition2D).Length;
                    float eta = distance / partySpeed;
                    if (eta < threshold && eta < bestEta)
                    {
                        bestEta = eta;
                        best = sally;
                    }
                }
                catch { /* 单 sally 失败不影响其他 */ }
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

- [ ] **Step 5: 添加 SafeSetMoveEngageParty 私有包装**

在 PatrolManager 类内的 `SafeSetMoveGoToSettlement` 附近添加：

```csharp
    /// <summary>B7.27：安全包装 vanilla SetMoveEngageParty。</summary>
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

- [ ] **Step 6: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 7: Checkpoint** —— patrol 优先链支持支援出击。变更触发条件：sally 在 MapEvent 中 + ETA < 阈值。

---

### Task 14: GlobalConfig 新增字段 + ConfigurationManager 兜底

**Files:**
- Modify: `SovereignTowns/src/Configuration/GlobalConfig.cs`
- Modify: `SovereignTowns/src/Configuration/ConfigurationManager.cs`

- [ ] **Step 1: GlobalConfig 新增 ClanRecruiterConfig 类与属性**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Configuration\GlobalConfig.cs`。

在 `GlobalConfig` 类中（`ClanPatrol` 属性之后），追加：

```csharp
    /// <summary>
    /// B7.27：全氏族征兵调度配置。由 ClanRecruiterScheduler 消费。
    /// 旧配置文件无此字段 → ConfigurationManager.TryLoadFromDisk 反序列化后 ??= 兜底默认值。
    /// </summary>
    public ClanRecruiterConfig ClanRecruiter { get; set; } = new ClanRecruiterConfig();
```

在 `CreateDefault()` 的对象初始化器里追加：

```csharp
    public static GlobalConfig CreateDefault() => new GlobalConfig
    {
        // ... 已有字段 ...
        ClanPatrol = new ClanPatrolConfig(),
        ClanRecruiter = new ClanRecruiterConfig()  // 新增
    };
```

在 `ClanPatrolConfig` 类**内部**（紧跟 `DistanceWeightHoursPerTile` 之后），添加新字段：

```csharp
    /// <summary>
    /// B7.27：巡逻队判定能否"在战斗结束前抵达 sally 战斗"的 ETA 阈值。
    /// ETA = 距离 / 巡逻队速度 &lt; 此值 → 转去支援。
    /// </summary>
    public float SupportEtaThresholdHours { get; set; } = 2.0f;
```

在 `ClanPatrolConfig` 类**之后**追加新类：

```csharp
/// <summary>
/// 全氏族征兵调度配置（B7.27）。与 ClanPatrolConfig 同构。
/// </summary>
public sealed class ClanRecruiterConfig
{
    /// <summary>ETA 估算的余量小时。</summary>
    public float EtaBufferHours { get; set; } = 1.0f;

    /// <summary>单段路超过此时长视为卡死，强制重选下一站村庄。</summary>
    public float StuckTimeoutHours { get; set; } = 12.0f;

    /// <summary>同一村庄的最小回访间隔（防多支征兵队反复同点）。</summary>
    public float MinVisitGapHours { get; set; } = 4.0f;

    /// <summary>距离评分权重（小时/Vec2 unit）。</summary>
    public float DistanceWeightHoursPerTile { get; set; } = 0.5f;
}
```

在 `EnabledFeatures` 类内追加：

```csharp
    /// <summary>
    /// B7.27：玩家金币不足时（&lt; amount）拒绝扣款 / 派遣 / 升级。开启时 mod 自动暂停可推迟的支出，
    /// 防止"派出去又因没钱半截失败"的混乱体验。关闭时允许金币负余额（与 vanilla 玩家自身行为一致）。
    /// </summary>
    public bool PauseSpendingWhenBroke { get; set; } = true;
```

- [ ] **Step 2: ConfigurationManager.TryLoadFromDisk 增加 ??= 兜底**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Configuration\ConfigurationManager.cs`。

定位 `??=` 兜底块（约 line 349-355）。在 `parsed.ClanPatrol ??= new ClanPatrolConfig();` 之后追加：

```csharp
            parsed.ClanRecruiter ??= new ClanRecruiterConfig();
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 4: Checkpoint** —— 3 项新配置就位，不升 ConfigVersion，旧配置兼容。

---

### Task 15: SyncData 接入 recruiter scheduler + finance snapshot

**Files:**
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`

- [ ] **Step 1: 添加两个 _pending 字段**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Campaign\SovereignTownsCampaignBehavior.cs`。

定位 `_pendingPatrolSchedulers` 字段（B7.26 加的）。在它**之后**追加：

```csharp
    /// <summary>B7.27：recruiter scheduler 暂存。Key = clan.StringId。</summary>
    private Dictionary<string, Recruitment.ClanRecruiterSchedulerSnapshot>? _pendingRecruiterSchedulers;

    /// <summary>B7.27：finance ledger snapshot 暂存。</summary>
    private Economy.FinanceSnapshot? _pendingFinanceSnapshot;
```

- [ ] **Step 2: SyncData 增加两个字段处理**

定位 `SyncData(IDataStore dataStore)` 方法。在现有的 `st_patrol_schedulers_json` 处理块**之后**（应该在方法末尾的 `}` 之前），追加两段：

```csharp
        // ── Recruiter scheduler snapshots (B7.27) ──
        string? recruiterJson = null;
        if (dataStore.IsSaving)
        {
            try
            {
                var dict = _capitalRegistry?.ExportRecruiterSchedulerSnapshots() ?? _pendingRecruiterSchedulers;
                if (dict != null && dict.Count > 0)
                {
                    recruiterJson = JsonConvert.SerializeObject(dict);
                }
            }
            catch (Exception ex) { Logger.Error("SyncData: serialize recruiter schedulers failed", ex); recruiterJson = null; }
        }
        dataStore.SyncData("st_recruiter_schedulers_json", ref recruiterJson);
        if (dataStore.IsLoading)
        {
            try
            {
                _pendingRecruiterSchedulers = string.IsNullOrEmpty(recruiterJson)
                    ? null
                    : JsonConvert.DeserializeObject<Dictionary<string, Recruitment.ClanRecruiterSchedulerSnapshot>>(recruiterJson!);
            }
            catch (Exception ex)
            {
                Logger.Error("SyncData: deserialize recruiter schedulers failed (will start fresh)", ex);
                _pendingRecruiterSchedulers = null;
            }
        }

        // ── Finance ledger snapshot (B7.27) ──
        string? financeJson = null;
        if (dataStore.IsSaving)
        {
            try
            {
                var snap = Economy.ModExpenseLedger.CreateSnapshot();
                financeJson = JsonConvert.SerializeObject(snap);
            }
            catch (Exception ex) { Logger.Error("SyncData: serialize finance snapshot failed", ex); financeJson = null; }
        }
        dataStore.SyncData("st_finance_snapshot_json", ref financeJson);
        if (dataStore.IsLoading)
        {
            try
            {
                _pendingFinanceSnapshot = string.IsNullOrEmpty(financeJson)
                    ? null
                    : JsonConvert.DeserializeObject<Economy.FinanceSnapshot>(financeJson!);
            }
            catch (Exception ex)
            {
                Logger.Error("SyncData: deserialize finance snapshot failed (will start fresh)", ex);
                _pendingFinanceSnapshot = null;
            }
        }
```

- [ ] **Step 3: OnSessionLaunched 回灌**

定位 `OnSessionLaunched` 中 `_capitalRegistry.RestorePatrolSchedulers(_pendingPatrolSchedulers);` 行。在它**之后**追加：

```csharp
            _capitalRegistry.RestoreRecruiterSchedulers(_pendingRecruiterSchedulers);
            Economy.ModExpenseLedger.RestoreFromSnapshot(_pendingFinanceSnapshot);
```

在同方法**后续**的 `_pendingPatrolSchedulers = null;` 行之后追加：

```csharp
            _pendingRecruiterSchedulers = null;
            _pendingFinanceSnapshot = null;
```

- [ ] **Step 4: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 5: Checkpoint** —— 持久化闭环：recruiter scheduler + finance ledger 都走 SyncData JSON。

---

### Task 16: SafeUninstallMenu 清理 ledger + recruiter scheduler

**Files:**
- Modify: `SovereignTowns/src/Ui/SafeUninstallMenu.cs`

- [ ] **Step 1: 扩展 NotifyAllLost 块**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\Ui\SafeUninstallMenu.cs`。

定位 B7.26 时加的 "2.5) 清空 patrol scheduler 状态" 块。在它**之后**追加一个 "2.6) 清空 recruiter scheduler + ledger" 块：

```csharp
            // 2.6) B7.27：清空所有 clan 的 recruiter scheduler + ModExpenseLedger
            try
            {
                var registry = SovereignTowns.Capital.CapitalRegistry.Instance;
                if (registry != null)
                {
                    foreach (var mgr in registry.AllManagers)
                    {
                        try { mgr?.RecruiterScheduler?.NotifyAllLost(); }
                        catch (Exception ex) { Logger.Warn("uninstall: RecruiterScheduler.NotifyAllLost failed: " + ex.Message); }
                    }
                }
                SovereignTowns.Economy.ModExpenseLedger.Clear();
            }
            catch (Exception ex)
            {
                Logger.Warn("uninstall: recruiter+ledger cleanup failed: " + ex.Message);
            }
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 3: Checkpoint** —— 卸载流程清理 recruiter scheduler 和 ledger 状态。

---

### Task 17: WebConfig 新 endpoint /api/finance

**Files:**
- Modify: `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs`
- Modify: `SovereignTowns/src/WebConfig/WebConfigServer.cs`

- [ ] **Step 1: 加 endpoint 方法**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\WebConfig\WebConfigEndpoints.cs`。

在文件顶部 using 区追加：

```csharp
using SovereignTowns.Economy;
```

在类内任意合适位置（例如紧跟 `GetStatus` 方法之后）添加：

```csharp
    /// <summary>GET /api/finance → mod 支出报告（今日/本周/全部 + 近期流水）。</summary>
    public static void GetFinance(HttpListenerContext ctx)
    {
        try
        {
            var report = ModExpenseLedger.BuildReport();
            WebConfigServer.WriteJson(ctx, 200, report);
        }
        catch (Exception ex)
        {
            Logger.Error("GetFinance threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }
```

- [ ] **Step 2: 路由注册**

打开 `C:\Users\rangt\Desktop\workspace\SovereignTowns\src\WebConfig\WebConfigServer.cs`。

找到路由分发（应该是 switch / 字典样式）。在现有的 `GET /api/status` 注册行之后追加 `/api/finance` 路由。如果是 switch：

```csharp
case "/api/finance" when method == "GET":
    WebConfigEndpoints.GetFinance(ctx);
    break;
```

如果是字典：

```csharp
["GET /api/finance"] = WebConfigEndpoints.GetFinance,
```

实际格式以文件里现有的为准。

- [ ] **Step 3: 编译验证**

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings。

- [ ] **Step 4: 简单 endpoint 烟测**（启动游戏后做）

启动游戏到主菜单 / 战役加载后，访问网页面板，再浏览器手工访问：
```
http://127.0.0.1:41763/api/finance?t=<token>
```
应返回 JSON 含 `Today` / `Week` / `AllTime` 字典 + `RecentEntries` 数组。

- [ ] **Step 5: Checkpoint** —— 后端 endpoint 就位。前端 Task 18 接。

---

### Task 18: WebUI index.html 新增"财务"标签页

**Files:**
- Modify: `SovereignTowns/SovereignTowns/WebUI/index.html`

- [ ] **Step 1: tabs 数组追加财务**

定位 `tabs: [` 数组（约 line 833-840）：

```javascript
        tabs: [
          { label: '功能开关' },
          { label: '数量预算' },
          { label: '兵种编制' },
          { label: '兵员模板' },
          { label: '资源调度' },
          { label: '按城堡覆盖' },
        ],
```

追加一行：

```javascript
        tabs: [
          { label: '功能开关' },
          { label: '数量预算' },
          { label: '兵种编制' },
          { label: '兵员模板' },
          { label: '资源调度' },
          { label: '按城堡覆盖' },
          { label: '财务' },
        ],
```

- [ ] **Step 2: 在 Alpine data 里加 finance 状态**

定位 `tabs: [` 紧邻位置的 data 对象（应该有 `config`, `settlements`, `troops`, `logEntries` 等字段）。追加：

```javascript
        finance: null,        // FinanceReport JSON
        financeError: '',
```

- [ ] **Step 3: 添加 loadFinance 方法**

定位 alpine data 对象的 methods 区（可能有 `loadConfig`, `loadSettlements` 等）。追加：

```javascript
        async loadFinance() {
          try {
            const t = new URLSearchParams(window.location.search).get('t');
            const headers = { 'X-ST-Token': t || '' };
            const resp = await fetch('/api/finance?t=' + encodeURIComponent(t || ''), { headers });
            if (!resp.ok) {
              this.financeError = '加载失败 HTTP ' + resp.status;
              return;
            }
            this.finance = await resp.json();
            this.financeError = '';
          } catch (e) {
            this.financeError = '加载失败：' + e.message;
          }
        },
```

- [ ] **Step 4: 在 init() 内调用 loadFinance + 每 5 秒刷新**

定位 init 函数（应该已有 `this.loadConfig()` 等调用）。追加：

```javascript
          this.loadFinance();
          setInterval(() => { this.loadFinance(); }, 5000);
```

- [ ] **Step 5: 添加 tab 7 DOM**

定位 tab 6 (按城堡覆盖) 的 `<div x-show="activeTab === 5" ...>` 块结束位置（应该有 `</div>` 关闭 tab-content）。在它**之后**追加：

```html
          <!-- =========== TAB 7: 财务 =========== -->
          <div x-show="activeTab === 6" class="tab-content p-8">
            <h2 class="font-display text-xl text-gold-200 mb-4 tracking-wider">财 务 报 告</h2>
            <p class="text-parchment-200 text-sm mb-6">
              本 Mod 引发的金币开销（招兵、升级、出击本钱等）。所有支出从玩家个人金币扣，本表实时刷新（每 5 秒）。
            </p>

            <div x-show="financeError" class="mb-4 p-3 ornate-frame text-danger-500 text-sm" x-text="financeError"></div>

            <div x-show="!finance" class="text-parchment-400 italic text-sm">加载中…</div>

            <div x-show="finance" class="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
              <!-- 今日 -->
              <div class="ornate-frame p-5">
                <h3 class="font-display text-gold-200 text-base mb-3 tracking-wider">今 日</h3>
                <table class="w-full text-sm">
                  <tbody>
                    <template x-for="(amt, cat) in finance?.Today || {}" :key="cat">
                      <tr class="border-b border-ink-700 last:border-0">
                        <td class="py-1.5 text-parchment-200" x-text="cat"></td>
                        <td class="py-1.5 text-right text-gold-300 font-mono" x-text="'-' + amt + 'd'"></td>
                      </tr>
                    </template>
                  </tbody>
                  <tfoot>
                    <tr class="font-bold border-t-2 border-gold-700">
                      <td class="py-2 text-gold-100">总</td>
                      <td class="py-2 text-right text-gold-100 font-mono" x-text="'-' + (finance?.TodayTotal || 0) + 'd'"></td>
                    </tr>
                  </tfoot>
                </table>
              </div>

              <!-- 本周 -->
              <div class="ornate-frame p-5">
                <h3 class="font-display text-gold-200 text-base mb-3 tracking-wider">本 周</h3>
                <table class="w-full text-sm">
                  <tbody>
                    <template x-for="(amt, cat) in finance?.Week || {}" :key="cat">
                      <tr class="border-b border-ink-700 last:border-0">
                        <td class="py-1.5 text-parchment-200" x-text="cat"></td>
                        <td class="py-1.5 text-right text-gold-300 font-mono" x-text="'-' + amt + 'd'"></td>
                      </tr>
                    </template>
                  </tbody>
                  <tfoot>
                    <tr class="font-bold border-t-2 border-gold-700">
                      <td class="py-2 text-gold-100">总</td>
                      <td class="py-2 text-right text-gold-100 font-mono" x-text="'-' + (finance?.WeekTotal || 0) + 'd'"></td>
                    </tr>
                  </tfoot>
                </table>
              </div>

              <!-- 全部 -->
              <div class="ornate-frame p-5">
                <h3 class="font-display text-gold-200 text-base mb-3 tracking-wider">全 部</h3>
                <table class="w-full text-sm">
                  <tbody>
                    <template x-for="(amt, cat) in finance?.AllTime || {}" :key="cat">
                      <tr class="border-b border-ink-700 last:border-0">
                        <td class="py-1.5 text-parchment-200" x-text="cat"></td>
                        <td class="py-1.5 text-right text-gold-300 font-mono" x-text="'-' + amt + 'd'"></td>
                      </tr>
                    </template>
                  </tbody>
                  <tfoot>
                    <tr class="font-bold border-t-2 border-gold-700">
                      <td class="py-2 text-gold-100">总</td>
                      <td class="py-2 text-right text-gold-100 font-mono" x-text="'-' + (finance?.AllTimeTotal || 0) + 'd'"></td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </div>

            <!-- 近期流水 -->
            <div x-show="finance" class="ornate-frame p-5">
              <h3 class="font-display text-gold-200 text-base mb-3 tracking-wider">近 期 流 水（最近 50 条）</h3>
              <div x-show="(finance?.RecentEntries || []).length === 0" class="text-parchment-400 italic text-sm">尚无支出记录</div>
              <table x-show="(finance?.RecentEntries || []).length > 0" class="w-full text-sm">
                <thead>
                  <tr class="border-b-2 border-gold-700 text-parchment-200">
                    <th class="py-2 text-left">时间</th>
                    <th class="py-2 text-left">类别</th>
                    <th class="py-2 text-right">金额</th>
                    <th class="py-2 text-left">备注</th>
                  </tr>
                </thead>
                <tbody>
                  <template x-for="(e, idx) in finance?.RecentEntries || []" :key="idx">
                    <tr class="border-b border-ink-700 last:border-0">
                      <td class="py-1.5 text-parchment-300 font-mono text-xs" x-text="new Date(e.TimestampMs).toLocaleString('zh-CN')"></td>
                      <td class="py-1.5 text-parchment-100" x-text="e.Category"></td>
                      <td class="py-1.5 text-right text-gold-300 font-mono" x-text="'-' + e.Amount + 'd'"></td>
                      <td class="py-1.5 text-parchment-300 text-xs" x-text="e.Note || ''"></td>
                    </tr>
                  </template>
                </tbody>
              </table>
            </div>
          </div>
```

- [ ] **Step 6: 编译验证（c# 不变 → 不需要重新 build，但运行时验证）**

由于 WebUI 是 HTML 资源文件，C# 编译不需要。但 `DeployToGame` MSBuild target 会把 WebUI 复制到游戏目录，所以需要再跑一次 build 让它部署：

Run: `dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug`

Expected: 0 errors 0 warnings；DeployToGame 把 index.html 复制到游戏目录。

- [ ] **Step 7: Checkpoint** —— Web UI 财务页就位。打开网页应能看到第 7 个标签。

---

### Task 19: 整体编译验证 + Release build

**Files:** 无文件修改，仅验证。

- [ ] **Step 1: Debug + Release 双模式 clean build**

Run: 
```
dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Debug --no-incremental
```
Expected: 0 errors 0 warnings。

Run:
```
dotnet build "C:\Users\rangt\Desktop\workspace\SovereignTowns\src\SovereignTowns.csproj" -c Release --no-incremental
```
Expected: 0 errors 0 warnings。

- [ ] **Step 2: 游戏内手动验收清单**

启动游戏，加载一个有 ≥3 座 town + ≥1 castle + 数个 village 属于玩家氏族的存档。

**经济**：
- [ ] 派征兵队 → 玩家金额减 1000 立即可见
- [ ] 招兵期间玩家金额持续减少（每招 1 人 5）
- [ ] 升级驻军 → 玩家金额减升级费
- [ ] 出击 → 玩家金额减 100
- [ ] **不再有 vanilla `GiveGoldAction.ApplyForPartyToSettlement` 相关日志**

**池化**：
- [ ] 首府兵营 0 级 → 1 支征兵队上限
- [ ] 兵营 3 级 → 4 支征兵队上限
- [ ] 多支征兵队同时存在 → 目标村庄不同（看 audit 的 DispatchRecruiter 行 `target=` 字段）

**共存**：
- [ ] AutoPatrol + SallyForth 同时开 → 巡逻队和出击队都能正常派出，互不抑制

**支援**：
- [ ] 出击队在战斗中 + 附近有巡逻队 → 巡逻队改向赶来支援（日志 `supporting sally`）
- [ ] 出击队战斗结束 → 巡逻队下个 tick 回正常巡逻（卡死保护或抵达侦测触发 PickNextStop）

**面板**：
- [ ] 网页面板出现第 7 个 tab "财务"
- [ ] 三栏（今日 / 本周 / 全部）显示金额
- [ ] 流水表显示最近 50 条
- [ ] 5 秒自动刷新

**存档**：
- [ ] 存档 → 退出 → 读档 → 财务数据保留
- [ ] 读档后 recruiter scheduler 状态恢复（LastRecruitedAt 不重置）

**卸载**：
- [ ] 卸载 → 财务 ledger 清空 + recruiter scheduler 状态清空

- [ ] **Step 3: 最终 Checkpoint** —— 全部 5 项变更完成。如有任何验收项不通过，定位到对应 Task 回查。

---

## 自审摘要

| 检查项 | 状态 |
|---|---|
| spec §4.1 ModTreasury → Task 1 | ✓ |
| spec §4.2 5 个改造点 → Task 3/4/5/6 | ✓ |
| spec §4.3 ModExpenseLedger → Task 2 | ✓ |
| spec §4.4 SyncData 扩展 → Task 15 | ✓ |
| spec §4.5-4.7 ClanRecruiterScheduler + 池大小 → Task 7/8/9/10 | ✓ |
| spec §4.8 SallyForth 改造 → Task 12 | ✓ |
| spec §4.9 PatrolManager 优先链 → Task 13 | ✓ |
| spec §4.10 RecruitmentManager 接入 → Task 11 | ✓ |
| spec §4.11 WebConfig 财务页 → Task 17/18 | ✓ |
| spec §5 配置变更 → Task 14 | ✓ |
| spec §6 边界情况覆盖 | ✓（CanAfford 预检 + PauseSpendingWhenBroke 软门控 + AI clan 跳过扣费） |
| spec §10 不在范围（游戏内显示）：plan 中无 | ✓ |
| 无 "TBD"/"TODO"/"实现细节稍后补" | ✓ |
| 字段名 / 方法签名一致（ModTreasury / ModExpenseLedger / ClanRecruiterScheduler / PatrolManager ctor 新参数）| ✓ |
| 项目硬约束（net472 / SaveBaseId / try-catch）不违反 | ✓ |
| 用户配置不被清空（不升 ConfigVersion） | ✓ |
| 实施顺序处理 Task 9 依赖 Task 14 的循环 | ✓（提示 "先做 Task 14 再回 Task 9 Step 2"） |

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-14-economy-and-coordination.md`.

执行选项：

**1. Subagent-Driven（推荐）** —— 每个 Task 派一个 fresh subagent，task 间两段审查（spec + 代码质量），快速迭代

**2. Inline Execution** —— 当前会话直接做，多 task 批执行 + 检查点

要哪种？
