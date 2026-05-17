# SovereignTowns B16 重构后审计报告 — Round 1 (Initial)

**审计日期**: 2026-05-17
**审计基线**: master @ 7622e61 (B16.4a)
**审计方式**: 6 个 general-purpose agent 并行,维度互不重叠

## 总体结论

**是否存在高风险问题**: 是 — 发现 5 个 P0 (运行时可观测崩溃 / 兵员蒸发 / 功能停摆) + 多个 P1。但**未发现 RCE / 凭据泄漏 / 数据破坏**类极危漏洞。

**是否建议立刻合并**: **暂缓合并到 release-candidate 分支**,P0 全部修复后再切。但项目本身已声明 pre-release 状态(CLAUDE.md "无需向后兼容存档"),可以在 master 继续迭代,不需要回滚。

**最需要优先修复的 3 个问题**:

1. **Patrol 派遣无 daily 兜底 + 玩家进城跳 HourlyTickSettlement** — 玩家驻自家首府时整支氏族永远不会派出新巡逻队。空闲队 24h 后被 idle-disband 而无替换 → 巡逻系统在玩家最容易触发的场景下静默停摆。一行修复,但必须修。
   - SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:321-347 + SovereignTowns/src/Patrol/PatrolDispatcher.cs:51-73

2. **Transfer/Recruiter/Patrol 缺 OnDestroyed 救援 + 无 WarDeclared 事件订阅** — 玩家宣战瞬间路上的 Transfer/Recruiter 在敌方版图穿越被截杀,运载兵全员蒸发(默认 100 兵上限)。Sally 已有救援实现,证明可行,但未下沉到基类。
   - SovereignTowns/src/Parties/StPartyComponent.cs:87 (默认 no-op) vs SovereignTowns/src/Parties/StSallyPartyComponent.cs:212-247 (有救援)

3. **玩家中途换 Clan → CapitalRegistry 错位** — `Clan.PlayerClan` 在 vanilla 内可变(`ChangeClanLeader` 等),`CapitalRegistry._managers` 仍持旧 clan key 永不更新。后果: 新 player clan 既不在 registry(无任何 ST 接管),旧氏族的 in-flight 仍由玩家钱包扣费。完全静默。
   - SovereignTowns/src/Capital/CapitalRegistry.cs:39,65,214

---

## 高风险问题 (P0/P1)

### P0-1. Patrol 派遣无 daily 兜底
- 文件: SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:303-347
- 问题: `OnHourlyTickSettlement` 调 `_patrolDispatcher`,`OnDailyTickSettlement` 仅调 `_sallyDispatcher`。CLAUDE.md 硬约束 #6 已为 Sally 加 daily fallback,Patrol 漏了。
- 触发: 玩家停留在自家首府不出 → vanilla 跳过该 settlement 的 HourlyTickSettlement
- 后果: 巡逻队永远不新派
- 修复: `OnDailyTickSettlement` 中加 `_patrolDispatcher?.OnHourlyTickSettlement(settlement);`
- 需补测试: 玩家进城驻 3 天,旧 patrol 解散后是否有新 patrol 出门

### P0-2. Transfer/Recruiter/Patrol 缺 OnDestroyed 救援
- 文件: SovereignTowns/src/Parties/StPartyComponent.cs:87
- 问题: 基类 `OnDestroyed` 默认 no-op,仅 Sally 重写
- 触发: in-flight 队伍被强盗/敌方截杀
- 后果: Transfer 上限 100 兵 + Recruiter 已招新兵 + 玩家已付金币 全部蒸发
- 修复: 基类提供 default 实现 `MergeNonHeroTroopsIntoGarrison(home ?? capital)`;把 Sally 的实现下沉到基类
- 需补测试: 手动 `DestroyPartyAction` 在外征 Transfer 上,确认 garrison 收到兵

### P0-3. 无 `WarDeclared` 事件订阅
- 文件: SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:56-77
- 问题: 整个项目 grep `WarDeclared|DeclareWar|MakePeace` 无匹配
- 触发: 玩家与敌国宣战时,Transfer/Recruiter 已在敌方领土路径上
- 后果: 兵员遭遇被全歼,叠加 P0-2 → 蒸发
- 修复: 订阅 `CampaignEvents.WarDeclared`,新增 `MigrateByFaction` 方法
- 需补测试: 修补完后,自家附近 vs 远征 Transfer 在宣战瞬间是否会反向

### P0-4. 玩家中途换 Clan → CapitalRegistry 错位
- 文件: SovereignTowns/src/Capital/CapitalRegistry.cs:39,65,214
- 问题: `_managers` 以 `Clan` 引用为 key,`Initialize` 取 `Clan.PlayerClan` 一次,不刷新
- 触发: vanilla `Hero.Clan` 可变(`ChangeClanLeader`)
- 后果: 新 player clan 不在 `_managers` → ST 完全不接管;旧氏族的 in-flight 仍由玩家钱包扣费
- 修复: 订阅 `OnHeroChangedClanEvent`,在 `Hero.MainHero.Clan` 变化时移除旧 manager + EnsureForClan(newPlayerClan)
- 需补测试: 通过 console 或 mod 切玩家 clan,确认 capital 选取重新触发

