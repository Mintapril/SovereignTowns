# SovereignTowns 简化重构 实施 Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 删除 mod 自定义存档持久化 + 抽 Scheduler 基类 + 抽共享工具，净削 ~500 行代码 + 显著降单文件复杂度，降低 bug 表面积。

**Architecture:** 三组重构按 C → A → B 顺序执行。C 先做（删持久化）会让 A（scheduler 基类）省去 Snapshot DTO 兼容工作；B（工具类）独立于 A/C，最后做。无单元测试，每个 task 末尾以 `dotnet build` 0 错 0 警为 gate。

**Tech Stack:** C# net472, Bannerlord v1.3.15 TaleWorlds.CampaignSystem, Newtonsoft.Json

**Spec:** `docs/superpowers/specs/2026-05-14-simplification-refactor-design.md`

**Source root:** `SovereignTowns/src/`

**Build cmd:** `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`（Release 同）

---

## Task 总览

| # | 任务 | 改动文件数 | 预估代码净变 |
|---|---|---|---|
| C1 | 删除 SovereignTownsCampaignBehavior + CapitalRegistry 内 mod 持久化代码 | 2 | -200 行 |
| C2 | 删除各 Scheduler/Ledger/CapitalManager 的 Snapshot/Restore 方法 + Snapshot DTO 类 | 5 | -180 行 |
| C3 | 双模式构建验证 C 系列完成态 | 0 | 0 |
| A1 | 创建 `BaseSettlementVisitScheduler` 抽象基类 | 1 | +230 行 |
| A2 | 重构 `ClanPatrolScheduler` 继承基类 | 1 | -340 行 |
| A3 | 重构 `ClanRecruiterScheduler` 继承基类 | 1 | -210 行 |
| B1 | `PartyNameFormatter` + 4 manager 替换 | 5 | -50 行 |
| B2 | `TroopTransferHelper` + 3 manager 替换 | 4 | -90 行 |
| F  | 最终全局代码评审 + 双模式构建 | 0 | 0 |

净估：~ -640 行

---

# C 系列：删除 mod 自定义存档持久化

## Task C1: 删除 CampaignBehavior 与 CapitalRegistry 内的 mod 持久化

