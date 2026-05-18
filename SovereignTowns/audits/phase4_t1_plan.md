# Phase 4 — T1 重构计划卡：统一队伍粮食与自资金逻辑

> doc §20 #1（doc:1342）：「提取巡逻队的资金 / 粮食逻辑为可复用组件，由所有 ST 队伍共享」。
> **本计划卡需用户点头才能动手。**

---

## 1. 当前实现位置（事实勘验）

### 1.1 Patrol — 已有完整自资金闭环（标杆）

| 组件 | 实现 |
| --- | --- |
| 私有字段 | [StPatrolPartyComponent.cs:38](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:38) `[SaveableField(22)] private int _teamFunds;` |
| 默认值 | [StPatrolPartyComponent.cs:39](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:39) `const int InitialTeamFundsDefault = 2000;` |
| API（5 个方法） | [StPatrolPartyComponent.cs:67–101](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:67): `TeamFunds`/`InitTeamFundsFromHomeOwner`/`BuyFoodAtSettlement`/`SellLootAtSettlement`/`RefundTeamFundsToOwner`/`SetTeamFunds` |
| Dispatcher 扣款 | [PatrolDispatcher.cs:174–200](SovereignTowns/src/Patrol/PatrolDispatcher.cs:174): 玩家走 ModTreasury.Charge + SetTeamFunds；AI 走 InitTeamFundsFromHomeOwner |
| 创建后买粮 | [PatrolDispatcher.cs:202](SovereignTowns/src/Patrol/PatrolDispatcher.cs:202): `BuyFoodAtSettlement(created, settlement, 3f)` |
| Core 内维护 | [StPatrolPartyComponent.cs:246–268](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:246): 在 settlement 内卖战利品 + 食物 < 1 天买 3 天 |
| OnDestroyed 退款 | [StPatrolPartyComponent.cs:192–217](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:192): 玩家走 ModTreasury.Refund；AI 走 RefundTeamFundsToOwner |

### 1.2 Sally / Transfer / Recruiter — 都走「凭空塞食物」

| 队伍 | 创建时扣款 | 食物来源 | 销毁时退款 |
| --- | --- | --- | --- |
| **Sally** | [SallyDispatcher.cs:253–265](SovereignTowns/src/SallyForth/SallyDispatcher.cs:253) 玩家扣 `SallySeedGold=100` 到 ModTreasury（**不入 _teamFunds**） | [SallyDispatcher.cs:303](SovereignTowns/src/SallyForth/SallyDispatcher.cs:303) `GrantFoodForDays(party, 3f)` 凭空塞 | 无 |
| **Transfer** | 无 | [StTransferPartyComponent.cs:102](SovereignTowns/src/Parties/StTransferPartyComponent.cs:102) `GrantFoodForDays(party, 3f)` 凭空塞 | 无 |
| **Recruiter** | [RecruitmentDispatcher.cs:176](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs:176) 玩家扣 `RecruiterSeedGold=1000` 到 ModTreasury（**不入 _teamFunds**） | [StRecruiterPartyComponent.cs:172](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:172) `GrantFoodForDays(party, 3f)` 凭空塞 | 无 |

### 1.3 Helper 已就绪

[PartyEconomyHelper.cs](SovereignTowns/src/Common/PartyEconomyHelper.cs)（264 行）已提供全部 8 个工具方法：`GetCheapestFood`、`EstimateFoodForDays`、`GrantFoodForDays`、`BuyFoodFromSettlement`、`SellLootToSettlement`、`FoodDaysRemaining`、`ChargeHero`、`RefundHero`。

> 注释 [L17–L26](SovereignTowns/src/Common/PartyEconomyHelper.cs:17) 自承"Sally / Transfer 仍凭空塞；Patrol 才走自资金"——T1 的目标正是消除这条分叉。

---

## 2. 目标实现

### 2.1 基类化 `_teamFunds`（核心架构变化）

**移动**：`_teamFunds` + 5 个 API 方法从 [StPatrolPartyComponent.cs:38,67–101](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:38) 上提到 [StPartyComponent.cs](SovereignTowns/src/Parties/StPartyComponent.cs)。

**SaveableField 槽位**：基类 [10,20) 槽位已用 10（_homeSettlement）、11（_initialMemberCount）。`_teamFunds` 占 **12**（空槽）。

