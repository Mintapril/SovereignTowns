# Phase 4 — T2 完成报告

> 日期：2026-05-18
> 重构项：doc §20 #2「战利品集中处理逻辑废弃」
> 执行方式：主线程直接修改（范围收敛，无需 subagent）
> 最终验证：`dotnet build --no-incremental` 0 errors, 2 baseline warnings

---

## 1. 改动概览

### 1.1 代码删除（净 -700 行）

**整文件删除**：
- [SovereignTowns/src/Battle/BattleLootHandler.cs](deleted)（478 行）
- [SovereignTowns/src/Battle/BattleLootManager.cs](deleted)（105 行）
- `SovereignTowns/src/Battle/` 目录（删空后 rmdir）

**字段/引用删除**：
- [SovereignTownsCampaignBehavior.cs](SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs)：
  - L6 `using SovereignTowns.Battle;` 移除
  - L41 `private BattleLootManager? _battleLootManager;` 字段移除
  - L196 实例化 `new BattleLootManager(_capitalRegistry)` 移除
  - L411-428 `OnMapEventEnded` 简化（不再调 `_battleLootManager?.OnMapEventEnded(mapEvent)`）
- [GlobalConfig.cs](SovereignTowns/src/Configuration/GlobalConfig.cs) `EnabledFeatures` 类：
  - `AutoRecruitMatchingPrisoners`（默认 true）
  - `AutoSellNonMatchingPrisoners`（默认 true）
  - `AutoSellLoot`（默认 true）
  - 3 字段 + XML 注释一并删除
- [WebUI/index.html](SovereignTowns/SovereignTowns/WebUI/index.html)：
  - 3 个 `featureSwitches` entry 删除（战利品：招募匹配俘虏 / 出售非匹配俘虏 / 出售装备物品）

### 1.2 doc 更新（11 处修订）

| # | 章节 | 改动 |
| --- | --- | --- |
| D1 | §3 全局开关表 | 删除 3 行（战利品：招募匹配俘虏 / 出售非匹配俘虏 / 出售装备物品） |
| D2 | §7 doc:429 | 征兵 seed gold 措辞从"`RecruiterSeedGold`，默认 1000" → 4 类统一 `DefaultSeedGold=2000`，AI 路径说明 |
| D3 | §7 doc:440 | 征兵创建后买粮：1000 → 2000 |
| D4 | §8 doc:652 | 调拨创建后买粮：200 → 2000，加"调拨队**不**沿途补粮" |
| D5 | §9 doc:714-744 | 巡逻启动资金与粮食小节大幅精简：移除重复细节，引向 §14 |
| D6 | §10 doc:915 | 出击 seed gold：`SallySeedGold` 100 → `DefaultSeedGold=2000`，更新失败回滚措辞 |
| D7 | §10 doc:923 | 出击创建后买粮：100 → 2000，加"出击队**不**沿途补粮" |
| D8 | §11 整章替换 | 旧"集中处理"流程 4 小节（匹配俘虏 / 非匹配出售 / 装备出售 / 金币回流）整章替换为废弃说明：俘虏处理退化为 `PartyPrisonerCap` 随机踢出 |
| D9 | §14 队伍资金小节 | 升级到"T1+T2 共享"：加 `DefaultSeedGold=2000`、`ShouldReplenishFoodEnRoute` 控制 Sally/Transfer 不补粮、`TrySeedAndBuyInitialFood` helper |
| D10 | §18 字段索引表 | 删除 `RecruiterSeedGold` 和 `SallySeedGold` 两行（已从 config 删除）；"启动资金"小表合并为单行"4 类 ST 队伍统一启动资金 = 2000" |
| D11 | §19 行为边界 | 第 6 条更新为统一 2000 措辞 |
| D12 | §20 #2 重构待办 | 标删除线 + "(T2 已完成 2026-05-18)" + 重构内容摘要 |

---

## 2. 行为变化

### 战利品处理（物品/装备）

**Before T2**：
- `BattleLootHandler.ProcessItems` 在 MapEventEnded 时立即处理：找最近自家 town → `SellItemsAction` 卖物品 → 金币给 hero / party
- Patrol/Sally 双重路径：BattleLootHandler 处理一次 + 基类 `TryEconomicMaintenance.SellLootAtSettlement` 在下次到 settlement 再尝试一次

**After T2**：
- 唯一路径：基类 `TryEconomicMaintenance.SellLootAtSettlement` 在每小时 settlement.Town 内卖入 `_teamFunds`
- 物品在 ItemRoster 累积直到下次到达 town/castle 才卖；旅途中无变现机会（行为已与 Patrol 现状一致）

### 俘虏处理

**Before T2**：
- 匹配规则的俘虏 → 招入首府 garrison（功能丢失）
- 非匹配俘虏 → 卖到最近自家 town（功能丢失）

**After T2**：
- 俘虏占用 `PrisonRoster` 槽位，没有自动招募/出售逻辑
- 超过 `PartyPrisonerCap`（默认 30）时由 `TryEnforcePrisonerCap` 随机踢出非英雄俘虏
- 玩家若想用俘虏，可手动从 ST 队伍接管或等待 ST 队伍解散（解散时 prisoners 也归还）

### 队伍金币回流

**Before T2**：
- `party.PartyTradeGold` 销毁前转移到首府 Clan Leader（或 MainHero）

**After T2**：
- `party.PartyTradeGold` 不再被主动处理；vanilla 默认它跟着 party 一起销毁
- 但 `_teamFunds`（mod 内部账）在 `TryRefundOnDestroy` 中按账目对称退还首府 Clan Leader（玩家走 ModTreasury.Refund；AI 走 hero.Gold）
- 净影响：vanilla `PartyTradeGold` 可能少量丢失（短命任务难积累），但 `_teamFunds` 通过 helper + 战利品出售有完整经济闭环

