# 设计文档：城镇财政自治（Fiscal Autonomy）

- 日期：2026-05-21
- 目标：让受管氏族的城镇/城堡用**自己的税收**供养军事开销（vanilla 驻军工资 + 本 Mod 的征兵/升级/队伍开销），而不是由玩家个人钱包无差别承担。让 `SovereignTowns` 这个名字在「财政自给」维度上名副其实。
- 反编译来源（`_research/Vanilla/`，ilspycmd，本地不入 git）：
  `DefaultClanFinanceModel`、`DefaultSettlementTaxModel`、`DefaultPartyWageModel`、`DefaultSettlementProsperityModel`。
- 决策（已与用户确认）：
  - 自给范围 = **严格**：金库承担 vanilla 驻军工资 + ST 增量开销的全部。
  - 本文同时交付**调度器架构**与**算法设计**（可负担瀑布分配 + MCMF 集成 + 金库改道）。

---

## 1. 已核实的 vanilla 经济事实

### 1.1 收入侧

氏族每日金币变化由 `DefaultClanFinanceModel.CalculateClanGoldChange` 算出，**城镇/城堡收入直接计入氏族金币**（玩家氏族即玩家个人金币）。

| 收入项 | 公式 | 来源 |
|---|---|---|
| 城镇税 | `Prosperity × 0.35`，再经 loyalty/security/buildings/policies 加减 | `DefaultSettlementTaxModel.CalculateDailyTax` L73-74 |
| 关税（贸易税） | `Town.TradeTaxAccumulated / 5`（`RevenueSmoothenFraction = 5`） | `CalculateTownIncomeFromTariffs` L407-409、`RevenueSmoothenFraction` L868 |
| 村庄收入 | 每村 `Village.TradeTaxAccumulated / 5`；村庄**被劫掠/已洗劫时为 0** | `CalculateVillageIncome` L443-445 |
| 建筑项目收入 | `DenarByBoundVillageHeartPerDay` 等，小额 | `CalculateTownIncomeFromProjects` L432-441 |

- 城镇税与繁荣度严格线性，系数 0.35 —— 繁荣度是收入主旋钮。
- **城堡几乎无城镇税**：城堡走民兵/驻军进度而非繁荣度，`Prosperity ≈ 0`，城堡收入实际只剩所属村庄。
- 村庄收入被劫时归零 —— 战时最先蒸发。

### 1.2 开销侧

`AddExpensesFromPartiesAndGarrisons`：**对氏族每个领地的 `GarrisonParty` 按 `TotalWage` 全额扣**（`DefaultClanFinanceModel` L705-721）。玩家氏族**不打折**（预算门控 `if (num < 8000 && clan != PlayerClan)` 只对 AI 生效，L761）。

单兵每日工资 `DefaultPartyWageModel.GetCharacterWage` 的 Tier 阶梯（L23-41）：

| Tier | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7+ |
|---|---|---|---|---|---|---|---|---|
| 工资/天 | 1 | 2 | 3 | 5 | 8 | 12 | 17 | 23 |

vanilla 自动招募开销 `AutoRecruitmentExpenses / 5` 在受管城镇 ≈ 0（ST 已抑制 vanilla 自动招募）。

### 1.3 本 Mod 当前的增量开销

现状（`ModTreasury`）：所有 Mod 开销一律扣 `Hero.MainHero` 个人金币。

| 类别 | 单价 | 频率 |
|---|---|---|
| 征兵工资 `RecruiterWage` | 5/兵 | 每日，量随驻军缺口 |
| 驻军升级 `Upgrade` | `CharacterObject.GetUpgradeGoldCost`，预算上限 `max(BudgetLimit/4, 500)` | 每日首府后勤 |
| 4 类队伍 seed | `DefaultSeedGold = 2000`/队 | 每次派队，**销毁时退余款** |

seed gold 2000 是**周转金不是净开销**：买粮 + 卖战利品入 `_teamFunds` + 销毁退余款。一趟净成本 = 买粮 − 卖战利品，常接近 0。

### 1.4 调度器现状：MCMF 供需图

`CapitalLogisticsManager` 不是「算目标-填缺口」的简单循环，而是 **MCMF（最小费用最大流）求解器**：

```
EvaluateClan → SupplyDemandGraph.Run → 建图 → MinCostFlow.Solve → Decode 成指令 → ExecuteMcmfInstructions
```

