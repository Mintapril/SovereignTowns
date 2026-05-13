# RISK_REGISTER.md — 风险登记

> 来源：`PHASE1_RESEARCH_PLAN.md` §6 的初始预判，结合反编译证据修订。
> 状态码：✅ **已缓解** | ⏳ **方案已知，编码时落地** | ⚠️ **需第二阶段补查** | ❌ **未解决**

---

## P 类 — 性能风险

| ID | 风险 | 状态 | 缓解方案 | 备注 |
|---|---|---|---|---|
| **R-P1** | 每小时 Tick 遍历"所有玩家自有城镇 × 全部 MobileParty"做风险评估 | ✅ | 直接复用 `Settlement.NearbyLandThreatIntensity` / `NearbyLandAllyIntensity` —— 这两个字段由 vanilla 主动维护，**不需要我们重算**。我们只在 `HourlyTickSettlementEvent` 回调里读 settlement 自身字段（O(1) per call），完全跳过遍历 | 见 RESEARCH_FINDINGS §2.5 |
| **R-P2** | 征兵队 + 巡逻队 + 调拨队同时存在 → MobileParty 总数膨胀，大地图寻路压力 | ⏳ | (1) 巡逻队由 `Settlement.PatrolParty` 字段一对一锁定，**结构上保证每城镇最多 1 巡逻队** —— 已验证；(2) 征兵队 + 调拨队由 `PartyLifecycleManager` 显式上限（默认每城镇 1 征兵 + 1 调拨）+ 全局上限（默认 `<= Town.AllTowns.Count × 2`）；(3) 队伍空闲超过阈值（24 小时）自动解散 | MVP 2 起码要实现 (1)(2)，MVP 4 实现 (3) |
| **R-P3** | LLM 调用同步阻塞 Tick | ✅ | 见 UNCERTAINTY_LOG U11 — `async/await` + `Task.Run` + 10s 超时 + 回退到 `RuleBasedFallbackDecisionMaker` | MVP 5.5 实现 |
| **R-P4** | 调试日志按 Tick 写入拖性能 | ⏳ | (1) 日志分级：Debug / Info / Warn / Error；(2) 主回路只生成对象 + 入队，独立线程异步写盘；(3) 默认级别 Info，Debug 默认关；(4) 单文件大小上限 5 MB 自动轮转 | MVP 1 起就要做（参考 ImprovedGarrisons.Debugging.LogFileSystem） |
| **R-P5** (新) | `HourlyTickPartyEvent` 对全图所有 MobileParty 触发 | ⏳ | 在回调里**先判断 `mobileParty.PartyComponent is MyModPartyComponent`**，不是我们的队就立即 return — 这是 O(1) 过滤，能让 99% 的事件在 1 行内退出 | 编码硬规则，写到 CLAUDE.md |
| **R-P6** (新) | 配置文件每次启动全量加载 | ✅ | XML/JSON 配置文件单次启动加载一次，常驻内存。MCM 接入后改用其 `MCM.UI` 配置读写 API | 简单缓解 |

---

## S 类 — 存档风险

| ID | 风险 | 状态 | 缓解方案 | 备注 |
|---|---|---|---|---|
| **R-S1** | CampaignBehavior 直接持有 MobileParty 引用未走 SyncData | ✅ | 强制规则：**所有跨存档字段必须用 `[SaveableField]` 或在 `SyncData(IDataStore)` 里用 `dataStore.SyncData(...)` 显式同步**。Lint 规则：所有 CampaignBehaviorBase 派生类的私有字段必须有 attribute 或在 SyncData 处理 | 写到 CLAUDE.md |
| **R-S2** | SaveableTypeDefiner 的 `saveBaseId` 冲突 | ⏳ | (1) 本 Mod 取 `100_000_000` 作为 saveBaseId（取一个大数远离 Native）；(2) `LocalSaveId`（short）在每个类内严格递增并永不变更 —— **删除字段时也只能"墓碑式标记 obsolete"**，永远不要复用 | 第二阶段反编译 Native 的 TypeDefiner 后确认是否需要再大 |
| **R-S3** | 卸载 Mod 后存档孤儿 MobileParty | ⚠️ | 见 UNCERTAINTY_LOG U9 — 提供"安全卸载工具"菜单（用户在卸载前点击，本 Mod 自销毁全部我方 MobileParty 并还原 vanilla 设置） | MVP 5 实现该工具 |
| **R-S4** | 调拨队中转期间存档：兵员归属不明 | ⏳ | 中转队伍的 `MemberRoster` 由队伍自己持有（合法的 PartyBase 路径），**不会丢**。但要在 SyncData 里加一个"中转任务记录"字段，标记目的地与来源 | 在 GarrisonTransferManager 设计阶段写入 |
| **R-S5** | 配置文件版本迁移 | ⏳ | 从 MVP 1 起，配置文件 root 必带 `"configVersion": 1`；`ConfigurationManager.Load(...)` 路径里包含版本检测 + 迁移链（v1→v2→v3） | MVP 1 设计 ConfigurationManager 时硬性约束 |
| **R-S6** (新) | IG ↔ GDS 都有 SaveableTypeDefiner，若用户**先用 IG 后切到本 Mod**，存档里残留的 IGSaveData 类型 ID 是否冲突？ | ⚠️ | 本 Mod 选 `saveBaseId = 100_000_000` 即可避碰 IG（IG 范围未知，但 6 位数级别概率不重）。第二阶段反编译 IG 的 `TroopTypesSaveableTypeDefiner` 确认 | — |

