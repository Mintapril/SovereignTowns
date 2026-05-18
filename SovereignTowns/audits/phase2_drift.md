# Phase 2 — 差异清单（drift）

> 日期：2026-05-18
> 方法：5 个 Explore 子任务并行核 doc §1–§19（约 175 条断言）+ 主线程跨文件 grep 收敛 6 个 ❓。
> **本阶段不修改任何代码。** 仅记录 drift + 建议方向。

---

## 0. 汇总

| 状态 | 计数 | 说明 |
| --- | ---: | --- |
| ✅ 一致 | ~155 | doc 与代码完全一致，不需要改动 |
| ⚠️ 部分一致 | 4 | 语义偏差或 doc 措辞不准；可改 doc 或改代码均小工作量 |
| ❌ 不一致 | 5 | doc 描述了代码没实现的行为，或代码实现了 doc 没承诺的行为 |
| ❓ 待裁决 | 0 | 跨文件项已通过主线程 grep 全部收敛 |
| R20.x | 4 | §20 REFACTOR_TODO 现状记录，不计入对齐对错 |

**整体结论**：drift 比例 ~5%（9 / 175），主要 drift 集中在 §7（招募候选范围）、§9–§10（被劫村响应）、§13（XP 城/堡系数）。§20 #1 重构 WIP 状态保持原有事实。

按用户指令「**全部按照文档来**」，所有 5 项 ❌ 默认走"**改代码、不改文档**"。但每项仍列出可替代方向供你裁决（防止改成 doc 写错了的极端情况）。

---

## 1. ❌ 真实不一致项（5 项，全部建议"改代码"）

### D1 / A7.11 — 文档第 3 类招募候选来源未实现

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:447–451 |
| 文档断言 | 候选来源 3 类：① 首府直接附属村；② 同氏族其他自有城/堡的村；③ **非同氏族、与首府非交战状态（同阵营友军 / 中立第三方）的村庄** |
| 代码现状 | [RecruitmentPlanner.cs:106–145](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs:106) 仅实现前 2 类；[L190](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs:190) `if (villageSettlement.MapFaction != homeFaction) return;` 明确硬过滤掉所有非 home faction 的村庄 |
| 严重度 | **高**（doc 承诺的功能完全缺失，玩家会发现外派征兵队找不到友军/中立村） |
| 建议方向 | **改代码**：在 [RecruitmentPlanner.RankCandidates](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs:106) 加第 3 类扫描；过滤逻辑改为 `MapFaction != homeFaction || !IsAtWar` 形式 |
| 备选 | 若不想做，则改 doc 删去第 3 类描述（与"全部按文档来"原则冲突，**不推荐**） |

### D2 / A7.10 — "无距离要求"措辞与代码事实不符

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:445 |
| 文档断言 | "候选数量默认 RecruitmentCandidateBatchSize = 8，**无距离要求**" |
| 代码现状 | [RecruitmentPlanner.cs:74–78](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs:74) `maxDistance=100f`；[L116–117](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs:116) 直接隶属村 `includeDistanceFilter=false`，[L142–143](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs:142) 其他同氏族 town 的村 `includeDistanceFilter=true`（100m 限制） |
| 严重度 | **中**（doc 误导，代码已分场景实现） |
| 建议方向 | **改 doc**：行 445 改为"直接附属村无距离限制；其他自有 town 的村庄需在 100m 内"（这是文档措辞不准，代码行为更合理） |
| 备选 | **改代码**移除所有距离限制 — 但会带来跨地图征兵队"漫游"问题，不推荐 |

> ⚠️ D2 是"按文档来"的反例——doc 不一定字面正确，代码反而更合理。先按建议改 doc，若你坚持按文档字面改代码，请在 Phase 3 前明示。

### D3 / A9.19 — 巡逻被劫村防御响应分支完全缺失

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:807–811 |
| 文档断言 | "如果村庄被洗劫中：巡逻队选择距离自己最近的被洗劫中村庄；使用 vanilla api 支援村庄；同时设置 AI initiative：攻击 0.3，避战 0.7，持续 4 小时；如果目标围攻/劫掠状态结束，会立刻重新选普通下一站。" |
| 代码现状 | [ClanPatrolScheduler.GetDefenseTarget:89–134](SovereignTowns/src/Patrol/ClanPatrolScheduler.cs:89) **只收集 `IsUnderSiege`** 的 settlement，不收集 `VillageState == BeingRaided` 的村庄。`IsVillageRaided` 辅助方法[L138–149](SovereignTowns/src/Patrol/ClanPatrolScheduler.cs:138)存在但只用于候选过滤（line 46），**不触发防御响应**。 |
| 严重度 | **高**（doc 承诺的功能完全缺失） |
| 建议方向 | **改代码**：在 `GetDefenseTarget` 中扩展 `besieged` 集合，加入本氏族所辖被劫村（`Village.VillageState == BeingRaided`）；调用方 [StPatrolPartyComponent.cs:278–310](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:278) 已经能处理"非首府目标 → SafeSetMoveDefendSettlement + initiative" 路径，故只需调度器侧加来源；line 310 已经做 `IsUnderSiege \|\| (IsVillage && BeingRaided)` 的状态结束判断（已对上）。 |
| 备选 | 改 doc 删去被劫村响应段（不推荐） |

