# Manual-Mode 旋钮泄漏审计 — 待办 (2026-05-22)

## 已修 (P0)

`GarrisonAllocationSolver.ClanWageBudget` 战时预算底线曾用 `ConfigTargetHeads`,
后者读取 `TownGarrisonRule.TargetTotalCount`(默认 150)与 `BranchRule.TargetPower`
—— 二者均为 manual-mode 旋钮(`RequiresManualMode`),却在 auto 模式下经战时底线
间接支配调度器输出(推荐驻军被钉死在 150)。已改为 `Σ adequate × wagePerTroop`,
与 `Solve` 主循环同一 floor/hardCap/adequate 口径。`ConfigTargetHeads` 已删除。

## 待办 (P2 — 暂不修)

`SupplyDemandGraph.BuildSettlementStates` 在 settlement 缺席于 Pass A 结果时,
auto-mode 回退路径仍调用 `ComputeDesiredTarget(rule, risk)`(首府,~line 378)
与 `branch.TargetPower`(非首府,~line 421)—— 同样是 manual 旋钮口径。

**为何暂不修**:P0 修复后 `budgetCap <= 0` 几乎不可达(战时底线恒 > 0,和平期
也几乎总有非零税收),Pass A 必然产出每个 settlement 的目标,回退路径近乎死代码。
advisor 复核同意推迟。若未来观察到该回退被触发(日志中 settlement 缺席 Pass A),
再把回退口径统一到 `adequate`。

---

# 征兵队远征审计 (2026-05-22)

## 已修 — Phase 1:征兵村庄选择完全交给 MCMF

最初的 role-透传修复(`RankCandidates(desiredRole)`)是治标:MCMF 决定 role,
但选村仍由 `RankCandidates` 独立打分 —— 双层选村冲突。Phase 1 重构彻底解决:

- `SupplyDemandGraph` 的 `Village` source 改为 **per-village**:全图取距首府最近的
  Top-`RecruiterVillageCandidateCap`(默认 250,原版全图约 210 村即全纳入)个合格村,
  每 (村, role) 一个 source 节点,边费用计入真实地图距离。MCMF 直接选"兵种 + 距离"最优的村。
- `RecruiterPartyInstruction` 增 `TargetVillage`;`CapitalLogisticsManager` 按 role
  把多个目标村打包成最近邻行程;`StRecruiterPartyComponent` 改为行程驱动状态机
  (`_itinerary` / `_itineraryIndex` / `_tripCountTarget`,持久化)。
- 删除 `RecruitmentPlanner`(`RankCandidates`)、`ClanRecruiterScheduler`、
  `ClanRecruiterConfig` —— 独立打分层全部移除。设计见 `mcmf-village-redesign-plan.md`。

这同时关闭了之前推迟的 P2(village source 仅枚举首府直属村)。

## 运行期诊断信号

若仍观察到"远处有 X 兵种却不派征兵队":
- 主因:该村距首府成本(距离 + overhead)≥ `McmfUnmetCost`(默认 2000)→ MCMF 宁可走
  unmet。调高 `McmfUnmetCost`(注意会同时影响调拨决策)。
- 次因(仅超大 mod 地图):全图村数超过 `RecruiterVillageCandidateCap`(默认 250)→
  该村未进图。调高之。
日志 `Recruiter town=... village=... role=...` + 决策审计 `DispatchRecruiter` 可定位。

## 待办 (Phase 2 — 独立提交)

Prisoner 接入 MCMF:`PrisonerConvertInstruction` + `ExecuteMcmfInstruction` switch
case 已留钩子;加 `SourceKind.Prisoner` + 兵源枚举器 + 执行器。与 Phase 1 同设计模式。
