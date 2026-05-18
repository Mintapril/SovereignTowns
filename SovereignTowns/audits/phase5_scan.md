# Phase 5 — 健壮性扫描报告

> 日期：2026-05-18
> 方法：3 个 Explore subagent 并行扫 7 维度 + 主线程核实关键发现
> 范围：67 个 `.cs` 文件，14,815 行

---

## 0. 汇总

| 优先级 | 计数 | 说明 |
| --- | ---: | --- |
| **P0 真实 bug / 风险** | **0** | T1 SaveableField 槽位重叠经主线程 grep 核实为误报（CLAUDE.md #3 是 per-class，基类 [10-12] vs 子类 [20-23] 各自独立 namespace，不冲突） |
| P1 应修 | 10 | 5 项行为风险 + 5 项错误处理同根问题（合并为 1 个批量修复） |
| P2 改进 | 18 | 日志精度 / 资源清理 / null 防御 等 nice-to-have |
| ✅ 已覆盖 | 全部 7 维度 | 详见各维度小节 |

按用户 prompt 要求：**本阶段只列清单，等用户勾选要做的再执行**。

---

## 1. 各维度概览

### 维度 1: 错误处理（CLAUDE.md #5 entry-point try-catch）

✅ **已覆盖**：4 个 ST Component 的 OnHourlyTick / OnMapEventEnded / OnDestroyed 全部 try-catch 包裹 + Logger.Error。WebConfig HTTP endpoint 全部 try-catch 转 500 响应。ModTreasury / ChargeHero 等金币路径完整包裹。

⚠️ **同根问题**：**13 处 `Logger.Warn(ex.Message)` 模式丢失 stack trace**（见 P1.E）。

### 维度 2: 边界值

✅ **已覆盖**：所有 `Settlement.All / Town.AllTowns / Clan.Settlements` 迭代有 `t == null continue`；`Count == 0` 路径有兜底返回；`RiskAssessmentService.cs:82` 用 `threat/(ally+1f)` 避免除零；`PartyPrisonerCap=30` 缓解 roster 溢出；string null 合并用 `?? ""` 或 `?? "<null>"`。

⚠️ **需补**：1 项 P1（roster 上界）+ 2 项 P2。

### 维度 3: null 安全

✅ **已覆盖**：vanilla API 返回值（Settlement.Town / Clan.Leader / MapFaction / OwnerClan）普遍使用 `?.` 链式访问 + `?? fallback`。基类提供 `HomeSettlementOrNull` 用于诊断路径。

⚠️ **需补**：3 项 P1（关键 null edge）+ 1 项 P2。
**baseline 2 个 CS8604 警告**（BaseSettlementVisitScheduler.cs:120 + PatrolDispatcher.cs:92）— 仍存在（主线程 `--no-incremental` 确认），Agent C 报告"未发现"是 incremental build 缓存假象。

### 维度 4: 并发与状态一致性

✅ **已覆盖**：`WebConfigGameThreadSync` 用 `Interlocked.Exchange` + `ConcurrentQueue`；`ConfigurationManager` / `ModExpenseLedger` / `DecisionAuditLogger` / `PerSettlementActivityRing` 全部用 lock 保护共享数据；`SettlementsSnapshot.cs:56,93` 用 `Volatile.Write` 原子赋值。

⚠️ **需补**：2 项 P1（Drain 溢出 / Instance race）+ 3 项 P2。

### 维度 5: 外部依赖失败路径

✅ **已覆盖**：ConfigurationManager 原子写（temp + rename）+ 加载失败 fallback；JSON 反序列化失败回退默认；HTTP 端口冲突自动 +1 重试（最多 50 次）；token 文件失败返空串；vanilla API 调用都有内层 try-catch。

⚠️ **需补**：1 项 P1（浮点 NaN 检查）+ 2 项 P2（文件 IO 异常分层）。

### 维度 6: 资源释放

✅ **已覆盖**：`HttpListener` Stop+Close 在 OnSubModuleUnloaded；`CancellationToken` Dispose 后重建；`OnConfigChanged` delegate 在 behavior 注册前先 -= 旧；File.AppendAllText 无 stream 泄漏；Logger.Shutdown() Wait 写入 Task 2s 超时。

⚠️ **需补**：1 项 P1（后台 Task 异常无感知）+ 2 项 P2。

### 维度 7: 日志与可观测性

✅ **已覆盖**：关键决策路径（征兵派遣 / 调拨创建 / 巡逻防御响应）有 Logger.Info；`[DIAG]` 日志均在 Debug 级别（release 过滤）；WebConfig token 不写日志；OnHourlyTick 入口诊断包含 home/cur/target/members。

⚠️ **需补**：3 项 P2 改进（日志精度）。

### 维度 8: 类型签名一致性

