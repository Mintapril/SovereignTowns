# B1 — 接通已有链路（盲区修复批次 1）

**日期**：2026-05-13
**适用版本**：SovereignTowns v0.0.1，对应 Bannerlord v1.3.15
**范围**：盲区 #2 / #1.B / #6.B / #7（4 项；原 9 项中其余 5 项归 B2–B4）
**前置 verify 结论**：原盲区 #1 主链路 + #6 SallyForth feature 已在主分发中接通，本批次**不动**它们。

## 1. 目标

让"manager 已就绪、调用方/读取点缺失"的 4 处链路真正生效：
1. LLM advice 不再只进日志，而是作为一条 `GarrisonDecision` 参与决策派发。
2. 规则引擎产出的 `RequestTransferIn` intent 不再被 `MVP 3 fallback` 丢弃，能真正触发跨城调拨。
3. `RequestDisbandExcess` intent 不再被丢弃，能真正裁撤超额驻军（"退伍回乡"语义）。
4. `AlertLowFood` intent 不再仅是 `Logger.Warn`，能真正暂停所有招募入口。

## 2. 非目标（明确划出 B1 之外）

- 不动 LLM 的"建议优先级 vs 规则优先级"全局架构；advice 与规则**共存**，三重校验后的 advice 与规则同台 sort。
- 不改 `GarrisonTransferManager.TryDispatchTransfer` 内部决策（已工作）；只新增"按需触发"入口。
- 不动 `EnabledFeatures` 的语义；新行为仍受现有 toggle 控制。
- 不改 hard invariants（net472、SaveBaseId、LocalSaveId、try/catch 包裹、PartyComponent 首行过滤、LLM 禁即时路径）。

## 3. 改动点设计

### 3.1 #2 — LLM advice 真驱动决策

**文件**：`src/Managers/TownGarrisonManager.cs`（仅此一处）

**改动**：把 `EvaluateOne` line 122-130 的"消费 advice 仅打日志"扩展为"翻译为 `GarrisonDecision[]` 并合并入决策列表"。

**翻译表**（`LLMAdvice.Action` → `GarrisonActionKind`）：

| advice.Action | 映射 Kind | priority | magnitude | 备注 |
|---|---|---|---|---|
| `send_recruiting_party` | `RequestRecruitment` | `60` | `advice.MagnitudeSuggested` | 介于 gap≥10 的 `50+gap/2` 与 siege=100 之间 |
| `transfer_troops` | `RequestTransferIn` | `60` | `advice.MagnitudeSuggested` | 同上 |
| `adjust_rule` | — | — | — | 仅日志 + 审计（rule 修改非 B1 范围） |
| `advise_user` | — | — | — | 仅日志 + 审计 |
| `do_nothing` | — | — | — | 不入决策列表 |

**决策列表合并**：

```text
原：var decisions = RuleBasedFallbackDecisionMaker.Decide(town);
新：var ruleDecisions = RuleBasedFallbackDecisionMaker.Decide(town);
    var llmDecisions  = TranslateLlmAdvice(pendingLlmAdvice);   // 可能 0 或 1 条
    var decisions = MergeDedupSort(ruleDecisions, llmDecisions);
```

**Dedup 规则**（避免重复派发被下游限额拒）：同 `Kind` 多条时取 `priority` 最高的一条；并列时 `source=Llm` 优先（因为 LLM 已通过三重校验，magnitude 更贴合最新局势）。其余条目仅落审计、不参与派发循环。

**审计**：每条 advice-derived decision 在落 `DecisionAuditLogger.LogRule` 时把 `source` 标记 `Llm`（不是 `Rule`）。复用现有 `DecisionAuditLogger.LogLlm(...)` 或新增轻量 overload。

**安全帽**：翻译前再校验 `advice.MagnitudeSuggested ≥ 0`；若 < 0 视为非法 advice，不入决策列表，落 `LLMAdviceRejected` 审计（与 `LlmAutoExecuteBridge` 现有拒绝路径风格一致）。

**LLM 不可用时**：`pendingLlmAdvice == null` 或 action 不在翻译表 → llmDecisions 为空 → 等价于现状。零回归风险。

### 3.2 #1.B — `RequestTransferIn` intent 接通

