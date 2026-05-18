# Phase 4 — T1 完成报告

> 日期：2026-05-18
> 重构项：doc §20 #1「统一队伍粮食与自资金逻辑」
> 执行方式：3 个 Wave 共 6 个 subagent + 主线程修复
> 最终验证：`dotnet build --no-incremental` 0 errors, 2 baseline warnings (CS8604 pre-existing)

---

## 1. 改动总览

### Wave 1 — 基类化（1 个 subagent，4 步）
- **T1.1** 基类 `StPartyComponent` 新增 `[SaveableField(12)] _teamFunds` + 6 个 public API（TeamFunds/Init/Buy/Sell/Refund/SetTeamFunds）+ abstract `GetExpenseCategoryForKind`
- **T1.2** `StPatrolPartyComponent` 删除自有 `_teamFunds` + 5 API（继承基类）
- **T1.3** 基类 `OnHourlyTick` 新增 `TryEconomicMaintenance` 步骤；Patrol Core 删除内部经济块
- **T1.4** 基类 `OnDestroyed` 新增 `TryRefundOnDestroy` 步骤；Patrol OnDestroyed override 删除

### Wave 2 — 三个 Dispatcher 改造（3 个并行 subagent）
- **T1.5** Sally：`SallyDispatcher` 玩家路径 SetTeamFunds + AI 路径 InitTeamFundsFromHomeOwner + 创建后 BuyFoodAtSettlement；删除 GrantFoodForDays
- **T1.6** Transfer：`ExpenseCategory.TransferSeed` 新增 + `GlobalConfig.TransferSeedGold=200` 新增 + `TransferDispatcher` 仿 Patrol 模式扣款 + 买粮；`StTransferPartyComponent` 删 GrantFoodForDays、`GetExpenseCategoryForKind()` 由 `Other` 改 `TransferSeed`
- **T1.7** Recruiter：`RecruitmentDispatcher` SetTeamFunds/InitTeamFundsFromHomeOwner + 创建后 BuyFoodAtSettlement；`StRecruiterPartyComponent.CreateForTown` 删 GrantFoodForDays

### Wave 3 — 收尾（2 个并行 subagent + 主线程修复）
- **T1.8** `BattleLootHandler.ProcessItems` 对 Sally early return（R3 缓解，T2 整段移除前的临时方案；俘虏处理保留）
- **T1.9** doc 9 处修订（§7/§8/§10/§11/§14/§18×2/§20）
- **Hotfix** Wave 3 T1.8 引入 CS8602 警告（`party?.PartyComponent is X` 让 compiler 推断后续路径可能 null），简化检查为 `party.PartyComponent is X`（参数非空），回到 baseline

---

## 2. 文件改动统计

| 文件 | 改动概要 |
| --- | --- |
| [StPartyComponent.cs](SovereignTowns/src/Parties/StPartyComponent.cs) | +156/-8（_teamFunds + 6 API + TryEconomicMaintenance + TryRefundOnDestroy + abstract method） |
| [StPatrolPartyComponent.cs](SovereignTowns/src/Parties/StPatrolPartyComponent.cs) | +10/-118（删字段 + API + Core 经济块 + OnDestroyed override；加 GetExpenseCategoryForKind） |
| [StSallyPartyComponent.cs](SovereignTowns/src/Parties/StSallyPartyComponent.cs) | +14/-4（add using Economy + override；其他 T1.5 Sally 改动在 dispatcher 而非 component） |
| [StTransferPartyComponent.cs](SovereignTowns/src/Parties/StTransferPartyComponent.cs) | +11/-5（add using + override + 删 GrantFoodForDays） |
| [StRecruiterPartyComponent.cs](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs) | +4/-3（override + 删 GrantFoodForDays in CreateForTown） |
| [SallyDispatcher.cs](SovereignTowns/src/SallyForth/SallyDispatcher.cs) | T1.5 改动 + 用户/linter 又做了进一步调整（保留） |
| [TransferDispatcher.cs](SovereignTowns/src/Transfer/TransferDispatcher.cs) | +28 行（seed gold 扣款 + BuyFoodAtSettlement，玩家扣款失败不回滚仍创建） |
| [RecruitmentDispatcher.cs](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs) | +13 行（玩家 SetTeamFunds / AI InitTeamFundsFromHomeOwner + BuyFoodAtSettlement） |
| [BattleLootHandler.cs](SovereignTowns/src/Battle/BattleLootHandler.cs) | +6 行（Sally early return + class XML remarks） |
| [GlobalConfig.cs](SovereignTowns/src/Configuration/GlobalConfig.cs) | +3 行（`TransferSeedGold = 200`） |
| [ModTreasury.cs](SovereignTowns/src/Economy/ModTreasury.cs) | +2 行（`ExpenseCategory.TransferSeed`） |
| [mod-behavior-guide.zh-CN.md](SovereignTowns/docs/mod-behavior-guide.zh-CN.md) | 9 处修订（§7/§8/§10/§11/§14/§18/§20） |

**总计**：12 个文件改动；约 +250 / -160 净行；其中 4 个 ST Component 文件 + 4 个 Dispatcher 文件 + 3 个 Config/Battle/Economy 文件 + 1 个 doc 文件。

---

## 3. 核心模型变化

**Before T1**：
- Patrol：自资金闭环（seed=2000，卖战利品入资金，销毁退款）
- Sally / Transfer / Recruiter：凭空塞 3 天免费食物（`GrantFoodForDays(party, 3f)`），ModTreasury 扣款入虚账（玩家），AI 不扣