图结构（`SupplyDemandGraph.RunInternal`）：

- **demand 节点 → superSink**：容量 = 该(领地,role)的兵员缺口。首府按 role 拆（`MatchPolicy.DesiredCount(rule, role, DesiredTotal)`）；非首府按 power（`DesiredPower − ProjectedPower`）。
- **source 节点**：InPlace（notable 志愿兵）、Village（村庄志愿兵）、Garrison（驻军超额，可抽走调拨）。
- **source → demand**：容量 `min(bucket, demand)`，cost = 距离 + overhead + tier 罚分。
- **superSource → unmetNode → demand**：cost = `McmfUnmetCost`（未满足缺口很贵，逼流量走真实兵源）。

关键：首府的 `DesiredTotal` 由 `ComputeDesiredTarget(rule, risk) = round(rule.TargetTotalCount × multiplier)`（L476-482）算出 —— **这就是固定 150 的所在，可负担约束从这里切入**。

---

## 2. 经济闭环验证

「合理驻军」按默认 `TargetTotalCount = 150`、`MinTier 2 / MaxTier 5`。`TroopUpgradeService` 每日把驻军往 `MaxTier` 推，**稳态平均单兵工资 ≈ 8-12**（用 ~10）。

### 案例 A —— 健康城市（繁荣度 4000，2-3 村完好，稳态）

| 项 | 金/天 |
|---|---|
| 城镇税 4000×0.35 / 关税 / 村庄 | +1400 / +600 / +600 |
| **收入合计** | **+2600** |
| 驻军工资 150×~10 / ST 征兵 / ST 升级 / 队伍食宿 | −1500 / −120 / −150 / −50 |
| **开销合计** | **−1820** |
| **净** | **+780/天 ✓ 闭环成立** |

### 案例 B —— 城堡（驻军 60，T4 为主）

| 收入（仅村庄，被劫时 0） | +200 |
| 驻军工资 60×~8 | −480 |
| **净** | **−280/天 ✗ 结构性亏损** |

### 案例 C —— 城市在战争（村庄被劫，目标×1.5）

| 收入（税+关税，村庄归零） | +1700 |
| 驻军工资 225×~10 + ST 增量 | −2550 |
| **净** | **−850/天 ✗ 战时亏损** |

### 2.1 结论

1. **闭环对「繁荣度 ≳ 2000 且村庄完好的城市」成立**，≳3000 有舒适余量。
2. **城堡结构性亏损** —— 无税基。
3. **战争是收入↓+开销↑的耦合双击**。
4. 成败取决于 **驻军规模 × 平均 Tier ÷ 繁荣度**，固定 `TargetTotalCount = 150` 是风险源。

→ **核心要求：驻军目标必须由「可负担收入」派生。** 这是 §3.3 算法的设计动机。

---

## 3. 设计

### 3.0 设计决策

| 决策 | 取值 | 理由 |
|---|---|---|
| 自给范围 | 严格（金库代付驻军工资） | 用户已确认；只有驻军工资进同一本账，「闭环」命题才成立 |
| 金库改道范围 | **仅玩家氏族** | AI 氏族走 vanilla 财政 —— 把 AI 领地收入锁进它无法他用的金库会饿死 AI 野战军（真实平衡 bug）；本特性也只面向玩家 |
| 可负担瀑布范围 | **所有受管氏族** | 瀑布只塑形驻军规模、不碰金币，对 AI 无害，还能让 AI 城镇规模更合理 |

即：**金币改道（§3.2）只对玩家氏族；驻军目标塑形（§3.3）对所有受管氏族**。

### 3.1 调度器架构（组件布局）

四个新增件，沿 CLAUDE.md 分层：

```
Layer 4  UI            : ControlPanel / WebUI — 新增「财政自治」分组 + 财务页扩展
Layer 3  Dispatchers   : CapitalLogisticsManager — 接入可负担计划 + 遣散超额步骤
                         CapitalManager — 持有 ClanTreasury（与现有 scheduler 同模式）
Layer 2  Evaluators    : AffordabilityPlanner（新增，无状态服务，类比 RiskAssessmentService）
Layer 1  Infrastructure: ClanTreasury（新增，per-clan，Saveable）
                         STClanFinanceModel : DefaultClanFinanceModel（新增 GameModel）
```