**文件**：
- `src/Transfer/CastleSupportManager.cs`（新增公开方法）
- `src/Managers/TownGarrisonManager.cs`（消费 case）

**新方法**：`CastleSupportManager.TryDispatchForDemand(Town destination, int requestedMagnitude) → int /* tasks_dispatched */`

**语义**：在 `EvaluateAll` 的内部逻辑基础上限定 `destination` 已固定，从所有候选 source 中筛配对（复用现有 deficit/surplus/distance/MapFaction/siege/risk 过滤），按 `Priority` 排序后最多取 `ceil(requestedMagnitude / MaxTroopsPerTask)` 个 task，逐个调 `GarrisonTransferManager.TryDispatchTransfer`。返回真正被派发的 task 数。

**注意**：DailyTick 路径已经会跑全量配对，本方法的价值是
- (i) 在 LLM advice 触发 `RequestTransferIn` 时**当轮**响应（无需等到下一个 DailyTick）；
- (ii) 给审计留下 intent → 真实 dispatch 的关联痕迹。

**复用与去重**：当 DailyTick 全量 dispatch 已经覆盖该 destination 时，`GarrisonTransferManager.TryDispatchTransfer` 内部的 `MaxTransfersPerTown=1` 限额会自然拒掉重复 dispatch，无需在此层额外去重。

**TownGarrisonManager.EvaluateOne 新 case**：

```text
else if (d.Kind == GarrisonActionKind.RequestTransferIn && _castleSupportManager != null
         && features.CastleSupport)
{
    int dispatched = _castleSupportManager.TryDispatchForDemand(town, d.Magnitude);
    dispatched > 0;  // accepted 标志
    rejectionReason = dispatched == 0 ? "no feasible donor / already at transfer limit" : null;
}
```

**依赖注入**：`TownGarrisonManager` 构造函数需要新接 `CastleSupportManager` 引用；`SovereignTownsCampaignBehavior.OnSessionLaunched` 已经先构造 `_castleSupportManager` 后构造 `_townGarrisonManager`（line 106 / 140），顺序天然满足，仅需把 `_castleSupportManager` 传进 ctor。

### 3.3 #6.B — `RequestDisbandExcess` 真实裁撤（"退伍回乡"R1 方案）

**新文件**：
- `src/Parties/DismissPartyComponent.cs` — 新 `CustomPartyComponent` 子类（见下节存档契约）
- `src/Lifecycle/DisbandReturnPartyDispatcher.cs` — 静态调度器，纯逻辑

**修改文件**：
- `src/SaveSystem/SovereignTownsTypeDefiner.cs` — `AddClassDefinition(typeof(DismissPartyComponent), 4)`
- `src/Ui/SafeUninstallMenu.cs` — 卸载向导销毁列表加 `DismissPartyComponent` case
- `src/Lifecycle/PartyLifecycleManager.cs` — 新 kind 常量 + Migrate 特判

**DismissPartyComponent 存档契约**（LocalSaveId=4）：

| 字段 | SaveableField | 类型 | 用途 |
|---|---|---|---|
| `_homeVillageStringId` | 1 | `string` | 目标 village stringId |
| `_dismissedFromTownStringId` | 2 | `string` | 兵员被裁撤的源 town stringId（仅审计/调试） |
| `_departureTime` | 3 | `CampaignTime` | 出发时间（lifecycle idle 检测用） |

`PartyOwner` 与 `HomeSettlement` 动态从 `_dismissedFromTownStringId` 派生（与 RecruitingParty/TransferParty 同模式，避免 owner 易主后过期）。`AvoidHostileActions = true`。stringId 前缀 `st_dismiss_<townId>_<ticks>`。

**新 case in TownGarrisonManager.EvaluateOne**：

```text
else if (d.Kind == GarrisonActionKind.RequestDisbandExcess && rule.AutoDisbandExcess)
{
    int dismissed = DisbandReturnPartyDispatcher.DismissExcess(town, d.Magnitude);
    accepted = dismissed > 0;
    rejectionReason = dismissed == 0 ? "no eligible troops to dismiss" : null;
}
```

**Dispatcher 行为（R1：同文化随机村）**：

