# 主权城镇 Mod 行为说明

本文按当前源码行为编写，面向普通 Bannerlord 玩家，用“玩家会看到什么”和“代码实际怎么判断”来解释 Mod。它不是宣传文案，而是需求对齐用的行为翻译。

实现主要分布：

- 入口与事件：`src/SovereignTownsSubModule.cs`，`src/Campaign/SovereignTownsCampaignBehavior.cs`
- 首府：`src/Capital/CapitalManager.cs`，`src/Capital/CapitalRegistry.cs`
- 每日驻军调度：`src/Managers/CapitalLogisticsManager.cs`
- 征兵：`src/Recruitment/*`，`src/Parties/StRecruiterPartyComponent.cs`
- 巡逻：`src/Patrol/*`，`src/Parties/StPatrolPartyComponent.cs`
- 调拨：`src/Transfer/*`，`src/Parties/StTransferPartyComponent.cs`
- 主动出击：`src/SallyForth/SallyDispatcher.cs`，`src/Parties/StSallyPartyComponent.cs`
- 战利品与俘虏：`src/Battle/*`，`src/Recruitment/PrisonerRecruitmentManager.cs`
- 网页控制面板：`src/WebConfig/*`，`SovereignTowns/WebUI/index.html`

## 0. 基本概念

### 首府

“首府”只能是城市，不能是城堡。代码里的硬条件是 `settlement.IsTown == true`。城堡可以被纳入驻军评估、兵力调拨和 vanilla 自动招募抑制，但不能作为首府，也不能作为首府派出征兵队、巡逻队的中心。

如果玩家氏族没有城市，Mod 对这个氏族处于“无首府”状态。无首府时每日后勤、首府招募、外派征兵、巡逻派遣等首府制行为都会自动停住。

### 实际驻军

本文说的“驻军”默认指 `Town.GarrisonParty.MemberRoster.TotalManCount`，不包括民兵。多数阈值、比例、抽兵、缺口计算都用这份实际驻军。

### Tick 节奏

Mod 主要依赖这些游戏事件：

- 游戏载入战役后初始化配置、首府、队伍生命周期、Web 面板。
- 每日 tick：做首府级后勤评估，决定招募、升级、调拨。
- 每个定居点小时 tick：尝试创建巡逻队、主动出击队。
- 每个定居点每日 tick：作为小时 tick 漏触发时的兜底，统一兜底四条路径：XP 注入、俘虏转化、巡逻派遣、出击派遣。其中 XP 注入和俘虏转化只在首府上跑；巡逻派遣和出击派遣的真实执行条件仍按各自创建前提判定。
- 每个队伍小时 tick：推进 Mod 自建队伍的状态机。
- 战斗结束：处理巡逻/出击战利品，并让相关队伍判断是否回家。
- 城镇易主、宣战、玩家换氏族：触发迁移、撤退或重新建首府管理器。

## 1. 启动与兼容

游戏启动时，Mod 会先检查互斥模块：

- `ImprovedGarrisons`
- `GarrisonDoSomething`

如果检测到这些模块启用，`SovereignTownsSubModule` 会进入退化模式：不注册 CampaignBehavior，也就是本 Mod 不工作，并在主菜单显示红色警告。

未发现互斥模块时，Mod 注册三个 GameModel：

- 自建队伍容量模型：征兵、调拨、巡逻、出击队使用专门容量上限。
- 自建队伍速度模型：所有 ST 队伍获得 20% 移速加成。
- 自建队伍工资模型：所有 ST 队伍氏族工资为 0，不走 vanilla 家族军饷。

随后启动本地 Web 面板服务器：

- 只绑定 `127.0.0.1`。
- 默认端口 `41763`，占用时最多向后尝试 50 个端口。
- API 和静态页面都需要 token。
- token 不写入游戏聊天和日志，只通过打开控制面板的 URL 或文档目录下的 `auth.txt` 使用。

配置文件、兵种清单和诊断文件写入：