**StPatrolPartyComponent**：删除 line 38 + line 39 默认值常量（移到调用方）+ line 67–101 整段 API（继承基类）；SaveableField(22) 槽位**废弃但保留**为占位 `object` 以避免反序列化崩溃……
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
**Save 策略**（按 [CLAUDE.md](CLAUDE.md) "pre-release rapid iteration"）：**允许破坏存档**。直接删除 SaveableField(22)。bump `CurrentConfigVersion`（如果存档系统对它有依赖）或 `SaveBaseId`（若需要）。玩家被告知"删 global.json 重启"。

### 2.2 基类 OnHourlyTick 增加经济维护步骤

[StPartyComponent.cs:66–119 OnHourlyTick](SovereignTowns/src/Parties/StPartyComponent.cs:66) 在 IsAtHome 检查之后、`OnHourlyTickCore` 之前插入新的"经济维护"步骤：

```csharp
if (IsAtHome(self)) { OnArrivedHome(self); return; }

// 新增：经济维护（doc §20 #1 — 所有 ST 队伍共享）
TryEconomicMaintenance(self);

OnHourlyTickCore(self, capital!);
```

`TryEconomicMaintenance` 实现：

```csharp
private void TryEconomicMaintenance(MobileParty self)
{
    var atSettlement = self.CurrentSettlement;
    if (atSettlement == null || atSettlement.Town == null) return;
    
    // 1) 卖战利品（无论食物状态）
    try
    {
        int gained = SellLootAtSettlement(self, atSettlement);
        if (gained > 0)
            Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' sold loot at '{atSettlement.Name}' +{gained}d (funds={_teamFunds})");
    }
    catch (Exception ex) { Logger.Warn($"sell-loot tick threw: {ex.Message}"); }
    
    // 2) 食物 < 1 天 → 买 3 天
    try
    {
        float daysLeft = PartyEconomyHelper.FoodDaysRemaining(self);
        if (daysLeft < 1f && _teamFunds > 0)
        {
            int spent = BuyFoodAtSettlement(self, atSettlement, 3f);
            if (spent > 0)
                Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' food low ({daysLeft:F1}d) → bought at '{atSettlement.Name}' for {spent}d (funds={_teamFunds})");
        }
    }
    catch (Exception ex) { Logger.Warn($"food top-up tick threw: {ex.Message}"); }
}
```

Patrol Core 内的 L246–275 经济块**删除**（已上移到基类）。

### 2.3 基类 OnDestroyed 增加退款步骤

[StPartyComponent.cs:146–174 OnDestroyed](SovereignTowns/src/Parties/StPartyComponent.cs:146) 在 base behavior 之前插入退款逻辑：

```csharp
public virtual void OnDestroyed(MobileParty self, PartyBase? destroyer)
{
    // 新增：退款（doc §20 #1 — 所有 ST 队伍共享）
    TryRefundOnDestroy(self);
    
    // 原 rescue 兵员逻辑
    try { ... } catch { ... }
}

private void TryRefundOnDestroy(MobileParty self)
{
    if (_teamFunds <= 0) return;
    try
    {
        var refundClan = self.ActualClan ?? HomeSettlementOrNull?.OwnerClan;
        if (CapitalRegistry.ShouldChargeClan(refundClan))
        {
            int toRefund = _teamFunds;
            _teamFunds = 0;
            ModTreasury.Refund(GetExpenseCategoryForKind(), toRefund, $"{GetType().Name}_destroyed home={HomeSettlementOrNull?.StringId ?? "null"}");
            if (toRefund > 0) Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' OnDestroyed — refunded {toRefund}d via ModTreasury (player clan)");
        }
        else
        {
            int refunded = RefundTeamFundsToOwner();
            if (refunded > 0) Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' OnDestroyed — refunded {refunded}d to AI owner");
        }
    }
    catch (Exception ex) { Logger.Warn($"OnDestroyed refund threw: {ex.Message}"); }
}

protected abstract ExpenseCategory GetExpenseCategoryForKind();
```

子类各自实现 `GetExpenseCategoryForKind` 返回对应类目（PatrolSeed / SallySeed / RecruiterSeed / 新增 TransferSeed）。

**StPatrolPartyComponent.OnDestroyed override 删除**（继承基类）。

### 2.4 三个 Dispatcher 改造（Sally / Transfer / Recruiter）

