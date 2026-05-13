# SovereignTowns 完整审查报告

**审查日期**：2026-05-12
**Mod 版本**：v0.0.1（含 4 大功能：分布式巡逻 / 控制面板 / 跨城调拨 / 主动出击）
**目标游戏**：Mount & Blade II Bannerlord v1.3.15
**审查方式**：5 个并行 subagent，分别交叉比对
- A：v1.3.15 API 签名（reflection + IG 反编译双重验证）
- B：GarrisonDoSomething 反编译对比
- C：ImprovedGarrisons 反编译对比
- D：内部 7 项不变量一致性
- E：Nexus / GitHub / docs.bannerlordmodding 社区已知问题

---

## 总评

| 维度 | 结论 |
|---|---|
| 编译 / API | ✅ 0 CRITICAL，与 v1.3.15 公开 API 全部匹配 |
| 替代 IG 功能完整度 | ❌ 招募 / 升级 / 模板路径核心肌肉群是空壳（C1-C4） |
| 替代 GDS 功能完整度 | ⚠️ 方向正确（MapEventEnded 修战后卡死）但触发太弱、半径太小 |
| 存档安全 | ❌ saveBaseId 太低 + 强制依赖陷阱（E1-E2） |
| Gameplay 体验 | ⚠️ 缺三个 GameModel → 玩家经济会受 vanilla 全额 wage 暴击 |
| 边界条件 | ⚠️ 首府失守时 sally 队成孤儿，非首府失守的 in-flight party 无清理 |

**优先级总览**：先修 CRITICAL 1-4（存档安全 + 招募实际工作），再修 5-9（GDS 触发 + GameModel + sally 孤儿）。

---

## 🔴 CRITICAL（必修 — 否则核心功能无法工作或损坏存档）

### CRIT-1（来自 E1）`saveBaseId = 100_000_000` 处于 ButterLib 与社区共识"安全区"之外
- **现象**：ButterLib 占用 `2_002_018_000+` 区间是当前已知最高、最稳的 base。我们用 `100_000_000` 是个低位 8 位数，**任何也用此范围的 mod 会冲突 → 存档损坏**。
- **来源**：`https://github.com/BUTR/Bannerlord.ButterLib/blob/dev/docs/articles/SaveSystem/Overview.md`
- **修复**：改为 `~1_700_000_000` 或 `~1_900_000_000` 区间（避开 ButterLib 的 2_002_018_000）；或迁移到 ButterLib `SyncDataAsJson`（无需 SaveableTypeDefiner）。
- **文件**：`src/SaveSystem/SovereignTownsTypeDefiner.cs`

### CRIT-2（来自 E2）SaveableTypeDefiner 注册自定义类型 → 存档硬依赖本 mod
- **现象**：3 个 `CustomPartyComponent` 子类通过 SaveableTypeDefiner 注册 → 卸 mod 后所有相关 MobileParty `OnBeforeNonReadyObjectsDeleted` NRE。
- **来源**：`https://docs.bannerlordmodding.lt/modding/crashes/`（RBM 同类型问题）
- **修复**：在 README 显式说明"不可卸载"；提供 SafeUninstall 菜单先把所有自定义 party 转回 garrison 再 DestroyParty。已有 `src/Ui/SafeUninstallMenu.cs`，需验证它真的能清光 3 类自定义 party。
- **文件**：`src/Ui/SafeUninstallMenu.cs`、`README`

### CRIT-3（来自 C1）`RecruitingPartyComponent` **实际不招募任何兵员**
- **现象**：RecruitingParty 抵达村庄后，代码只在到家时 `AddToCounts` 转兵到驻军，但**从未真正消费 `notable.VolunteerTypes[bitCode]`**。IG `GarrisonRecruitmentLogic.RecruitFromSettlement:447-461` 是显式 `recruitingParty.AddElementToMemberRoster(notable.VolunteerTypes[bitCode], 1, false)` + `notable.VolunteerTypes[bitCode] = null`，配 `VolunteerModel.MaximumIndexHeroCanRecruitFromHero` 过滤。
- **影响**：整个"首府主导招募"功能形同虚设，玩家感受 = "Recruiter 队在地图上空跑"。
- **修复**：在 `RecruitmentManager` 增加抵达村庄后的招募 tick（参考 IG `GarrisonRecruitmentLogic.cs`）。
- **文件**：`src/Recruitment/RecruitmentManager.cs`

