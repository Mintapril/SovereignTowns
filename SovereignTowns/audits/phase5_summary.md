# Phase 5 — 最终报告

> 日期：2026-05-18
> 阶段：收尾扫描与健壮性补强
> 最终验证：`dotnet build --no-incremental` 0 errors, 2 pre-existing CS8604 warnings ✅

---

## 1. 改动清单（10 项已执行）

### Step 1 — Logger 重载增强
- [Logger.cs](SovereignTowns/src/Logging/Logger.cs)：新增 `Warn(string, Exception?)` 重载，与 `Error(string, Exception?)` 同模式（+6 行）

### Step 2 — 批量修复 20 处 Logger.Warn 丢失 stack trace
| 文件 | 改动数 |
| --- | ---: |
| [PartyEconomyHelper.cs](SovereignTowns/src/Common/PartyEconomyHelper.cs) | 8 处 |
| [StRecruiterPartyComponent.cs](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs) | 5 处 |
| [CapitalRegistry.cs](SovereignTowns/src/Capital/CapitalRegistry.cs) | 2 处 |
| [PartyMergeService.cs](SovereignTowns/src/Lifecycle/PartyMergeService.cs) | 2 处 |
| [PartyLifecycleManager.cs](SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs) | 3 处 |
| **合计** | **20 处** |

模式统一为：`Logger.Warn($"…: {ex.Message}")` → `Logger.Warn($"…", ex)`

### Step 3 — **B1**：[SettlementsSnapshot.cs:83](SovereignTowns/src/WebConfig/SettlementsSnapshot.cs:83) roster TotalManCount 上界 `Math.Min(value, 10000)` 防御性截断

### Step 4 — **N1 no-op**：核实 [CapitalLogisticsManager.cs:189](SovereignTowns/src/Managers/CapitalLogisticsManager.cs:189) 已有 `if (ownerClan == null) return;` 早返。Phase 5 扫描误报。

### Step 5 — **N2 no-op**：核实 [StRecruiterPartyComponent.cs](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs) 中 grep `Clan.PlayerClan` = 0 匹配。Phase 5 扫描误报（Agent A 行号错位）。

### Step 6 — **C1**：[WebConfigGameThreadSync.cs:52](SovereignTowns/src/WebConfig/WebConfigGameThreadSync.cs:52) Drain 限流 `MaxDrainPerTick = 256` + deferred 深度警告

### Step 7 — **C2 no-op**：核实 `CapitalRegistry.Instance` 在 HTTP 侧 0 访问，唯一访问点 [WebConfigGameThreadSync.cs:45](SovereignTowns/src/WebConfig/WebConfigGameThreadSync.cs:45) 在 game-thread Drain 上下文。无 race 风险。

### Step 8 — **I3**：[PartyEconomyHelper.cs:69](SovereignTowns/src/Common/PartyEconomyHelper.cs:69) 和 line 220 防御 `float.IsNaN || float.IsInfinity` 用于 `FoodChange` / `Food`

### Step 9 — **R2**：[WebConfigServer.cs:101](SovereignTowns/src/WebConfig/WebConfigServer.cs:101) `Task.Run(AcceptLoopAsync)` 加 `ContinueWith(... OnlyOnFaulted)` 异常感知

### Step 10 — **F6**：BuyFood 缺货兜底
- [GlobalConfig.cs:118](SovereignTowns/src/Configuration/GlobalConfig.cs:118) 新增 `EnabledFeatures.AllowFreeFoodFallback = true`
- [StPartyComponent.cs:151](SovereignTowns/src/Parties/StPartyComponent.cs:151) `TrySeedAndBuyInitialFood` 内 `BuyFoodAtSettlement` 返 0 时调 `GrantFoodForDays(party, 1f)` 兜底
- [WebUI/index.html:974](SovereignTowns/SovereignTowns/WebUI/index.html) 加 toggle entry
- [doc §3 toggle 表](SovereignTowns/docs/mod-behavior-guide.zh-CN.md) 加 `食物缺货兜底` 行

---

## 2. 文件改动统计