**Sally** ([SallyDispatcher.cs:253–303](SovereignTowns/src/SallyForth/SallyDispatcher.cs:253))：
- 删除 [L303 GrantFoodForDays](SovereignTowns/src/SallyForth/SallyDispatcher.cs:303)
- ModTreasury.Charge 后增加 `sallyParty.PartyComponent.SetTeamFunds(InitialSallyGold)` 或 AI 走 `InitTeamFundsFromHomeOwner(InitialSallyGold)`
- 创建后调 `BuyFoodAtSettlement(sallyParty, settlement, 3f)`

**Transfer** ([StTransferPartyComponent.cs:79–102 + TransferDispatcher.cs](SovereignTowns/src/Parties/StTransferPartyComponent.cs:79))：
- 新增 `GlobalConfig.Thresholds.TransferSeedGold` 默认 200
- TransferDispatcher 创建前 Charge + 创建后 BuyFoodAtSettlement
- 删 [StTransferPartyComponent.cs:102 GrantFoodForDays](SovereignTowns/src/Parties/StTransferPartyComponent.cs:102)

**Recruiter** ([RecruitmentDispatcher.cs:176 + StRecruiterPartyComponent.cs:172](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs:176))：
- ModTreasury.Charge 后 `recruiterParty.PartyComponent.SetTeamFunds(RecruiterSeedGold)` 或 AI 走 `InitTeamFundsFromHomeOwner(RecruiterSeedGold)`
- 删 [StRecruiterPartyComponent.cs:172 GrantFoodForDays](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:172)
- 创建后 BuyFoodAtSettlement

### 2.5 doc 更新

doc §7 / §8 / §10 中"获得约 3 天免费食物"措辞改为"创建时扣 seed gold → 在 settlement 买 3 天食物"。具体行号：
- doc:441 征兵队"免费获得约 3 天食物"
- doc:653 调拨队"免费获得约 3 天食物"
- doc:923 出击队"获得约 3 天免费食物"

doc §11 第 956 行可加注："T1 完成后所有 ST 队伍走自资金 + 自卖战利品；§11 集中处理仅用于俘虏。"（§11 的物品出售部分会被 T2 整段移除。）

### 2.6 ExpenseCategory 新增项

[Economy/ModExpenseLedger.cs](SovereignTowns/src/Economy/ModExpenseLedger.cs) 中 `ExpenseCategory` enum 新增 `TransferSeed`（如果还没有）。其他三类（PatrolSeed/SallySeed/RecruiterSeed）应已存在。

---

## 3. 调用方迁移清单

按修改文件展开（每个改动独立 commit）：

| 文件 | 改动概要 | 行数估算 |
| --- | --- | --- |
| [StPartyComponent.cs](SovereignTowns/src/Parties/StPartyComponent.cs) | 增 `_teamFunds`/`InitTeamFundsFromHomeOwner`/`BuyFoodAtSettlement`/`SellLootAtSettlement`/`RefundTeamFundsToOwner`/`SetTeamFunds`/`TeamFunds`，新 `TryEconomicMaintenance`/`TryRefundOnDestroy`/`GetExpenseCategoryForKind`（abstract） | +80 |
| [StPatrolPartyComponent.cs](SovereignTowns/src/Parties/StPatrolPartyComponent.cs) | 删 _teamFunds 字段与 5 个 API 方法、删 Core 内 L246–275 经济块、删 OnDestroyed override、增 GetExpenseCategoryForKind() 返回 PatrolSeed | -90 / +5 |
| [StSallyPartyComponent.cs](SovereignTowns/src/Parties/StSallyPartyComponent.cs) | 增 GetExpenseCategoryForKind() 返回 SallySeed | +5 |
| [StTransferPartyComponent.cs](SovereignTowns/src/Parties/StTransferPartyComponent.cs) | 删 L102 GrantFoodForDays，增 GetExpenseCategoryForKind() 返回 TransferSeed | +5 / -1 |
| [StRecruiterPartyComponent.cs](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs) | 删 L172 GrantFoodForDays，增 GetExpenseCategoryForKind() 返回 RecruiterSeed | +5 / -1 |
| [SallyDispatcher.cs](SovereignTowns/src/SallyForth/SallyDispatcher.cs) | 替换 ModTreasury.Charge 路径为 SetTeamFunds/InitTeamFundsFromHomeOwner；删 L303 GrantFoodForDays；增创建后 BuyFoodAtSettlement | +10 / -3 |
| [TransferDispatcher.cs](SovereignTowns/src/Transfer/TransferDispatcher.cs) | 创建前 Charge + 创建后 BuyFoodAtSettlement | +20 |
| [RecruitmentDispatcher.cs](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs) | 替换 ModTreasury.Charge 路径为 SetTeamFunds/InitTeamFundsFromHomeOwner；删调用 GrantFoodForDays；增 BuyFoodAtSettlement | +10 / -3 |
| [PatrolDispatcher.cs](SovereignTowns/src/Patrol/PatrolDispatcher.cs) | 改 `SetTeamFunds`/`InitTeamFundsFromHomeOwner` 调用方为基类版本（API 签名不变，路径相同） | 0 净改动 |
| [GlobalConfig.cs](SovereignTowns/src/Configuration/GlobalConfig.cs) | 新增 `TransferSeedGold` 字段默认 200 | +3 |
| [ModExpenseLedger.cs](SovereignTowns/src/Economy/ModExpenseLedger.cs) | enum 新增 `TransferSeed`（如果还没） | +1 |
| [SovereignTownsTypeDefiner.cs](SovereignTowns/src/SaveSystem/SovereignTownsTypeDefiner.cs) | 无需改动（SaveableField 槽位由 vanilla 自动通过反射注册） | 0 |
| [PartyEconomyHelper.cs](SovereignTowns/src/Common/PartyEconomyHelper.cs) | 注释更新：删去"Sally/Transfer 凭空塞、Patrol 自资金"的分叉描述 | +0 / -10 |
| [docs/mod-behavior-guide.zh-CN.md](SovereignTowns/docs/mod-behavior-guide.zh-CN.md) | §7/§8/§10/§11 措辞更新 | ~10 处替换 |