**Files:**
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`
- Modify: `SovereignTowns/src/Capital/CapitalRegistry.cs`

**Context for implementer:** 当前 mod 自定义持久化 2 处入口：
- `SovereignTownsCampaignBehavior.SyncData` 5 个 JSON 块 + 对应 `_pending*` 字段 + `OnSessionLaunched` 内 restore 下发
- `CapitalRegistry` 内的 `_pendingPlayerCapitalStringId` / `_pendingAiCapitalStringIds` 字段 + `RestorePlayerCapital / RestoreAiCapitals / ExportPlayerCapitalStringId / ExportAiCapitalStringIds` 方法 + `EnsureForClan(Clan, string?)` 第二参数读取

本 task 把这些**全部删干净**。Vanilla `[SaveableField]` / `SovereignTownsTypeDefiner` **不动**。

### Step 1: grep 定位
- [ ] Run: `grep -n "SyncData\|_pending" SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`
  - Expected: 5 处 `dataStore.SyncData(...)` + 多处 `_pending*` 字段与引用
- [ ] Run: `grep -n "_pending\|Restore.*Capital\|Export.*Capital\|GetCapitalStringId" SovereignTowns/src/Capital/CapitalRegistry.cs`
  - Expected: 2 个 `_pending*` 字段 + `RestorePlayerCapital / RestoreAiCapitals / ExportPlayerCapitalStringId / ExportAiCapitalStringIds` 方法

### Step 2: 清空 `SovereignTownsCampaignBehavior.SyncData`
- [ ] 把整个 `protected override void SyncData(IDataStore dataStore)` 方法体替换为：

```csharp
protected override void SyncData(IDataStore dataStore)
{
    // TODO: 稳定版后实现 mod 自定义存档持久化（capital/scheduler/finance）。
    // 现阶段不存这些数据 — 重新加载后由 RebuildFromCampaign + 规则选举重建。
    // vanilla [SaveableField] / SovereignTownsTypeDefiner 不在此方法范围。
}
```

### Step 3: 删除 `SovereignTownsCampaignBehavior` 内所有 `_pending*` 字段及其 `OnSessionLaunched` 引用
- [ ] 删除所有 `_pendingCapitalStringId / _pendingAiCapitalsJson / _pendingPatrolSchedulers / _pendingRecruiterSchedulers / _pendingFinanceSnapshot` 字段声明
- [ ] 删除 `OnSessionLaunched` 内对它们的：
  - JSON deserialize 调用
  - `_capitalRegistry.RestorePlayerCapital(...)` / `_capitalRegistry.RestoreAiCapitals(...)` 调用
  - `_capitalRegistry.RestorePatrolSchedulers(...)` / `RestoreRecruiterSchedulers(...)` 调用
  - `ledger.RestoreFromSnapshot(...)` 调用
  - 任何 `_pendingXxx = null` 重置
- [ ] **保留**：`_capitalRegistry.Initialize()` 调用，`ledger` 与 manager 的实例化

### Step 4: 删除 `CapitalRegistry` 内 `_pending*` 字段与 Restore/Export 方法
- [ ] 删除字段：
  - `private Dictionary<string, string>? _pendingAiCapitalStringIds;`
  - `private string? _pendingPlayerCapitalStringId;`
- [ ] 删除 public 方法：
  - `RestorePlayerCapital(string?)`
  - `RestoreAiCapitals(Dictionary<string, string>?)`
  - `ExportAiCapitalStringIds()`
  - `ExportPlayerCapitalStringId()`
- [ ] 简化 `private CapitalManager EnsureForClan(Clan clan, string? pendingStringId)` 签名为 `private CapitalManager EnsureForClan(Clan clan)`，删除方法体内 `mgr.RestoreFromStringId(pendingStringId);` 行
- [ ] 修 `Initialize()` 内 `EnsureForClan(Clan.PlayerClan, _pendingPlayerCapitalStringId)` → `EnsureForClan(Clan.PlayerClan)`
- [ ] 修 `Initialize()` 内末尾 `_pendingPlayerCapitalStringId = null; _pendingAiCapitalStringIds = null;` 两行 → 删
- [ ] 修 `EnsureForAllAiClans()` 内 `_pendingAiCapitalStringIds` 查询块（约 L398-401）→ 简化为 `EnsureForClan(c);`

### Step 5: 验证编译
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
  - Expected: 编译错误，**只指向** C2 范围（`CreateSnapshot/RestoreFromSnapshot/Snapshot DTO` 引用残留），其他错误必须排查清楚
  - 把所有错误清单记下，C2 直接对照清理

---

## Task C2: 删除 Scheduler/Ledger/CapitalManager 的 Snapshot 方法 + Snapshot DTO 类

**Files:**
- Modify: `SovereignTowns/src/Patrol/ClanPatrolScheduler.cs`
- Modify: `SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs`
- Modify: `SovereignTowns/src/Capital/CapitalRegistry.cs`
- Modify: `SovereignTowns/src/Capital/CapitalManager.cs`
- Modify: `SovereignTowns/src/Economy/ModExpenseLedger.cs`

**Context for implementer:** C1 已清空 CampaignBehavior 与 CapitalRegistry 的入口持久化。本 task 清"产出/消费 Snapshot" 的方法与 DTO 类。

### Step 1: 删 `ClanPatrolScheduler` 的 Snapshot 方法 + DTO
- [ ] 打开 `SovereignTowns/src/Patrol/ClanPatrolScheduler.cs`
- [ ] 删 `public ClanPatrolSchedulerSnapshot CreateSnapshot()` 方法
- [ ] 删 `public void RestoreFromSnapshot(...)` 方法
- [ ] 删文件内或独立文件的 `ClanPatrolSchedulerSnapshot` 类（grep `class ClanPatrolSchedulerSnapshot` 确认位置）

### Step 2: 删 `ClanRecruiterScheduler` 的 Snapshot 方法 + DTO
- [ ] 同 Step 1，对 `ClanRecruiterScheduler.cs` 和 `ClanRecruiterSchedulerSnapshot`

### Step 3: 删 `CapitalRegistry` 的 scheduler 导出/导入方法
- [ ] 删 `ExportPatrolSchedulerSnapshots()`
- [ ] 删 `RestorePatrolSchedulers(...)`
- [ ] 删 `ExportRecruiterSchedulerSnapshots()`
- [ ] 删 `RestoreRecruiterSchedulers(...)`

### Step 4: 删 `CapitalManager` 的剩余持久化辅助
- [ ] 打开 `SovereignTowns/src/Capital/CapitalManager.cs`
- [ ] Run: `grep -n "RestoreFromStringId\|GetCapitalStringId\|CreateSnapshot\|RestoreFromSnapshot" SovereignTowns/src/Capital/CapitalManager.cs`
- [ ] 删 `RestoreFromStringId(string)` 方法（C1 后已无调用者）
- [ ] 删 `GetCapitalStringId()` 方法（C1 后已无调用者）
- [ ] 删任何其他 Snapshot 相关方法

### Step 5: 删 `ModExpenseLedger` 的 Snapshot 方法 + DTO
- [ ] 打开 `SovereignTowns/src/Economy/ModExpenseLedger.cs`
- [ ] 删 `CreateSnapshot()` 方法
- [ ] 删 `RestoreFromSnapshot(...)` 方法
- [ ] 删文件内或独立文件的 `FinanceSnapshot` 类（grep 确认）
- [ ] **保留**：`Record / BuildReport / Clear` 方法（WebConfig 财务页与 SafeUninstallMenu 用）

### Step 6: 全工程 grep 确认无遗漏
- [ ] Run: `grep -rn "CreateSnapshot\|RestoreFromSnapshot\|SchedulerSnapshot\|FinanceSnapshot\|RestoreFromStringId\|GetCapitalStringId\|_pending" SovereignTowns/src/`
  - Expected: 0 匹配（如有，回对应 Step 处理）

### Step 7: 验证编译
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
  - Expected: 0 错 0 警
- [ ] 若有 warning 是 unused field/using，一并清

---

## Task C3: 双模式构建验证

- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug` → 0 错 0 警
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Release` → 0 错 0 警

两个 build clean 后 C 系列完成。

---

# A 系列：Scheduler 基类抽象

## Task A1: 创建 `BaseSettlementVisitScheduler` 抽象基类

**Files:**
- Create: `SovereignTowns/src/Coordination/BaseSettlementVisitScheduler.cs`

**Context for implementer:** `ClanPatrolScheduler` 与 `ClanRecruiterScheduler` 共享调度结构：
- 字段：`_clan` / `_lastVisitedAt: Dictionary<string, CampaignTime>` / `_bookedUntil` / `_lastStopChangedAt: Dictionary<MBGUID, CampaignTime>` / `_lastSeenLocation: Dictionary<MBGUID, string>`
- 共享方法：`RecordVisit / PreemptiveBook / IsStuck / TryMarkArrival / NotifySettlementLost / NotifyAllLost / NotifyPartyDestroyed / ComputeEtaHours`
- 子类差异：候选源（patrol = `Clan.Settlements`，recruiter = `RecruitmentPlanner.RankCandidates`）+ 过滤（patrol 加 raided/IsUnderSiege）+ 配置 section 名

C2 已删 Snapshot 相关，基类**不需要**任何 Create/Restore Snapshot 代码。

### Step 1: 创建基类文件（完整代码）

Create `SovereignTowns/src/Coordination/BaseSettlementVisitScheduler.cs`：

```csharp
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Coordination;