### CRIT-4（来自 C3）驻军升级 XP **来源消失** → 永远升不动
- **现象**：`TroopUpgradeService.TryUpgradeGarrison` 直接读 `roster.GetElementXp(ch)`，但驻军在 vanilla 下每日仅获得极少固定 XP。IG 用 `GarrisonUpgradeLogic.GiveGarrisonExp` 通过 `DailyTroopXpBonusModel` 主动注入 XP。
- **影响**：驻军兵种永远停留在初始 tier，模板配置无意义。
- **修复**：实现 `DailyGarrisonXpInjection`（DailyTickSettlement 内对玩家 town 每日加 XP）。
- **文件**：`src/Upgrades/TroopUpgradeService.cs`

### CRIT-5（来自 B2）`DetectionRadius = 15f` 比 GDS 默认 50 小 3 倍
- **现象**：sally party 触发条件 = 半径内有敌党。15f 在 vanilla 地图上是非常小的距离，多数场景 `FindBestEnemyTarget` 返回 null → **sortie 几乎不会触发**。
- **修复**：默认 `50f`，可在 `ConfigurationManager` 暴露 slider。
- **文件**：`src/SallyForth/SallyForthManager.cs:34`

### CRIT-6（来自 B1）SallyForth 用 `HourlyTickSettlement` 触发 → 启动延迟最坏 24h
- **现象**：玩家在 vanilla 城内停留时游戏会跳过整点 tick（已知现象）。GDS 用 `TickEvent` 自调速（`CampaignTime.Now` 差值 ≥ `AttackInterval`）。
- **修复**：要么改用 `TickEvent` + `CampaignTime` 自调速；要么至少在 `OnHourlyTickParty` / `OnDailyTickSettlement` 内做双兜底。
- **文件**：`src/SallyForth/SallyForthManager.cs`

### CRIT-7（来自 D-I3）sally party 在首府失守时不会被清理
- **现象**：`SallyForthManager.MapEventEnded` 仅检查 `home.OwnerClan != Clan.PlayerClan` 后 return，**不解散** → 敌占区出现己方残留 party。`OnHourlyTickParty` 同问题。
- **修复**：失守判定 → 立即 `DestroyPartyAction.Apply(null, party)` 或 SetMoveGoToSettlement(回新首府)。
- **文件**：`src/SallyForth/SallyForthManager.cs:107-150`

---

## 🟡 WARNING（高价值，建议在 CRITICAL 之后立刻处理）

### WARN-1（来自 C2）Prisoner-to-Recruit 完全缺失
- IG `RecruitPrisonersInSettlement` + `PrisonerRecruitmentCalculationModel` 一条完整产线。我们零代码 → 替代 IG 后这条收入消失。
- 文件：`src/Recruitment/RecruitmentManager.cs`

### WARN-2（来自 C9）缺三个 GameModel（PartySize / Speed / Cost）→ 玩家经济暴击
- IG 的 `GarrisonpartySizeLimitModel` / `GarrisonSpeedModel` / `GarrisonCostModel` 全无 → 我们的 Recruiter / Transfer / SallyForth party 走 vanilla 默认 30 兵上限 + 全额 wage 扣家族金币。
- 文件：新增 `src/Models/STGarrisonCostModel.cs` / `STPartySizeLimitModel.cs` / `STSpeedModel.cs`，在 `OnGameLoaded` 内 `gameStarter.AddModel(...)` 注册。

### WARN-3（来自 C7）PatrolManager 状态机比 IG 弱很多
- IG 有 6 个状态（Patrol/Trade/ReturnToRegion/PrisonerTurnIn/Heal/ClearHideout） + 动态 `patrolRadius` 按 BoundVillages 距离。我们 5 个 Order + 硬编码 25f。
- `Defense` Order 只 `SetInitiative(0.3, 0.7, 4h)` —— 没真正 `SetMoveDefendSettlement`。
- 文件：`src/Patrol/PatrolManager.cs`

### WARN-4（来自 C6）配置面板缺 `SetForceVsync` / `LoadingWindow` / `PromptIsOpen` 守卫
- IG `ConfigMenuGauntletScreen` 三个 lifecycle hooks 我们都没。卡顿 + 切面板时 loading window 不恢复。
- 文件：`src/Ui/ConfigScreen/SovereignTownsConfigScreen.cs`

### WARN-5（来自 D-I5）UI Ratio sum 验证缺陷
- UI 允许写入 `Cavalry + Infantry + Archer ≠ 1.0`，Save 写盘，**下次启动 ValidateConfig fail → 整份 GlobalConfig fallback 默认**，玩家感知"我的设置丢了"。
- 修复：UI 增加 sum live 显示 + `Save()` 前调 `ValidateConfig` 拒绝写盘。
- 文件：`src/Configuration/ConfigurationManager.cs`、`src/Ui/ConfigScreen/SovereignTownsConfigVM.cs`

