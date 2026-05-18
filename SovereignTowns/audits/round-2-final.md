# Round 2 — Narrow audit (terminal round)

紧承 Round 1 + commit `7560a3d`（GPT-5.5 P0 三项修复）。按 advisor 校准的 3 维度并行 audit，每维度一个 read-only subagent。

## 维度 1 — recruiter/patrol 配置面板一致性

文件：`StRecruiterPartyComponent.cs` / `ClanRecruiterScheduler.cs` / `RecruitmentDispatcher.cs` / `GlobalConfig.cs`

结论：**0 P0 / 0 P1**

- 两处新静态属性 `CandidateBatchSize` / `PlanMaxDistance` 每次调用都走 `ConfigurationManager.Current?.Thresholds?.X ?? default`，不缓存为实例字段 → 配置热更新下一次 `PlanNextHop` / `EnumerateCandidates` 即生效。
- `RecruitmentFallbackMaxDistance`（默认 200f）与 `PlanMaxDistance`（默认 100f）独立，fallback 守卫 `if (fallbackDist > PlanMaxDistance)` 仍然正确。
- 同文件无其它需要联动的字面量。

P2（入 backlog，不阻塞终止）：
- `StRecruiterPartyComponent` `VolunteerMul=2.0f` / `CostDiscount=0.5f` / `DefaultGoldPerRecruit=10` 三常量未暴露到面板。语义偏"游戏平衡设计"，需要 WebUI `thresholdSpecs` 联动。

## 维度 2 — WebConfig 端点副作用

文件：`WebConfigEndpoints.cs` / `ConfigurationManager.cs` / `SovereignTownsCampaignBehavior.cs` (OnConfigChangedHandler) / `index.html` (mount/fetchAll/reloadAll)

结论：**0 P0 / 0 P1**

关键核验点：
- `ReplaceAndSave` 在 `changed = !ConfigsAreEqual(_current, newConfig)` 之后才更新 `_current.LastModified = DateTime.UtcNow.ToString("O")`，时间戳不参与 diff，不会造成永远 changed=true。
- `TryReload` 比较的两个对象（`_current` 与刚从磁盘 loaded）的 `LastModified` 来自上次写盘的同一值，未变化时严格字符串相等。
- UI 端 `POST /api/reload` 只在显式「↻ 重读」按钮触发，`PUT /api/config` 只在「保存」按钮；无定时器或监听器隐式触发。
- `OnConfigChangedHandler` 的 `MobileParty.AllCustomParties.ToList()` 包在外层 try 内，符合硬不变量 #5。
- `WebConfigGameThreadSync.RequestConfigChanged` 完全包在 `if (configChanged)` 内，无副作用泄漏。

P2（入 backlog）：
- `ConfigsAreEqual` JSON 序列化异常时保守 fallback 为 changed=true，是有意行为，不构成 bug。

## 维度 3 — ModTreasury 巡逻路径覆盖

文件：`ModTreasury.cs` / `PatrolDispatcher.cs` / `StPatrolPartyComponent.cs` / `PartyEconomyHelper.cs`

结论：**0 P0 / 2 P1**（均不阻塞终止）

P1-a：`garrison!` null-forgiving（line 182/189）
- 事实安全：`gRoster=garrison?.MemberRoster` + `if (gRoster != null && pRoster != null)` 守卫 + `moved <= 0` early return 共同保证 `garrison == null` 永远到不了 Charge 块。
- 风险：隐式控制流保证无注释，将来重构脆弱。**纯可维护性，不构成漏洞。**

P1-b：`OnDestroyed` `HomeSettlementOrNull` 可能已 null
- 销毁时机若 vanilla 已清空 `PartyComponent.HomeSettlement`，玩家氏族 patrol 的 `_teamFunds` 剩余金额会路由到 AI 分支（`RefundTeamFundsToOwner` → `RefundHero(null, ...)` no-op）→ 玩家退款丢失。
- **核心定性：这是预先存在的 bug，不是本轮回归。** 修复前 `RefundTeamFundsToOwner` 内部也是 `HomeSettlementOrNull?.OwnerClan?.Leader`，home==null 时玩家和 AI 同样丢失。本轮 P0-3 仅改"创建时门控扣款"，"销毁时退款路由"是邻接独立关注点。
- 推荐根治方案（backlog）：用 `[SaveableField] bool _seedChargedToPlayer` 在 Charge 成功时置位，OnDestroyed 查该 bool 路由退款。约 5 行 + 1 个 LocalSaveId（CLAUDE.md 允许 renumber/drop）。

## 终止判定（终止条件机械触发）

终止条件（来自 round-1-initial.md）：
1. ✅ P0-1、P0-2、P0-3 三项 verified fixed 且 build 通过（commit `7560a3d`）。
2. ✅ Round-2 narrow audit 产出 **0 P0 / 2 P1**（恰好等于上限）。
3. ✅ 其余 P1/P2 入 [b1-hygiene-backlog.md](b1-hygiene-backlog.md)。
4. ✅ 硬上限 3 轮 — 2 轮收。

**Terminal round closed.** 不再扩 scope。剩余项目交由后续 B+1 周期处理。