/// <summary>
/// Scheduler 基类：管理"某氏族范围内多支 party 对定居点的差异化访问调度"。
/// 子类只需提供候选源 EnumerateCandidates、过滤 PassesCandidateFilter、
/// 以及 3 个配置参数（MinVisitGapHours / DistanceWeightHoursPerTile / EtaBufferHours）。
///
/// 评分公式：score = -hoursSinceVisit + DistanceWeight * distance，越小越优先（久未访问优先 + 距离近优先）。
/// 持久化：C 系列后无 mod 自定义存档；瞬态字典在游戏会话内有效。
/// </summary>
public abstract class BaseSettlementVisitScheduler
{
    protected readonly Clan _clan;
    protected readonly Dictionary<string, CampaignTime> _lastVisitedAt = new();   // key: Settlement.StringId
    protected readonly Dictionary<string, CampaignTime> _bookedUntil = new();     // 瞬态
    protected readonly Dictionary<MBGUID, CampaignTime> _lastStopChangedAt = new();  // 瞬态
    protected readonly Dictionary<MBGUID, string> _lastSeenLocation = new();      // 瞬态

    protected BaseSettlementVisitScheduler(Clan clan)
    {
        _clan = clan ?? throw new ArgumentNullException(nameof(clan));
    }

    public Clan OwnerClan => _clan;

    // ── 子类钩子 ──

    /// <summary>子类返回该氏族范围内此次调度的候选定居点。</summary>
    protected abstract IEnumerable<Settlement> EnumerateCandidates(MobileParty party);

    /// <summary>子类候选过滤：true=接受。如 patrol 加 IsUnderSiege / IsVillageRaided 过滤。</summary>
    protected abstract bool PassesCandidateFilter(Settlement s, MobileParty party);

    /// <summary>子类提供配置中此 scheduler 段的 MinVisitGapHours（短期回访保护小时数）。从 ConfigurationManager.Current 静态读，支持热重载。</summary>
    protected abstract float MinVisitGapHours { get; }

