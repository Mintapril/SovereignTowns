# Phase 3 — 新增发现（followups）

> Phase 3 执行时发现的、未在 Phase 2 中识别的问题。**本阶段不处理**，留给 Phase 4 / Phase 5 / 新 backlog。

---

## F1 — RecruitmentPlanner 调用方的"100→200 距离 fallback"在 D2 之后成为 dead code

### 背景
[GlobalConfig.cs:237](SovereignTowns/src/Configuration/GlobalConfig.cs:237) 定义：
> `RecruitmentCandidateBatchDistanceMax`：B2：RecruitmentPlanner.RankCandidates 第一轮 maxDistance=100 无候选时第二轮的上限。0 关闭降级搜索。默认 200。

调用点：
- [RecruitmentDispatcher.cs:111,124](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs:111) — 2-轮 fallback 显式实现
- [StRecruiterPartyComponent.cs:449,464](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:449) — 同模式

### 现状（Phase 3 D2 之后）
D2 移除 `RecruitmentPlanner.TryAdd` 内的 100m 距离过滤后，`maxDistance` 参数对内部行为已无影响。

→ 调用方的"第一轮 100m 无果再 200m"实际两轮都返回相同候选集，第二轮纯属浪费一次 `Settlement.All` 扫描。

### 推荐处理
Phase 4 重构候选：
1. 删除调用方的 fallback 第二轮调用
2. 同时删除 `RankCandidates` 的 `maxDistance` 参数（API 变更，5 个调用点全部调整）
3. 删除 `GlobalConfig.RecruitmentCandidateBatchDistanceMax` 字段（CLAUDE.md 允许 ConfigVersion 升级即可）
4. 删除相关 WebUI 字段（若有暴露）

工作量：中等。**勿在 Phase 3 处理（不夹带重构）。**

---

## F2 — 2 个 pre-existing CS8604 编译警告

### 现象
全量 rebuild 报：
```
CS8604(47,120) warning: Possible null reference argument for parameter 'party' in 'BaseSettlementVisitScheduler.ComputeEtaHours'
CS8604(47,92) warning: Possible null reference argument for parameter 'home' in 'PartyLifecycleManager.GetCapFor'
```

调用点：
- `ComputeEtaHours(party, best)` 出现于 [BaseSettlementVisitScheduler.cs:120](SovereignTowns/src/Coordination/BaseSettlementVisitScheduler.cs:120)
- `GetCapFor(settlement, ...)` 出现于 [PatrolDispatcher.cs:92](SovereignTowns/src/Patrol/PatrolDispatcher.cs:92)

RTK 代理压缩了警告输出的文件路径，行号"47"是 RTK 输出格式残留，**与实际源码行号不对应**。

### 现状
Phase 3 启动前 baseline `dotnet build` 是 incremental 模式（0 warnings 报告），但全量 rebuild 一致显示这 2 个警告。即这 2 个警告 **不是 Phase 3 引入**，Phase 1/2 baseline 漏报。

### 推荐处理
Phase 5 健壮性检查中处理：要么在调用点加 `if (party == null) return ...` 守卫；要么把方法签名改为 `MobileParty?`。

---

## F3 — `PartyEconomyHelper.cs` 与 doc §20 #1 重构现状

### 背景
工作树中 [PartyEconomyHelper.cs](SovereignTowns/src/Common/PartyEconomyHelper.cs)（new file, 264 行）的[L17–L26 注释](SovereignTowns/src/Common/PartyEconomyHelper.cs:17)明言：

> Sally / Transfer：短命任务，凭空塞 2-3 天食物，简化复杂度，无队伍资金。
> Patrol：终身户外巡逻，从首府所有者扣 2000 第纳尔作启动资金；用资金买食物 + 战利品卖掉补充资金；销毁时余款还首府所有者（自负盈亏）。

但 doc §20 #1（doc:1342）要求「**由所有 ST 队伍共享**」。

### 现状（Phase 3 后无变化）
Phase 3 不处理 §20 重构。当前分叉设计保留。

### 推荐处理
**Phase 4 主战场**。具体计划：
1. 扩展 `PartyEconomyHelper` 让 Sally/Transfer/Recruiter 也支持"启动资金 → 卖战利品 → 买粮"闭环
2. `StPartyComponent` 增加 `_teamFunds` 字段（移到基类）
3. 4 子类全部继承经济组件
4. §11 BattleLootHandler 简化（依赖 §20 #1 完成）
5. doc §0–§19 中"3 天免费食物"措辞更新

工作量：大（涉及 4 个 Component + Battle 处理）。

---

## F4 — `b1-hygiene-backlog` 残留 P1（按用户指令本轮不处理）

旧 backlog（已删除，git HEAD 仍可读）中 P1 两项 — `PatrolDispatcher garrison! null-forgiving 显式保护` 与 `玩家氏族巡逻退款 home==null 容错` — 用户回复"全部按照文档来"时未明示纳入本轮。这两项**与 doc §0–§19 行为对齐无关**，故未列入 Phase 3 任务。

### 推荐处理
- 单独入新 `audits/b2-hygiene-backlog.md`，由你择期单独周期处理
- 或在 Phase 5 收尾时被"健壮性补强"扫到（B1.1 是 null-forgiving smell，B1.2 是 owner-changed 边界）

---
