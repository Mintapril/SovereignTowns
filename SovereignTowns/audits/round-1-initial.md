# Round 1 — Initial audit (基于 GPT-5.5 报告 + 本地核验)

来源：用户提交的 GPT-5.5 审计报告。本轮在不修改代码前做 Bucket C 核验。

## 核验结论：3 项 P0 全部 reproduce

### P0-1 ✅ WebUI 打开/刷新即重置所有 recruiter
- [index.html:1169-1182](SovereignTowns/SovereignTowns/WebUI/index.html:1169)：`reloadAll()` 第一行 `await this.http('POST', '/api/reload')`，并在 `mounted` 钩子的初始化路径中被调用（line 1164）。
- [WebConfigEndpoints.cs:144-160](SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:144)：`PostReload` 成功后无条件 `RequestConfigChanged(null, "POST /api/reload")`。
- [SovereignTownsCampaignBehavior.cs:503-527](SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:503)：`OnConfigChangedHandler(null)` 遍历所有 `StRecruiterPartyComponent`，`SetAssignedTarget(null)` + `TransitionTo(Dispatching)`。
- 影响：玩家打开/刷新面板即重置全部正在赶路的 recruiter。

### P0-2 ✅ 面板配置在 PlanNextHop / Clan scheduler 中未生效
- [GlobalConfig.cs:294-298](SovereignTowns/src/Configuration/GlobalConfig.cs:294)：`RecruitmentCandidateBatchSize`、`RecruitmentPlanMaxDistance` 已暴露。
- [RecruitmentDispatcher.cs:38-41](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs:38)：初始派遣路径正确读 `ConfigurationManager.Current?.Thresholds?.X ?? default`。
- [StRecruiterPartyComponent.cs:44-45](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:44)：`CandidateBatchSize = 8`、`PlanMaxDistance = 100f` 是 `private const`，被 `PlanNextHop` 在 line 447、448、457、459、463 引用。
- [ClanRecruiterScheduler.cs:45-50](SovereignTowns/src/Recruitment/ClanRecruiterScheduler.cs:45)：字面量 `maxDistance: 100f, maxResults: 8`。
- 影响：用户改面板只对首次派遣生效；运行中 recruiter 的下一跳规划与 clan-level 调度器仍按硬编码 100/8 执行。

### P0-3 ✅ 巡逻队启动资金绕过 ModTreasury / PauseSpendingWhenBroke
- [PatrolDispatcher.cs:167](SovereignTowns/src/Patrol/PatrolDispatcher.cs:167)：`stc.InitTeamFundsFromHomeOwner(created, 2000)`。
- [StPatrolPartyComponent.cs:68-74](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:68)：直接调 `PartyEconomyHelper.ChargeHero(owner, amount)`。
- [PartyEconomyHelper.cs:230-247](SovereignTowns/src/Common/PartyEconomyHelper.cs:230)：直接 `GiveGoldAction.ApplyBetweenCharacters(hero, null, deduct, ...)` — 不查 `PauseSpendingWhenBroke`，不写 `ModExpenseLedger`，不写审计。
- 对比 [RecruitmentDispatcher.cs:177-189](SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs:177) / [SallyDispatcher.cs:253-264](SovereignTowns/src/SallyForth/SallyDispatcher.cs:253)：两者用 `CapitalRegistry.ShouldChargeClan(homeClan)` + `ModTreasury.CanAfford/Charge/Refund`。
- 影响：玩家氏族首府仍可在金币 0 时创建巡逻队并真实扣钱；财务面板看不到这笔；与 Recruiter/Sally 行为不一致。

## P1 / P2 GPT-5.5 报告中其它项目（待 advisor 校准是否打捞）

P1：
- B1 `OnConfigChanged` 应按字段 diff 决定是否重置（不是全部都需要重新规划）。
- B2 巡逻初始资金 `2000` 字面量与 `StPatrolPartyComponent.InitialTeamFundsDefault` 默认值并存（需要先确认 InitialTeamFundsDefault 是否真的存在）。
- B3 `PUT /api/config` 对 UI-only 字段（如 ShowDailySummary）也广播全局重置。
- B4 `OnConfigChangedHandler` 迭代 `MobileParty.AllCustomParties` 未快照。

P2（默认进 backlog，不在本轮 fix）：
- 巡逻初始资金、初始买粮天数、补粮阈值、initiative 权重、SallyMaxHours 等"硬编码暴露面板"工作（典型 BX+1 工作）。
- `[DIAG]` 日志清理。
- 招募 cooldown 不入存档。

## Bucket B（产品决策——需要用户裁决）

P0-3 修复涉及"AI clan 首府是否也走 ModTreasury 门控"。代码现有惯例：
- `CapitalRegistry.ShouldChargeClan(clan)` 仅在玩家氏族 = true → ModTreasury 路径
- AI clan = false → 跳过 ModTreasury（不扣玩家钱），实际上现在巡逻队是从 AI hero 钱包扣

**默认决策（与现有 Recruiter/Sally 一致）**：
- 玩家氏族首府：走 `ModTreasury.Charge(ExpenseCategory.PatrolSeed, 2000, ...)`，受 `PauseSpendingWhenBroke` 门控，入账 + 审计。
- AI 氏族首府：保留 `InitTeamFundsFromHomeOwner` 从 AI 领主扣，无门控。
- 创建失败回滚 → `ModTreasury.Refund`。

如果用户希望"AI 氏族也不应该扣钱（巡逻队对 AI 应免费）"或"AI 氏族也要门控"，本决策需调整。

## 终止条件（lock 在此处）

1. P0-1、P0-2、P0-3 三项 verified fixed 且 build 通过。
2. Round-2 narrow audit（仅 patrol + recruiter + webconfig + campaignbehavior diff 文件）产出 0 P0 且 ≤2 P1。
3. 其余 P1/P2 入 [b1-hygiene-backlog.md](SovereignTowns/audits/b1-hygiene-backlog.md)。
4. 硬上限：3 轮。