    /// <summary>子类提供配置中此 scheduler 段的 DistanceWeightHoursPerTile。</summary>
    protected abstract float DistanceWeightHoursPerTile { get; }

    /// <summary>子类提供配置中此 scheduler 段的 EtaBufferHours。</summary>
    protected abstract float EtaBufferHours { get; }

    /// <summary>子类 log 标签，如 "ClanPatrolScheduler" 或 "ClanRecruiterScheduler"。</summary>
    protected abstract string SchedulerLogTag { get; }

    // ── 公共 API（基类实现） ──

    /// <summary>
    /// 为 party 选下一站。按 "最久未访问 + 距离权重" 评分，排除被过滤/被他队预占/最小回访间隔内的。
    /// 选中后自动 PreemptiveBook。返回 null 表示当前无合适候选（调用方可让 party 回首府）。
    /// </summary>
    public Settlement? PickNext(MobileParty party)
    {
        if (party == null) return null;
        try
        {
            var now = CampaignTime.Now;
            var partyPos = party.GetPosition2D;
            Settlement? best = null;
            float bestScore = float.MaxValue;

            foreach (var s in EnumerateCandidates(party))
            {
                if (s == null) continue;
                if (!PassesCandidateFilter(s, party)) continue;

                // 多队互补：被他队预占且未到期 → 跳过
                if (_bookedUntil.TryGetValue(s.StringId, out var booked) && booked > now)
                    continue;

                // 最小回访间隔
                if (_lastVisitedAt.TryGetValue(s.StringId, out var lva))
                {
                    if (lva.ElapsedHoursUntilNow < MinVisitGapHours) continue;
                }

                float hoursSinceVisit = _lastVisitedAt.TryGetValue(s.StringId, out var l)
                    ? (float)l.ElapsedHoursUntilNow
                    : 1e6f;
                float distance = (partyPos - s.GetPosition2D).Length;
                float score = -hoursSinceVisit + DistanceWeightHoursPerTile * distance;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }

            if (best != null)
            {
                float etaHours = ComputeEtaHours(party, best);
                PreemptiveBook(best, party, etaHours);
            }
            return best;
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.PickNext failed for clan '{_clan?.StringId}'", ex);
            return null;
        }
    }

    public void RecordVisit(Settlement settlement)
    {
        if (settlement == null) return;
        try
        {
            _lastVisitedAt[settlement.StringId] = CampaignTime.Now;
            _bookedUntil.Remove(settlement.StringId);
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.RecordVisit failed for '{settlement?.StringId}'", ex);
        }
    }