**After T1**：
- 全部 4 类 ST 队伍**共享基类 `_teamFunds` 自资金闭环**：
  - 创建时：玩家氏族通过 `ModTreasury.Charge` → `SetTeamFunds(seedAmount)`；AI 走 `InitTeamFundsFromHomeOwner(seedAmount)` 从 home owner hero.Gold 扣
  - 创建后：基类 API `BuyFoodAtSettlement(party, settlement, 3f)` 用 vanilla SellItemsAction 真实购入食物
  - 每小时：基类 `TryEconomicMaintenance` 在 settlement.Town != null 时自动调用 `SellLootAtSettlement` + 食物 <1 天调用 `BuyFoodAtSettlement`
  - 销毁时：基类 `TryRefundOnDestroy` 退还剩余 `_teamFunds` 给 home 所有者（玩家走 ModTreasury.Refund 保账目；AI 走 hero.Gold 路径）

| 队伍 | Seed Gold | Source 字段 | ExpenseCategory |
| --- | --- | --- | --- |
| Patrol | 2000（硬编码常量） | `PatrolDispatcher.cs:174` | PatrolSeed |
| Sally | 100（`SallySeedGold`） | `GlobalConfig.SallySeedGold` | SallySeed |
| Recruiter | 1000（`RecruiterSeedGold`） | `GlobalConfig.RecruiterSeedGold` | RecruiterSeed |
| **Transfer** | **200（`TransferSeedGold`，T1 新增）** | `GlobalConfig.TransferSeedGold` | **TransferSeed（新）** |

---

## 4. R3 缓解（Sally 战利品双重处理）

**问题**：T1 完成后，Sally 物品同时被 `BattleLootHandler.ProcessItems`（旧集中处理）和基类 `TryEconomicMaintenance.SellLootAtSettlement`（新自资金路径）处理 → 双重卖。

**临时方案（T1.8）**：`BattleLootHandler.ProcessItems` 第一行加 `if (party.PartyComponent is StSallyPartyComponent) return;`。
- 俘虏处理（`ProcessMatchingPrisonersToCapital` / `SellNonMatchingPrisoners`）保留——Sally 俘虏仍走 §11 匹配 / 出售。
- Patrol 物品处理保留（与 Patrol 自资金共存，pre-existing 设计）。

**T2 时**：整个 BattleLootHandler.ProcessItems 移除，所有 ST 队伍都走基类自资金路径。

---

## 5. 已知未覆盖项（留 followup）

### F5 — Recruiter 长旅途粮食耗尽（R5 from plan card）
- Recruiter 跑多村招募，村庄无 Town → 中途无法补粮。
- 1000 seed gold 在 50 兵规模下约够 2 天 → 需要至少回首府或者其他自家 town 才能补粮。
- T1 不解决；记入 [phase4_followups.md](audits/phase4_followups.md)（待写）

### F6 — R4 "settlement 食物缺货" 兜底未实现
- 计划卡 §4.R4 提到加 `AllowFreeFoodFallback` 开关让 BuyFoodAtSettlement 返 0 时凭空塞 1 天保命粮。
- T1.5/T1.6/T1.7 均未实现此兜底（直接接受 BuyFoodAtSettlement 可能返 0）。
- 影响：极端边界场景（settlement 库存清空）下队伍创建后无粮，下次到下个 settlement 才会补。
- 计划：观察实际游戏中是否触发；若触发再 patch。记入 followups。

### F7 — `_teamFunds` save 槽位破坏
- 旧存档中 `StPatrolPartyComponent._teamFunds`（slot 22）数据被丢弃；新 slot 12（base）默认 0。
- 按 [CLAUDE.md](CLAUDE.md) "pre-release rapid iteration" 政策允许。
- 玩家若加载旧档：Patrol 队伍的 _teamFunds 变 0，下次到 settlement 无法 buy food（但能 sell loot 重新积累资金）。

---

## 6. 验证

### Build（每 Wave 通过）
- Wave 1 完成后：0 errors, 2 warnings ✅
- Wave 2 完成后：0 errors, 2 warnings ✅
- Wave 3 完成后（含 hotfix）：0 errors, 2 warnings ✅

### 运行时验证（**待你在游戏中测**）
- 见 [phase4_t1_plan.md §5.3](audits/phase4_t1_plan.md) 验证清单
- 建议先开新存档：避免 F7 旧存档 _teamFunds 丢失带来的迷惑

### 静态测试
按你的指令已删除 `tests/static-regression.ps1`，无 regression 测试。

---

## 7. 下一步

**T2（doc §20 #2 战利品集中处理废弃）已经具备启动条件**：
- 所有 4 类 ST 队伍现在走自资金路径，§11 集中处理已经"对 Sally 失效（T1.8 early return）+ 对 Patrol 重复（pre-existing 状态）"
- T2 工作：
  1. 完全移除 `BattleLootHandler.ProcessItems`（俘虏处理可能移到独立 `BattlePrisonerHandler` 或继续保留——视 doc §11.4/§11.5 是否仍需要"匹配俘虏招首府"和"非匹配卖最近自家城"）
  2. doc §11 整章重写或标 deprecated
  3. doc §20 #2 标完成

T2 计划卡可待你点头后输出。或者直接进 Phase 5（健壮性扫描），把 T2 推迟到下一个迭代周期。

— T1 完成报告完