✅ **已覆盖**：SaveableField 槽位无重复（基类 [10-12]、子类各自独立 namespace [20-23]）；SaveBaseId = 1,900,000,000（符合 CLAUDE.md #2）；抽象成员 `Name` / `AvoidHostileActions` / `GetExpenseCategoryForKind` 在所有子类正确 override；public API 签名与 doc §14 队伍资金描述一致。

⚠️ **需补**：3 项 P2（异常状态返 0 vs 实际 0 区分 / VisitedThisTrip 兼容性 / nullable 显式化）。

---

## 2. P1 需补清单（10 项）

### P1.E — Logger.Warn 丢失 stack trace（13 处同根问题，建议批量修复为 1 个 commit）

| ID | 位置 | 改动 |
| --- | --- | --- |
| E1 | [PartyEconomyHelper.cs:55,98,153,161,195,205,220](SovereignTowns/src/Common/PartyEconomyHelper.cs:55) | `Logger.Warn($"…{ex.Message}");` → `Logger.Warn($"…", ex);` |
| E2 | [StRecruiterPartyComponent.cs:423,537,595,613,633,656](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:423) | 同上 |
| E3 | [CapitalRegistry.cs:276,280](SovereignTowns/src/Capital/CapitalRegistry.cs:276) | 同上 |
| E4 | [PartyMergeService.cs:87,195](SovereignTowns/src/Lifecycle/PartyMergeService.cs:87) | 同上 |
| E5 | [PartyLifecycleManager.cs:385,387,391](SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs:385) | 同上 |

**修复模式**：所有 `catch (Exception ex) { Logger.Warn($"…{ex.Message}"); }` 改为 `catch (Exception ex) { Logger.Warn($"…", ex); }`。Logger 应该有 `Warn(string, Exception)` 重载。如无，需先在 [Logger.cs](SovereignTowns/src/Logging/Logger.cs) 补 overload。

### P1.其他（5 项）

| ID | 位置 | 问题 | 建议 |
| --- | --- | --- | --- |
| **B1** | [SettlementsSnapshot.cs:83](SovereignTowns/src/WebConfig/SettlementsSnapshot.cs:83) | `MemberRoster?.TotalManCount ?? 0` 无上界检查（vanilla 可能极大值） | 加 `Math.Min(value, 10000)` 防御性截断 |
| **N1** | [CapitalLogisticsManager.cs:207](SovereignTowns/src/Managers/CapitalLogisticsManager.cs:207) | `partyClan != ownerClan` 比较时 ownerClan 可能 null | 加 `if (ownerClan == null) return;` 前置守卫 |
| **N2** | [StRecruiterPartyComponent.cs:447](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:447) | `Clan.PlayerClan` 在边缘启动可能 null | OnDailyTick 内再 null check |
| **C1** | [WebConfigGameThreadSync.cs:22](SovereignTowns/src/WebConfig/WebConfigGameThreadSync.cs:22) | `_pendingChangedSettlementIds` ConcurrentQueue 无 Drain 长度限制；HTTP PUT 大量 settlement IDs 时 Drain 卡 game thread | Drain loop 加 `if (i > 256) break;` 限流 |
| **C2** | [CapitalRegistry.cs:36](SovereignTowns/src/Capital/CapitalRegistry.cs:36) | `static Instance` 在 HTTP 线程读、game 线程 Initialize 写；race 风险 | HTTP 调用前 `var reg = Instance; if (reg != null) reg.SyncFromConfig();` 双 check |
| **I3** | [PartyEconomyHelper.cs:68-69,220](SovereignTowns/src/Common/PartyEconomyHelper.cs:68) | vanilla `party.FoodChange` 可能返 NaN/Infinity（未来版本风险） | 加 `if (float.IsNaN(change) \|\| float.IsInfinity(change)) change = 0;` |
| **R2** | [WebConfigServer.cs:101](SovereignTowns/src/WebConfig/WebConfigServer.cs:101) | `Task.Run(AcceptLoopAsync)` 无 await/异常处理；后台任务崩溃主线程无感知 | 加 ContinueWith 日志；或 Stop() 时 Wait 该 task |
| **F5** | (跨文件) | Recruiter 长旅食物耗尽（2000d 在 50 兵规模下约 4-6 天） | 已在 §14 doc：Recruiter 有 ShouldReplenishFoodEnRoute=true 沿途补粮（到 town 时）；村庄旅途中确实无法补，等到下个 town。**实际场景**：是否真饿肚子取决于行程频率。**建议**：观察实际游戏行为后再决定是否加 fallback |
| **F6** | [PartyEconomyHelper.cs:107-164](SovereignTowns/src/Common/PartyEconomyHelper.cs:107) | settlement 食物缺货时 BuyFoodFromSettlement 返 0，无凭空塞 1 天保命粮 fallback | 加配置开关 `AllowFreeFoodFallback`（默认 true），buy=0 时调 GrantFoodForDays(party, 1f) 兜底 |

---

## 3. P2 改进清单（18 项，按类别）