    public void PreemptiveBook(Settlement settlement, MobileParty party, float etaHours)
    {
        if (settlement == null || party == null) return;
        try
        {
            float bookHours = Math.Max(0.5f, etaHours + EtaBufferHours);
            _bookedUntil[settlement.StringId] = CampaignTime.HoursFromNow(bookHours);
            _lastStopChangedAt[party.Id] = CampaignTime.Now;
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.PreemptiveBook failed", ex);
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

    public void NotifySettlementLost(Settlement settlement)
    {
        if (settlement == null) return;
        try
        {
            _lastVisitedAt.Remove(settlement.StringId);
            _bookedUntil.Remove(settlement.StringId);
            Logger.Info($"{SchedulerLogTag}({_clan.StringId}): cleared state for lost settlement '{settlement.StringId}'");
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.NotifySettlementLost failed for '{settlement?.StringId}'", ex);
        }
    }

    public void NotifyAllLost()
    {
        try
        {
            _lastVisitedAt.Clear();
            _bookedUntil.Clear();
            _lastStopChangedAt.Clear();
            _lastSeenLocation.Clear();
            Logger.Info($"{SchedulerLogTag}({_clan.StringId}): NotifyAllLost — all state cleared");
        }
        catch (Exception ex)
        {
            Logger.Error($"{SchedulerLogTag}.NotifyAllLost failed", ex);
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
            Logger.Error($"{SchedulerLogTag}.NotifyPartyDestroyed failed", ex);
        }
    }

    protected static float ComputeEtaHours(MobileParty party, Settlement target)
    {
        try
        {
            float distance = (party.GetPosition2D - target.GetPosition2D).Length;
            float speed = Math.Max(party.Speed, 0.1f);
            return distance / speed;
        }
        catch
        {
            return 24f;
        }
    }
}
```

### Step 2: 验证编译
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
  - Expected: 0 错 0 警（基类是新文件，不影响现有代码；ClanPatrol/ClanRecruiter 仍是 sealed class 各自工作）

---

## Task A2: 重构 `ClanPatrolScheduler` 继承基类

**Files:**
- Modify: `SovereignTowns/src/Patrol/ClanPatrolScheduler.cs`

**Context for implementer:** A1 已建基类。把 `ClanPatrolScheduler` 改为继承基类，仅保留巡逻特化：候选源 = `_clan.Settlements`，过滤 = `OwnerClan == _clan + !IsUnderSiege + (!IsVillage 或 AvoidRaidedVillages 且非 raided)`，以及 patrol 独有的 `GetDefenseTarget` 方法。**保留** `PickNextStop` API 名（PatrolManager 调用方），通过薄包装委托给 `base.PickNext`。

### Step 1: 重写文件

完整替换 `SovereignTowns/src/Patrol/ClanPatrolScheduler.cs` 为：

```csharp
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using SovereignTowns.Configuration;
using SovereignTowns.Coordination;

namespace SovereignTowns.Patrol;

/// <summary>
/// B7.26：全氏族巡逻调度器。继承 BaseSettlementVisitScheduler，仅保留巡逻特化。
/// 候选源：_clan.Settlements 全集。过滤：跳过被围攻/被劫掠村庄。
/// 保留旧 API 名 PickNextStop（PatrolManager 调用方）。
/// </summary>
public sealed class ClanPatrolScheduler : BaseSettlementVisitScheduler
{
    public ClanPatrolScheduler(Clan clan) : base(clan) { }

    protected override IEnumerable<Settlement> EnumerateCandidates(MobileParty party)
    {
        return _clan.Settlements ?? (IEnumerable<Settlement>)System.Array.Empty<Settlement>();
    }

    protected override bool PassesCandidateFilter(Settlement s, MobileParty party)
    {
        if (s.OwnerClan != _clan) return false;
        if (s.IsUnderSiege) return false;
        var config = ConfigurationManager.Current.ClanPatrol;
        if (s.IsVillage && config.AvoidRaidedVillages && IsVillageRaided(s)) return false;
        return true;
    }

    protected override float MinVisitGapHours
        => ConfigurationManager.Current.ClanPatrol.MinVisitGapHours;

    protected override float DistanceWeightHoursPerTile
        => ConfigurationManager.Current.ClanPatrol.DistanceWeightHoursPerTile;

    protected override float EtaBufferHours
        => ConfigurationManager.Current.ClanPatrol.EtaBufferHours;

    protected override string SchedulerLogTag => "ClanPatrolScheduler";

    /// <summary>保留旧 API 名给 PatrolManager 调用。</summary>
    public Settlement? PickNextStop(MobileParty patrolParty) => PickNext(patrolParty);

    /// <summary>
    /// 巡逻特化：判断被围攻的同氏族城是否需要本队赶去防守。
    /// 返回 null 表示当前无需防御响应。
    /// </summary>
    public Settlement? GetDefenseTarget(MobileParty patrolParty)
    {
        if (patrolParty == null) return null;
        try
        {
            foreach (var s in _clan.Settlements ?? (IEnumerable<Settlement>)System.Array.Empty<Settlement>())
            {
                if (s == null) continue;
                if (s.OwnerClan != _clan) continue;
                if (!s.IsUnderSiege) continue;
                if (!s.IsTown && !s.IsCastle) continue;
                return s;
            }
        }
        catch (System.Exception ex)
        {
            SovereignTowns.Logging.Logger.Error($"ClanPatrolScheduler.GetDefenseTarget failed for clan '{_clan?.StringId}'", ex);
        }
        return null;
    }

    private static bool IsVillageRaided(Settlement village)
    {
        return village.Village?.VillageState == Village.VillageStates.Looted
            || village.Village?.VillageState == Village.VillageStates.BeingRaided;
    }
}
```

**注意**：上面 `GetDefenseTarget` 是粗略骨架。**实施前先 Read 现有 `ClanPatrolScheduler.cs` 内 `GetDefenseTarget`（约 L205-）原始实现**，逐字搬运（含可能的距离过滤、优先级排序、log 等），不要凭空写。

### Step 2: 全工程 grep 确认 API 调用面未变
- [ ] Run: `grep -rn "ClanPatrolScheduler\.\|\.PickNextStop\|\.RecordVisit\|\.GetDefenseTarget\|\.PreemptiveBook\|\.IsStuck\|\.TryMarkArrival\|\.NotifyAllLost\|\.NotifySettlementLost\|\.NotifyPartyDestroyed" SovereignTowns/src/`
- [ ] 确认所有调用点用的方法在基类或子类存在

### Step 3: 验证编译
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
  - Expected: 0 错 0 警

---

## Task A3: 重构 `ClanRecruiterScheduler` 继承基类

**Files:**
- Modify: `SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs`

**Context for implementer:** 与 A2 同模式。候选源 = `RecruitmentPlanner.RankCandidates(capitalTown, 100f, 8, null, rule)` 转换 `cand.VillageSettlement`。无过滤（RankCandidates 已处理）。保留旧 API 名 `PickNextVillage`。**注意**字段重命名：原 `_lastRecruitedAt` 被基类的 `_lastVisitedAt` 取代，**语义相同**。

### Step 1: 重写文件

完整替换 `SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs` 为：

```csharp
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using SovereignTowns.Capital;
using SovereignTowns.Configuration;
using SovereignTowns.Coordination;

namespace SovereignTowns.Recruitment;

/// <summary>
/// B7.27：全氏族征兵调度器。继承 BaseSettlementVisitScheduler，候选源用 RecruitmentPlanner.RankCandidates。
/// 保留旧 API 名 PickNextVillage（RecruitmentManager 调用方）。
/// </summary>
public sealed class ClanRecruiterScheduler : BaseSettlementVisitScheduler
{
    public ClanRecruiterScheduler(Clan clan) : base(clan) { }

    protected override IEnumerable<Settlement> EnumerateCandidates(MobileParty party)
    {
        var capitalMgr = CapitalRegistry.Instance?.GetForClan(_clan);
        var capitalTown = capitalMgr?.GetCapital();
        if (capitalTown == null) return System.Array.Empty<Settlement>();

        var rule = ConfigurationManager.GetRuleFor(capitalTown);
        if (rule == null) return System.Array.Empty<Settlement>();

        var candidates = RecruitmentPlanner.RankCandidates(
            capitalTown,
            maxDistance: 100f,
            maxResults: 8,
            excludeSettlements: null,
            matchingRule: rule);
        if (candidates == null || candidates.Count == 0) return System.Array.Empty<Settlement>();

        return candidates
            .Select(c => c.VillageSettlement)
            .Where(s => s != null)!;
    }

    protected override bool PassesCandidateFilter(Settlement s, MobileParty party) => true;

    protected override float MinVisitGapHours
        => ConfigurationManager.Current.ClanRecruiter.MinVisitGapHours;

    protected override float DistanceWeightHoursPerTile
        => ConfigurationManager.Current.ClanRecruiter.DistanceWeightHoursPerTile;

    protected override float EtaBufferHours
        => ConfigurationManager.Current.ClanRecruiter.EtaBufferHours;

    protected override string SchedulerLogTag => "ClanRecruiterScheduler";

    /// <summary>保留旧 API 名给 RecruitmentManager 调用。</summary>
    public Settlement? PickNextVillage(MobileParty recruiterParty) => PickNext(recruiterParty);
}
```

**注意**：`RecruitmentPlanner.RankCandidates` 的真实签名在 `Recruitment/RecruitmentPlanner.cs`，参数名称已与现有 ClanRecruiterScheduler 调用一致（`maxDistance / maxResults / excludeSettlements / matchingRule`）。`VillageSettlement` 是候选项字段名（也已与现有调用一致）。如 implementer 遇到字段名报错，去 RecruitmentPlanner 核对。

### Step 2: 全工程 grep 确认 API 调用面未变
- [ ] Run: `grep -rn "ClanRecruiterScheduler\.\|\.PickNextVillage" SovereignTowns/src/`
- [ ] 确认调用点都仍能命中（PickNextVillage 在子类，其他方法在基类）

### Step 3: 验证编译
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
  - Expected: 0 错 0 警

### Step 4: Release build
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Release`
  - Expected: 0 错 0 警

A 系列完成。

---

# B 系列：共享工具类

## Task B1: `PartyNameFormatter` + 替换 4 manager 调用

**Files:**
- Create: `SovereignTowns/src/Common/PartyNameFormatter.cs`
- Modify: `SovereignTowns/src/SallyForth/SallyForthManager.cs`
- Modify: `SovereignTowns/src/Patrol/PatrolManager.cs`
- Modify: `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs`
- Modify: `SovereignTowns/src/Recruitment/RecruitmentManager.cs`

**Context for implementer:** 4 个 manager 各自有私有 `SafeName(MobileParty?)` / `SafeName(Settlement?)` / `SafeMemberCount(MobileParty?)` 实现（共约 32 行重复）。抽 static class `PartyNameFormatter`，删私有实现。

### Step 1: 创建工具类

Create `SovereignTowns/src/Common/PartyNameFormatter.cs`：

```csharp
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace SovereignTowns.Common;

/// <summary>
/// MobileParty / Settlement 的 null-safe 名称与计数读取。
/// 在 log 与 telemetry 中用于产出可读字符串，绝不抛异常。
/// </summary>
public static class PartyNameFormatter
{
    public static string SafeName(MobileParty? party)
    {
        if (party == null) return "(null party)";
        try { return party.Name?.ToString() ?? "(unnamed)"; }
        catch { return "(name error)"; }
    }

    public static string SafeName(Settlement? settlement)
    {
        if (settlement == null) return "(null settlement)";
        try { return settlement.Name?.ToString() ?? "(unnamed)"; }
        catch { return "(name error)"; }
    }

    public static int SafeMemberCount(MobileParty? party)
    {
        if (party == null) return 0;
        try { return party.MemberRoster?.TotalManCount ?? 0; }
        catch { return 0; }
    }
}
```

### Step 2-5: 在 4 个 manager 替换调用

对 `SallyForthManager.cs` / `PatrolManager.cs` / `PartyLifecycleManager.cs` / `RecruitmentManager.cs` **逐一**：
- [ ] 加 `using SovereignTowns.Common;`
- [ ] 删 private `SafeName(MobileParty?)` / `SafeName(Settlement?)` / `SafeMemberCount(MobileParty?)` 方法体（如有）
- [ ] 全文 `SafeName(` 改为 `PartyNameFormatter.SafeName(`；`SafeMemberCount(` 改为 `PartyNameFormatter.SafeMemberCount(`
- [ ] 不动其他工具方法（如 `SafeActualClan` 等留在原处，本 task 不涉及）

### Step 6: 验证编译
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
  - Expected: 0 错 0 警

---

## Task B2: `TroopTransferHelper` + 替换 3 manager 调用

**Files:**
- Create: `SovereignTowns/src/Common/TroopTransferHelper.cs`
- Modify: `SovereignTowns/src/SallyForth/SallyForthManager.cs`
- Modify: `SovereignTowns/src/Patrol/PatrolManager.cs`
- Modify: `SovereignTowns/src/Recruitment/RecruitmentManager.cs`

**Context for implementer:** 3 个 manager 各有"从城堡 garrison 抽兵到自定义 party"和"反向归并 garrison"的 95% 重复实现。**不要凭空写**：

> **关键约束**：先 **Read** 现有 `SallyForthManager.TransferTroopsFromGarrison` 完整实现（约 76 行）和 `RecruitmentManager.ExtractLowTierEscort` 完整实现，**逐字复制**其 vanilla API 调用（包括 TroopRoster 的具体方法名、null 检查路径、log 模板）到 `TroopTransferHelper.TransferFromGarrison`。**唯一改造**：把"取低 tier 还是高 tier 先"参数化成 `SortStrategy` enum。原代码经过生产测试，不要改 vanilla API 路径。
>
> 同样对 `TransferTroopsBackToGarrison`：Read 现有实现，逐字搬运到 `TroopTransferHelper.TransferBackToGarrison`。

排序策略 enum：

```csharp
public enum SortStrategy { LowestTierFirst, HighestTierFirst }
```

调用方决定排序：
- SallyForth 出击抽兵：**HighestTierFirst**（精锐先出）
- Patrol 巡逻抽兵：**LowestTierFirst**（保留城内精锐）
- Recruitment 护卫抽兵：**LowestTierFirst**

### Step 1: Read 现有实现作为模板
- [ ] Read `SovereignTowns/src/SallyForth/SallyForthManager.cs` 找 `TransferTroopsFromGarrison` 方法（全文 grep `TransferTroopsFromGarrison`）
- [ ] Read `SovereignTowns/src/SallyForth/SallyForthManager.cs` 找 `TransferTroopsBackToGarrison`
- [ ] Read `SovereignTowns/src/Recruitment/RecruitmentManager.cs` 找 `ExtractLowTierEscort` / `TryRestoreEscort`

### Step 2: 创建 `TroopTransferHelper`

Create `SovereignTowns/src/Common/TroopTransferHelper.cs`，**结构**为：

```csharp
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common;

public static class TroopTransferHelper
{
    public enum SortStrategy { LowestTierFirst, HighestTierFirst }

    /// <summary>
    /// 从 source garrison 抽兵到 target party。返回实际转移人数。
    /// 失败时返回 0，兵员留在 source 不蒸发。
    /// </summary>
    public static int TransferFromGarrison(
        Settlement source,
        MobileParty target,
        int desiredCount,
        SortStrategy sort = SortStrategy.LowestTierFirst,
        Func<CharacterObject, bool>? filter = null)
    {
        // TODO: implementer 在此处 ↓
        //   1. 把 SallyForthManager.TransferTroopsFromGarrison 的方法体逐字复制进来
        //   2. 用 sort 参数替换原硬编码的 Tier 排序方向
        //   3. 用 filter 参数（如有）替换原硬编码的"跳过 Hero / 跳过特定 character" 过滤；
        //      若原实现无 filter，把 filter 当作"附加"过滤层（null 时表示无附加）
        //   4. 错误日志前缀改 `[TroopTransferHelper.TransferFromGarrison]`
        throw new NotImplementedException("Implementer：从 SallyForthManager.TransferTroopsFromGarrison 逐字搬运");
    }

    /// <summary>
    /// 把 source party 的 member roster 全部归并回 target garrison。
    /// 失败时兵员留在 party 不蒸发。
    /// </summary>
    public static int TransferBackToGarrison(MobileParty source, Settlement target)
    {
        // TODO: implementer 在此处 ↓
        //   1. 把 SallyForthManager.TransferTroopsBackToGarrison（如存在）或类似 manager 内的反向归并方法逐字复制
        //   2. 错误日志前缀改 `[TroopTransferHelper.TransferBackToGarrison]`
        throw new NotImplementedException("Implementer：从 SallyForthManager.TransferTroopsBackToGarrison 逐字搬运");
    }
}
```

**Implementer 必须**：把上面 2 个 `NotImplementedException` 全部替换为从现有 manager 逐字复制的实现，不能交付带 `NotImplementedException` 的版本。

### Step 3: 替换 SallyForthManager 内调用
- [ ] 加 `using SovereignTowns.Common;`
- [ ] 找 private `TransferTroopsFromGarrison(...)` / `TransferTroopsBackToGarrison(...)` 方法
- [ ] 选项 A（最小侵入）：把方法体替换为对 `TroopTransferHelper` 的薄包装调用：
  ```csharp
  private int TransferTroopsFromGarrison(Settlement home, MobileParty party, int desiredCount)
      => TroopTransferHelper.TransferFromGarrison(home, party, desiredCount, TroopTransferHelper.SortStrategy.HighestTierFirst);
  ```
- [ ] 选项 B（彻底）：删私有方法，改所有调用点为 `TroopTransferHelper.TransferFromGarrison(home, party, count, SortStrategy.HighestTierFirst)` —— **implementer 自己判断哪种 diff 小**

### Step 4: 替换 PatrolManager 内调用
- [ ] 同 Step 3，排序用 `LowestTierFirst`

### Step 5: 替换 RecruitmentManager 内调用
- [ ] 同 Step 3，排序用 `LowestTierFirst`（征兵护卫）

### Step 6: 验证编译
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug`
  - Expected: 0 错 0 警

### Step 7: Release build
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Release`
  - Expected: 0 错 0 警

B 系列完成。

---

# F: 最终全局评审

## Task F: 全局代码评审 + 最终构建

### Step 1: 双模式 build
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug` → 0 错 0 警
- [ ] Run: `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Release` → 0 错 0 警

### Step 2: 派 final code reviewer agent
向 final reviewer 提供：
- spec 全文（`docs/superpowers/specs/2026-05-14-simplification-refactor-design.md`）
- plan 全文（本文件）
- 改动文件清单（C 系列 7 个文件、A 系列 3 个、B 系列 5-6 个）

Reviewer 检查项：
- 是否所有 CLAUDE.md 硬不变量都保留？（net472 / SaveBaseId / [SaveableField] / GameModels-OnGameStart / LLM-no-realtime / try/catch / HourlyTickParty-first-line-filter）
- 删除 Snapshot 后是否还有遗漏的引用？（grep `_pending\|Snapshot\|CreateSnapshot\|RestoreFromSnapshot\|RestoreFromStringId\|GetCapitalStringId\|RestorePlayerCapital\|RestoreAiCapitals\|ExportPlayerCapital\|ExportAiCapital`）
- BaseSettlementVisitScheduler.PickNext 得分公式是否与原 `PickNextStop`/`PickNextVillage` 行为一致？（`score = -hoursSinceVisit + DistanceWeight*distance` 最小化）
- TroopTransferHelper 是否丢兵员？特别是 NotImplementedException 必须全部消失
- `ClanPatrolScheduler.GetDefenseTarget` 是否完整保留巡逻特化行为
- 所有 manager 内的 `SafeName` 私有实现是否都已删

### Step 3: 修复 reviewer 发现的问题
- [ ] 如有，dispatch implementer 修复 → reviewer 复评 → 通过

### Step 4: 报告用户
向用户汇报：
- 净行数变化
- 改动文件数
- Debug + Release 双 0/0
- final review 通过
- 提示用户做游戏内手动验证（spec §"验证" 1-6 项）