### WARN-6（来自 D-I5）UI 字段覆盖率仅 30%
- `TownGarrisonRule` 有 20+ 字段，UI 只暴露 6 个。Crossbow/Thrower Ratio、MinTier/MaxTier、Wartime/Peacetime Multiplier、FoodSafetyThreshold 等只能手编 JSON。
- 修复：UI 加二级面板"Advanced Globals"。

### WARN-7（来自 D-I2）`new Random()` 随机首府选择非确定性
- 影响存档可重现性。
- 修复：用 `MBRandom`（vanilla 的可控随机源）或 seed = `Campaign.Current.UniqueGameId.GetHashCode()`。
- 文件：`src/Capital/CapitalManager.cs:31`

### WARN-8（来自 B-WarPartyComponent.OnFinalize）sally party 战场全歼时兵蒸发
- GDS 用 `WarPartyComponentPatch.OnFinalizePrefix` 拦截 finalize 把存活兵塞回 garrison。我们的 `TransferAndDestroy` 仅在 `LastVisitedSettlement == home` 时触发 → 战场被歼灭路径完全丢失兵员。
- 修复：订阅 `MobilePartyDestroyed` 事件，在 destroyed 时尝试 transfer 幸存 roster 回 home。
- 文件：`src/SallyForth/SallyForthManager.cs`

### WARN-9（来自 B-MobilePartyPatch.get_TotalWagePrefix）sally 跨整点 wage tick 扣空 town 金库
- 我们的 `MaxSallyHours=12h` 可能跨过 vanilla 周薪结算 tick → town 金库被扣全额工资。
- 修复：在 sally party 上覆盖 `WagePaymentLimit` 模型，或在 daily tick 内对 sally party 补偿。
- 文件：新增 `STWageModel` 或 patch 入 `STGarrisonCostModel`

### WARN-10（来自 E-DestroyPartyAction）`DestroyPartyAction.Apply` 在战中 / 加载中 NRE
- 社区报告：`MobileParty.PreAfterLoad()` 与 `BesiegerCamp.RemoveSiegePartyInternal` 链路上 NRE。
- 修复：所有 `DestroyPartyAction.Apply(null, p)` 前加 `if (p.MapEvent != null) { schedule for MapEventEnded; return; }`。
- 文件：`src/SallyForth/SallyForthManager.cs:289,447`、`src/Lifecycle/PartyLifecycleManager.cs`

### WARN-11（来自 D-I2）非首府失守的 in-flight party 清理缺失
- CapitalManager 只在 *当前首府失守* 时 `MigrateAllOrDisband`。非首府失守且有 transfer/recruiter 挂靠该城时 → 这些 party home 已易主，要等 lifecycle 24~36h idle 才解散。
- 修复：`OnSettlementOwnerChanged` 对任何玩家失守城都触发 `_lifecycle.MigrateAllOrDisband(settlement)`。
- 文件：`src/Capital/CapitalManager.cs:121-191`

### WARN-12（来自 A）`GiveGoldAction.ApplyForSettlementToParty` 漏 `disableNotification=true`
- 创建出击 / 招募队时玩家屏幕弹通知"settlement 给了 party N 金币"。
- 修复：`...CreateForTown(homeTown, gold, disableNotification: true)`
- 文件：`src/Parties/RecruitingPartyComponent.cs`、`TransferPartyComponent.cs`、`SallyForthPartyComponent.cs`

### WARN-13（来自 A）`TroopRoster.AddToCounts` 隐式默认参数语义不一致
- `GarrisonTransferManager.cs:240` 用 2 参，其它处用 5 参显式 `removeDepleted=false`。
- 修复：显式补齐 5 参。

### WARN-14（来自 E）SaveableField vs SaveableProperty 不可混用，否则数据静默丢失
- 需要审查 3 个 PartyComponent 子类，全部统一为 `SaveableField`。
- 文件：`src/Parties/*.cs`

### WARN-15（来自 C8）SallyForth 缺 prisoner-tracking
- IG `OrderEscort` 在被护送者被俘 → 改 engage 俘虏方。我们的 SallyForth 目标失效就放手。
- 设计取舍，可暂不修。

---

## 🟢 OK — 经审查证实正确的做法

