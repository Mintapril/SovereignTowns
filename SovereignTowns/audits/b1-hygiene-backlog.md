# B1 hygiene backlog（B17.4 T9 收尾后产出）

来源：GPT-5.5 审计报告 + Round-2 narrow audit 校准结果。本轮（commit `7560a3d`）只关闭了 3 项 P0；以下为入 backlog 项，由后续周期挑选处理。

---

## P1（建议优先于 P2 处理；但本轮终止后不阻塞）

### 1. PatrolDispatcher `garrison!` null-forgiving 加显式保护
- 文件：[PatrolDispatcher.cs:182](SovereignTowns/src/Patrol/PatrolDispatcher.cs:182)、[:189](SovereignTowns/src/Patrol/PatrolDispatcher.cs:189)
- 现状：玩家氏族 ModTreasury 扣款失败回滚路径用 `garrison!.MemberRoster`，依赖上游 `moved <= 0` early return 保证 `garrison != null`。事实安全但隐式。
- 修复：要么在 Charge 块之前加 `if (garrison == null) { Logger.Error("..."); return; }`，要么在 `garrison!` 旁加 inline 注释引用 line 148 守卫。
- 工作量：约 2 行。

### 2. 玩家氏族巡逻退款 home==null 容错
- 文件：[StPatrolPartyComponent.cs OnDestroyed](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:192)
- 现状：`HomeSettlementOrNull?.OwnerClan` 在销毁时机可能已被 vanilla 清空，玩家氏族 patrol `_teamFunds` 剩余金额会路由到 AI 分支 → no-op 丢失。
- 注：这是预先存在的行为（修复前 AI/玩家同样依赖 HomeSettlementOrNull，相同丢失风险），本轮 P0-3 未引入回归。
- 推荐修复：新增 `[SaveableField(N)] bool _seedChargedToPlayer`（CLAUDE.md 允许 renumber Local IDs），Charge 成功时置位。`OnDestroyed` 直接查该 bool 决定路由。
- 工作量：约 5 行 + 1 个 LocalSaveId 槽。

---

## P2（暴露硬编码到面板 — 典型 B+1 周期工作）

### 3. 招募经济常量
- [StRecruiterPartyComponent.cs:40-43](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:40)：
  - `VolunteerMul = 2.0f` (volunteer slot 上限放宽倍率)
  - `CostDiscount = 0.5f` (玩家氏族单兵金币折扣)
  - `DefaultGoldPerRecruit = 10` (每兵默认成本)
- 影响：玩家可感知的游戏平衡参数；面板 `thresholdSpecs` 需联动添加。

### 4. 巡逻经济与战术常量
- [PatrolDispatcher.cs:174](SovereignTowns/src/Patrol/PatrolDispatcher.cs:174)：`patrolSeedGold = 2000`
- [PatrolDispatcher.cs:202](SovereignTowns/src/Patrol/PatrolDispatcher.cs:202)：买粮天数 `3f`
- [StPatrolPartyComponent.cs:39](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:39) 附近：补粮目标 `3f` / 触发阈值 `1f`
- [StPatrolPartyComponent.cs:274](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:274)：守城 initiative `0.3f / 0.7f`（attack/avoid）
- [StPatrolPartyComponent.cs:51](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:51)：卡住进度距离阈值 `1.0f`

### 5. 出击队任务时长
- [StSallyPartyComponent.cs:30](SovereignTowns/src/Parties/StSallyPartyComponent.cs:30)：`MaxSallyHours = 12f`
- [SallyDispatcher.cs:303](SovereignTowns/src/SallyForth/SallyDispatcher.cs:303)：免费粮天数 `3f`

---

## P3（观察/记录，不一定要修）

### 6. `ConfigsAreEqual` JSON diff 行为
- 文件：[ConfigurationManager.cs:213](SovereignTowns/src/Configuration/ConfigurationManager.cs:213)
- 现状：序列化异常时保守返回 `false`（视为有变化，触发 OnConfigChanged）。是有意保守策略，不构成 bug。
- 仅记录，避免后续 reviewer 误判。

### 7. `OnConfigChanged` 按字段 diff 决定重置粒度
- 现状：只要 `ConfigsAreEqual` 报 changed=true 就重置**所有** in-flight recruiter。
- 改 ShowDailySummary 这种 UI-only 字段不应重置 recruiter（虽然现在 changed=true 就重置）。
- 改进方向：在 `OnConfigChangedHandler` 内进一步判断哪些字段实际影响 recruiter 规划（Thresholds.Recruitment* 等），只在这些字段变化时重置。
- 工作量：中等。需要分类配置字段为"影响规划" vs "纯展示"。

### 8. `RecruitmentCooldown` 不入存档
- 读档后村庄招募冷却丢失，可能短时间内重复访问同一村庄。
- 产品取舍决定，非 bug。

---

## 不在本 backlog 范围

- `[DIAG]` 日志清理：与本轮无关，等待 B16.4 大重构日志体系统一时一起处理。
- 招募/巡逻/回援逻辑产品语义（如回援目标消失后是否立即返程）：需要产品决策，不属于 hygiene。