### D4 / A10.5 — Sally 优先支援被劫村分支缺失

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:847（创建前提 14 中"在搜索半径内找到敌方目标，或本 settlement 下辖的村庄被劫掠中"）+ doc:868（目标选择"优先支援被劫掠的村庄，其次选择健康兵力最少的敌方队伍"） |
| 代码现状 | [SallyDispatcher.FindBestEnemyTarget:173–225](SovereignTowns/src/SallyForth/SallyDispatcher.cs:173) **只按 `strength = TotalManCount - TotalWounded` 选敌军最弱者**；无被劫村检测分支；[SallyDispatcher.cs:91](SovereignTowns/src/SallyForth/SallyDispatcher.cs:91) `target = FindBestEnemyTarget(settlement)` 后未追加村庄分支 |
| 严重度 | **高**（doc 重要功能缺失：被劫村救援是出击队的标志性场景之一） |
| 建议方向 | **改代码**：在 [SallyDispatcher](SovereignTowns/src/SallyForth/SallyDispatcher.cs) 加 `FindRaidedVillageInRange` 路径，优先返回被劫村作为出击目标；持续可见忽略 / 半径不限（doc:859 / doc:849） |
| 备选 | 改 doc 删去这段（不推荐） |

### D5 / A13.4 — 驻军 XP 的 "城/堡数额外乘算 1.5/0.5" 系数完全缺失

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:1072–1077 |
| 文档断言 | "额外 XP：尝试读取 vanilla DailyTroopXpBonusModel；`townBonus = round(baseXp × multiplier)`；**按照首府拥有者所属城镇和城堡数量进行额外乘算，城镇数量乘算因子为 1.5，城堡为 0.5**；失败时额外 XP 为 0。" |
| 代码现状 | [GarrisonXpInjector.cs:90–105](SovereignTowns/src/Upgrades/GarrisonXpInjector.cs:90) 仅调用 vanilla `DailyTroopXpBonusModel.CalculateGarrisonXpBonusMultiplier`，**没有任何"城镇数量 × 1.5 + 城堡数量 × 0.5"** 的实现（grep "1\\.5f|0\\.5f|TownOwnedCount|CastleOwnedCount" 0 匹配） |
| 严重度 | **高**（doc 承诺的功能完全缺失） |
| 建议方向 | **改代码**：在 `GarrisonXpInjector` 计算 baseXp + townBonus 后，再乘 `(townCount × 1.5 + castleCount × 0.5)`。首府拥有者氏族 = `settlement.OwnerClan`，遍历 `clan.Settlements` 数 IsTown / IsCastle 即可 |
| 备选 | 改 doc 删去这段（不推荐） |

---

## 2. ⚠️ 部分一致项（4 项）

### W1 / A8.5 — TransferBranchToBranchPenalty 命名与语义反差

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:598-604（doc 自承） |
| 现状 | 字段名叫 "Penalty"（惩罚），但代码实际是 "减分"（即偏好 branch-to-branch）。doc 已显式说明此反差。 |
| 严重度 | **低**（语义不影响行为，仅命名 smell） |
| 建议方向 | Phase 4 重构候选：要么改名为 `TransferBranchToBranchPreferenceBonus`，要么 doc 里加 inline 注释把"实际偏好 branch-to-branch"放到字段说明里。 |
| 备选 | 保留现状（doc 已承认） |

### W2 / A14.5 — "进展定义"中"成员数量改变"的口径

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:1156–1158 |
| 现状 | 文档说"成员数量改变"刷新 LastActiveAt；代码 [PartyLifecycleManager.cs:421–434](SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs:421) 实现一致，但用的是 `currentMembers != meta.LastObservedMemberCount`（snapshot/diff 模式），与 doc 口径一致。 |
| 严重度 | **低** |
| 建议方向 | 无 — 实际是一致，仅记录方便 Phase 5 查表 |