```text
DismissExcess(Town town, int magnitude) →
  1. roster = town.GarrisonParty.MemberRoster
  2. 按 Tier 升序遍历，挑出最多 `magnitude` 个非英雄、IsRegular 兵员（与 GarrisonTransferManager 抽兵保留低 Tier 优先逻辑相同）
  3. 统计被挑兵员的 dominant culture (count-by-Culture argmax)
  4. 找 home village =
       (a) town 周围 ≤ 80f、Culture == dominantCulture、非 raided 的 village 集合，
           按 Distance 升序 + MBRandom 取首批前 3 中随机一个；
       (b) 失败 fallback 到 town 周围 ≤ 80f 任意非 raided village（忽略 Culture）；
       (c) 仍失败 → 直接 RemoveTroop（"无家可归"语义，落日志），不创建 party
  5. DismissPartyComponent.CreateForTown(town, homeVillage) 工厂方法
     —— 内部 MobileParty.CreateParty(stringId="st_dismiss_<townId>_<ticks>", component=new DismissPartyComponent(...))
     —— 名称 "{=ST_DismissedParty}Dismissed Troops of {TOWN_NAME}"
     —— roster 起始为空（兵员由 dispatcher 下一步 AddToCounts 注入）
  6. 把抽出的兵员 AddToCounts 到新 party，从 garrison RemoveTroop
  7. PartyLifecycleManager.RegisterTrackedParty(party, kind="dismiss", home=town.Settlement)
  8. SetMoveGoToSettlement(home_village)
  9. 抵达 home village 由 HourlyTickParty 检测 → DestroyPartyAction.Apply(null, party)
     （兵员随之消散，符合"退伍回家"语义）
```

**Lifecycle 改动**：新增 kind 常量 `KindDismiss = "dismiss"`、`MaxDismissPerTown = 1`、`IdleHoursBeforeDisband` 复用现有 36h。`PartyLifecycleManager.GetMaxFor` 加 case。`RebuildFromCampaign` 不重建 dismiss party（它们生命短、读档时若仍在路上，由 lifecycle idle 检测自然回收）。

**HourlyTickParty 抵达回调**：在 `PartyLifecycleManager.OnHourlyTickPartyEvent` 已有的"按 kind 派发"分支里加 dismiss case：当 `party.HomeSettlement == party.TargetSettlement` 且 `party.LastVisitedSettlement == home village` 时 `DestroyPartyAction.Apply(null, party)`。

**审计**：`DecisionAuditLogger.LogRule("DisbandExcess", inputSummary, decisionJson, accepted=true, rejectionReason=null)`，DecisionJson 含被裁撤 count 与目标 village stringId。

**首府失守 / village 易主**：dismiss party 走 lifecycle 通用孤儿处理路径——`MigrateAllOrDisband` 或 `MigrateByHomeSettlement` 会把它的 roster 兵员（已是被裁撤的"退伍者"）转回新首府 garrison 或蒸发。**注意**："已裁撤的兵员"本就要消散，所以若被 capital 切换路径吸回新首府反而违背语义，应改为：`MigrateAllOrDisband` 对 kind=dismiss 直接 `DestroyPartyAction.Apply(null, party)`（兵员蒸发，不再进新首府）。需在 `PartyLifecycleManager.MigrateAllOrDisband` 加 kind=="dismiss" 的特判分支。

### 3.4 #7 — `AlertLowFood` 真暂停招募

**改动点**（3 个招募入口 + 1 个上游）：

| 文件 | 改动 |
|---|---|
| `src/Recruitment/RecruitmentManager.cs` | `TryDispatchRecruiter(town, decision)` 首行加 food guard |
| `src/Recruitment/CapitalInPlaceRecruiter.cs` | `RecruitFromCapitalNotables(settlement)` 首行加 food guard |
| `src/Recruitment/PrisonerRecruitmentManager.cs` | `OnDailyTickSettlement(settlement)` 首行加 food guard |

**food guard 共享辅助**（新增 `src/Configuration/FoodGuard.cs` 静态类）：

```text
public static bool IsRecruitmentPausedForFood(Town town, TownGarrisonRule rule)
{
    if (town?.Settlement == null) return false;
    bool paused = town.FoodChange < rule.FoodSafetyThreshold;
    if (paused) {
        Logger.Info($"recruitment paused at '{town.Name}' (foodChange={town.FoodChange:F2} < threshold={rule.FoodSafetyThreshold:F2})");
        DecisionAuditLogger.LogRule(
            "RecruitmentPausedLowFood",
            $"town={town.Settlement.StringId} foodChange={town.FoodChange:F2}",
            $"{{\"threshold\":{rule.FoodSafetyThreshold:F2}}}",
            accepted: false,
            rejectionReason: "FoodSafetyThreshold");
    }
    return paused;
}
```

