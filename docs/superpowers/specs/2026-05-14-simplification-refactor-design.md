# SovereignTowns 业务逻辑简化重构 设计文档

**日期**: 2026-05-14
**触发**: 用户要求"通读项目、简化业务逻辑、复用代码、降低复杂度以确保低 bug 率"
**前置**: 4 个 Explore subagent 的全量普查报告（见会话历史）

---

## 目标

合并镜像实现、抽出共享工具、统一持久化协议，**净削减约 500-550 行代码 + 显著降低单文件复杂度**，从而降低 bug 表面积。

## 范围（用户在 brainstorm 中确认）

**做：A + B + C**
- **A. Scheduler 基类抽象** — 合并 `ClanPatrolScheduler` (405 行) 与 `ClanRecruiterScheduler` (276 行)
- **B. 共享工具类** — `PartyNameFormatter.SafeName(...)` + `TroopTransferHelper.TransferTroops{From,To}Garrison`
- **C. SyncData envelope** — 8 个 `_pending*` 字段 + 8 个 JSON SyncData 块 → 单个 `PersistedState` POCO

**不做（留待未来）**
- LLM 子系统重构（用户表达"野心"：未来 LLM 尽可能介入决策，单独 brainstorm 周期）
- D. CapitalRegistry 字典优化（约 60 行收益，下轮再评估）
- E. 长方法拆分（RecruitFromTargetVillage 164 行、TryCreateSallyParty 85 行）
- F. 死代码 / obsolete 常量清理

**不需要兼容旧存档**（用户明确说明：mod 仍在快速迭代，旧存档可丢弃）。这显著简化 C 的实现 —— 不需要 fallback 读取旧 SyncData key。

---

## A. Scheduler 基类抽象

### 现状
`ClanPatrolScheduler` (405 行) 与 `ClanRecruiterScheduler` (276 行) 是镜像实现：
- `_lastVisitedAt: Dictionary<Settlement, CampaignTime>` — 一致
- `_preemptiveBook: Dictionary<Settlement, CampaignTime>` — 一致
- `RecordVisit / PreemptiveBook / NotifySettlementLost / NotifyAllLost / NotifyPartyDestroyed / IsStuck / TryMarkArrival` — 完全一致
- `CreateSnapshot / RestoreFromSnapshot` — 仅 DTO 字段名不同
- `PickNext*` — 候选源不同（Patrol 来自 `Clan.Settlements` 全集，Recruiter 来自 `RecruitmentPlanner.RankCandidates` 筛后村庄），得分函数相同（`-hoursSinceVisit + DistanceWeight * distance`）

### 新结构
```
src/Coordination/
  BaseSettlementVisitScheduler.cs   (~220 行, 共享逻辑)
src/Patrol/
  ClanPatrolScheduler.cs            (~90 行子类, 重写候选源 + 巡逻特化的 IsVillageRaided 过滤)
src/Recruitment/
  ClanRecruiterScheduler.cs         (~80 行子类, 重写候选源)
```

基类提供：
- `protected Dictionary<Settlement, long> _lastVisitedAtMs` (改用 game-epoch ms 长整型，避免 CampaignTime 序列化坑)
- `protected Dictionary<Settlement, long> _preemptiveBookMs`
- `public virtual Settlement? PickNext(MobileParty party, Clan clan)` — 调用 abstract `EnumerateCandidates(party, clan)` + 共享评分排序
- `protected abstract IEnumerable<Settlement> EnumerateCandidates(MobileParty party, Clan clan)` — 子类实现差异点
- `protected virtual bool AcceptCandidate(Settlement s, MobileParty party)` — 默认 `true`，PatrolScheduler 重写为加 raided 过滤
- `public void RecordVisit / PreemptiveBook / NotifySettlementLost / ...`（直接继承）
- `public Snapshot CreateSnapshot()` / `void RestoreFromSnapshot(Snapshot)` — DTO 字段统一为 `LastVisitedAtMs` / `PreemptiveBookMs`，**不保留向后兼容**

### Snapshot DTO
统一一个 `SchedulerSnapshot` 类，被两个子类共用：
```csharp
public sealed class SchedulerSnapshot {
    public Dictionary<string, long> LastVisitedAtMs { get; set; } = new();
    public Dictionary<string, long> PreemptiveBookMs { get; set; } = new();
}
```