- **CampaignEvents 订阅 + AddNonSerializedListener**：与 BannerKings 生产代码一致
- **CustomPartyComponent ctor 9 参 + `InitializationArgs` 5 参** —— reflection 验证 + IG 印证
- **`DestroyPartyAction.Apply(null, party)` 双参** —— 比 `RemoveParty()` 安全（社区共识）
- **`MapEventEnded` 订阅修战后卡死战场 bug** —— GDS 没订阅是它"卡死"根因，我们方向正确
- **CustomPartyComponent 不占 `Clan.WarPartyComponents` slot** —— 天然规避 GDS 的 `DefaultClanTierModelPatch`
- **PartyOwner 动态返回 `OwnerClan.Leader`** —— 自动跟随易主，比 GDS `ActualClan` 锁定更稳健
- **stringId 唯一化（前缀+settlement+ticks）** —— 比 IG 固定 stringId 更不易冲突
- **OnGameLoaded + RebuildFromCampaign** —— BannerKings 同款模式
- **日志路径迁 `Documents/Mount and Blade II Bannerlord/Configs/ModLogs/SovereignTowns`** —— 与 ButterLib 一致
- **SubModule.xml StoryMode 移出 DependedModules** —— 与 BUTR 模板一致
- **互斥模块硬抛 InvalidOperationException** —— 比 vanilla `<IncompatibleModules>` 软警告强（防 BLSE 绕过）
- **CapitalManager 短路在每个 manager 的入口** —— TownGarrison / Patrol / CastleSupport / SallyForth 都有
- **`AvoidHostileActions = true`** 用于 Recruit/Transfer 与 IG 一致
- **`PartyComponent is X` 模式重建 `_tracked`** —— 比反射快
- **Newtonsoft.Json 用游戏自带** —— 替代 440 行手写 regex JSON，正确

---

## 修复优先级建议（顺序执行）

| 阶段 | 任务 | 估时 |
|---|---|---|
| **P0 必修** | CRIT-1 改 saveBaseId 到 ~1.9B 区间 | 5 min |
| | CRIT-7 sally 失守清理 | 30 min |
| | CRIT-5 DetectionRadius 15→50 + 加 slider | 15 min |
| | CRIT-6 SallyForth 改 TickEvent 自调速 或 加 daily 兜底 | 1h |
| **P1 核心功能** | CRIT-3 RecruitingParty 实际招募 | 3h |
| | CRIT-4 驻军 XP 注入 | 1h |
| | WARN-1 Prisoner-to-Recruit | 2h |
| | WARN-2 三个 GameModel | 3h |
| **P2 体验** | WARN-3 PatrolManager 状态机 | 4h |
| | WARN-5 UI Ratio sum 校验 | 1h |
| | WARN-6 UI Advanced Globals | 2h |
| | WARN-7 MBRandom | 15 min |
| **P3 鲁棒性** | WARN-8/9 战场全歼兵员回收 + wage 防扣 | 2h |
| | WARN-10 DestroyPartyAction in-MapEvent 防护 | 30 min |
| | WARN-11 非首府失守清理 | 30 min |
| | WARN-12/13/14 代码品质 | 30 min |
| | CRIT-2 SafeUninstall 验证 | 30 min |

---

## 审查盲区（未验证项）

- IG `_research` 完整可读但 GDS ConfuserEx 加密 → GDS 4-档概率/文化加成的实际数值仅从元数据推断
- `StartFindingLocatablesAroundPosition` 的 vanilla caller 未实际 dnSpy 查看
- `NavigationType.Default` 在围攻城市的行为未验证
- `OnSessionLaunched / OnGameLoaded / SyncData` 三者先后顺序的官方文档未找到（BannerKings 防御性写法已成事实标准）

## 关键参考资源

- ButterLib SaveSystem: https://github.com/BUTR/Bannerlord.ButterLib/blob/dev/docs/articles/SaveSystem/Overview.md
- Bannerlord Modding Docs: https://docs.bannerlordmodding.com/_csharp-api/savesystem/
- BannerlordModding.LT crashes: https://docs.bannerlordmodding.lt/modding/crashes/
- BannerKings PartyNeedsBehavior（OnGameLoaded 重建索引范例）: https://github.com/R-Vaccari/bannerlord-banner-kings/blob/main/BannerKings/Behaviours/PartyNeeds/BKPartyNeedsBehavior.cs
- IG 反编译（本地）: `_research/ImprovedGarrisons/**`
- GDS 反编译 + 元数据（本地）: `_research/GarrisonDoSomething/**`