### W3 / R20.2 — Sally / Transfer / Recruiter 仍走 GrantFoodForDays「凭空塞」

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:441 / 653 / 923（当前行为）+ doc:1342（§20 #1 重构意图） |
| 现状 | 当前 [SallyDispatcher.cs:303](SovereignTowns/src/SallyForth/SallyDispatcher.cs:303)、[StTransferPartyComponent.cs:102](SovereignTowns/src/Parties/StTransferPartyComponent.cs:102)、[StRecruiterPartyComponent.cs:172](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:172) 都调用 `GrantFoodForDays(party, 3f)`，是 doc §7/§8/§10 描述的"3 天免费食物"——**与当前行为 doc 一致**。但 doc §20 #1 已明确意图要把这些都改为"巡逻风格的自资金 + 买粮"。 |
| 严重度 | **中**（Phase 2 对当前行为对齐 → ✅；Phase 4 执行 §20 #1 时这项必须改） |
| 建议方向 | Phase 2 标 ✅；交给 Phase 4 重构 |

### W4 / A7.13 — 村庄冷却 72h 的"默认"是 config 默认值

| 维度 | 内容 |
| --- | --- |
| 文档位置 | doc:464 / doc:1251 |
| 现状 | doc:1251（§18）说"VillageCooldownHours 默认 72"，[GlobalConfig.cs:36](SovereignTowns/src/Configuration/GlobalConfig.cs:36) 确认默认值 72 ✅。`RecruitmentCooldown.cs:46` 当 config 为 null 时冷却禁用，这是 fail-safe 路径。doc 措辞"默认 72 小时"是配置默认值，不是硬编码常量。 |
| 严重度 | **极低** |
| 建议方向 | 无 — 实际一致 |

---

## 3. ✅ 一致项概览（不分行列表，避免噪音）

按章节给出"已核对一致"的统计：

| 章节 | doc 行范围 | 核对项数 | 全 ✅ |
| --- | --- | ---: | --- |
| §1 启动与兼容 | 41–67 | 7 | 是（包括 IG/GDS 互斥、3 个 GameModel、Web 端口/token/路径） |
| §2 首府系统 | 69–118 | 9 | 是（初始化顺序、扫城市不含城堡、手动设首府 5 前提、易主迁移） |
| §3 配置与面板 | 120–219 | 全部默认值与开关 | 是（12 开关 + 17 garrison 字段 + 5 二级分类 + PUT/api/config 排队） |
| §4 风险评估 | 221–244 | 6 | 是（包括 Critical 仅围城、High 排除调拨源） |
| §5 每日后勤 | 246–312 | 9 | 是（Demand / ProjectedMen / Priority 全部公式精确匹配） |
| §6 首府原地招募 | 314–361 | 6 | 是（11 触发条件 + 玩家 5 第纳尔扣费 + Role 配额） |
| §7 外派征兵队 | 363–559 | 22 / 23 | 除 A7.10 / A7.11 外全 ✅（4 阶段状态机逐步对齐） |
| §8 调拨队 | 561–667 | 11 | 是（13 创建前提、抽兵规则、A4.6 Critical 返源、A14.4 不走战后返航） |
| §9 巡逻队 | 669–830 | 20 / 21 | 除 A9.19 外全 ✅（startSeedGold=2000 / batchSize 0.10 / reserve 0.80 / 卡死 12h+24h） |
| §10 主动出击 | 832–952 | 13 / 14 | 除 A10.5 外全 ✅（14 创建前提 / Engaging-Returning / SallySeedGold=100） |
| §11 战利品/俘虏/金币 | 954–1012 | 8 | 是（Winner-only / 匹配俘虏 10 步 / 装备物品 fallback / 金币回流） |
| §12 首府每日俘虏 | 1013–1049 | 4 | 是（conformity =(level+1)×5 钳制 0–3 / 7 步） |
| §13 XP 与升级 | 1051–1109 | 8 / 9 | 除 A13.4 外全 ✅（baseXp =(level+1)×10 / 11 步升级 / 默认值） |
| §14 通用基类 | 1111–1190 | 11 | 是（12 项前置检查 / CurrentSettlement==home / Idle 24h/36h / 速度 +20% / 工资 0） |
| §15 vanilla 抑制 | 1192–1214 | 4 | 是（5 条件 + 4 副作用 + 易主切换） |
| §16 AI 接管 | 1216–1233 | 3 | 是（默认关闭 / 开启行为 / 关闭降级） |
| §17 对话拦截 | 1235–1243 | 2 | 是（4 类 ST 拦截 + vanilla 巡逻不走） |
| §18 字段索引 | 1245–1307 | 42 | 是（全部默认值与 GlobalConfig.cs 精确匹配） |
| §19 行为边界 | 1325–1336 | 8 | 是（首府仅城市 / 城堡参与后勤 / XP/俘虏仅首府 等） |

---