- **`ClanTreasury`**（Layer 1）：per-clan 数据。字段 `Balance`、近 7 日实际开销环形缓冲（用于 §3.5 缓冲金上限）。方法 `Credit / Debit / CanAfford / SkimAboveBufferCap`。`Balance` 是 `Saveable`。由 `CapitalManager` 持有（与 `_patrolScheduler` / `_recruiterScheduler` 同模式），`CapitalManager.Treasury` 暴露。
- **`STClanFinanceModel`**（Layer 1）：子类化 `DefaultClanFinanceModel`，`OnGameStart` 注册（不变量 #4）。负责金币改道（§3.2）。
- **`AffordabilityPlanner`**（Layer 2）：无状态静态服务。输入 `CapitalManager`，输出 `Dictionary<Settlement, int>`（每领地可负担驻军头数）。算法见 §3.3。
- **`CapitalLogisticsManager` / `SupplyDemandGraph`**（Layer 3）：`BuildSettlementStates` 调 `AffordabilityPlanner.PlanFor` 一次，用结果替换 `ComputeDesiredTarget`；`EvaluateClan` 末尾新增遣散超额步骤（§3.4）。

### 3.2 金库收支与改道（`STClanFinanceModel`）

```
override CalculateClanGoldChange(clan, desc, applyWithdrawals, details):
    en = base.CalculateClanGoldChange(clan, desc, applyWithdrawals, details)
    if clan != Clan.PlayerClan or not managed(clan): return en      # AI / 非受管走 vanilla
    treasury = CapitalRegistry.GetManager(clan)?.Treasury
    if treasury == null: return en

    # 只读重算（applyWithdrawals=false，绝不二次扣减 TradeTaxAccumulated / PartyTradeGold）
    income = Σ 受管领地: CalculateTownTax + CalculateTownIncomeFromTariffs(clan,t,false)
                         + Σ村 CalculateVillageIncome(clan,v,false) + CalculateTownIncomeFromProjects
    wage   = Σ 受管领地: GarrisonParty?.TotalWage ?? 0

    if applyWithdrawals:                       # vanilla 财政每日 tick 唯一一次走此分支
        treasury.Credit(income)
        treasury.Debit(wage)                   # ST 增量开销当天即时 Debit，不在此重复
        overflow = treasury.SkimAboveBufferCap()
        shortfall = treasury.SettleNegative()  # Balance<0 → 按 §3.5 兜底，返回需玩家补的额
    else:
        (overflow, shortfall) = treasury.Preview(income - wage)

    # 氏族金币应得 = vanilla − income(改入金库) + wage(金库代付) + overflow − shortfall
    en.Add(-income + wage + overflow - shortfall, "主权城镇金库结算")
    return en
```

正确性要点（advisor 已确认）：

- `base.CalculateClanGoldChange` 在 `applyWithdrawals=true` 时已扣减过 `TradeTaxAccumulated` / 动过 `PartyTradeGold`。本类的重算用 `applyWithdrawals=false`，只**读毛值**、不二次副作用。
- `CalculateClanGoldChange` 每帧被财政 UI 预览多次调用（`applyWithdrawals=false`）—— **只有 `applyWithdrawals=true` 分支动金库**。
- 用到的 vanilla 方法全是 public 且无副作用（`SettlementTaxModel.CalculateTownTax` 纯函数；`CalculateTownIncomeFromTariffs/VillageIncome(...,false)` 不扣减；`GarrisonParty.TotalWage` 纯属性）。
- 子类化包 try/catch，异常 → 退回 `base` 结果（fail-safe）。

`ModTreasury` 改造：扣款入口从「扣 `Hero.MainHero`」改为「扣 `clan` 对应金库」。`Charge` 签名新增 `Clan`（或 `Settlement`）参数以解析金库；金库余额不足 → 见 §3.5。

### 3.3 可负担瀑布算法（`AffordabilityPlanner`）

每个受管氏族每日一次，把「氏族可持续收入」转成「每领地可负担驻军头数」。