| 文件 | 改动 |
| --- | --- |
| [Logger.cs](SovereignTowns/src/Logging/Logger.cs) | +6（Warn 重载） |
| [PartyEconomyHelper.cs](SovereignTowns/src/Common/PartyEconomyHelper.cs) | 8 处 Warn 改写 + 4 行 NaN/Infinity 守卫（I3） |
| [StRecruiterPartyComponent.cs](SovereignTowns/src/Parties/StRecruiterPartyComponent.cs) | 5 处 Warn 改写 |
| [CapitalRegistry.cs](SovereignTowns/src/Capital/CapitalRegistry.cs) | 2 处 Warn 改写 |
| [PartyMergeService.cs](SovereignTowns/src/Lifecycle/PartyMergeService.cs) | 2 处 Warn 改写 |
| [PartyLifecycleManager.cs](SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs) | 3 处 Warn 改写 |
| [SettlementsSnapshot.cs](SovereignTowns/src/WebConfig/SettlementsSnapshot.cs) | +1（B1） |
| [WebConfigGameThreadSync.cs](SovereignTowns/src/WebConfig/WebConfigGameThreadSync.cs) | +7（C1 限流） |
| [WebConfigServer.cs](SovereignTowns/src/WebConfig/WebConfigServer.cs) | +6（R2 ContinueWith） |
| [GlobalConfig.cs](SovereignTowns/src/Configuration/GlobalConfig.cs) | +3（F6 字段） |
| [StPartyComponent.cs](SovereignTowns/src/Parties/StPartyComponent.cs) | +12（F6 fallback block） |
| [WebUI/index.html](SovereignTowns/SovereignTowns/WebUI/index.html) | +1（F6 toggle） |
| [docs/mod-behavior-guide.zh-CN.md](SovereignTowns/docs/mod-behavior-guide.zh-CN.md) | +1（§3 表新行） |

**总计**：13 文件，净 +50 / -25 行

---

## 3. 验证

### Build（每 Step 独立）
- Step 1 → 10：每步独立 `dotnet build` 通过
- **最终**：0 errors, 2 pre-existing CS8604 warnings ✅

### 静态测试
按用户指令已删除 `tests/static-regression.ps1`，本期无 regression 测试。

### 运行时验证（待用户手测）
按 [CLAUDE.md](CLAUDE.md) "There are no unit tests. Verification = launch the game"。

建议手测场景：
1. **Logger 升级**：触发任意 ST 队伍异常（如手动删除 `Configs/SovereignTowns/global.json` 中字段）→ ModLogs 应出现完整 stack trace
2. **F6 食物缺货兜底**：选一个 settlement 把库存食物清空（编辑器或控制台），创建 ST 队伍 → 应观察到日志 "BuyFood 返 0 (缺货)，fallback 凭空塞 N 单位 (1 天)"
3. **C1 Drain 限流**：HTTP PUT 一次 300+ settlement override 配置 → ModLogs 应出现 "Drain: limited to 256 per tick, deferring remaining"
4. **R2 Task 异常**：测试 HTTP 服务边界（无法实际触发，但 ContinueWith 现在有日志路径）

---

## 4. Phase 5 整体回顾

### 维度覆盖

| 维度 | 已覆盖 ✅ | 本期修补 ⚠️ |
| --- | --- | --- |
| **错误处理** | entry-point try-catch（CLAUDE.md #5）;HTTP endpoint 500 转换;金币路径 ModTreasury 包裹 | 20 处 Logger.Warn 加 ex（统一栈追踪） |
| **边界值** | 空集合 null check;除零守卫;PartyPrisonerCap=30;string null 合并 | B1 roster 防御性上界 |
| **null 安全** | 普遍 `?.` 链式访问 + `?? fallback`;HomeSettlementOrNull 诊断 | （N1/N2 误报） |
| **并发** | Interlocked + ConcurrentQueue + lock 普遍 | C1 Drain 限流（C2 误报） |
| **外部依赖** | 原子写;JSON fallback;HTTP 端口自动重试;token 失败返空 | I3 浮点 NaN/Infinity;F6 食物缺货 fallback |
| **资源释放** | HttpListener Stop+Close;CTS Dispose;delegate -=;File.AppendAllText | R2 后台 Task 异常感知 |
| **日志** | 关键决策 Logger.Info;[DIAG] Debug 级;敏感信息保护 | 间接通过 P1.E 改善 |
| **类型签名** | SaveableField 无重复;SaveBaseId 合规;抽象成员完整 | （T1-T4 误报或推迟） |

### 完成统计

- **本期完成**：7 项实际修复 + 3 项扫描误报核销（含 1 项 doc 更新）
- **build 验证**：每 step 独立 + 最终 full rebuild 全绿
- **代码净改动**：13 文件、约 50 行

---

## 5. 未处理项（留作 backlog）

按用户指示，P2 全部留 backlog 不动。明细：

### P2 改进项（18 项，详见 [phase5_scan.md §3](audits/phase5_scan.md)）

