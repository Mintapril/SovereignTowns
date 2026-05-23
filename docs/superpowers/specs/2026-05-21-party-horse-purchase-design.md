# 设计文档：自负盈亏队伍买马保移速

- 日期：2026-05-21
- 目标：ST 自负盈亏队伍(征兵 / 调拨 / 出击 / 巡逻)创建出发时用队伍资金买一批松散坐骑,使大地图移速不被无马步兵拖慢;解散返航时连同其余物资一起卖掉。
- 决策前提(已与用户确认)：不加特性开关(YAGNI,低风险 —— 用队伍自己的钱)。

## 1. 背景

四类 ST 队伍都有 `_teamFunds`(队伍资金),由 `StPartyComponent.TrySeedAndBuyInitialFood` 统一在出发地播种 + 买 3 天粮。`PartyEconomyHelper` 已有真实市场买卖范式(`SellItemsAction.Apply` + `Town.GetItemPrice`,村庄走 `Village.Bound.Town` 定价)。

## 2. 核心数据(已核实 vanilla `DefaultPartySpeedCalculatingModel`)

- `ItemRoster` 中的松散坐骑可让"无马步兵"骑乘:每名无马步兵配 1 匹马时移速加成拉满。
- 坐骑数超过无马步兵数 → 多出的进入"畜群";畜群规模超过队伍总人数后**反而减速**。
- 因此**最优买马数 = 出发时的无马步兵数** `MobileParty.Party.NumberOfMenWithoutHorse`(已扣除天生骑兵)。少买仅加成不满、无惩罚;多买有害。
- 坐骑判定:`ItemObject.HasHorseComponent && HorseComponent.IsMount`(= 可骑乘且非驮兽)。`ItemRoster.NumberOfMounts` 给出当前松散坐骑数。

## 3. 实现

### 3.1 买(`PartyEconomyHelper.BuyHorsesFromSettlement`,新增)

仿 `BuyFoodFromSettlement`:取 `(pricingTown, counterparty, settlementInv)` 三元组(town / village 两路);`wantUnits = NumberOfMenWithoutHorse − ItemRoster.NumberOfMounts`(钳制 ≥ 0);循环买最便宜的坐骑,受 `teamFunds` 限制,买不够也无妨(部分加成、无惩罚);`ref teamFunds` 扣款;返回花费。

`StPartyComponent` 加实例包装 `BuyHorsesAtSettlement(self, settlement)` → 上述方法(`ref _teamFunds`),紧邻现有 `BuyFoodAtSettlement`。

### 3.2 买的触发点

`StPartyComponent.TrySeedAndBuyInitialFood` —— 食物购买成功后(`return true` 之前)调一次 `component.BuyHorsesAtSettlement(party, origin)`。best-effort:买不到马**不**取消派遣(与食物缺货不同)。四类队伍共用此例程,故全部覆盖。

### 3.3 卖

- 征兵队 / 巡逻队 / 调拨队(返源路径):已走基类 `DefaultMergeAndDisband` → `SellAllItemsAtSettlement`,全清算(含马),无需改动。
- 调拨队(抵达 dest 路径)`DeliverAndDisband`、出击队 `OnArrivedHome`:目前不清算 —— 各在解散前补一次 `SellAllItemsAtSettlement`。
- `StPartyComponent.SellAllItemsAtSettlement` 由 `private` 改 `protected`,供上述两个子类调用。

## 4. 错误处理

- 买 / 卖全程 try-catch,失败仅 Logger.Warn,绝不影响派遣 / 解散主流程。
- `NumberOfMenWithoutHorse` / `NumberOfMounts` 访问包 try-catch,异常按 0 处理。

## 5. 验证(游戏内,无单测)

1. 受管首府派出队伍 → 日志出现 `BuyHorsesFromSettlement ... bought N` ,N ≈ 出发时无马步兵数。
2. 队伍大地图移速明显高于不买马的情形。
3. 队伍解散 → 日志 `SellAllItems... sold N horse`,资金回流。
4. **关键待验证**:松散坐骑留在 `ItemRoster`、不被 vanilla 自动装备到士兵身上(ST 是自定义 PartyComponent,预期不会;须确认,否则"回城卖马"无马可卖)。

## 6. 已知取舍

- 买马数按**出发时**人数定。征兵队途中扩编,新招的兵不享受骑乘加成 —— 符合"出门前买一定数量"的字面要求,不做途中补购。
- 改动文件:`PartyEconomyHelper.cs`、`StPartyComponent.cs`、`StTransferPartyComponent.cs`、`StSallyPartyComponent.cs`。