### 日志精度（3 项）
- **L1** [WebConfigEndpoints.cs:80](SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:80) declared 数值无单位（字节/MB）
- **L2** [PatrolDispatcher.cs:103-116](SovereignTowns/src/Patrol/PatrolDispatcher.cs:103) [DIAG] 日志无 clan 上下文
- **L3** [PartyEconomyHelper.cs](SovereignTowns/src/Common/PartyEconomyHelper.cs) BuyFood / SellLoot break 路径无原因日志

### 类型签名与兼容性（3 项）
- **T2** [PartyEconomyHelper.cs:65-80](SovereignTowns/src/Common/PartyEconomyHelper.cs:65) EstimateFoodForDays 吞异常返 0，调用方无法区分"真 0"vs"异常 0"
- **T3** [StRecruiterPartyComponent.cs:65-73](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs:65) VisitedThisTrip lazy-init 旧存档兼容性
- **T4** PartyEconomyHelper 公共 API 返 int/float 无 nullable，失败 0 与真 0 混淆

### 边界值（2 项）
- **B2** [PartyLifecycleManager.cs:612](SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs:612) building level 负数防御
- **B3** RecruitmentPlanner 304-306 notables.Count 异常风险

### null 安全（1 项）
- **N3** [ConfigurationManager.cs:492](SovereignTowns/src/Configuration/ConfigurationManager.cs:492) JsonConvert.DeserializeObject setter 运行时错误未捕获

### 并发改进（3 项）
- **C3** PerSettlementActivityRing GetOrAdd lambda 抛异常风险
- **C4** ConfigurationManager.Current getter lock 性能 → ReaderWriterLockSlim
- **C5** _pendingCapitals 文档无线程约束注释

### I/O 改进（2 项）
- **I1** [ConfigurationManager.cs:102-103,116](SovereignTowns/src/Configuration/ConfigurationManager.cs:102) File.WriteAllText 无分层异常捕捉（IOException / UnauthorizedAccessException）
- **I2** [WebConfigServer.cs:106-107](SovereignTowns/src/WebConfig/WebConfigServer.cs:106) port.txt 写入前未 EnsureDirectory

### 资源清理（2 项）
- **R1** Logger.Shutdown / DecisionAuditLogger 二次 Dispose 异常
- **R3** DecisionAuditLogger ConcurrentQueue Shutdown 时未 drain 落盘

### Followup 处理（4 项）
- **F1** RankCandidates 100→200 二轮 fallback（已被 config 参数化，无害）
- **F8** party.PartyTradeGold 不再处理（短命任务难积累，合理退役）
- **F9** PartyEconomyHelper.GrantFoodForDays 残留 API（subagent C 报告说"PartyEconomyHelper.cs:83 本身有调用"——但已无外部调用方；建议 F9 改判定为 P2 dead code，可清理）
- **F10** 俘虏功能完全丢失（T2 后；如玩家反馈需要，下一周期重新加到基类 TryEconomicMaintenance）

---

## 4. 已自然消解项

| ID | 状态 | 备注 |
| --- | --- | --- |
| F7 | 已允许 | _teamFunds slot 12 旧存档兼容由 CLAUDE.md "pre-release rapid iteration" 政策覆盖 |

---

## 5. 建议处理策略（待用户勾选）

按工作量与价值估算，**建议本期处理优先级**：

### A. 高优先级（1 个 commit 可完成 P1.E 全部 13 处）
- [ ] **P1.E — Logger.Warn 加 ex 参数**（13 处批量，~30 分钟）

### B. 应修但需逐个评估
- [ ] **B1** — roster TotalManCount 上界（5 行）
- [ ] **N1** — CapitalLogisticsManager ownerClan null（3 行）
- [ ] **N2** — Clan.PlayerClan null check（2 行）
- [ ] **C1** — Drain 长度限流（5 行）
- [ ] **C2** — Instance double-check（HTTP 侧 5 行）
- [ ] **I3** — FoodChange NaN/Infinity（10 行）
- [ ] **R2** — Task.Run 异常感知（10 行）
- [ ] **F6** — BuyFood 缺货 fallback + 配置开关（20 行）

### C. 留观 / 推迟
- [ ] **F5** — Recruiter 长旅食物（建议玩家手测后决定）
- [ ] **F9** — GrantFoodForDays dead code 清理
- [ ] **18 项 P2** — 全部 nice-to-have

### D. 跳过（已自然消解 / 已被政策覆盖）
- F7 / T1（误报）

---

## 6. 工作量估算

| 选项 | 范围 | 时间 |
| --- | --- | --- |
| **仅 A** | P1.E 批量（13 处）| 30 分钟 |
| **A + B 全部** | + 8 项 P1 散点 | +2 小时 |
| **A + B + C 部分** | + F6 食物 fallback 等 | +30 分钟 |
| **全部 P1 + P2 一些** | 18 项 P2 中挑 6-8 项 | +1-2 小时 |

按用户 prompt 要求：**本阶段先列清单，等用户勾选要做的再执行**。请勾选要做的项。

— Phase 5 扫描报告完