---

## 3. 文件改动统计（仅 T2 范围）

| 文件 | 改动 |
| --- | --- |
| [BattleLootHandler.cs](deleted) | **整文件删除**（478 行） |
| [BattleLootManager.cs](deleted) | **整文件删除**（105 行） |
| `Battle/` 目录 | **rmdir** |
| [SovereignTownsCampaignBehavior.cs](SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs) | -5（移除 using/字段/实例化/调用） |
| [GlobalConfig.cs](SovereignTowns/src/Configuration/GlobalConfig.cs) | -8（3 字段 + 注释） |
| [WebUI/index.html](SovereignTowns/SovereignTowns/WebUI/index.html) | -3（3 featureSwitch entry） |
| [docs/mod-behavior-guide.zh-CN.md](SovereignTowns/docs/mod-behavior-guide.zh-CN.md) | 11 处修订（删 §11 旧 4 小节 ~50 行 + §3/§7/§8/§9/§10/§14/§18/§19/§20 多处更新） |

**净行数变化**：约 -700 / +50（其中代码净删 ~700，doc 净改 ~50）。

---

## 4. 验证

### Build
- T2 执行中首次 build：fail（残留 `using SovereignTowns.Battle;`）→ 即修
- T2 最终 build：0 errors, 2 pre-existing CS8604 warnings ✅

### 运行时验证（待你在游戏中测）

**重点验证场景**：
1. 巡逻队 / 出击队战斗后是否还能正常卖战利品（应在下次到 settlement.Town 时自动卖入 `_teamFunds`）
2. 俘虏在 ST 队伍 PrisonRoster 中是否正确累积到 30 上限然后被踢出
3. ModLogs 不应再出现 `BattleLootHandler` / `BattleLootManager` 日志
4. WebUI 的功能开关页面不应再显示 3 个战利品 toggle

---

## 5. T1 + T2 累计后状态

### 共享基类基础设施
- `StPartyComponent.DefaultSeedGold = 2000` 基类常量
- `StPartyComponent._teamFunds` 基类 SaveableField(12)
- 6 个公共 API（`TeamFunds` / `InitTeamFundsFromHomeOwner` / `BuyFoodAtSettlement` / `SellLootAtSettlement` / `RefundTeamFundsToOwner` / `SetTeamFunds`）
- 基类 Template Method 集成：`OnHourlyTick` → `TryEconomicMaintenance`；`OnDestroyed` → `TryRefundOnDestroy`
- 基类静态 helper：`TrySeedAndBuyInitialFood`（4 个 Dispatcher 统一调用）
- 基类虚属性：`ShouldReplenishFoodEnRoute`（Patrol/Recruiter=true，Sally/Transfer=false）

### 4 个 Dispatcher 统一模式
```csharp
if (party.PartyComponent is SubclassType sc)
{
    sc.SnapshotInitialMembers(party);
    if (!StPartyComponent.TrySeedAndBuyInitialFood(
        sc, party, originSettlement,
        ExpenseCategory.XxxSeed, originSettlement.OwnerClan,
        $"xxx_seed home={originSettlement.StringId}"))
    {
        TroopTransferHelper.TransferBackToGarrison(party.MemberRoster, garrison.MemberRoster);
        PartyMergeService.Instance.DestroyAndUntrack(party, "seed failed rollback", deferIfInMapEvent: false);
        return;
    }
}
```

### 已删除的部分（之前为重复 / 冗余）
- `BattleLootHandler` + `BattleLootManager`（T2）
- `GlobalConfig.SallySeedGold` / `RecruiterSeedGold` / `TransferSeedGold`（T1 重整）
- `GlobalConfig.AutoRecruitMatchingPrisoners` / `AutoSellNonMatchingPrisoners` / `AutoSellLoot`（T2）
- WebUI 5 个 thresholdSpec / featureSwitch entry（T1 重整 + T2）
- `StPatrolPartyComponent` 的私有 `_teamFunds` + 5 个 API 方法 + OnDestroyed override（T1）
- `PartyEconomyHelper.GrantFoodForDays` 仍存在但**不再被任何 Dispatcher 调用**（残留 API，未删除——可选 Phase 5 清理）

---

## 6. 留作 Phase 5 处理（followup）

| ID | 项 | 说明 |
| --- | --- | --- |
| F1（已知） | RankCandidates 100→200 二轮 fallback dead code | T1 d2 移除距离过滤后，调用方两轮返回相同结果 |
| F2（已知） | 2 个 pre-existing CS8604 警告 | BaseSettlementVisitScheduler.cs:120 + PatrolDispatcher.cs:92 |
| F5（已知） | Recruiter 长旅途粮食耗尽 | 1000d seed 在 50 兵规模下约 2 天就花完 — 现在 2000 翻倍但仍可能不够 |
| F6（已知） | settlement 食物缺货兜底未实现 | BuyFoodAtSettlement 返 0 时无凭空塞 1 天保命粮 fallback |
| F7（已知） | _teamFunds slot 12 旧存档丢失 | CLAUDE.md 已允许 |
| F8（T2 新） | PartyTradeGold 不再处理 | 短命任务难积累；可能少量金币丢失 |
| F9（T2 新） | PartyEconomyHelper.GrantFoodForDays 残留 API | 无调用方，可选清理 |
| F10（T2 新） | 俘虏功能完全丢失 | 玩家若需"自动招募匹配俘虏"，重构周期可重新加入到基类 `TryEconomicMaintenance` |

---

## 7. 下一步

进 **Phase 5（健壮性扫描）**。覆盖：
- 错误处理 / 边界值 / 空值 / I/O 失败
- F1–F10 followup 项的逐项裁决
- 全局复审

— T2 完成报告完
