# B18 Hygiene Backlog — 延后到下一周期的清单

**生成日期**: 2026-05-17 (Round 3 终止时)
**前置 commit**: e173f8d (B17.3)
**advisor 判定**: 这些项是"could do more / robustness / nice-to-have",非破坏性 bug

按维度组织。建议在 B18 周期内分多个小 commit 处理,不要一次性堆。

---

## 1. 政策决策类(需用户重新拍板)

### 1.1 WarDeclared 撤退范围扩展
**Round 1 用户决策**: "只有征兵队的行为会改变"。
**Round 2 audit 反馈**: Transfer 在敌方版图穿越被截杀也会损失;Patrol/AI Recruiter 同样未覆盖。
**待用户决策**: 是否扩展到 Transfer? AI Recruiter? Patrol? 还是维持 Recruiter only?

建议优先级:中 — 当前 P0-2 OnDestroyed 基类救援已经保证 Transfer 被截杀后兵员回 home,所以"功能完全失效"已避免;扩展只是更主动的"出险前撤回"。

---

## 2. UI/UX 改进类

### 2.1 setToggleValue $nextTick 写回竞态
**文件**: `SovereignTowns/SovereignTowns/WebUI/index.html:1219-1221`
**问题**: 写 false → \$nextTick 写 true。极快双击 + save() 同一微任务窗口可能写盘 false。
**修复**: 不要先写 false,直接 \$nextTick 强制刷新 true
**优先级**: 低 (极低概率 corner case)

### 2.2 init() 每次开页强制 reload
**文件**: `SovereignTowns/SovereignTowns/WebUI/index.html:1128`
**问题**: 每次浏览器刷新强制 POST /api/reload,会覆盖玩家未保存的浏览器端修改
**修复**: init() 不自动 POST,仅由"↻ 重读"按钮触发
**优先级**: 低 (UX 改进)

### 2.3 PUT /api/config 绕过 UI confirm + 后端无通知
**文件**: `SovereignTowns/src/Capital/CapitalRegistry.cs:SyncFromConfig`
**问题**: UI confirm 仅 UI 路径;PUT JSON 或编辑 global.json 可绕过,后端解散 AI in-flight 时无 Logger.Info / SafeDisplay 通知玩家
**修复**: SyncFromConfig 移除 AI manager 路径加 `Logger.Info` + 可选 `InformationManager.DisplayMessage`
**优先级**: 中

---

## 3. 校验扩展类

### 3.1 ClanPatrolConfig / ClanRecruiterConfig 浮点字段零校验
**文件**: `SovereignTowns/src/Configuration/ConfigurationManager.cs (ValidateConfig)`
**漏校验字段**:
- `ClanPatrolConfig.EtaBufferHours` (1.0)
- `ClanPatrolConfig.StuckTimeoutHours` (12.0)
- `ClanPatrolConfig.MinVisitGapHours` (4.0)
- `ClanPatrolConfig.DistanceWeightHoursPerTile` (0.5)
- `ClanPatrolConfig.SupportEtaThresholdHours` (2.0)
- `ClanRecruiterConfig.EtaBufferHours` (1.0)
- `ClanRecruiterConfig.MinVisitGapHours` (4.0)
- `ClanRecruiterConfig.DistanceWeightHoursPerTile` (0.5)
**修复**: 全部加 IsFiniteFloat + 合理范围 (各 [0, 168] 或 [0, 5] 取决于字段语义)
**优先级**: 中 (NaN 输入会让 ETA 算无效,但需 PUT 注入)

### 3.2 VillageCooldownHours 上限
**文件**: `ConfigurationManager.cs:443-447`
**问题**: 仅 `>= 0` 校验,无上限,用户填 1_000_000 会让 cooldown 数十年
**修复**: 加 `<= 720` (30 天 hours)
**优先级**: 低

### 3.3 PerSettlementOverrides 字典大小 + key 长度
**文件**: `ConfigurationManager.cs:430-441` + WebConfigEndpoints.cs:PutConfig
**问题**: PUT body 1MB 上限,但 key 长度 / 字典 entry 数无单独 cap
**修复**: ValidateConfig 加 `overrides.Count <= 200`, key length <= 64
**优先级**: 低 (1MB 上限已抑制大多数注入)

### 3.4 AllowedCultureIds / PriorityTroopIds / BannedTroopIds 列表大小 + 字符串长度
**文件**: `TownGarrisonRule.cs:49-58` + ConfigurationManager.cs:ValidateRule
**问题**: 列表大小无上限,元素 stringId 长度无限制
**修复**: 加 `list.Count <= 50`, `stringId.Length <= 64`
**优先级**: 低

### 3.5 ExactTroopTemplate key 长度
**文件**: `ConfigurationManager.cs:ValidateRule`
**问题**: dict key (兵种 stringId) 长度无校验
**修复**: 加 `key.Length <= 64`
**优先级**: 低