```
PlanFor(manager) -> Dictionary<Settlement,int>:
    towns = manager.OwnerClan 的全部受管 town+castle
    # 1. 可持续收入（保守口径：税+关税，排除易被劫的村庄收入）
    clanSustainableIncome = Σ towns: CalculateTownTax(t) + CalculateTownIncomeFromTariffs(clan,t,false)
    clanGarrisonBudget    = GarrisonWageBudgetRatio × clanSustainableIncome        # gold/天
    # 2. 单兵工资估算 —— 保守取满级（假设升级最终到位）
    wage = Campaign.Models.PartyWageModel.GetCharacterWage(stubTroopOfTier(MaxTier))  # RBM 安全：走 vanilla 模型
    # 3. 战时：有缓冲金则按全额配置供养（不裁人头，见 §3.5）
    atWar = clan 处于战争 OR 任一受管领地 risk≥High
    effectiveBudget = (atWar && treasury.Balance > 0)
                      ? max(clanGarrisonBudget, clanConfigWageBill)
                      : clanGarrisonBudget
    # 4. 优先级瀑布：高优先领地先吃满预算，低优先吃剩余
    ordered = towns 按优先级降序排（围攻 > High > 首府 > Medium > Low > Safe）
    budget = effectiveBudget;  plan = {}
    for s in ordered:
        mult      = risk(s)≥High ? WartimeMultiplier : PeacetimeMultiplier
        wantHeads = configTargetHeads(s) × mult         # 首府=TargetTotalCount；分支=TargetPower→头数
        grant     = clamp(wantHeads × wage, 0, max(0,budget))
        heads     = clamp(grant / wage, MinGarrisonFloor, wantHeads)
        plan[s]   = heads
        budget   -= heads × wage                        # floor 可能让 budget 转负 = 已接受的补贴
    return plan
```

设计要点：

- **优先级瀑布而非等比缩放**：等比缩放会把被围攻的城也一起缩小 —— 错。瀑布让围攻/高危领地先吃满预算，低危领地吃剩余、降到 `MinGarrisonFloor`。被围攻城兵力得到保护。
- **`MinGarrisonFloor`**（新配置，默认 40）：再穷的领地保留象征性驻军，其赤字由缓冲金/玩家补贴吸收（§3.5）。城堡 `clanSustainableIncome` 贡献 ≈ 0，自然只拿到 floor —— 缺口由氏族池子（富城）补贴，这正是「氏族级金库」而非「按城分账」的理由。
- **`wage` 取满级、固定**：故意保守。`TroopUpgradeService` 会把驻军推向 `MaxTier`，按当前中位 Tier 估算会在升级落地后超支。固定满级 → 目标从第 1 天就稳定、不随升级震荡；早期实际工资低于估算 = 留了余量，不是 bug。
- **分支 power↔头数转换**：已核实 `GarrisonPowerEvaluator.ComputeRosterPower` 是干净的 `Σ 单兵power × 数量` 线性和（自检基准 T1≈0.66/T3≈1.30/T6≈2.56）。转换 `头数 ≈ power / 参考单兵power`（参考 = MinTier..MaxTier 中位）成立，作为塑形启发式精度足够。
- **无状态**：`AffordabilityPlanner` 不持久化，每次从 vanilla 模型现算收入；只读 `treasury.Balance`（容忍 1 tick 陈旧，见 §4 排序说明）。

### 3.4 MCMF 集成

`plan` 在 `SupplyDemandGraph.BuildSettlementStates` 里替换现有目标：

- 首府：`ComputeDesiredTarget` 的返回值改为 `plan[capital]`（不再是 `round(TargetTotalCount × mult)`）。
- 分支：`DesiredPower` 改为 `min(branch.TargetPower, headsToPower(plan[branch]))`。
- 其余 MCMF 图结构、cost、求解**不动** —— 可负担只塑形 demand 节点容量，不改流。

**不引入预算节点**（已评估并否决）：征兵开销 5/兵 × ~50/天 相对 ~2000 收入是噪声，不值得把第二种商品（金币）塞进流图。金库见底的边界情形由 `ModTreasury.Charge` 失败被动处理（指令优雅跳过）即可。

**遣散超额（关键杠杆）**：可负担目标只控制「招募增长方向」，**不会让现有超额驻军的工资实时下降** —— 一座继承来的 200 人驻军（只负担得起 100）在被攻击减员前会一直按 200 人计工资。真实降工资必须主动遣散。