**总计**：约 150 行净改动，13 个文件触动。

---

## 4. 风险与回滚点

### 风险

**R1 — Save 破坏**（已知 + 可接受）：现有玩家存档的 StPatrolPartyComponent._teamFunds 会丢失。按 [CLAUDE.md](CLAUDE.md) "pre-release rapid iteration" 政策允许；玩家被告知"删 global.json"。

**R2 — Transfer 经济变化**：Transfer 现在变成"必须有钱"才能调拨。AI 氏族领主金币不足时调拨力度下降。
- **缓解**：AI 走 `InitTeamFundsFromHomeOwner(seed)` 自动取 `min(seed, owner.Gold)`；即使为 0 也能创建（_teamFunds=0 但任务可执行，只是没钱买粮，仍能用 vanilla foraging 维持）。
- **缓解**：`TransferSeedGold=200` 是低门槛，AI 一般负担得起。

**R3 — Sally 战利品双重处理**：T1 期间 Sally 的战利品同时被 BattleLootHandler（§11 集中处理）和 Sally 自身基类的 `TryEconomicMaintenance.SellLootAtSettlement` 处理。
- **影响**：可能出现"卖了又被送"。
- **缓解**：T1 期间，**临时禁用 BattleLootHandler 对 Sally 物品的处理**（在 [BattleLootHandler.cs:256–343](SovereignTowns/src/Battle/BattleLootHandler.cs:256) ProcessItems 中加 `if (party.PartyComponent is StSallyPartyComponent) return;`）；俘虏处理保留（仍走 §11 匹配 / 出售）。T2 时彻底移除。

**R4 — 食物购入实际可行性**：BuyFoodFromSettlement 依赖 vanilla SellItemsAction + 市场食物库存。极端情况下 settlement 食物缺货则没买到。
- **影响**：Sally/Transfer/Recruiter 创建后可能没有食物（与 Patrol 现有风险一致）。
- **缓解**：保留 `GrantFoodForDays` 作为兜底——若 `BuyFoodAtSettlement` 返回 0（无食物可买）则凭空塞 1 天保命粮。这是与"非作弊基调"的妥协，建议加配置开关 `AllowFreeFoodFallback=true`。

**R5 — Recruiter 旅途中粮食耗尽**：Recruiter 走多村招募，到村庄不能补粮（村庄无 Town）。可能在旅途中粮食耗尽。
- **影响**：饿肚子兵员每日减员。
- **缓解**：保留 vanilla 默认"无粮则减员"行为；Recruiter 的 1000 seed gold 应当足够支撑 10+ 天（按每天 ~10 denar / 兵 × 50 兵 = 500 / 天，1000d 够 2 天……不够）。
- **决策**：**Phase 4-T1 不解决此问题**，作为 phase4_followup 记录。Recruiter 经济模型可能需要单独迭代。

### 回滚点

每个步骤独立 commit → `git revert` 单个 commit 回滚。建议 commit 顺序：