### P0-5. WartimeMultiplier × TargetTotalCount 整数溢出
- 文件: SovereignTowns/src/Managers/CapitalLogisticsManager.cs:171; SovereignTowns/src/Configuration/ConfigurationManager.cs:533-541
- 问题: `(int)Math.Round(rule.TargetTotalCount * multiplier)` 校验仅 `>= 0`,无上限
- 触发: WebConfig PUT 注入 `TargetTotalCount=1_000_000_000 × WartimeMultiplier=3` 溢出为负
- 后果: 首府目标兵力变成 1,补员系统失效
- 修复: `ValidateRule` 加 `TargetTotalCount <= 100_000` 与 multiplier `<= 10`
- 需补测试: PUT 异常值,验证返回 422 + global.json 不被污染

### P0-6. `GetSettlements` 在 HTTP 线程读 vanilla campaign 对象
- 文件: SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:150-178
- 问题: 直接 `foreach (var t in Town.AllTowns)` 在请求线程访问 `t.OwnerClan`
- 触发: 城镇易主瞬间发生 HTTP GET
- 后果: 偶发 NRE 或残缺数据
- 修复: 在 `OnDailyTick` 拉快照写入 `_settlementsSnapshot`,HTTP 读快照

### P1-1. CapitalRegistry.SyncFromConfig 移除 AI manager 留下僵尸 in-flight party
- 文件: SovereignTowns/src/Capital/CapitalRegistry.cs:242-278
- 问题: 玩家把 `ApplyToAiSettlementsToo` toggle off 时,已派出的 AI clan in-flight 仍在 `_tracked`,但 `ValidateAliveAndManaged` false → 队伍僵尸化漂浮
- 后果: 36h idle-disband 才被回收,UI 无警告
- 修复: SyncFromConfig 移除 AI manager 时 `MigrateAllOrDisband(aiClan, null)`

### P1-2. `_visitedThisTrip` 标为 [CachedData],重启丢失
- 文件: SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:56
- 问题: 玩家中途存档,重载后 visited 历史清空
- 后果: 候选评估可推荐刚访问过的村,绕远路
- 修复: 改 `[SaveableField(23)]`,在 TypeDefiner 注册

### P1-3. ManuallySetCapital 会解散全部 in-flight 而非改向
- 文件: SovereignTowns/src/Capital/CapitalManager.cs:294
- 问题: 玩家通过 UI 改首府,所有 Patrol/Recruiter/Transfer/Sally 兵员 merge 到新 capital
- 后果: Recruiter 已招高级别兵全部送给新 capital
- 修复: (a) UI 弹窗确认;(b) Sally 显式 `deferIfInMapEvent`

### P1-4. AiCulturePresets 文化限制实际不生效
- 文件: SovereignTowns/src/Configuration/AiCulturePresets.cs:42-65; SovereignTowns/src/Evaluators/GenericTroopMatcher.cs:160-183
- 问题: Preset 设 `AllowedCultureIds`,但 `MatchesRule` 完全不读
- 后果: AI 城仍会招到外文化兵
- 修复: 在 `MatchesRule` 增加 culture 校验

### P1-5. PUT /api/config 无 payload 上限 + 无 Content-Type 校验
- 文件: SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:50,228-233
- 问题: `ReadBody` 全量入内存,无 ContentLength64 拦截
- 后果: 拿到 token 的攻击者可发 GB 级 body 触发 OOM
- 修复: `req.ContentLength64 > 1MB` 早 reject;强制 Content-Type

### P1-6. Castle 玩家完全没有 Web UI 入口
- 文件: SovereignTowns/src/Ui/DiagnosticGameMenu.cs:57-90
- 问题: 三个菜单项全注册到 `menuId: "town"`
- 后果: 只持有 castle 的玩家无法打开网页配置面板
- 修复: 复制三项注册到 `menuId: "castle"`

### P1-7. STPartyComponent.HomeSettlement null-forgive
- 文件: SovereignTowns/src/Parties/StPartyComponent.cs:33
- 问题: `_homeSettlement!` null-forgive,SaveableField(10) 反序列化丢失会爆 NRE
- 修复: getter 改为 throw + RebuildFromCampaign 跳过 null home

### P1-8. `scheduler.NotifyPartyDestroyed` 未被订阅
- 文件: SovereignTowns/src/Coordination/BaseSettlementVisitScheduler.cs:196-208
- 问题: 方法存在但 `OnMobilePartyDestroyed` 不转调
- 后果: 内存泄漏(MBGUID 不复用,量有限)