- `CapitalLogisticsManager.EvaluateClan` 末尾新增步骤：对每个受管领地，若 `当前驻军 > plan[s] × DisbandExcessThreshold`（新配置，默认 1.2），遣散最低 Tier 的超额 `当前 − plan[s]` 人。
- **仅和平期**（risk < High）触发：战时保人头（§3.5）。
- MCMF 的 Garrison-surplus 兵源已会把超额优先**调拨**到缺兵领地（productive，非浪费）；遣散只处理「全氏族都超预算、无处可调」的残余。
- `DisbandUnaffordableExcess` 开关（默认开）。关掉 → 超额驻军靠战损缓慢消化，工资短期不降（诚实记录此行为）。

### 3.5 战争冲击处理（缓冲金 + 降级杠杆）

案例 C：战时必亏。naive 的「驻军 ≤ 当日可负担」会在最需要兵时裁军 —— 错。

1. **和平期缓冲金**：金库净溢出不立即回流氏族金币，先积累。**缓冲金上限 = `TreasuryBufferDays × (近 7 日实际开销 / 7)`**（新配置 `TreasuryBufferDays` 默认 30）。用**近 7 日实际开销**做分母 —— 避免「上限依赖日开销、日开销依赖可负担目标、目标又依赖含缓冲的预算」的循环依赖；7 日滚动也自然适应领地得失。`SkimAboveBufferCap` 把超上限部分回流氏族金币。
2. **战时全额供养**：`atWar && Balance > 0` → §3.3 的 `effectiveBudget` 取 `clanConfigWageBill`，配置全额驻军由缓冲金垫付。
3. **降级优先于裁员**：缓冲金耗尽且仍在战争 → 不砍人头，而是暂停 `TroopUpgradeService`（停止把工资往上推 → 自然减员 + 低 Tier 补员让平均工资缓降）+ 暂停 ST 外派征兵。`WartimeMultiplier` 的人头目标仍生效。
4. **玩家兜底（开关 `PlayerClanSubsidyWhenTreasuryEmpty`，默认开）**：金库见底时由玩家个人金币补足。
   - **驻军工资非自主开销**：无论开关如何，金库赤字部分的驻军工资始终从玩家金币兜底（vanilla 机制上必须支付，见 §3.2 的 `shortfall`）。
   - 开关只管**自主开销**（征兵/升级）：关掉 → 金库空时这些动作硬暂停（`Charge` 失败），不动玩家金币。
   - 金库 `Balance` 钳制 ≥ 0：赤字即时由兜底/暂停消化，不留负余额。

### 3.6 控制面板双端（Layer 4）

按 memory `feedback_control_panel_dual_surface`，新配置同时上 Gauntlet 面板与 WebUI。新增「财政自治」分组：

| 字段 | 默认 | 含义 |
|---|---|---|
| `GarrisonWageBudgetRatio` | 0.55 | 驻军工资最多占可持续收入的比例 |
| `MinGarrisonFloor` | 40 | 可负担再低也保留的驻军人数 |
| `TreasuryBufferDays` | 30 | 金库缓冲金上限（按近 7 日均开销计） |
| `DisbandExcessThreshold` | 1.2 | 当前驻军超可负担目标此倍数 → 和平期遣散超额 |
| `DisbandUnaffordableExcess` | 开 | 是否启用遣散超额杠杆 |
| `PlayerClanSubsidyWhenTreasuryEmpty` | 开 | 金库见底时自主开销是否由玩家金币兜底 |

财务页（已有 `FinanceTabVM` / `FinanceTableVM`）扩展：金库余额、缓冲金上限、各受管领地单城 P&L、首府对城堡的补贴额、可负担目标 vs 当前驻军。

校验：`ConfigurationManager` 新增 `ValidateFiscalAutonomy`（与 `ValidateThresholds` 并列），上下界与控制面板 spec 的 min/max 对齐。

---

## 4. 数据流与每日时序

```
vanilla 财政每日 tick（applyWithdrawals=true，每日一次）
  └─ STClanFinanceModel.CalculateClanGoldChange（玩家受管氏族）
       ├─ 受管领地 税+关税+村庄 → ClanTreasury.Credit
       ├─ 受管领地 GarrisonParty.TotalWage → ClanTreasury.Debit
       ├─ SkimAboveBufferCap → 超缓冲金上限部分回流氏族金币
       └─ SettleNegative → 赤字由 §3.5 兜底

ST 每日 tick → CapitalLogisticsManager.EvaluateAll
  └─ 每受管氏族：
       ├─ SupplyDemandGraph.BuildSettlementStates
       │    └─ AffordabilityPlanner.PlanFor → 每领地可负担头数（替换固定 150）
       ├─ MinCostFlow.Solve → Decode → 执行招募/调拨指令
       │    └─ ST 增量开销 → ModTreasury.Charge(clan,...) → ClanTreasury.Debit
       └─ 遣散超额步骤（和平期，当前驻军 > 可负担 × 阈值）

控制面板改值 → ConfigurationManager.ValidateFiscalAutonomy → 落盘 → 下个 tick 生效
```

