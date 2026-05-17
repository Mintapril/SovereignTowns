# Round 2 修复计划 — 基于 Round 1 报告 + Advisor 校准

**目标**: 不漂移地修完 Round 1 P0/P1,准备做 Round 2 复审。
**advisor 给出的硬终止条件**: P0 全部 verified fixed,新一轮 audit 产出 0 个新 P0 + ≤2 个新 P1。预计 3 轮,**硬封顶 5 轮**。
**核心约束**: 无法运行游戏,每次提交诚实声明 "build 通过,runtime 待玩家在游戏内验证"。

---

## Bucket A — 可自主修复(不需用户决策)

按建议顺序执行,每 3-5 个相关修复一个 commit:

### Commit 1: Tick 时序 + idle safety
- [ ] **P0-1**: `OnDailyTickSettlement` 加 `_patrolDispatcher?.OnHourlyTickSettlement(settlement)`,补 Patrol daily 兜底
- [ ] **H2 (Round 1 #3)**: `PartyLifecycleManager.OnHourlyTickParty` 早 return 移入 try
- [ ] **删除空壳**: `SovereignTownsCampaignBehavior.OnHourlyTickParty` 只剩 `DrainWebConfigSync`,移到 `OnHourlyTickSettlement` 已存在的调用即可

### Commit 2: WebConfig 输入硬化
- [ ] **P0-5**: `ValidateRule` 加 `TargetTotalCount <= 100_000`、各 multiplier <= 10、`BudgetLimit <= 10_000_000`、`SallyCreateMinPartyCount <= 1000`、`RecruiterReturnRecruitedCount <= 1000`
- [ ] **P1-5**: PUT /api/config 加 `ContentLength64 > 1MB` 早 reject + 强制 `Content-Type: application/json`
- [ ] **MaxTier 上限**: `ConfigurationManager.cs:523` 校验改 `<= 6`(vanilla 实际上限)

### Commit 3: HTTP 线程安全
- [ ] **P0-6**: `GetSettlements` 改为读快照;在 `OnDailyTickSettlement` 拉 `Town.AllTowns` 写入 `_settlementsSnapshot`,HTTP 读这个快照

### Commit 4: 状态机 hygiene
- [ ] **P1-2**: `StRecruiterPartyComponent._visitedThisTrip` 改 `[SaveableField(23)]` `List<Settlement>`(HashSet 难序列化),TypeDefiner 不需要额外注册(List<Settlement> 是 vanilla 已知类型)
- [ ] **P1-7**: `StPartyComponent.HomeSettlement` getter 改 `if (_homeSettlement == null) throw new InvalidOperationException(...)`;`RebuildFromCampaign` 在 collect 阶段跳过 `stc._homeSettlement == null`
- [ ] **P1-9**: `StSallyPartyComponent.cs:159, 270` 两处静默 catch → `Logger.Warn`
- [ ] **P1-8**: `PartyLifecycleManager.OnMobilePartyDestroyed` 转调每个 manager 的 scheduler.NotifyPartyDestroyed

### Commit 5: UI 入口完整性
- [ ] **P1-6**: `DiagnosticGameMenu.Register` 复制三项注册到 `menuId: "castle"`;condition 扩展 `s.IsTown || s.IsCastle`
- [ ] **Token chat 泄漏**(L6-4): `OnOpenWebConfigSelected` 失败时仅显示 `127.0.0.1:PORT + 提示查看 auth.txt`,不显示完整 URL
- [ ] **POST /api/reload UI 接线**: WebUI "↻ 重读" 改为先 POST /api/reload 再 GET /api/config

---

## Bucket B — 需用户决策的政策(escalate before fix)

这些**不是 clarifying questions**,是 gameplay 政策选择,我无权代决。本轮先 ask,得到答案后再做。

### Q1: P0-2 OnDestroyed 救援政策
Sally 已实现 "rescue to home → fallback capital → evaporate"。把它下沉到基类时,要决定 Transfer/Recruiter/Patrol 是否走同一逻辑。

### Q2: P0-3 WarDeclared 撤退范围
宣战瞬间撤回哪些 party?

### Q3: P0-4 玩家换 Clan 行为
首先要在 `_research/` 里 verify `OnHeroChangedClan` 事件在 v1.3.15 存在。然后选行为。

### Q4: P1-1 AI manager toggle off 的 UX
现行是立即 `MigrateAllOrDisband(aiClan, null)` 解散所有 AI in-flight,无 UI 警告。

---

## Bucket C — 等 verify / 不算 bug

- **PatrolDispatcher.CountExistingPatrolsAtHome vs CanCreateAnotherParty** (Round 1 #4.1): advisor 提示可能是有意区分"当前在家 vs 所有 tracked"。需读代码确认意图,**不当 bug 修**。
- **MobileParty StringId 用 `DateTime.UtcNow.Ticks` 后缀**: 真实碰撞需同 Ticks 时间戳(100ns)创建两支。需先 grep dispatcher 是否在同一帧 spawn 多支 — 如果不会,降为低风险 hygiene。
- **所有标注"需进一步确认"的项**: 是 agent 不确定性,不是 finding。如有时间 verify,无则维持现状。
- **P1-3 ManuallySetCapital**: 报告自己说"文档需更新而非 bug",不修。
- **P1-4 AiCulturePresets**: 改了会变 AI 默认行为,**用户决策**。先记入 Bucket B 但不优先。
- **硬编码 → 配置化(50+ 项)**: advisor 强调不应一次全暴露,**集中到最后一轮做** Sally 4 项 + idle 2 项即可。

---

## Round 2 复审范围(advisor 建议: 不全部重跑)

修完 Bucket A 后,只重跑这 4 个维度:
- audit_events (P0-1, H2, OnHourlyTickParty 空壳)
- audit_webconfig (P0-5, P1-5, P0-6)
- audit_lifecycle (P1-2, P1-7, P1-8, P1-9)
- audit_boundaries (P0-5 校验, P1-7 null-safety, P1-8 scheduler 泄漏)

audit_hardcoded 不重跑(本轮没动配置面板新加项)。
audit_config_ui 不重跑(本轮没改配置)。

---

## 提交诚实声明

每个 commit message 后加:
> Build verification: `dotnet build -c Debug` passes.
> Runtime verification: pending in-game test by the user.
> No tests changed (project has no unit tests per CLAUDE.md).