## 4. §20 REFACTOR_TODO 现状（R20.x，事实记录，不计 drift）

### R20.1 — §20 #1 helper 设计已分叉

`PartyEconomyHelper.cs` 在工作树中已添加（264 行，untracked），其[L17–L26 注释](SovereignTowns/src/Common/PartyEconomyHelper.cs:17)明确写：

> Sally / Transfer：短命任务，凭空塞 2-3 天食物，简化复杂度，无队伍资金。
> Patrol：终身户外巡逻，从首府所有者扣 2000 第纳尔作启动资金；用资金买食物 + 战利品卖掉补充资金；销毁时余款还首府所有者（自负盈亏）。

按你的"全部按文档来"指令，doc §20 #1 字面要求是「**由所有 ST 队伍共享**」 → 这个分叉设计是**未达成 §20 #1 验收标准**。

### R20.2 — 当前调用点分布

| 队伍 | 食物来源 | 资金来源 | 战利品处理 |
| --- | --- | --- | --- |
| 巡逻队 | `BuyFoodFromSettlement` ✅（自资金买） | `_teamFunds`（创建时扣 owner 2000；销毁时退） | `SellLootToSettlement` ✅（资金回流） |
| 出击队 | `GrantFoodForDays(party, 3f)` ❌（凭空塞） | 无 | 走 §11 集中处理（BattleLootHandler） |
| 调拨队 | `GrantFoodForDays(party, 3f)` ❌（凭空塞） | 无 | 不参与 |
| 征兵队 | `GrantFoodForDays(party, 3f)` ❌（凭空塞） | 无 | 不参与 |

### R20.3 — §20 #2 战利品集中处理废弃 — 完全没动

`BattleLootHandler.cs`（478 行）与 `BattleLootManager.cs`（105 行）仍在运行，符合 doc §11 描述。doc §20 #2 说「T1 完成后，所有 ST 队伍都走巡逻队风格的就地变现 / 自付薪资链路，§11 集中处理流程会被移除」。

→ R20.3 依赖 R20.1 完成。本轮 §20 #2 不应启动。

### R20.4 — §20 重构在 Phase 4 的具体计划草稿（待你裁决）

按"全部按文档来"，Phase 4 应：
1. **扩展 `PartyEconomyHelper` 为真正通用** — 让 Sally/Transfer/Recruiter 也支持"启动资金 → 卖战利品 → 买粮"闭环。
2. **`StPartyComponent` 增加 `_teamFunds` 字段** — 移到基类，4 子类全部继承。
3. **Sally/Transfer/Recruiter Dispatcher 创建时扣 home 所有者一笔 seed gold**（金额数值待你拍板，默认建议 = 现有 SallySeedGold 100 / TransferSeedGold 新增 / 现有 RecruiterSeedGold 1000）。
4. **§11 BattleLootHandler 简化为"queue → 队伍卖给最近自家城"**，统一走 helper 路径。
5. **Doc §0–§19 更新**：删除"3 天免费食物"措辞，改为"创建时扣 seed gold + 到 settlement 买粮"。

---

## 5. 行动指引（Phase 3 入口）

**默认按"全部按文档来"**：5 项 ❌ 全部走"改代码"。

但有 1 项例外建议（D2 / A7.10）请你裁决：
- **D2**：doc 措辞"无距离要求"不准确——代码已分场景实现（直接隶属村无距离，其他需 ≤100m），这个分场景设计更合理。建议改 doc 而非改代码。

其他 4 项 ❌（D1 / D3 / D4 / D5）建议无歧义走"改代码"。

⚠️ **裁决问题清单（Phase 3 启动前需你回复）**：

| Q | 问题 | 默认 |
| --- | --- | --- |
| Q-P2.1 | D1（招募第 3 类候选）— 改代码（加扫友军/中立村）还是改 doc 删去？ | 改代码 |
| Q-P2.2 | D2（征兵候选距离描述）— 改 doc 还是改代码？ | 改 doc（更合理） |
| Q-P2.3 | D3（巡逻被劫村响应）— 改代码补 GetDefenseTarget 还是改 doc 删？ | 改代码 |
| Q-P2.4 | D4（Sally 优先被劫村）— 改代码补分支还是改 doc 删？ | 改代码 |
| Q-P2.5 | D5（XP 城/堡 1.5/0.5）— 改代码补系数还是改 doc 删？ | 改代码 |
| Q-P2.6 | R20.x（§20 重构）放 Phase 4 处理，本阶段不动？ | 是 |
| Q-P2.7 | W1（TransferBranchToBranchPenalty 命名反差）放 Phase 4 候选还是不动？ | 不动（doc 已承认） |

— Phase 2 报告完