---

## C 类 — 兼容性风险

| ID | 风险 | 状态 | 缓解方案 | 备注 |
|---|---|---|---|---|
| **R-C1** | 与 Diplomacy 等大 Mod 事件顺序问题 | ✅（暂） | Diplomacy 未在当前 Modules 清单。**用户后续若装则需重新评估** | 跟踪 |
| **R-C2** | 与其他驻军 Mod 双重操作 | ✅ | **由 U12 / 任务 #11 解决** — `<IncompatibleModules>` 静态声明 + `ModuleHelper.IsModuleActive(...)` 运行时检测。本 Mod 与 IG / GDS 强制互斥 | MVP 1 实现 |
| **R-C-RBM** | RBM 兼容性 | ✅ | **极低风险** — RBM 反编译表明它 99% 是 XML 数据 Mod + MissionLogic 战斗 Patch，**完全不动 Campaign 层**：没有 CampaignBehavior、没有 MobileParty 操作、没有 Settlement 操作。本 Mod 与 RBM **正交**。硬规则：(1) 兵种判定全部走运行时属性（`CharacterObject.Culture/Occupation/Tier/IsHero`），不硬编码 stringId；(2) SubModule.xml `<ModulesToLoadAfterThis>` 列入 RBM | MOD_SURVEY §3 |
| **R-C-MCM** | MCM 依赖缺失导致硬依赖路径报错 | ✅ | MCM 改为软依赖：SubModule.xml `Optional="true"`，运行时反射检测可用性。不可用就用自建配置 UI | MVP 5 |
| **R-C-NPCCharacters-RBM** (新) | RBM 通过 `NPCCharacters` XmlNode 重写兵种定义 —— 我们的兵种筛选 / 升级逻辑必须能动态适配 | ⏳ | 兵种过滤的所有判定走 `CharacterObject` 实例属性：`Tier` / `Culture` / `Occupation` / `Equipment[]` / `IsHero` —— 这些是运行时计算，RBM 改 XML 后 vanilla 会自动重新加载这些值 | 编码硬规则 |
| **R-C-ImprovedCombatAI** (新) | 用户已装 `ImprovedCombatAI` 模块，它可能 Patch 战斗 AI —— 与本 Mod 的 Campaign 层无交集，但战斗模拟时双方 Patch 可能干扰 | ✅ | 同 RBM，战斗层 Patch 与 Campaign 正交，**不需要特殊兼容工作** | 跟踪 |
| **R-C-Multi-Mod-Load-Order** (新) | 已装 70+ Mod 的复杂加载顺序 | ⏳ | 本 Mod 在 SubModule.xml 声明 `<ModulesToLoadAfterThis>` 列入：Native、SandBoxCore、Sandbox、StoryMode、Bannerlord.Harmony、RBM | MVP 1 |

---

## 综合风险图

```
                          高严重
                              ▲
                              │
       R-S3 (卸载坏档)        │      R-P2 (队伍膨胀)
          ⚠️                  │         ⏳
                              │
                              │  R-S2 (saveBaseId)
                              │     ⏳
                              │
   R-S6 (与IG ID冲突)         │
       ⚠️                     │   R-P4 (日志性能)
                              │       ⏳
                              │
─────────────────────────────────────────────────► 高概率
                              │
   R-C-Multi-Mod (加载顺序)   │   R-P5 (Hourly每帧遍历)
       ⏳                     │       ⏳
                              │
   R-P3 (LLM阻塞)             │   R-C-RBM
       ✅                     │      ✅
                              │
                              │   R-C2 (IG/GDS共存)
                              │       ✅ (由 U12 解决)
                          低严重
```

**优先级排序**（按"严重度 × 概率"）：
1. ⚠️ **R-S3** — 必须 MVP 5 之前提供"安全卸载工具"，否则用户体验崩溃
2. ⏳ **R-P2** — MVP 2/4 编码时硬性约束队伍上限
3. ⏳ **R-S2** — MVP 1 设计 TypeDefiner 时锁定 saveBaseId
4. ⚠️ **R-S6** — 第二阶段反编译 IG TypeDefiner 后能彻底解决

其余风险方案明确，编码阶段落地即可。
