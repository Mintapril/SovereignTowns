# 方案2:双层 MCMF 合并为单一图 — 可行性调研报告 (2026-05-22)

## 0. 目标

把 Pass A(`GarrisonAllocationSolver`,跨城预算/价值分配)与 Pass B
(`SupplyDemandGraph`,兵员路由)从"单向单次交接"合并为**单一 MinCostFlow 图、
单次求解**,以求 allocation + routing 的联合全局最优 —— 消除"A 对路由可行性瞎、
A 的价值梯度被压成标量"两处最优性损失。

---

## 1. 现状:两层如何连接

- **单向、单次、单标量**。Pass A 解完产 `GarrisonAllocationResult`;其中只有
  `Target: Dictionary<Settlement,int>`(每城目标头数)被 Pass B 算法消费。
- Pass B `BuildSettlementStates` 把 `passA.Target` 当 demand 大小:首府直接当头数,
  分支经 `HeadsToPower(heads, ~1.3)` 转 power 口径。
- Pass B 的结果**永不回流** Pass A。无迭代。
- 触发:**daily**,`SovereignTownsCampaignBehavior.OnDailyTick` → `EvaluateAll` →
  每 clan `EvaluateClan`:RunPassA → narrate/log → Stash(Assessments|Financial)→
  RunMcmf(passA)→ ExecuteMcmfInstructions → DisbandExcessGarrisons。同线程同步。

---

## 2. MinCostFlow 引擎:能力与边界

`src/Algorithm/MinCostFlow.cs` —— SSP + Dijkstra + Johnson potentials。

**能做**:单商品 min-cost-max-flow;边容量上界;非负线性费用;选择性边集
(= role / 可达性约束);多源多汇(接超级源汇);整数流。

**不能做**:
- 负费用(`cost<0` 抛异常)→ 价值必须用 `K − value` 偏移成非负。
- 流量下界(无 lower bound)→ "最低驻军"只能靠"floor tier 给极高价值"软实现(同今天)。
- 固定费用 / 每队一次性费用 → 同今天,只能每单位摊。
- 多商品 / 节点容量 → 需图变换(拆点等);本合并**不需要**(见下)。

**规模**:节点数百、边数千、总流量数千 —— daily tick 预算内无压力。

---

## 3. 可行性结论:可行

合并**在单商品 MinCostFlow 上可行**。关键判断:

- role 匹配靠"只在兵种相符的 source/demand 间加边"——单商品 + 选择性边集足够,
  无需多商品。
- **头数作单一货币**:分支 power demand 直接吃 Pass A 头数(反而省掉 `HeadsToPower`
  这一跳);denar 预算经 `÷ wagePerTroop` 进头数图(Pass A 本来就这么做)。
- 价值(收益)用 `K + routing − value` 非负化,与 Pass A 现有 `CostOffset` 同套路。

---

## 4. 合并图模型(草图)

本质 = **Pass A 的"预算 / tier / 价值"骨架,把"budgetNode 直连 tier"的边换成
Pass B 的"真实兵源 → 路由 → tier"子图**。

```
superSource ──[cap=budgetTroopCap]──> budgetNode ──> 招募兵源(InPlace/Village)
superSource ──────────────────────────────────────> 现有驻军兵源(每城每role每tier)
                                                     ↓ (source → demand 边)
   每城每role每tier 的 demand 节点 ← cost = K + routing − value(tier)
                                                     ↓
                                                  superSink
superSource ──> unspent 旁路(cost=K)──> superSink     # 预算/槽位不用
```

- **demand 节点** = Pass A 的价值分层(每城 × role × {floor / core-1..K / surplus})。
  分支用单一占位 role、头数口径(不再 power)。
- **兵源**:① 现有驻军(每城每 role 每 tier 一桶)—— 不耗招募预算;
  ② 招募源(村庄 per-village / 首府 InPlace notable)—— 经 budgetNode 耗预算。
- **source→demand 边费用** = `K + routing − value`:现有驻军留在本城 routing=0,
  调去别城 routing=距离(→ TransferInstruction),招募 routing=距离+overhead。
- **decode**:现有驻军源流到本城 = 留;流到别城 = Transfer;招募源流出 = Recruit/InPlace;
  现有驻军源**未流出** = 超额(v1 不在图内处理,见 §6.1)。
- 一个兵单位同时受 role(边集)、预算(budgetNode 容量)、城 hardCap(该城各 tier
  容量之和)三重约束 —— 单商品天然表达。

---

## 5. 合并后必须保持的对外契约(不可破坏)

1. `DispatchInstruction` 列表(InPlace / Recruiter 含 TargetVillage 行程 / Transfer)。
2. `Target` 字典 —— **每个 fief 必须有条目**(DisbandExcessGarrisons Gate 5 靠存在/缺失区分)。
3. `Budget`(денар/日)+ `BudgetTroopCap`(头数)。
4. `Breakdown` 每城诊断串。
5. **manual 模式推荐值** —— 见 §6.4。
6. `SupplyDemandGraphResult` 统计(SettlementCount / Unmet / TotalFlow / TotalCost)。
7. public/internal helper `WagePerTroopAtMaxTier`、`IsClanAtWar` 被别处复用,不可删。

---

## 6. 五个硬问题

### 6.1 disband 做成涌现行为(决策已锁定)