**调拨不暂停**：`CastleSupportManager` / `GarrisonTransferManager` 不读 food guard——调拨入兵不增食物压力，反而能让低粮城提早补足；与玩家直觉一致。

**升级不暂停**：`TroopUpgradeService` 不读 food guard——升级不增人。

**审计去重**：`FoodGuard` 每次都落审计，导致同一 DailyTick 会落 3 条（3 个入口都路过 guard）。可接受，但若日志噪声困扰可改为"每 town 每天最多落 1 条"——B1 不优化，留 B2 决定。

## 4. 对架构契约的影响

| Hard invariant | 影响 |
|---|---|
| net472 / SaveBaseId | 无影响 |
| LocalSaveId | **新增 1 个**：DismissPartyComponent → LocalSaveId=4（连续分配，1/2/3 已占） |
| try/catch 包裹 vanilla 事件 | 所有新代码沿用现有模式 |
| LLM 禁即时路径 | **维持**：advice 仅在 DailyTick 路径被消费；HourlyTick 不读 LLM |
| HourlyTickParty 首行 PartyComponent 过滤 | **维持**：dismiss party 经 `PartyLifecycleManager._tracked` 过滤，不直接靠 PartyComponent 类型 |
| Hard-uninstall via SafeUninstallMenu | **维持但需扩展**：dismiss party 引入 `DismissPartyComponent`（CustomPartyComponent 第 4 个子类）。`SafeUninstallMenu` 销毁列表加一条：`mp.PartyComponent is DismissPartyComponent → DisbandPartyAction.StartDisband(mp)`。卸载后所有 dismiss party 一并清理 |
| Newtonsoft.Json | 无新序列化字段 |

## 5. 验证（无单测，靠日志 + 游戏内观察）

| 改动 | 验证步骤 | 预期日志 |
|---|---|---|
| #2 | 启 LLM Ollama provider，触发 DailyTick；后一日 DailyTick 应消费上一日 advice | `consuming LLM advice... action=send_recruiting_party` + `decision: RequestRecruitment priority=60 source=Llm` |
| #1.B | 让首府 gap≥30 + 开 CastleSupport + 其他玩家 town 有 surplus | DailyTick 日志含 `TryDispatchForDemand: dispatched=N` + 审计 `TransferRequestedByIntent` |
| #6.B | 在 config 开 AutoDisbandExcess + 让 garrison > target×1.2 | 日志见 `DisbandExcess` decision + 一支临时 party 出现在地图上 + 抵达村庄后销毁 |
| #7 | 让首府 FoodChange = -3.0（手动作弊 settlement.FoodChange）+ DailyTick | 三处入口各落一条 `recruitment paused at...` + 审计 `RecruitmentPausedLowFood` |

## 6. 回滚

每项改动都是"加 case / 加 guard / 加方法"，不改既有签名。回滚 = 删除新增 case 与 FoodGuard 调用。无存档兼容问题（无新 SaveableField）。

## 7. 实施顺序建议（供 plan 阶段参考）

1. **#7 FoodGuard**（最小，先建立审计基线）
2. **#1.B `TryDispatchForDemand`** + EvaluateOne case（中等，依赖 #7 无关）
3. **#2 LLM 翻译表** + decision merge（中等，依赖审计基线）
4. **#6.B**（最大，最后做，子步骤顺序）：
   1. `DismissPartyComponent` + `SovereignTownsTypeDefiner` LocalSaveId=4
   2. `PartyLifecycleManager` 加 `KindDismiss / MaxDismissPerTown=1` 常量 + `GetMaxFor` case + Migrate 特判（kind=="dismiss" → DestroyParty 蒸发）
   3. `DisbandReturnPartyDispatcher.DismissExcess` 实现
   4. `TownGarrisonManager.EvaluateOne` 加 case
   5. `SafeUninstallMenu` 加销毁 case

每步独立可验证，失败不影响下一步实施可继续。