`Documents\Mount and Blade II Bannerlord\Configs\SovereignTowns\`

主配置文件是 `global.json`。

## 2. 首府系统

### 自动选择首府

每个受管氏族有一个 `CapitalManager`。玩家氏族永远受管；AI 氏族只有在“AI 城镇纳入 ST 规则”开启时才受管。

初始化时选择首府的顺序：

1. 如果存档里有该氏族之前保存的首府 stringId，且这个 settlement 仍存在、仍是城市、仍属于该氏族，则沿用。
2. 否则扫描该氏族拥有的所有城市。
3. 如果拥有至少一个城市，随机选一个城市作为首府。
4. 如果没有城市，首府为空。

注意：这里扫描的是城市，不含城堡。

### 手动设置首府

玩家在自家城市菜单里可以看到“主权城镇：设为首府”。城堡菜单不会显示这个选项，因为首府只能是城市。

手动设首府必须同时满足：

1. 当前 settlement 存在。
2. 当前 settlement 是城市。
3. 当前 settlement 属于玩家氏族。
4. 玩家氏族的 CapitalManager 存在。
5. 当前城市不是已经生效的首府。

切换成功后，所有该氏族正在外面的 ST 队伍会被迁移/解散：队伍里的非英雄兵员尝试并入新首府驻军，然后队伍解散。

### 首府失守

如果当前首府失守：

1. 扫描该氏族剩余自有城市。
2. 如果还有城市，随机选一个为新首府。
3. 如果没有城市，首府变为空。
4. 该氏族所有在途 ST 队伍会尝试把非英雄兵员并入新首府；没有新首府时只能解散，兵员没有可用驻军容器。

如果失守的是非首府：

1. 只清理以该失守 settlement 为 home 以及目的地是该 settlement 的 ST 队伍。
2. 调拨队、征兵队、出击队：尝试返回当前首府，基类在到家时把非英雄兵员并入首府驻军。
3. 巡逻队：不返家解散，而是从该氏族剩余合法 settlement 中重新选择下一个巡逻站。
4. 其他城市派出的队伍不受影响。

### 获得城市

如果一个氏族原本没有首府，又获得了一个城市，这个城市会自动成为首府。

如果获得的是城堡，不会成为首府。

## 3. 配置和 Web 面板

### 全局开关

面板里的功能开关及默认值：

| 开关 | 默认 | 实际影响 |
| --- | --- | --- |
| 自动招募 | 开 | 首府原地招募、外派征兵、俘虏转化、驻军 XP/升级路径才会运行 |
| 自动巡逻 | 关 | 是否从首府派出 ST 巡逻队；关闭后已有巡逻队不会继续推进巡逻状态机 |
| 兵力调拨 | 开 | 是否在自有城市/城堡之间创建调拨队 |
| 主动出击 | 关 | 是否在有敌军靠近时创建出击队 |
| 抑制 vanilla 自动招募 | 开 | 自动招募开启时，关闭受管城/堡的 vanilla 驻军自动招募和自动升级 |
| AI 城镇纳入 ST 规则 | 关 | 给有城市的 AI 氏族也建立首府制 |
| 金币不足时暂停支出 | 开 | 玩家金币不足时拒绝创建队伍/升级/招兵扣费 |
| 每日活动汇总弹窗 | 开 | 每日显示招募、调拨、巡逻、出击、俘虏招募数量 |

### 全局驻军规则

默认全局规则：

| 字段 | 默认 | 行为 |
| --- | --- | --- |
| 目标驻军总数 | 150 | 后勤调度的基础目标人数 |
| 通用匹配 | 开 | 用骑兵/骑射/步兵/远程比例和 Tier 范围筛兵 |
| 精确兵员模板 | 空 | 通用匹配关闭时才生效，按兵种 stringId 比例筛兵 |
| 骑兵比例 | 0.20 | 通用匹配下的骑兵目标 |
| 骑射比例 | 0.05 | 通用匹配下的骑射目标 |
| 步兵比例 | 0.50 | 通用匹配下的步兵目标 |
| 远程比例 | 0.25 | 通用匹配下的远程目标 |
| 最低 Tier | 2 | 低于此 Tier 的兵不作为招募目标 |
| 最高 Tier | 5 | 高于此 Tier 的兵不作为招募目标或 XP 注入目标 |
| 允许文化 | 空 | 空表示所有文化；非空时兵种文化必须在列表内 |
| 优先兵种 | 空 | 命中后候选评分加 100 |
| 禁用兵种 | 空 | 命中后直接排除 |
| 允许贵族兵 | 开 | 关闭时 noble line 会被排除 |
| 最少防守比例 | 0.20 | 只约束主动出击抽兵，不能让城内低于这个比例 |
| 高威胁目标乘数 | 1.5 | 风险达到 High 时目标驻军 = 目标总数 × 该值 |
| 常态目标乘数 | 1.0 | 风险低于 High 时目标驻军 = 目标总数 × 该值 |
| 招募预算基准 | 5000 | 外派征兵队每村招募预算；自动升级预算也由它派生 |
| 食物安全阈值 | -2.0 | `Town.FoodChange` 低于它时暂停招募 |

通用匹配的兵种分类：

- 骑射：`DefaultFormationClass.HorseArcher`
- 骑兵：骑兵、轻骑、重骑
- 远程：默认远程
- 步兵：步兵、重步兵
- 未设置、将军、散兵等会按是否骑乘、是否远程再兜底分类
- 英雄、非正规兵、禁用兵种不匹配

比例校验：

- 四类兵种比例总和必须在 `0.9` 到 `1.1` 之间。
- 精确模板比例总和也必须在 `0.9` 到 `1.1` 之间。
- Tier 必须在 1 到 6 之间，且最高 Tier 不能低于最低 Tier。

### 首府专属开关

以下两个开关虽然字段位置在驻军规则里，但只读首府的规则、对城堡上的同名字段无效。逻辑上等同于氏族级开关：

| 字段 | 默认 | 行为 |
| --- | --- | --- |
| 允许俘虏转化 | 开 | 每日首府地牢俘虏转驻军是否允许 |
| 允许自动升级 | 开 | 每日后勤评估时是否尝试升级首府驻军 |

### 单领地覆盖

按领地覆盖只覆盖这些字段：

- 目标驻军总数
- 最少防守比例
- 招募预算基准
- 高威胁目标乘数
- 常态目标乘数
- 食物安全阈值

兵种模板、Tier、文化过滤、优先/禁用兵种等隐藏字段不会被单领地覆盖冻结，仍来自全局规则或 AI 文化预设。

### 数量预算与资源调度

Web UI 当前把原本的“数量预算”和“资源调度”合并在同一个设置页内，并做二级分类：

- 目标预算：目标人数、预算、威胁乘数、食物阈值。
- 招募：村庄冷却、征兵队护卫、候选搜索、回访节奏、初始金币。
- 巡逻调拨：巡逻队创建、巡逻站点、Sally 支援、调拨评分与距离。
- 主动出击：敌军搜索、规模、冷却、持续可见。
- 生命周期升级：返航、俘虏上限、卡死保护、空闲解散、自动升级。

保存配置走 `PUT /api/config`。HTTP 线程不会直接碰游戏对象，而是把“配置已变化”排队到下一次游戏主线程 tick 处理。

配置变化后：

- AI 接管开关会在主线程同步创建或移除 AI CapitalManager。
- vanilla 自动招募抑制会重新应用到所有城/堡。
- 正在外面的征兵队会切回 Dispatching 阶段，下一次按新规则重新选目标。
- 其他队伍不会因为普通配置变化被整体重建。

## 4. 风险评估

所有定居点风险由 `RiskAssessmentService.Assess` 计算。

判断顺序：

1. settlement 为 null：Safe，分数 0。
2. settlement 非激活：Safe，分数 0。
3. 正在被围攻：Critical，分数 10。
4. 其他情况计算：
   `ratio = NearbyLandThreatIntensity / (NearbyLandAllyIntensity + 1)`
5. `ratio >= 3.0`：High。
6. `ratio >= 1.5`：Medium。
7. `ratio >= 0.5`：Low。
8. 低于 0.5：Safe。

注意：非围攻状态最高只会到 High；Critical 目前来自围城。

风险用途：

- High 或 Critical 使用高威胁目标乘数。
- 调拨源如果风险达到 High，会被排除，不从它抽兵。
- 征兵队在路上发现目标村庄风险达到 High，会放弃这个目标并重选。
- 调拨队目的地风险达到 Critical，会改返源城。

## 5. 每日首府后勤总流程

每天 `CapitalLogisticsManager.EvaluateAll` 会对每个受管氏族做一次评估。

一个氏族会被跳过的情况：

1. 没有有效首府。
2. 首府不是城市（防御式检查；§0 已规定首府必须 `IsTown==true`，正常路径下不应命中）。
3. 首府不再属于该氏族。
4. 没有任何自有城市/城堡节点。

有效时，流程固定为：

1. 收集该氏族所有自有城市和城堡作为后勤节点。
2. 给每个节点计算风险、当前驻军、目标驻军、是否首府。
3. 把在途征兵队和调拨队计入 inbound/outbound。
4. 记录一份日志快照。
5. 尝试升级首府驻军。
6. 协调招募：先首府原地招募，再决定是否派征兵队。
7. 因为招募可能改变首府驻军，所以重新构建节点。
8. 协调兵力调拨。

### 目标、缺口和富余

每个后勤节点的目标人数：

`DesiredTarget = round(TargetTotalCount × multiplier)`

其中：

- 风险 High/Critical 用 `WartimeMultiplier`。
- 其他风险用 `PeacetimeMultiplier`。
- 最低结果为 1。

在途兵员计算：

- 调拨队从源城出发：源城记录 outbound，目的地记录 inbound。
- 调拨队如果正在返回源城：源城记录 inbound。
- 征兵队：home 记录 inbound，队伍现有人数全部算作将来会回到 home。

预计驻军：

`ProjectedMen = CurrentMen + Inbound`

这里不扣 outbound。也就是说，缺口判断只看现有兵加将到兵；调拨容量另算。

缺口：

`Demand = max(0, DesiredTarget - ProjectedMen)`

危急阈值：

`CriticalThreshold = round(DesiredTarget × TransferCriticalProjectedRatio)`

默认 `TransferCriticalProjectedRatio = 0.24`。如果比例大于 0，阈值至少为 1。

危急缺口：

`CriticalDemand = max(0, CriticalThreshold - ProjectedMen)`

可调出容量：

`TransferCapacity = max(0, CurrentMen - DesiredTarget)`

调拨优先级：

`Priority = (CriticalDemand > 0 ? 1000 : 0) + RiskLevel × 100 + Demand + CriticalDemand × 2`

## 6. 首府原地招募

首府原地招募不创建队伍，直接从首府城市 notable 的志愿兵槽招兵进首府驻军。

触发条件：

1. 该氏族有有效首府。
2. 当前每日后勤评估发现总缺口或危急缺口大于 0。
3. 自动招募开启。
4. 首府是城市。
5. 首府属于受管氏族。
6. 首府没有被围攻。
7. 首府有或能重建 GarrisonParty。
8. 当前驻军未达到容量上限和调度目标。
9. `Town.FoodChange >= FoodSafetyThreshold`。
10. 首府所有者有 Leader。
11. vanilla VolunteerModel 存在。

首府本次想招到的目标库存：

1. `totalDemand = 所有节点 Demand 总和`
2. `branchDemand = 非首府节点 Demand 总和`
3. `stockpileForBranches = min(branchDemand, TargetTotalCount)`
4. `desiredCapitalStock = 首府目标 + stockpileForBranches`

也就是“首府自身目标 + 为其他领地准备的一部分库存”。`stockpileForBranches` 通过 `min(branchDemand, TargetTotalCount)` 钳制，保证为其他领地准备的库存不会无限堆叠。

招募时逐 notable 扫描：

1. notable 必须存在。
2. notable 必须 `CanHaveRecruits`。
3. notable 的 `VolunteerTypes` 不能空。
4. 用 vanilla `MaximumIndexHeroCanRecruitFromHero` 得到可招槽位上限。
5. 原地招募只扫描 vanilla 允许的槽位，不做 2 倍扩展。
6. 候选兵必须符合当前驻军规则。
7. 该兵种所属 role 没有达到 role 配额。
8. 玩家氏族每招 1 人扣 5 第纳尔；AI 免费。
9. 金币不足且“金币不足时暂停支出”开启时，立刻停止本次原地招募。
10. 成功加入驻军后，把 notable 的该志愿兵槽置空。

Role 配额按当前招募上限计算：

- 骑兵目标 = `round(CavalryRatio × cap)`
- 骑射目标 = `round(HorseArcherRatio × cap)`
- 步兵目标 = `round(InfantryRatio × cap)`
- 远程目标 = `round(RangedRatio × cap)`

如果某 role 已达到配额，即使槽里有符合 Tier/文化的兵，也不会再招该 role。

## 7. 外派征兵队

### 何时决定派征兵队

首府原地招募完成后，后勤系统计算剩余缺口：

- `remainingDemand = max(0, totalDemand - 原地招募人数)`
- `remainingCriticalDemand = max(0, criticalDemand - 原地招募人数)`
- `recruitmentDemandThreshold = max(1, round(首府当前驻军 × RecruitmentMinDemandRatio))`

默认 `RecruitmentMinDemandRatio = 0.07`。

满足任一条件就请求派征兵队：

- `remainingDemand >= recruitmentDemandThreshold`
- 或 `remainingCriticalDemand > 0`

请求派出人数参数取：

`max(remainingDemand, remainingCriticalDemand)`

这个参数只用于说明需求规模，不直接决定征兵队人数。征兵队实际护卫来自首府驻军比例，实际招募人数来自村庄志愿兵。

### 派出征兵队的前提条件

`RecruitmentDispatcher.TryDispatchRecruiter` 会逐项检查：

1. homeTown 存在，且有 Settlement。
2. requestedMagnitude 大于 0。
3. 自动招募开启。
4. 该城市属于受管氏族。
5. 该城市必须是该氏族当前首府。
6. 当前规则可读取。
7. 首府不在围城中。
8. `Town.FoodChange >= FoodSafetyThreshold`。
9. 未达到该首府的征兵队上限。
10. 找得到候选村庄。
11. 首府驻军数不低于 `RecruiterMinHomeGarrison`。
12. 如果玩家氏族，需要有足够金币支付征兵队初始金币。
13. 创建队伍成功。

征兵队上限：

- 使用首府兵营等级 + 1。
- 城市读 `settlement_garrison` 建筑等级。
- 找不到建筑时上限为 1。
- 0 级为 1 支，3 级为 4 支。

没有护卫禁止派出征兵队。

### 征兵队护卫

护卫人数：

`round(首府驻军 × RecruiterEscortRatio)`

默认 `RecruiterEscortRatio = 0.10`。

抽兵规则：

- 从首府驻军抽。
- 低 Tier 优先。
- 不抽英雄。
- 如果算出的护卫为 0，不允许派出。
- 如果抽不到兵，不允许派出。

所有 4 类 ST 队伍创建时统一扣 `StPartyComponent.DefaultSeedGold = 2000` 第纳尔作为初始 `_teamFunds`：玩家氏族走 `ModTreasury.Charge`（不足则拒绝派遣），AI 氏族从 home owner `hero.Gold` 按可用余额扣（即便为 0 也允许派出）。详见 [§14 队伍资金](#14-所有-st-队伍的通用行为)。

扣费后如果队伍创建失败，已经抽出的护卫会还回驻军，金币会退款。

创建后的征兵队：

- 名称为“征兵队 - 首府名”。
- 攻击性设为 0。
- 避免主动敌对行为。
- 禁止 vanilla AI 自己做新决定。
- 记录出发时兵员数。
- 创建时由 home 所有者扣 `StPartyComponent.DefaultSeedGold = 2000` 第纳尔入队伍资金，立刻在首府市场用资金购买约 3 天食物（详见 §14 队伍资金）。
- 进入生命周期管理器。

### 候选村庄选择

候选数量默认 `RecruitmentCandidateBatchSize = 8`，无距离要求。

候选来源：

1. 首府直接附属村庄。
2. 同氏族其他自有城市和城堡的村庄。
3. 非同氏族、与首府非交战状态（同阵营友军或中立第三方）所属的村庄。

候选村庄必须满足：

1. 是村庄。
2. 已激活。
3. 不在本趟排除列表里。
4. 不在村庄招募冷却中。
5. 与首府非交战状态。
6. 没有被劫掠中，也不是已被洗劫状态。
7. notable 志愿兵非空槽位数量大于 0。
8. 如果提供规则，村里至少有一个志愿兵符合当前招募规则。

村庄冷却默认 72 小时。征兵队到达村庄后无论是否实际招到人，都会打 72 小时冷却。

候选优先级：

`priority = 10 × min(可招槽位数, 6) - 0.5 × 距离 - 5 × NearbyLandThreatIntensity`

分数越高越优先。


### 征兵队行程状态

征兵队有四个阶段：

- Dispatching：准备选目标。
- Travelling：正在去村庄或回家。
- AtVillage：已经抵达村庄，执行招募。
- Returning：正在回首府。

每小时状态机执行一次。

Dispatching：

1. 如果有合法未访问的 assigned target，去这个目标。
2. 否则用调度器选下一村。
3. 选到村庄：移动过去，阶段改为 Travelling。
4. 没有目标：保持等待，下个 tick 再试。

Travelling：

1. 如果已经抵达 assigned target，且当前目标仍是 assigned target，且该目标是村庄，立即切 AtVillage 并同 tick 招募。
2. 如果本趟已招人数达到 `RecruiterReturnRecruitedCount`，默认 50，改回首府。
3. 如果当前没有目标，重新规划；仍没有则回首府。
4. 如果目标村庄失效，标记已访问后重新规划。
5. 如果目标风险达到 High 或更高，标记已访问后重新规划。

村庄失效包括：

- 不是村庄。
- 未激活。
- 被劫掠中。
- 已被洗劫。

AtVillage：

1. 必须确实在村庄内。
2. 如果到的不是 assigned target，回到 Travelling。
3. 如果目标是首府，进入 Returning。
4. 如果目标失效，标记已访问并重选。
5. 执行村庄招募。
6. 累计本趟招募人数（哪怕为 0），并给村庄打 72 小时冷却。
7. 标记村庄已访问。
8. 如果累计招募达到返航阈值，回首府。
9. 否则选下一村；选不到就回首府。

Returning：

- 基类检测到当前停在 home 后，会把队伍非英雄兵员合并进首府驻军，然后解散。

### 村庄实际招募逻辑

征兵队到村后：

1. 读取首府规则。
2. 玩家氏族单兵成本 = `round(10 × 0.5)`，即 5 第纳尔。
3. AI 免费。
4. 读取当前首府驻军和征兵队自身兵员，预测最终回城后的 role 数量。
5. 对每个 notable：
   - notable 必须 `CanHaveRecruits`。
   - VolunteerTypes 不能为空。
   - 用 vanilla 算出玩家/领主可招槽位上限。
   - 外派征兵队会把槽位上限扩大为约 2 倍，但不超过 VolunteerTypes 实际长度。
   - 每个槽位兵种必须匹配规则。
   - 计算候选评分。
6. 所有候选按评分从高到低排序。
7. 逐个尝试招募：
   - 预算不足则停止。
   - 该槽位已经为空则跳过。
   - 该兵种 role 已达到配额则跳过。
   - 玩家金币不足则停止。
   - 先把兵加入征兵队。
   - 扣费失败则回滚刚加入的兵并停止。
   - 成功后把 volunteer 槽位置空。
   - 更新 role 计数、预算和已招人数。

通用模式评分：

- 如果 role 还有缺口，缺口 × 2 加分。
- 再加该 role 的比例值。
- 如果兵种在优先列表，额外 +100。

精确模板模式评分：

- 先计算模板目标兵种缺口。
- 候选如果能升级到某个缺口目标，按缺口、目标 Tier、是否已经是目标兵种等打分。
- 无法通向模板目标的兵不招。

## 8. 兵力调拨队

调拨由每日首府后勤统一决定。它会在同氏族自有城市/城堡之间运兵。

### 目的地排序

可作为目的地的节点必须：

1. Demand 大于 0。
2. 目的地没有被围攻。

排序：

1. Priority 高的优先。
2. Priority 相同，离首府更近的优先。

### 调拨源选择

对每个目的地，循环寻找最佳源，直到缺口填完或找不到源。单个目的地最多循环节点数量次，防止无限循环。

源必须满足：

1. 不是目的地自己。
2. 剩余可调容量大于 0。
3. 源和目的地属于同一氏族。
4. 源和目的地同阵营。
5. 源没有被围攻。
6. 源风险低于 High。
7. 不是“首府给首府”。
8. 不再有源到目的地的最大距离硬限制；距离只作为调度评分 / MCMF cost 的一部分。

源评分：

`score = distance - min(capacity, maxTroopsPerTask) × TransferCapacityWeight`

默认 `TransferCapacityWeight = 0.05`。

如果目的地不是首府：

- 源也不是首府：`score -= TransferBranchToBranchPenalty`，默认 25。
- 源是首府：`score += TransferCapitalSourcePenalty`，默认 10。

分数最低的源被选中。

注意：字段名叫 `TransferBranchToBranchPenalty`，但当前代码是减分，实际效果是更偏好非首府之间互相调拨。

### 单次调拨人数

对已选源城：

- `maxTroopsPerTask = round(source.CurrentMen × TransferMaxTroopsPerTaskRatio)`，默认 0.67。
- `byRatio = round(source.CurrentMen × TransferRatio)`，默认 0.30，最低 1。
- `amount = min(maxTroopsPerTask, demand, capacity, byRatio)`。

然后检查小额调拨：

- `minTransferTroops = round(source.CurrentMen × TransferMinTroopRatio)`，默认 0.13。
- 如果 `amount < minTransferTroops` 且目的地没有危急缺口，则放弃这次调拨。
- 如果目的地有危急缺口，小额调拨也允许。

### 创建调拨队的前提

`TransferDispatcher` 逐项检查：

1. TransferTask 不为空。
2. source、destination 不为空。
3. 请求人数大于 0。
4. source != destination。
5. source 和 destination 属于同一氏族。
6. 兵力调拨开关开启。
7. 源城没有被围攻。
8. 目的地没有被围攻。
9. 源城未达到调拨队上限。
10. 源城有 Town、GarrisonParty、MemberRoster。
11. 源驻军总数不小于请求人数。
12. 从源驻军实际抽到了兵。
13. 队伍创建成功。

调拨队上限固定为每个源城 2 支。

抽兵规则：

- 从源驻军抽。
- 低 Tier 优先。
- 不抽英雄。

创建后的调拨队：

- 名称为“调拨队 - 源城 → 目的地”。
- 避免主动敌对行为。
- 攻击性为 0。
- 禁止 vanilla AI 自作决定。
- 创建时由源城所有者扣 `StPartyComponent.DefaultSeedGold = 2000` 第纳尔入队伍资金，立刻在源城市场用资金购买约 3 天食物（详见 §14 队伍资金）。注：调拨队**不**沿途补粮（单目的地短命任务，无中转 settlement）。
- 不应用战后兵员过少/伤兵过多返航规则。

### 调拨队在路上的行为

每小时检查：

1. 如果当前停在目的地，非英雄兵员并入目的地驻军，队伍解散。
2. 如果目的地已经不属于本队氏族，寻找安全 fallback：
   - 优先源城，如果源城仍属本氏族。
   - 否则当前首府。
   - 否则最近的本氏族自有未被围攻城市/城堡。
   - 如果没有 fallback，队伍解散，兵员无法注入。
3. 如果已经停在 fallback，就把兵员并入 fallback。
4. 如果目的地风险达到 Critical，改返源城。
5. 如果返抵源城，基类会把兵并回源城驻军并解散。

## 9. 巡逻队

巡逻队只从首府创建，但巡逻范围是整个氏族自有 settlement。

### 创建巡逻队的前提

每个定居点小时 tick 和每日兜底 tick 都会调用巡逻派遣逻辑，但真正创建必须满足：

1. 当前 tick 的 settlement 不为空。
2. settlement 是城市。
3. 自动巡逻开启。
4. settlement 属于受管氏族。
5. settlement 正是该氏族当前首府。
6. 首府不在围城中。
7. 当前 ST 巡逻队数量低于上限。
8. 首府实际驻军大于 0，按比例算出的 batchSize 大于 0。
9. 抽出巡逻队后，首府驻军仍不低于保留比例。
10. 创建 ST 巡逻队成功。
11. 实际从驻军移动了兵。
12. 玩家氏族支付巡逻队启动资金成功。
13. 调度器能找到非 home 的下一站。

巡逻队上限：

- 按首府兵营等级 + 1。
- 城市读 `settlement_garrison`。
- 找不到建筑时上限为 1。
- 0 级 1 支，3 级 4 支。

巡逻队人数：

`batchSize = round(首府驻军 × PatrolTroopBatchRatio)`

默认 `PatrolTroopBatchRatio = 0.10`，首府有兵且比例为正时最低 1。

首府保留：

`reserveAfterCreation = round(首府驻军 × PatrolReserveAfterCreationRatio)`

默认 0.80。必须满足：

`首府驻军 - batchSize >= reserveAfterCreation`

抽兵规则：

- 低 Tier 优先。
- 不抽英雄。

### 巡逻队启动资金和粮食

T1+T2 之后，巡逻队的"启动资金 + 创建后买粮 + 沿途经济维护"是**所有 4 类 ST 队伍共享**的基类行为，详见 [§14 队伍资金](#14-所有-st-队伍的通用行为)。本节仅记录与巡逻队相关的字段：

- 启动资金：`StPartyComponent.DefaultSeedGold = 2000`（4 类统一）。
- 巡逻队 `ShouldReplenishFoodEnRoute = true` —— 沿途食物 <1 天时自动补 3 天（与 Recruiter 同）。Sally / Transfer 不补粮。
- 食物剩余天数：`FoodChange >= 0` 视为无风险；否则 `Food / -FoodChange`。

### 巡逻目标选择

巡逻调度器候选来自该氏族的 `Clan.Settlements`。

候选必须满足：

1. settlement 属于该氏族。
2. settlement 没有被围攻。
3. 如果是村庄，不能是已洗劫。
4. 没有被其他队伍预占。
5. 距上次访问时间不小于最小回访间隔。

评分：

`score = -hoursSinceVisit + DistanceWeightHoursPerTile × distance`

默认距离权重 0.5。分数越小越优先。

从未访问过的 settlement 视为“已过 1,000,000 小时”，因此会强烈优先访问。

选中目标后，调度器会预占该目标：

`bookHours = max(0.5, ETA + EtaBufferHours)`

默认 ETA 缓冲 1 小时。预占用于避免多支队伍抢同一站。

如果创建时找不到非 home 候选，刚创建的巡逻队会把兵还回首府并解散，避免在家门口空转。

### 巡逻队每小时行为

巡逻队回到 home 的语义是“任务结束并销毁”。正常巡逻目标选择会排除 home。

每小时顺序：

1. 如果自动巡逻关闭，巡逻队本 tick 不继续执行巡逻逻辑。
2. 如果缺少氏族或首府管理器，跳过。
3. 如果当前在 settlement 内，卖战利品、补粮。
4. 检查防御响应。
5. 检查是否能支援正在战斗的出击队。
6. 检查是否真正抵达一个 settlement。
7. 如果抵达，记录访问并选下一站。
8. 如果没有抵达，检查卡死。

### 防御响应

巡逻队会扫描本氏族所有正在被围攻的 settlement 和 被洗劫中的村庄。

如果首府被围攻：

- 巡逻队直接把兵并入首府驻军并解散。

如果非首府被围攻：

- 巡逻队选择距离自己最近的被围攻 settlement。
- 使用 vanilla `SetMoveDefendSettlement` 去防守。
- 同时设置 AI initiative：攻击 0.3，避战 0.7，持续 4 小时。
- 如果目标围攻/劫掠状态结束，会立刻重新选普通下一站。

如果村庄被洗劫中：
- 巡逻队选择距离自己最近的被洗劫中村庄。
- 使用vanilla api 支援村庄。
- 同时设置 AI initiative：攻击 0.3，避战 0.7，持续 4 小时。
- 如果目标围攻/劫掠状态结束，会立刻重新选普通下一站。

### 支援出击队

如果同氏族有出击队正在 MapEvent 战斗中，巡逻队会估算 ETA：

`ETA = 距离 / 巡逻队速度`

如果 ETA 小于 `SupportEtaThresholdHours`，默认 2 小时，则去支援最近可支援的出击队。

### 卡死保护

巡逻调度器记录每次目标变化时间。

如果单段路超过 `ClanPatrol.StuckTimeoutHours`，默认 12 小时，视为卡死：

1. 第一次卡死时记录卡死开始时间和位置。
2. 如果之后移动距离超过 1.0 地图单位，认为恢复，清除卡死状态。
3. 如果卡死持续超过 `StuckTeleportHours`，默认 24 小时，且该值大于 0，直接把队伍位置设到 home 的 GatePosition。
4. 如果还没到瞬移阈值，则重新选择下一站；没有下一站就指向首府。

## 10. 主动出击队

主动出击队从任意受管城镇和城堡创建，不要求只能首府创建，但该氏族必须有可用首府。

### 创建前提

每个 settlement 的小时 tick 和每日兜底 tick 都会尝试，但必须满足：

1. settlement 不为空。
2. settlement 属于受管氏族。
3. 该氏族有可用首府。
4. 主动出击开关开启。
5. 首府有 Town 数据。
6. settlement 自己没有被围攻。
7. 该 settlement 未达到出击队上限。
8. 在搜索半径内找到敌方目标，或本 settlement 下辖的村庄被劫掠中。
9. 冷却已结束。
10. 敌方目标连续可见 tick 数达到阈值（本 settlement 下辖的村庄被劫掠中时忽略这个）。
11. 计算出的出击人数达到创建下限。
12. 玩家氏族支付初始金币成功。
13. 创建队伍成功。
14. 实际抽兵数量达到创建下限。

出击队上限固定为每城 1 支。

### 敌方目标或目标村庄选择

如果是敌方目标，则搜索半径默认 `SallyDetectionRadius = 50`，如果是村庄被劫掠中，不设搜索半径。

候选敌军必须满足：

1. 队伍活跃。
2. 不是玩家主队。
3. 有阵营。
4. 与本城阵营处于战争状态。

目标选择规则：优先支援被劫掠的的村庄，其次选择健康兵力最少的敌方队伍。

健康兵力近似：

`TotalManCount - TotalWounded`

### 持续可见与冷却

如果没有敌方目标：

- 清除该城的连续可见计数。

如果有敌方目标：

- 连续可见计数 +1。
- 默认必须达到 `SallyMinSustainedTicks = 3`，也就是连续 3 个小时 tick 看见敌人，才会出击。

出击结束后，该城进入冷却：

- 默认 `SallyCooldownHours = 24`。
- 出击队回家合并或被销毁时记录结束时间并清除持续可见计数。

### 出击人数计算

读取本城驻军数 `garrisonCount`。

1. `minDef = round(garrisonCount × MinimumDefenderRatio)`
2. `extractable = max(0, garrisonCount - minDef)`
3. `byGarrisonRatio = round(garrisonCount × SallyExtractionRatio)`
4. `byTarget = ceil(敌方总兵力 × SallyTargetPartySizeMultiplier)`
5. `sallySize = min(byTarget, extractable, byGarrisonRatio)`

默认：

- `MinimumDefenderRatio = 0.20`
- `SallyExtractionRatio = 0.60`
- `SallyTargetPartySizeMultiplier = 2.0`
- `SallyCreateMinPartyCount = 30`

如果 `sallySize < SallyCreateMinPartyCount`，不创建。

抽兵规则：

- 从本城驻军抽。
- 高 Tier 优先。
- 不抽英雄。

所有 4 类 ST 队伍创建时统一扣 `StPartyComponent.DefaultSeedGold = 2000` 第纳尔。玩家氏族走 `ModTreasury.Charge`（不足则拒绝派遣 + 抽兵已完成时回滚兵+销毁实例）；AI 从 home owner `hero.Gold` 按可用余额扣。详见 [§14 队伍资金](#14-所有-st-队伍的通用行为)。

出击队创建后：

- 名称为“出击队 - 城名”。
- 攻击性为 0。
- 不避免敌对行为。
- 禁止加入玩家战斗。
- 创建时由 home 所有者扣 `StPartyComponent.DefaultSeedGold = 2000` 第纳尔入队伍资金，立刻在本城市场用资金购买约 3 天食物（详见 §14 队伍资金）。注：出击队**不**沿途补粮（单目的地短命任务）。
- 直接 `SetMoveEngageParty` 追击目标。

### 出击队在路上和战后

出击队有两个阶段：

- Engaging
- Returning

Engaging 每小时检查：

1. 离家超过 12 小时：强制回家。
2. 目标进入 settlement：回家。
3. 目标为空或不活跃：回家。
4. 否则每小时重新设置不做新决定，并再次 `SetMoveEngageParty` 追击目标。

战斗结束：

- 无论战果和损失如何，出击队都切 Returning。

Returning：

- 回到 home 后，非英雄兵员并回驻军，然后队伍直接销毁。

如果队伍被 vanilla 销毁：

- 基类会尝试把残余非英雄兵员救回 home。
- home 不可用时尝试救回当前首府。
- 同时通知 SallyDispatcher 进入冷却。

## 11. 战利品与金币（已废弃，T2 完成 2026-05-18）

旧版"战利品集中处理"已**整章删除**（doc §20 #2）。原 `BattleLootHandler` / `BattleLootManager` 与 3 个 EnabledFeatures toggle（`AutoRecruitMatchingPrisoners` / `AutoSellNonMatchingPrisoners` / `AutoSellLoot`）一并移除。

**当前模型**：所有 4 类 ST 队伍走基类自资金路径，详见 [§14 队伍资金](#14-所有-st-队伍的通用行为)。

- **战利品 / 装备物品**：到 `settlement.Town` 时由 `TryEconomicMaintenance.SellLootAtSettlement` 自动卖入 `_teamFunds`（vanilla `SellItemsAction`）。
- **俘虏处理**：不再有"匹配招首府"或"非匹配卖城"的集中处理。捕获的俘虏占用 `PrisonRoster` 槽位；超过 `PartyPrisonerCap`（默认 30）后由 `TryEnforcePrisonerCap` 随机踢出非英雄俘虏。
- **队伍金币回流**：销毁时 `TryRefundOnDestroy` 把 `_teamFunds` 余款退还 home 所有者（玩家走 `ModTreasury.Refund`；AI 走 `hero.Gold`）。

## 12. 首府每日俘虏转化

每日 settlement tick 里，只有当 settlement 正是该氏族当前首府时，才调用俘虏转化。

因此当前实际行为是：

- 只在首府运行。
- 首府只能是城市。
- 城堡不会跑这条每日俘虏转化路径。

前提条件：

1. settlement 是城市。
2. settlement 所属氏族是受管氏族且有首府。
3. 自动招募开启。
4. 首府不在围城中。
5. 当前规则允许俘虏转化。
6. `Town.FoodChange >= FoodSafetyThreshold`。
7. settlement 有 PrisonRoster。
8. vanilla PrisonerRecruitmentCalculationModel 存在。

每日 conformity：

- 读取城市地牢建筑 `settlement_dungeon` 等级。
- 等级钳制到 0 到 3。
- 每日 conformity = `(level + 1) × 5`。
- 找不到建筑时按 5。

遍历俘虏：

1. 跳过英雄。
2. 兵种必须匹配规则。
3. vanilla 模型计算当前可招数量。
4. 如果可招数量小于该俘虏总数，给整组俘虏增加 conformity XP，再重新计算。
5. 可招数量不能超过驻军 PartySizeLimit 剩余空间。
6. 先把兵加入驻军，再从 PrisonRoster 扣俘虏和 conformity。
7. 扣俘虏失败会回滚驻军加入，避免复制兵。

## 13. 驻军 XP 和自动升级

每日 settlement tick 里，只有当前 settlement 是该氏族首府时才调用 XP 注入。由于首府只能是城市，当前实际行为是：首府城市获得每日 XP 注入，城堡不走这条路径。

XP 注入前提：

1. settlement 存在。
2. settlement 是城市。
3. 所属氏族是受管氏族。
4. 自动招募开启。
5. 城市不在围城中。
6. 有或能重建 GarrisonParty。
7. 有 MemberRoster。

基础 XP：

- 读取城市兵营建筑 `settlement_garrison` 等级。
- 等级钳制 0 到 3。
- 每兵基础 XP = `(level + 1) × 10`。
- 找不到建筑时按 10。

额外 XP：

- 尝试读取 vanilla DailyTroopXpBonusModel。
- `townBonus = round(baseXp × multiplier)`。
- 按照首府拥有者所属城镇和城堡数量进行额外乘算，城镇数量乘算因子为1.5，城堡为0.5
- 失败时额外 XP 为 0。

对每个驻军元素：

1. 跳过 null。
2. 跳过英雄。
3. 跳过数量小于等于 0。
4. 如果兵种 Tier 高于规则 MaxTier，不注入 XP。
5. 注入 `(基础 XP + townBonus) × 该兵种数量`。

### 后勤里的自动升级

每日后勤会先尝试升级首府：

1. 当前规则 `AllowAutoUpgrade` 必须开启。
2. 首府驻军总数大于 0。
3. T1+T2 占比必须达到 `AutoUpgradeMinTierRatio`，默认 0.30。
4. 升级预算 = `max(BudgetLimit / 4, AutoUpgradeMinBudget)`，默认最低 500。
5. 单次最多升级 `AutoUpgradeMaxPerCall` 个，默认 20。

`TroopUpgradeService` 升级规则：

1. 只看非英雄、正规兵。
2. 当前 Tier 必须低于规则 MaxTier。
3. 必须有升级目标。
4. 低 Tier 优先。
5. 在可升级目标中选择最符合当前模板的目标。
6. XP 必须足够。
7. 金币预算必须足够。
8. 玩家氏族升级扣玩家金币；AI 免费。
9. 先加目标兵，再移除原兵。
10. 如果移除失败，会回滚目标兵并退款。
11. 最后扣原兵 XP。

## 14. 所有 ST 队伍的通用行为

四种队伍都继承 `StPartyComponent`：

- 征兵队
- 调拨队
- 巡逻队
- 出击队

通用小时 tick 前置检查：

1. 队伍必须活跃。
2. 队伍必须有 ActualClan。
3. CapitalRegistry 必须存在。
4. home 必须存在。
5. 如果 home 已不属于队伍氏族：
   - 有当前首府时，把非英雄兵员并入首府并解散。
   - 没有首府时直接解散。
6. 该氏族必须有当前首府。
7. 非出击队会每小时强制 `SetDoNotMakeNewDecisions(true)`，防止 vanilla AI 抢走目标。
8. 如果玩家被这支 ST 队俘获，队伍立刻回 home。
9. 如果玩家主队正在追/点选这支 ST 队，非出击队会停下让玩家追上。
10. 如果当前真的停在 home，执行到家处理。
11. 否则执行各子类状态机。
12. 最后执行俘虏上限清理。

重要修正口径：到家只看 `CurrentSettlement == home`。不再用 `LastVisitedSettlement`，因为 Bannerlord 离开 settlement 后仍会保留上次访问点，容易误判“刚出门就到家”。

### 战后返航规则

除调拨队外，战斗结束后都会检查是否要回家解散。

触发任一条件：

- 当前兵员 < 出发兵员 × `PartyReturnSizeRatio`，默认 0.5。
- 当前受伤兵员 / 当前总兵员 > `PartyReturnWoundedRatio`，默认 0.3。

命中后队伍改回 home。

调拨队不使用这个规则，因为调拨兵员本身就是要送达的货物。

### 空闲检测

生命周期管理器每小时对被跟踪队伍检测进展。

进展定义：

- TargetSettlement 改变。
- 或队伍成员数量改变。

有进展就刷新 LastActiveAt。

如果队伍正在 MapEvent 或有 BesiegedSettlement，跳过空闲检测。

否则：

- 空闲达到 `IdleHoursBeforeForceReturn`，默认 24 小时：强制指向 home。
- 空闲达到 `IdleHoursBeforeDisband`，默认 36 小时：直接开始解散并取消跟踪。

### 俘虏上限

所有 ST 队伍每小时末尾检查 PrisonRoster。

- 默认 `PartyPrisonerCap = 30`。
- 0 表示关闭。
- 超过上限时，随机移除超额数量的非英雄俘虏。

### 容量、速度和工资

所有 ST 队伍：

- 速度：在 vanilla 最终速度上加 20%。
- 工资：队伍工资返回 0，不扣家族军饷。

容量：

- 征兵队容量 = 当前成员 + 本趟剩余可招人数。
- 调拨队容量 = 当前成员和源驻军比例上限两者较大值。
- 巡逻队容量 = 当前成员和“巡逻抽兵比例 × 2”的缓冲上限两者较大值，最低缓冲 30。
- 出击队容量 = 当前成员和出击规模上限两者较大值。

### 队伍资金（T1+T2 共享，2026-05-18）

所有 4 类 ST 队伍继承基类 `_teamFunds` 字段。基类常量 `StPartyComponent.DefaultSeedGold = 2000`。

- **创建时**：由 home 所有者扣 `DefaultSeedGold = 2000` 入 `_teamFunds`（玩家走 `ModTreasury.Charge`，扣款失败 → 回滚兵+销毁；AI 走 `InitTeamFundsFromHomeOwner` 从 home owner `hero.Gold` 按可用余额扣，即便为 0 也允许派出）。
- **创建后立即买粮**：调用 `BuyFoodAtSettlement(party, origin, 3f)` 用 `_teamFunds` 在出发地市场购买约 3 天食物（vanilla `SellItemsAction` 真实交易）。
- **每小时（仅 town/castle 内）**：基类 `OnHourlyTick` 调 `TryEconomicMaintenance`：
  - 卖战利品（所有类型）：`SellLootAtSettlement` 把非食物物品卖入 `_teamFunds`。
  - 补粮（仅 Patrol / Recruiter，`ShouldReplenishFoodEnRoute=true`）：食物 <1 天时调 `BuyFoodAtSettlement` 买 3 天。
  - **Sally / Transfer 不补粮**（`ShouldReplenishFoodEnRoute=false`）—— 单目的地短命任务，沿途无中转 settlement。
- **销毁时**：基类 `OnDestroyed` 调 `TryRefundOnDestroy`：剩余 `_teamFunds` 退还 home 所有者（玩家走 `ModTreasury.Refund` 保账目对称；AI 走 `hero.Gold`）。
- **统一 Dispatcher helper**：基类静态方法 `TrySeedAndBuyInitialFood(component, party, origin, expenseCategory, chargeFromClan, noteContext)` 封装"扣款 + 注资 + 买粮"流程；4 个 Dispatcher 全部调用此 helper，扣款失败统一回滚兵+销毁。
- 在荒郊和村庄不交易（`settlement.Town == null`）。

## 15. vanilla 自动招募抑制

当以下条件同时满足时，Mod 会把受管城/堡的 `Town.GarrisonAutoRecruitmentIsEnabled` 设为 false：

1. 抑制 vanilla 自动招募开启。
2. 自动招募开启。
3. 该 settlement 属于受管氏族。
4. 该氏族有可用首府。
5. 如果 AI 接管关闭，则只处理玩家氏族。

效果：

- vanilla 不再自动从 notable 志愿兵拉兵进 GarrisonParty。
- vanilla 不再自动升级 GarrisonParty 兵种。
- notable 自己的 VolunteerTypes 刷新不受影响。
- 民兵自然增长不受影响。

如果玩家关闭抑制，或关闭自动招募，Mod 会遍历所有城/堡把这个 vanilla flag 恢复为 true。

城镇易主时：

- 进入受管范围：禁用 vanilla 自动招募。
- 离开受管范围：恢复 vanilla 自动招募。

## 16. AI 氏族接管

默认关闭。

开启后：

1. 给每个至少拥有 1 个城市的非玩家、未灭亡氏族创建 CapitalManager。
2. AI 首府同样只能是城市。
3. AI 城镇/城堡参与驻军目标、调拨、招募、巡逻、出击、俘虏、战利品等路径。
4. AI 招募和升级不扣玩家金币。
5. AI 规则基础会优先尝试使用文化预设；没有预设时用全局默认。

关闭 AI 接管时：

1. 对每个 AI 受管氏族，尝试把在途 ST 队伍兵员并入该氏族当前首府。
2. 如果没有首府，退化到该氏族任意自有城市/城堡。
3. 都没有时，队伍只能解散。
4. 移除 AI manager。

## 17. 玩家可遇到的队伍交互

玩家点击自家 ST 队伍时，Mod 注册了高优先级对话拦截：

- 征兵队、调拨队、出击队都会显示“我们是某地派出的某某队伍，向您致意。”
- 玩家只有“祝你顺利。”选项。
- 选择后离开遭遇，不进入攻击/勒索等 vanilla 敌对选项。
- 自家 ST 巡逻队也有类似问候。
- vanilla 自动刷的巡逻队不属于 ST 巡逻队，不走这个拦截。

## 18. 主要可配置字段索引

### 招募相关

| 字段 | 默认 | 影响 |
| --- | --- | --- |
| `VillageCooldownHours` | 72 | 村庄被征兵队访问后多少小时不再候选（无论是否实际招到人） |
| `RecruitmentMinDemandRatio` | 0.07 | 剩余缺口低于首府驻军这个比例时不派征兵队，危急缺口除外 |
| `RecruiterEscortRatio` | 0.10 | 派征兵队时抽首府驻军作为护卫的比例 |
| `RecruiterReturnRecruitedCount` | 50 | 本趟招到多少人后返航 |
| `RecruiterMinHomeGarrison` | 0 | 派征兵队前首府最低驻军，0 表示关闭 |
| `RecruitmentCandidateBatchSize` | 8 | 每轮候选村庄数量 |
| `ClanRecruiter.EtaBufferHours` | 1 | 征兵目标预占额外小时 |
| `ClanRecruiter.DistanceWeightHoursPerTile` | 0.5 | 征兵调度距离权重 |

### 巡逻与调拨相关

| 字段 | 默认 | 影响 |
| --- | --- | --- |
| `PatrolReserveAfterCreationRatio` | 0.8 | 创建巡逻后首府必须保留的驻军比例 |
| `PatrolTroopBatchRatio` | 0.10 | 每支巡逻队抽首府驻军比例 |
| `ClanPatrol.AvoidRaidedVillages` | true | 巡逻普通目标是否避开被劫掠村庄 |
| `ClanPatrol.EtaBufferHours` | 1 | 巡逻目标预占额外小时 |
| `ClanPatrol.StuckTimeoutHours` | 12 | 巡逻单段路多久算卡死 |
| `ClanPatrol.MinVisitGapHours` | 4 | 同 settlement 最小回访间隔 |
| `ClanPatrol.DistanceWeightHoursPerTile` | 0.5 | 巡逻调度距离权重 |
| `ClanPatrol.SupportEtaThresholdHours` | 2 | 巡逻队支援出击战斗 ETA 阈值 |
| `TransferCriticalProjectedRatio` | 0.24 | 低于目标这个比例视为危急缺口 |
| `TransferRatio` | 0.30 | 源城单次按当前驻军这个比例抽兵 |
| `TransferMaxTroopsPerTaskRatio` | 0.67 | 单次调拨人数上限比例 |
| `TransferMinTroopRatio` | 0.13 | 非危急调拨小于该比例就放弃 |
| `TransferCapacityWeight` | 0.05 | 调拨源评分里的容量权重 |
| `TransferBranchToBranchPenalty` | 25 | 当前代码中对非首府源减分，实际偏好 branch-to-branch |
| `TransferCapitalSourcePenalty` | 10 | 从首府出兵到非首府时加分惩罚，越大越不愿用首府 |

### 主动出击相关

| 字段 | 默认 | 影响 |
| --- | --- | --- |
| `SallyDetectionRadius` | 50 | 搜索敌方目标半径 |
| `SallyCooldownHours` | 24 | 出击结束后的冷却 |
| `SallyMinSustainedTicks` | 3 | 敌人连续可见多少小时才出击 |
| `SallyExtractionRatio` | 0.60 | 出击人数不能超过驻军这个比例 |
| `SallyTargetPartySizeMultiplier` | 2.0 | 出击目标人数 = 敌军人数 × 该倍数 |
| `SallyCreateMinPartyCount` | 30 | 算出的出击人数低于它不创建 |
| `MinimumDefenderRatio` | 0.20 | 出击后城内最低保留比例 |

### 生命周期与升级

| 字段 | 默认 | 影响 |
| --- | --- | --- |
| `PartyReturnSizeRatio` | 0.5 | 战后当前兵力低于出发兵力该比例则返航 |
| `PartyReturnWoundedRatio` | 0.3 | 战后伤兵比例高于它则返航 |
| `PartyPrisonerCap` | 30 | 所有 ST 队伍俘虏上限，0 关闭 |
| `StuckTeleportHours` | 24 | 巡逻卡死二段瞬移阈值，0 关闭 |
| `IdleHoursBeforeForceReturn` | 24 | 队伍无进展多久强制回 home |
| `IdleHoursBeforeDisband` | 36 | 队伍无进展多久直接解散 |
| `AutoUpgradeMinTierRatio` | 0.30 | T1+T2 比例达到多少才触发升级 |
| `AutoUpgradeMinBudget` | 500 | 升级预算最低值 |
| `AutoUpgradeMaxPerCall` | 20 | 单次最多升级人数 |

### 仍是代码固定值的行为

这些当前不在面板里：

| 值 | 当前值 | 用途 |
| --- | --- | --- |
| 4 类 ST 队伍统一启动资金 | 2000 | `StPartyComponent.DefaultSeedGold` 基类常量；T1+T2 之后所有队伍同值（详见 §14 队伍资金） |
| 征兵单兵基础价 | 10 | 外派征兵队按 0.5 折扣后为 5 |
| 征兵单兵折扣 | 0.5 | 玩家外派征兵和原地招募实际单兵成本为 5 |
| 外派征兵槽位扩展倍率 | 2.0 | 征兵队到村时可扫描约 vanilla 可招槽位的 2 倍 |
| 出击队最长离家时间 | 12 小时 | 超过后强制返航 |
| 巡逻队食物补给触发 | 低于 1 天 | 到 settlement 时买粮 |
| 巡逻队买粮目标 | 3 天 | 每次补到约 3 天 |
| 巡逻卡死位置恢复距离 | 1.0 地图单位 | 卡死后移动超过此距离视为恢复 |

## 19. 当前行为边界提示

这些不是报错，只是当前代码口径：

1. 首府只能是城市；城堡-only 时没有首府，首府制自动化不会运行。
2. 城堡参与后勤节点和调拨，但不会成为首府。
3. 每日 XP 注入和每日俘虏转化只在首府触发；因为首府只能是城市，所以城堡不会走这两条每日路径。
4. 巡逻队从首府创建，且不会把 home 当普通巡逻站；回 home 就解散。
5. 调拨的 ProjectedMen 不扣 outbound，因此刚派出的调拨可能让源城“当前驻军”下降，但缺口评估里的 projected 不直接用 outbound 做负项。
6. 4 类 ST 队伍统一启动资金 `StPartyComponent.DefaultSeedGold = 2000`，基类常量、不在 Web 面板（T1+T2 之后）。
7. 征兵队和调拨队避免敌对行为；出击队和巡逻队不避免敌对行为。
8. vanilla 自动招募抑制不影响民兵增长，也不影响 notable 志愿兵自然刷新。

## 20. 重构待办

以下条目是已知的设计意图，但当前代码尚未实现或仍在使用旧方案。它们不是当前行为描述，仅作为后续重构的指引：

1. ~~统一队伍粮食与自资金逻辑~~ **(T1 已完成 2026-05-18)**：4 类 ST 队伍现共享基类 `_teamFunds` + `TryEconomicMaintenance` + `TrySeedAndBuyInitialFood` helper，所有队伍 seed gold 统一为 `DefaultSeedGold = 2000`；Sally / Transfer 不沿途补粮（`ShouldReplenishFoodEnRoute=false`），详见 §14 队伍资金。
2. ~~战利品集中处理逻辑废弃~~ **(T2 已完成 2026-05-18)**：`BattleLootHandler` / `BattleLootManager` 整段删除；3 个 EnabledFeatures toggle（`AutoRecruitMatchingPrisoners` / `AutoSellNonMatchingPrisoners` / `AutoSellLoot`）一并移除；俘虏处理退化为 `PartyPrisonerCap` 随机踢出非英雄。详见 §11（废弃说明）和 §14 队伍资金。