合并图里"现有驻军源未流出 = 超额遣散" —— 涌现 disband。最初担心"遣散 5 → 下 tick
招 5"颤动,重新推演后**不成立**:存在内建迟滞 —— 现有驻军留本城 routing≈0,招新兵
routing=距离+overhead>0;同一 tier"留"恒比"招"便宜,差额=招募成本。一旦 solver 判定
某兵不值得"留"而遣散,下 tick 不可能花更高成本招回。迟滞带宽=招募成本。叠加 §6.3 的
EWMA 平滑,稳态每 tick 同解,不颤。

实现要点:
- siege / risk / feature-flag(`DisbandUnaffordableExcess`)三道 Gate → 编码为图结构:
  受保护的城,其现有驻军获一条"免费、不占预算、无上限"的保留边,永不被挤出。
- 旧的"超目标 1.x 倍"安全余量取消,solver 修到联合最优。遣散销毁真兵不可逆 → decode
  时加"每 tick 每城遣散人数上限"(速率限制,非阈值,不引入颤动)。
- 据此可**删除** `DisbandExcessGarrisons` 整个 6-Gate 方法。

### 6.2 标度统一 —— v1 真正的工作量大头

Pass A 价值量级 ~0–15M(`CostOffset=20M`);Pass B 路由费用 ~百–数千
(`McmfUnmetCost=2000`)。合并目标 `Σvalue − Σrouting` 里若 value 是百万级、routing
是千级,routing 沦为舍入噪声 → solver 永远无视距离。

**必须重新校准整套 value 函数**(`ValueFloorBase/CoreBase/SurplusEdgeCost`、dim、
threat、strat),使典型 floor-tier 价值与 `McmfUnmetCost(~2000)` 同量级。这不是改
几个常数,是重定标 value 函数的整个输出空间,且**必须靠实战回归验证**(对比合并
前后每城 `Target` 差异能否解释)。代码改动小,playtest 不可压缩。

### 6.3 目标稳定性 —— 合并会破坏隐式低通滤波

现状 Pass A 的输入(prosperity/threat/budget)变化慢 → `Target` 跨 tick 稳定;
Pass B 路由是 per-tick 反应。"慢变目标 + 快变路由"的分离让征兵队/调拨队不会因
tick 噪声反复改向。

合并后单次求解每 tick 重解,**target 可能每 tick 抖动** → 已派出的征兵队半路目标
变了 → dispatcher 颤动。**决策已锁定:两者都做** —— (a) 对 value 函数输入做 EWMA
平滑(治本:目标平滑变化);(b) 给"已在飞"的 instruction 一个粘性偏置(治标:保护在途队伍)。

### 6.4 manual 模式 —— shadow Pass A

manual 模式下路由用玩家手动目标,但 `GarrisonAssessment`(`RecommendedTarget` /
`DailyWageDelta` / `LoopClosesAtPlayerTarget`)仍需"价值函数推荐值"。
**方案**:manual 模式下保留 Pass A 单独跑(几百行、毫秒级)作为推荐引擎,与合并
solver 解耦。即"砍掉 Pass A 入路由的部分,Pass A 本体作为 shadow 留着"。

### 6.5 stockpile 删除(决策已锁定)

今天首府的"招募囤兵 stockpile" demand 节点是**两层分离的脚手架**:Pass B 表达不了
"在首府招兵以转发给分支",只好加一个大小=分支总缺口的假需求逼首府超额招募。

合并图里,"招兵 → 经首府 → 送分支"就是一条流路径(村庄兵源 → 分支 demand 的复合边,
费用=招募+转运,decode 成 Recruiter + Transfer 两条指令)。stockpile 因此冗余;保留它
还会让分支缺口被表示两次(分支 demand 节点 + 首府 stockpile),有重复计数风险。
**决定:合并时删除 stockpile,连同 `IsRecruitmentStockpile` 相关代码。**

---

## 7. v1 验证策略:parallel-run(交付物之一,非事后补)

单跑 build 远不够。在 `EvaluateClan` 里:
- 始终跑 legacy 两层(产 legacy Target / instructions)。
- 同时跑新合并 solver(产 new Target / instructions),**不派发**,只 log 对比。
- 配置开关决定哪份送 dispatcher。

tuning 期逐 clan / 逐 settlement 看差异,直到差异都能解释(或新方案系统性更优)
再切换。这是 v1 的**交付物**。

---

## 8. 工作量估计(实事求是)

- 新写合并 solver:~600–1000 行(图建模 + decode + 契约保留)。
- 重新校准 value 函数 + tuning loop:代码小、playtest 大。
- parallel-run 验证骨架:~200 行,关键。
- 删 legacy(Pass A 入路由的部分;Pass A 本体作 shadow 留)。
- 集成测试 + 多 clan / 多场景验证。

**约 Phase 1 的 2–3 倍投入,且 tuning 不可压缩**(需观察数个游戏内季度才能确信
合并后行为符合预期)。

---

## 9. 设计决策(已锁定 2026-05-22)

1. **disband**:做成合并图的涌现行为(§6.1)。`DisbandExcessGarrisons` 6-Gate 方法删除。
2. **stockpile**:合并时删除(§6.5)。
3. **目标稳定性**:EWMA 平滑 + 在飞粘性偏置 两者都做(§6.3)。
4. Pass A 本体在 manual 模式作 shadow 推荐引擎保留(§6.4)。

## 10. 下一步

进入详细设计:合并图精确节点/边/费用规格 + value 重定标方案 + parallel-run 骨架 +
文件级任务分解 → 见 `mcmf-merge-design.md`。