1. T1.1 — Base 增加 _teamFunds 与 API 方法（保留 Patrol 子类 API 临时兼容）
2. T1.2 — Patrol 子类切换到基类 API（删除子类版本）
3. T1.3 — Base OnHourlyTick 增加 TryEconomicMaintenance；Patrol Core 删除内部经济块
4. T1.4 — Base OnDestroyed 增加 TryRefundOnDestroy；Patrol 子类删除 override
5. T1.5 — Sally Dispatcher + Component 改造
6. T1.6 — Transfer Dispatcher + Component 改造 + GlobalConfig 新字段
7. T1.7 — Recruiter Dispatcher + Component 改造
8. T1.8 — BattleLootHandler 临时禁用 Sally 物品处理（R3 缓解）
9. T1.9 — doc §7/§8/§10 措辞更新

---

## 5. 验证方式

### 5.1 编译验证（每步独立）

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

每步 commit 前必须 0 errors。

### 5.2 静态测试

按用户指令已删除 `tests/static-regression.ps1`。本轮无自动 regression。

### 5.3 运行时验证（用户手测，T1 完成后整体跑）

游戏内验证清单：

1. **新建战役**：随便选个氏族（带 1 城 1 堡），等到自动派出 Patrol/Sally/Transfer/Recruiter。
2. **检查 ModLogs**：搜索 `team funds initialized`、`bought ... at ...`、`sold loot at ...`、`refunded ... to`。每种 ST 队伍都应有这些日志。
3. **检查 hero.Gold**：派遣前后玩家氏族领主金币变化；销毁后部分退款。
4. **食物自动补**：让队伍跑到郊外 5 天后食物消耗，到下个 settlement 自动补到 3 天。
5. **战利品**：Sally / Patrol 打完仗有战利品 → 下次停 settlement 自动卖掉 → _teamFunds 增加。
6. **AI 路径**：AI 氏族（开启 AI 接管）同样跑通；AI 领主金币不足时 InitTeamFunds 返回 0 但队伍仍创建。
7. **存档**：旧存档（带 StPatrolPartyComponent._teamFunds=2000）加载——Patrol 的 _teamFunds 应丢失变 0；后续运行不应崩溃。

### 5.4 表征测试（characterization tests）

由于无单元测试，不写 characterization。本轮重构强变外部可观察行为（doc §7/§8/§10 措辞已变化），不需要"保持现状"的锁住测试。

---

## 6. 关键裁决问题

| Q | 问题 | 建议默认 |
| --- | --- | --- |
| Q-T1.1 | `TransferSeedGold` 默认值（doc 未指明）？ | 200 第纳尔（短命任务、低门槛） |
| Q-T1.2 | `AllowFreeFoodFallback` 配置开关（R4 缓解）？ | 加，默认 true（兜底凭空塞 1 天保命粮） |
| Q-T1.3 | Recruiter 长旅途粮食耗尽（R5）？ | 本 T1 不解决，记入 phase4_followups |
| Q-T1.4 | R3 的 BattleLootHandler 临时禁用 Sally 物品处理 — 在 T1 内做还是 T2 做？ | T1 做（T2 才能彻底移除集中处理） |
| Q-T1.5 | doc §11 整段是否在 T1 标"deprecated"还是等 T2 完成？ | T1 标"将在 T2 移除"，T2 真正删 |
| Q-T1.6 | F1（招募 100→200 二轮 fallback dead code）顺手清理还是单独？ | T1 不做，T2 / Phase 5 顺手 |

---

## 7. 工作量估算

- 步骤 T1.1 基类化：30 分钟（移动 + 测试）
- 步骤 T1.2 Patrol 适配：15 分钟
- 步骤 T1.3 OnHourlyTick 集成：20 分钟
- 步骤 T1.4 OnDestroyed 集成：20 分钟
- 步骤 T1.5 Sally：30 分钟
- 步骤 T1.6 Transfer：40 分钟（含新 GlobalConfig 字段）
- 步骤 T1.7 Recruiter：30 分钟
- 步骤 T1.8 BattleLootHandler 临时禁用：15 分钟
- 步骤 T1.9 doc 措辞更新：20 分钟

**总计约 3 小时实施 + 每步 build/log check。**

---

## 8. 启动前确认

如果你点头，我会按上述 9 步顺序执行，每步：
1. 修改对应文件
2. 跑 `dotnet build` 验证 0 errors
3. 简短汇报"diff 摘要 + build OK"
4. 进入下一步

中间任一步失败立即停下汇报。

— T1 计划卡完