---

## 4. 一致性类(HomeSettlement OrNull migration)

### 4.1 STPartyDialogRegistration 用 HomeSettlement?
**文件**: `SovereignTowns/src/Ui/STPartyDialogRegistration.cs:84,92,95`
**问题**: 损坏存档下 `HomeSettlement?.X` 中 ?. 不拦截 getter throw,导致对话 UI 抛 InvalidOperationException (被外层 try-catch 包,不崩游戏)
**修复**: 改为 `HomeSettlementOrNull?.X`
**优先级**: 低 (一致性改进)

### 4.2 多处 try-catch 内仍用 HomeSettlement?
列表 (advisor + audit_lifecycle_r2 + audit_lifecycle_r3 已认可这些被覆盖):
- `BattleLootManager.cs:90-91`
- `BattleLootHandler.cs:444-445`
- `CapitalLogisticsManager.cs:247`
- `SallyDispatcher.cs:135`

这些都在 try-catch 内,异常被吞 + Logger.Error,不崩游戏。一致性修复可一次 sweep。
**优先级**: 低

---

## 5. 性能 / Scalability

### 5.1 SettlementsSnapshot.Refresh() 频率
**文件**: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:358`
**问题**: OnDailyTickSettlement 每个 settlement 触发一次 → 100+ Refresh/天 × 全图 70-80 Town 遍历 ≈ 12000 Town 读取/天
**修复选择**:
- 改用 OnDailyTick (campaign-level,无 settlement 参数) 每天 1 次
- 或 dirty flag 机制 (易主 / PUT 时 mark dirty)
**优先级**: 中 (daily tick 密集时可见帧率短暂下降)

### 5.2 WarDeclared handler 全图扫描
**文件**: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:393-431`
**问题**: 遍历 MobileParty.AllCustomParties (100+),每次宣战。
**修复**: 通过 PartyLifecycleManager._tracked 替代 (只看本 mod tracked 队伍)
**优先级**: 低 (宣战频率不高)

---

## 6. 诊断 / 日志改进

### 6.1 411 拒绝消息缺客户端 IP
**文件**: `WebConfigEndpoints.cs:74`
**修复**: `Logger.Warn($"PUT /api/config: chunked encoding rejected from {ctx.Request.RemoteEndPoint?.Address}")`
**优先级**: 低

---

## 7. 硬编码配置化 (Round 1 audit_hardcoded 完整清单的高优子集)

按 audit_hardcoded 报告"重点风险摘要"的 8 项,以及 advisor 建议先做 Sally 4 项 + Idle 2 项。

### 7.1 Sally 4 项
- `SallyDispatcher.cs:30,31,34,35`:
  - `DetectionRadius` (50f) → `Thresholds.SallyDetectionRadius`
  - `InitialSallyGold` (100) → `Thresholds.SallySeedGold`
  - `SallyCooldownHours` (24f) → `Thresholds.SallyCooldownHours`
  - `MinSustainedTicks` (3) → `Thresholds.SallyMinSustainedHours`
- `StSallyPartyComponent.cs:31`: `MaxSallyHours` (12f) → `Thresholds.SallyMaxAwayHours`

### 7.2 Idle 2 项
- `PartyLifecycleManager.cs:36,37`:
  - `IdleHoursBeforeForceReturn` (24) → `Thresholds.IdleForceReturnHours`
  - `IdleHoursBeforeDisband` (36) → `Thresholds.IdleDisbandHours`

### 7.3 Recruiter 经济参数
- `RecruitmentDispatcher.cs:31`: `DefaultInitialGold` (1000)
- `StRecruiterPartyComponent.cs:39,40`: `CostDiscount` (0.5), `DefaultGoldPerRecruit` (10)

### 7.4 RiskAssessmentService 阈值
- `RiskAssessmentService.cs:77-96`: 4 档风险阈值 (10/3/1.5/0.5)

### 7.5 STPartySpeedModel
- `STPartySpeedModel.cs:25`: `SpeedBonusFactor` (0.2)

完整清单 (50+ 项) 见 Round 1 audit_hardcoded agent 报告(对话上下文,已不在文件中)。

**优先级**: 高的可逐步暴露 UI;低的保持硬编码。建议一次只暴露 5-10 项,避免面板过载。

---

## 总览

| 类别 | 项数 | 优先级 |
|---|---|---|
| 政策决策 | 1 | 中 |
| UI/UX | 3 | 低-中 |
| 校验扩展 | 5 | 低-中 |
| 一致性 | 2 (含多处) | 低 |
| 性能 | 2 | 低-中 |
| 诊断 | 1 | 低 |
| 硬编码配置化 | 50+ (建议分批 10 项) | 高(精选)~低 |

合计 ~70 项,实际可 collapse 到 ~15 个 commit (按维度合批)。**不要试图一轮做完所有**,B18 应分多个小 commit。