**时序无硬依赖**：vanilla 财政 tick 与 ST 每日 tick 的先后不保证。`AffordabilityPlanner` 每次现算收入（不依赖金库当天是否已 Credit）；只有 `treasury.Balance`（供战时判断）可能陈旧 1 tick —— 战时缓冲判断容忍此误差。

---

## 5. 存档影响

- `ClanTreasury.Balance` + 近 7 日开销环形缓冲需持久化 → 新增 `Saveable` 字段，分配新 `LocalSaveId`（不变量 #3），`SaveBaseId` 不变。
- 新配置字段 → `ConfigVersion` bump，旧 `global.json` 回退默认（CLAUDE.md 允许，无迁移代码）。
- `STClanFinanceModel` 是 GameModel，运行期注册，不持久化。

## 6. 风险与范围外

- **风险：`CalculateClanGoldChange` 预览调用**。子类化必须只在 `applyWithdrawals=true` 时动金库（§3.2 已处理）。
- **风险：负金库与 vanilla `PartyTradeGold`/morale 交互**。本设计把 `Balance` 钳制 ≥0、赤字即时兜底，规避负余额；但 vanilla 在 `base` 里已对 `GarrisonParty.PartyTradeGold` 做过扣减/补足 —— 实现阶段需确认改道后驻军不会误触发 `ApplyMoraleEffect` 欠饷惩罚（`DefaultClanFinanceModel.ApplyMoraleEffect` L892）。
- **风险：与其它子类化 `ClanFinanceModel` 的 mod 冲突**。IG 正是子类化此模型者，已在 `<IncompatibleModules>` 排除。
- 范围外：AI 氏族金库（§3.0 决策，AI 走 vanilla 财政）；工坊/商队收入（候选功能 D）；税率/政策玩家可调（候选功能 E）。

## 7. 实现顺序（待用户批准设计后另出实现计划）

1. `ClanTreasury`（Layer 1）+ `Saveable` 接入 + `CapitalManager.Treasury`。
2. `STClanFinanceModel` 子类化 + `OnGameStart` 注册。
3. `ModTreasury.Charge` 改道金库（签名加 `Clan`/`Settlement` 参数）。**5 处调用点**需同步：`RecruitmentDispatcher.TryDispatchRecruiter`（seed）、`CapitalInPlaceRecruiter`、`BranchInPlaceRecruiter`（征兵工资）、`TroopUpgradeService.TryUpgradeGarrison`（升级）、`StPartyComponent.TrySeedAndBuyInitialFood`（4 类队伍 seed）。
4. `AffordabilityPlanner`（Layer 2）+ 接入 `SupplyDemandGraph.BuildSettlementStates`。
5. `CapitalLogisticsManager` 遣散超额步骤。
6. 战时缓冲 / 降级杠杆。
7. 控制面板双端「财政自治」分组 + 财务页扩展 + `ValidateFiscalAutonomy`。

## 8. 建议的运行期验证

§2 的系数（`Prosperity × 0.35`）由反编译确证，但 perk/建筑/issue 的加减层难从源码估准。建议在有受管城镇的存档里用 WebConfig 端点探一次真实 `CalculateTownTax` 返回值，校准案例 A 余量。无单元测试（CLAUDE.md）—— 验证靠启动游戏 + 看 `ModLogs/SovereignTowns/` 日志：

1. 金库每日 Credit/Debit、缓冲金随近 7 日开销变化、Skim 溢出回流。
2. 可负担目标随繁荣度变化；瀑布优先级正确（围攻城不被缩小）。
3. 和平期超额驻军被遣散；战时不遣散、缓冲耗尽后暂停升级。
4. 城堡只拿 `MinGarrisonFloor`，缺口由富城补贴。
5. AI 受管氏族财政走 vanilla（金库改道不生效），但驻军目标仍被可负担塑形。
6. Gauntlet 面板与 WebUI 都能显示/保存「财政自治」6 个旋钮 + 财务页扩展。