### 关键不变量
- `RestoreFromSnapshot` 必须能容忍 settlement stringId 失效（土地被毁/拆除），跳过失效项不抛
- 评分公式参数化（基类持有 `DistanceWeightHoursPerTile`，子类构造时传入对应 config）

---

## B. 共享工具类

### B1. `PartyNameFormatter`

**新文件**: `src/Common/PartyNameFormatter.cs`

```csharp
public static class PartyNameFormatter {
    public static string SafeName(MobileParty? party);
    public static string SafeName(Settlement? settlement);
    public static int SafeMemberCount(MobileParty? party);
}
```

**替换位置**（合计 ~8 处实现）：
- `SallyForthManager.SafeName` / `SafeMemberCount`
- `PatrolManager.SafeName` / `SafeMemberCount`
- `PartyLifecycleManager.SafeName` / `SafeActualClan` 相关
- `RecruitmentManager.SafeName`

### B2. `TroopTransferHelper`

**新文件**: `src/Common/TroopTransferHelper.cs`

```csharp
public static class TroopTransferHelper {
    // 从城堡 garrison 抽兵到 party.MemberRoster：返回实际转移人数
    public static int TransferFromGarrison(
        Settlement source,
        MobileParty target,
        int desiredCount,
        SortStrategy sort = SortStrategy.LowestTierFirst,
        Func<CharacterObject, bool>? filter = null);

    // 反向：party 残兵全部并回 garrison
    public static int TransferBackToGarrison(MobileParty source, Settlement target);

    public enum SortStrategy { LowestTierFirst, HighestTierFirst, Random }
}
```

**替换位置**（~3 处近 95% 相同实现）：
- `SallyForthManager.TransferTroopsFromGarrison` / `TransferTroopsBackToGarrison` (~76 行)
- `PatrolManager.TransferTroopsFromGarrison` (~40 行)
- `RecruitmentManager.ExtractLowTierEscort` / `TryRestoreEscort` (~50 行，差排序 key)

排序策略参数化避免分支爆炸。

### 关键不变量
- 必须容忍 `MemberRoster` 在转移中被并发修改（仅在 vanilla 主线程触发，但仍 try/catch）
- 转移失败必须**不丢失兵员**（要么留 source，要么到 target，绝不蒸发）

---

## C. 完全移除 mod 自定义存档持久化（稳定版后再做）

### 用户决策（2026-05-14）
"移除所有关于存档兼容的模块，留空文件，后续稳定版后再写存档兼容部分"

把整个 mod-side 持久化层删干净；vanilla 反序列化必需的 `[SaveableField]` / `SovereignTownsTypeDefiner` **保留**，否则 custom party component 反序列化时字段为默认值会运行时崩溃。

### 2026-05-14 二次决策：首府是例外
用户审查重载后体感"每次都要重设首府"过重，决定**仅回补首府持久化**：
- 加回 mod 自定义 SyncData 1 个 JSON 块（envelope-lite：`Dictionary<clanStringId, settlementStringId>`），存所有 clan（玩家 + AI）的首府映射
- Scheduler 历史 / Finance ledger 仍**不存**（B7.27 起重载后清零；24h 内自然恢复）
- 后续 X 系列任务专门做此反向回补

### 删除范围（mod 自定义 SyncData）

#### C1. `SovereignTownsCampaignBehavior.SyncData` 内 5 个 JSON 块
现状（grep 确认行号）：
1. L103 `st_capital_stringid` — legacy 单首府
2. L120 `st_ai_capitals_json` — AI 多氏族首府映射
3. L150 `st_patrol_schedulers_json`
4. L180 `st_recruiter_schedulers_json`
5. L207 `st_finance_snapshot_json`

整个 `SyncData(IDataStore dataStore)` 方法体清空，仅留 `// TODO: 稳定版后实现 mod 自定义存档持久化` 占位注释 + 空 try/catch 框架（防止 vanilla 在 saving 阶段 reflection 假定方法存在但抛 NRE）。

#### C2. 删除所有 `_pending*` 字段与对应 restore 调用
扫描并删除（在 `SovereignTownsCampaignBehavior`）：
- `_pendingCapitalStringId`
- `_pendingAiCapitalsJson` / 对应解析后字典
- `_pendingPatrolSchedulers`
- `_pendingRecruiterSchedulers`
- `_pendingFinanceSnapshot`

以及 `OnSessionLaunched` 内对它们的 deserialize / 下发逻辑（如 `_capitalRegistry.RestorePatrolSchedulers(...)`、`ledger.RestoreFromSnapshot(...)`）。