### P1-9. StSallyPartyComponent 静默吞异常
- 文件: SovereignTowns/src/Parties/StSallyPartyComponent.cs:159,270
- 问题: 两处 `catch { /* swallow */ }`
- 修复: 改 `catch (Exception ex) { Logger.Warn(...); }`

---

## 硬编码变量清单 — 高优先级配置化

| 文件 | 变量 | 当前值 | 建议配置名 | 建议默认 | 建议范围 |
|---|---|---|---|---|---|
| SallyDispatcher.cs:34 | SallyCooldownHours | 24 | Thresholds.SallyCooldownHours | 24 | [0,168] |
| SallyDispatcher.cs:35 | MinSustainedTicks | 3 | Thresholds.SallyMinSustainedHours | 3 | [1,24] |
| SallyDispatcher.cs:30 | DetectionRadius | 50 | Thresholds.SallyDetectionRadius | 50 | [10,200] |
| SallyDispatcher.cs:31 | InitialSallyGold | 100 | Thresholds.SallySeedGold | 100 | [0,5000] |
| StSallyPartyComponent.cs:31 | MaxSallyHours | 12 | Thresholds.SallyMaxAwayHours | 12 | [1,72] |
| RecruitmentDispatcher.cs:31 | DefaultInitialGold | 1000 | Thresholds.RecruiterSeedGold | 1000 | [0,10000] |
| StRecruiterPartyComponent.cs:40 | DefaultGoldPerRecruit | 10 | Thresholds.RecruiterGoldPerTroop | 10 | [0,100] |
| StRecruiterPartyComponent.cs:39 | CostDiscount | 0.5 | Thresholds.RecruiterPlayerCostDiscount | 0.5 | [0,1] |
| StRecruiterPartyComponent.cs:37 | VolunteerMul | 2.0 | Thresholds.RecruiterVolunteerSlotMultiplier | 2.0 | [1,5] |
| PartyLifecycleManager.cs:36 | IdleHoursBeforeForceReturn | 24 | Thresholds.IdleForceReturnHours | 24 | [4,96] |
| PartyLifecycleManager.cs:37 | IdleHoursBeforeDisband | 36 | Thresholds.IdleDisbandHours | 36 | [6,168] |
| STPartySpeedModel.cs:25 | SpeedBonusFactor | 0.2 | Thresholds.PartySpeedBonus | 0.2 | [0,1] |
| WebConfigServer.cs:26 | DefaultPort | 41763 | GlobalConfig.WebConfig.Port | 41763 | [1024,65535] |
| RiskAssessmentService.cs:77-96 | 风险阈值 10/3/1.5/0.5 | — | Thresholds.Risk.{Critical,High,Medium,Low}Threshold | — | [0,100] |

完整 50+ 条清单见对话历史中的 audit_hardcoded_v2 agent 报告。

---

## 面板配置一致性

### 死字段(POCO 有但运行时无读取)
- `TownGarrisonRule.AllowLowTierFiller` — TownGarrisonRule.cs:61 仅 Clone
- `TownGarrisonRule.RestrictToFactionCultures` — TownGarrisonRule.cs:49 无 reader
- `ClanRecruiterConfig.StuckTimeoutHours` — GlobalConfig.cs:164 无 reader

### 隐藏配置(逻辑使用但 UI 不暴露)
- `EnabledFeatures.PauseSpendingWhenBroke`
- `ClanPatrol.*` 全部 6 项
- `ClanRecruiter.*` 全部 4 项
- per-settlement override UI 仅 6 字段,TownGarrisonRule 有 20+ 字段

### Endpoint 与 UI 不一致
- `POST /api/reload` 存在但 UI 不调用

### 默认值/校验上限不一致
- `MaxTier <= 7` 校验,UI 仅 1-6,vanilla 实际上限 6
- `SallyTargetPartySizeMultiplier` / `RecruiterReturnRecruitedCount` / `BudgetLimit` / `SallyCreateMinPartyCount` 均无上限校验

---

## 建议补充的测试用例(具体场景)

1. Transfer 路上被截杀兵员救援测试(P0-2)
2. Recruiter `_visitedThisTrip` 持久化测试(P1-2)
3. WarDeclared 中途宣战测试(P0-3)
4. ManuallySetCapital 改首府测试(P1-3)
5. 玩家驻城 patrol 兜底测试(P0-1)
6. AI 接管 toggle 测试(P1-1)
7. WebConfig 数值溢出测试(P0-5)
8. WebConfig 大 payload 测试(P1-5)
9. WebConfig 错误 Content-Type 测试(P1-5)
10. IG/GDS 共存测试
11. RBM 兼容测试
12. 玩家换 Clan 测试(P0-4)
13. 首次安装空存档测试
14. 保留 ST 字段卸载再装回测试
15. ConfigVersion 不匹配测试
16. Castle 玩家 WebUI 入口测试(P1-6)
17. Token 显示在 chat 测试
18. 同 tick 创建多支 party
19. StSallyPartyComponent silent catch(P1-9)