- **L1-L3**（3）：日志精度改进
- **T2-T4**（3）：类型签名 / 兼容性改进
- **B2-B3**（2）：边界值改进
- **N3**（1）：JSON setter runtime error 兜底
- **C3-C5**（3）：并发改进（ReaderWriterLockSlim 优化等）
- **I1-I2**（2）：文件 IO 分层异常 + EnsureDirectory
- **R1, R3**（2）：资源清理（二次 Dispose、Audit drain）
- **F1**（1）：RankCandidates 二轮 fallback dead code 清理
- **F5**（1）：Recruiter 长旅食物（等玩家手测）
- **F8**（1）：party.PartyTradeGold 不再处理（合理退役）
- **F9**（1）：GrantFoodForDays 标签误判（仍在 Helper 内被 F6 fallback 调用）
- **F10**（1）：俘虏功能完全丢失（如玩家需要，下周期重做）

### P0/P1 误报核销

- T1 SaveableField 槽位重叠 → CLAUDE.md #3 是 per-class namespace，基类 [10-12] vs 子类 [20-23] 独立不冲突
- N1 CapitalLogisticsManager ownerClan null → line 189 已早返
- N2 StRecruiterPartyComponent Clan.PlayerClan → 实际无引用，scan agent 行号错位
- C2 CapitalRegistry.Instance race → HTTP 侧 0 访问，无 race
- F2 CS8604 警告"已消失" → Agent C 是 incremental cache 假象，实际仍是 baseline 2 warnings

---

## 6. 整体审计周期总览（Phase 1–5）

| Phase | 范围 | 产出 |
| --- | --- | --- |
| **Phase 1** | 探索与盘点 | [phase1_inventory.md](audits/phase1_inventory.md)：~120 条断言 + 67 文件清单 |
| **Phase 2** | 逐行对比 drift | [phase2_drift.md](audits/phase2_drift.md)：5 项 ❌ + 4 项 ⚠️（约 95% 对齐度） |
| **Phase 3** | 对齐执行 | [phase3_done.md](audits/phase3_done.md)：5 项 ❌ 全修（D1-D5 改代码按文档） |
| **Phase 4 T1** | 统一队伍粮食/资金 | [phase4_t1_done.md](audits/phase4_t1_done.md)：基类化 + 4 类共享 + Dispatcher 提取 |
| **Phase 4 T1 重整** | seed=2000 统一 + helper 提取 | seed 统一 / Sally/Transfer 不补粮 / `TrySeedAndBuyInitialFood` helper |
| **Phase 4 T2** | 战利品集中处理废弃 | [phase4_t2_done.md](audits/phase4_t2_done.md)：删 BattleLootHandler+Manager（净 -700 行） |
| **Phase 5** | 健壮性扫描 + P1 修复 | [phase5_summary.md](audits/phase5_summary.md)（本文件）：10 P1 + 1 Logger overload |

### 累计净改动估算
- 代码：~+1100 / -1500（净 -400）
- doc：约 100 处修订
- 文件：~40 个改动 + 5 个删除（含 audit 中间产物）

---

## 7. 下一步

按 prompt Phase 5 规则：
> d. 产出《最终报告 phase5_summary.md》：改动清单、测试结果、未处理项及原因。

**本文件即最终报告。** 整个 5-Phase 审计周期完成。

**剩余可选事项**：
1. Git commit 整理（5-7 个有意义的 commit 或 1 个大 commit）
2. 游戏中手测验证清单（特别是 T1 经济模型 + F6 食物兜底 + Logger 升级）
3. 把 audits/ 整目录入 git（之前一直 untracked）

— Phase 5 最终报告完

---

## 8. Post-decision 更新（2026-05-18，写报告后）

advisor 在收尾复审时指出 F6 默认值（`AllowFreeFoodFallback = true`）与用户早前的
"非作弊基调 — 出门前都要在出发地买粮食"方向相悖，问询用户后用户决定
**没有粮就不要出发** —— F6 整体撤销，不保留 toggle。落地：

- 删 `EnabledFeatures.AllowFreeFoodFallback` 字段
- `TrySeedAndBuyInitialFood`：`BuyFoodAtSettlement` 返 0 时改为 return false（取消派遣），
  依赖 Dispatcher 调用方 `TransferBackToGarrison` + `DestroyAndUntrack` 走 OnDestroyed → TryRefundOnDestroy 退还种子金（路径已对称验证）
- 删 WebUI 该 toggle
- 删 doc §3 该行
- 删 `PartyEconomyHelper.GrantFoodForDays`（F6 撤销后无调用方，dead code）

新行为：origin settlement 食物缺货 = 不派遣，种子金原路退回。

§1 Step 10 / §2 改动统计 / §5 F9 标签条目仅作为历史记录保留，不再反映现状。