#### C3. 各 Manager / Scheduler / Ledger 的 `CreateSnapshot / RestoreFromSnapshot`
扫描并删除：
- `ClanPatrolScheduler.CreateSnapshot / RestoreFromSnapshot`
- `ClanRecruiterScheduler.CreateSnapshot / RestoreFromSnapshot`
- `CapitalRegistry.ExportPatrolSchedulerSnapshots / RestorePatrolSchedulers`
- `CapitalRegistry.ExportRecruiterSchedulerSnapshots / RestoreRecruiterSchedulers`
- `CapitalRegistry.ExportAiCapitals / RestoreAiCapitals`（如存在）
- `CapitalManager` 内类似导出/导入方法（如存在）
- `ModExpenseLedger.CreateSnapshot / RestoreFromSnapshot`

#### C4. Snapshot DTO 类删除
- `ClanPatrolSchedulerSnapshot`
- `ClanRecruiterSchedulerSnapshot`
- `FinanceSnapshot`
- 任何其他 mod-only 序列化 DTO（grep 确认）

### 保留范围（vanilla 反序列化必需）
- `SovereignTownsTypeDefiner.cs`（vanilla 通过 reflection 扫描 `SaveableTypeDefiner` 子类，必须存在；删了 4 个 component 类无法反序列化）
- `RecruitingPartyComponent` / `TransferPartyComponent` / `SallyForthPartyComponent` / `DismissPartyComponent` 上的 `[SaveableClass]` / `[SaveableField]`（共 11 个字段：home / target / state）—— 删了 component 反序列化时字段为 default，立刻崩
- `PartyLifecycleManager.RebuildFromCampaign()` —— 从 live `MobileParty.All` 扫描重建 `_tracked` dict。这是加载后的"现场重建"，**不依赖** mod-side SyncData

### 重载存档后行为
- 玩家氏族首府：**丢失** → 玩家需重新设定
- AI 氏族首府：**丢失** → 由首次 daily tick 内规则选举重建
- 巡逻/征兵 scheduler 历史：**清零** → 多支 party 前几小时可能撞同村，约 24h 后差异化恢复
- Finance ledger 流水：**清零**
- Custom party 实例（4 个 component 子类）：**保留** ← vanilla 自动序列化 + `RebuildFromCampaign` 重建 `_tracked`

### 关键不变量
- 任何对 `_pending*` 字段或 `Restore*Snapshots`/`Create*Snapshot` 方法的引用必须**连根拔起**（编译器报错指出剩余位置）
- `OnSessionLaunched` 内不能再调任何 Restore 类方法
- `SafeUninstallMenu` 内 `ledger.Clear()` 调用**保留**（这是运行时清零，与存档无关）

---

## 跨范围硬约束（CLAUDE.md 不变量）

本次重构必须保留：
1. TargetFramework = `net472`（不动 csproj）
2. SaveBaseId / LocalSaveId 不变（C 删除 mod 自定义 SyncData 但保留 vanilla `[SaveableField]` + TypeDefiner）
3. GameModels 仍在 `OnGameStart` 注册（不动）
4. LLM 仍仅在 DailyTick / 用户主动（A/B 不涉及 LLM）
5. 每个事件入口的 try/catch 保留
6. `HourlyTickPartyEvent` 入口仍按 PartyComponent 类型首行过滤
7. Newtonsoft.Json 仍是 mod 默认 JSON 库（WebUI 端点等仍用），仅删 mod-side SyncData 引用

## 验证

无单元测试。验证流程：
1. `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug` → 0 错 0 警
2. `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Release` → 0 错 0 警
3. 启动游戏 → 新建战役 → 设首府 → 派巡逻队 / 征兵队 → 24h 后多支 party 不重复访问同村
4. 派出击 + 巡逻 → 出击进入战斗 → 巡逻 ETA<2h 时支援
5. 保存 / 退出 / 重新加载 → custom party 实例仍在（`RebuildFromCampaign` 重建追踪），但首府/scheduler/ledger 归零（已知行为）
6. 安全卸载 → 财务清零、所有 scheduler 清空、custom party 全部归并 garrison

## Out-of-scope（明确剔除）

- 改 csproj / 改 deploy 路径 / 改 build 流程
- 加新功能 / 改用户可见行为（重构必须**行为等价**）
- 改 SubModule.xml / 改 GUI prefab
- 改 README / ARCHITECTURE.md 等文档（如有需要单独追加 task）
