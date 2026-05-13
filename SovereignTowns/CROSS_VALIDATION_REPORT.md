# SovereignTowns 社区交叉验证报告

**验证日期**：2026-05-12
**Mod 版本**：v0.0.1
**目标游戏**：Mount & Blade II: Bannerlord v1.3.15
**验证方式**：3 个并行 subagent，分别交叉比对 Nexus Mods / GitHub 上的 ImprovedGarrisons、GarrisonDoSomething、RBM、BannerKings、ButterLib、MCM、BUTR Bannerlord.Module.Template 等社区源码

---

## 验证结论

社区证据**佐证** 6 个核心技术假设无误（见末段 OK 列表），同时**否决** 4 个 CRITICAL 假设 + 7 个 WARNING 偏差。本报告记录所有发现及实际修复。

---

## 🔴 CRITICAL（已修复）

### C1. SyncData 漏洞 — 读档后 `_tracked` 字典空 → 重复创建队伍 + idle 计时失效

| 现状 | 修复 |
|---|---|
| `SovereignTownsCampaignBehavior.SyncData` 完全留空；`PartyLifecycleManager._tracked` 只是 runtime 缓存。读档后 vanilla 恢复所有 CustomParty MobileParty，但 `_tracked` 空 → `CountActive` 永远 0 → `CanCreateAnotherParty` 永远 true → **重复创建巡逻 / 招募 / 调拨队 + IdleHoursBeforeDisband 完全失效**。 | (1) `SovereignTownsCampaignBehavior.RegisterEvents` 增加 `OnGameLoadedEvent` 订阅。<br>(2) 新增 `OnGameLoaded(CampaignGameStarter)` → 调 `_lifecycle.RebuildFromCampaign()`。<br>(3) `PartyLifecycleManager.RebuildFromCampaign()`：清空 `_tracked`，遍历 `MobileParty.AllCustomParties` + `AllPatrolParties`，按 PartyComponent 类型重建（recruiter / transfer / patrol），`LastActiveAt = CampaignTime.Now`。 |

**社区证据**：BannerKings `BKPartyNeedsBehavior` 用 `OnGameLoadedEvent` 遍历 `MobileParty.All*` 重建索引。
**API 验证**：`OnGameLoadedEvent: IMbEvent<CampaignGameStarter>`（已在 `_research/decompiled/CampaignEvents.cs:855` 验证）。

### C2. 日志路径 — Steam C 盘安装下 UAC 写失败

| 现状 | 修复 |
|---|---|
| `Logger.cs` + `DecisionAuditLogger.cs` 用 `ModuleHelper.GetModuleFullPath("SovereignTowns")/Logs` → 落到 Steam 安装目录。C 盘装 Steam 的玩家 UAC 保护 → `UnauthorizedAccessException`，日志写入 100% 失败。 | `_logDir` 改为 `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns`，Audit 走 `...\SovereignTowns\Audit` 子目录。删除 `using TaleWorlds.ModuleManager;`。 |

**社区证据**：ButterLib 官方文档 `By default, the ILogger implementation will write its logs in %GAME CONFIG%/ModLogs/...`。本机 `Documents\Mount and Blade II Bannerlord\Configs\ModLogs\` 已存在（ButterLib / IG / MCM 等都用）。

### C3. SubModule.xml StoryMode 硬依赖 — 拒绝无 StoryMode 的玩家

| 现状 | 修复 |
|---|---|
| `<DependedModules>` 含 `StoryMode` → 没装 StoryMode 的玩家启动器拒绝加载本 mod。 | 从 `<DependedModules>` 移除 `StoryMode`（CustomBattle 本来不在硬依赖）；`<ModulesToLoadAfterThis>` 已存在且已含 StoryMode/CustomBattle，无须新增。 |

**社区证据**：BUTR 官方模板把 StoryMode/CustomBattle 放在 `<ModulesToLoadAfterThis>`。

### C4. MCMIntegration ButterLib FQN 用 internal 类型

| 现状 | 修复 |
|---|---|
| `MCMIntegration.cs` 用 `Bannerlord.ButterLib.SubModuleWrappers2.MBSubModuleBaseExWrapper` 探测 → 该类型是 internal / 不稳定 FQN → `IsAvailable` 永远 false。 | 改为公开类型 `Bannerlord.ButterLib.ButterLibSubModule, Bannerlord.ButterLib`。MCM 的 FQN `MCM.MCMSubModule, MCMv5` 已验证正确，保留不动。 |

---

## 🟡 高价值 WARNING（已修复）

### W5. 手写 regex JSON 解析器多余

Bannerlord 自带 `Newtonsoft.Json.dll` (678 KB)。改造：
- `SovereignTowns.csproj` 加 `<Reference Include="Newtonsoft.Json" HintPath="$(GameBinPath)\Newtonsoft.Json.dll" Private="false">`
- `SovereignTownsCampaignBehavior.ParseLlmConfig` 改为 `JsonConvert.DeserializeObject<LLMConfig>` 一行；删除 3 个 regex helper（`ExtractStringField / ExtractNumberField / ExtractBoolField`）
- `ConfigurationManager` 删除 ~440 行手写 `MiniJson / JsonWriter / ReadRule / WriteRule`，改用 `JsonConvert.SerializeObject(_, Indented + NullValueHandling.Ignore)` + `JsonConvert.DeserializeObject<GlobalConfig>`

### W6. BLSE 用户可绕过 vanilla `<IncompatibleModules>` 软警告 → 残缺存档

`SovereignTownsSubModule.OnBeforeInitialModuleScreenSetAsRoot` 检测到 IG/GDS 启用时，在 `DisplayMessage` 红字之后追加 `throw new InvalidOperationException(...)` 硬终止，防止玩家在退化模式下产生残缺存档。

### W8. TroopClassifier IsCultureAllowed 大小写敏感

`StringComparison.Ordinal` → `OrdinalIgnoreCase`（仅文化判定路径）。Banner Kings 等自定义 culture id 大小写不一致时不再漏判。

---

## 🟡 已记录但未修复的 WARNING（后续小迭代）

| # | 问题 | 备注 |
|---|---|---|
| W7 | `Directory.Build.props` fallback 硬编码 D:\SteamLibrary\... | 用户已经用 `Condition` 允许覆盖，影响仅对协作者；后续改 `$(BANNERLORD_GAME_DIR)` 环境变量 |
| W9 | PatrolManager `OnHourlyTickSettlement` 频率偏高 | 已有"OwnerClan == PlayerClan"过滤，玩家通常 ≤5 城，实际负载可接受；未来可拆 Daily/Hourly |
| W10 | 单 Behavior 巨型分派非社区惯例 | BannerKings 用 20+ 独立 Behavior，本 Mod MVP 阶段可接受 |
| W11 | "RBM 是纯 XML+MissionLogic" 措辞错误（RBM 实际有 CampaignChanges.cs 40KB） | 兼容性结论不变（兵种判定不依赖 stringId），仅注释/文档措辞将来需修订 |
| 建议 | BLSE-style `<DependedModuleMetadata id="ImprovedGarrisons" order="Incompatible" />` | 可为 BLSE 玩家提供启动器层硬阻止，与现有 `<IncompatibleModules>` 互补 |

---

## 🟢 OK — 社区证据佐证的正确假设

- **真实招募流程**：`Town.GarrisonParty.MemberRoster.AddToCounts(char, n, false, woundedN, xp)` 是标准 API，无 vanilla 自动招募引发存档损坏的社区报告
- **`SetMoveGoToSettlement(dest, NavigationType.Default, false)`**：v1.3.15 三参签名正确，陆地 settlement 适用
- **PartyComponent 私有 ctor + static factory**：vanilla `GarrisonPartyComponent` / `CaravanPartyComponent` / `BanditPartyComponent` 都用 `CustomPartyComponent.InitializationArgs`，本 Mod `RecruitingPartyComponent.CreateForTown` / `TransferPartyComponent.CreateForRoute` 符合惯例
- **SaveableTypeDefiner with saveBaseId=100_000_000**：远超社区惯例区间（2-3M）但无冲突；建议 README 注明占用 100,000,000..100,000,255
- **HourlyTickParty 多 Manager 分派性能**：三 Manager 首行 `is not X return;` 类型过滤是 ns 级，中后期千余 party × 3 cast 远低于 vanilla 自身 hourly 负载
- **互斥定位（替代 IG/GDS）**：IG 自身 Nexus 文案承认"the game might crash if using another mod with similar functionality" → 印证替代方案正确

---

## 修复验证

- **统一 `dotnet build`**：0 错误 0 警告（2.07 s）
- **部署 DLL**：`D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\SovereignTowns\bin\Win64_Shipping_Client\SovereignTowns.dll` 142.5 KB（已自动覆盖）

## 实际修改文件清单

| 文件 | 改造 |
|---|---|
| `SubModule.xml` | DependedModules 移除 StoryMode |
| `src/SovereignTowns.csproj` | 加 Newtonsoft.Json Reference |
| `src/SovereignTownsSubModule.cs` | IG/GDS 检测时硬抛 InvalidOperationException |
| `src/Campaign/SovereignTownsCampaignBehavior.cs` | 加 OnGameLoadedEvent；ParseLlmConfig 改 Newtonsoft；删 3 个 regex helper |
| `src/Lifecycle/PartyLifecycleManager.cs` | 新增 `RebuildFromCampaign()` |
| `src/Configuration/ConfigurationManager.cs` | 删 ~440 行手写 JSON，改 Newtonsoft |
| `src/Logging/Logger.cs` | 日志路径迁 Documents/.../ModLogs |
| `src/Audit/DecisionAuditLogger.cs` | 同上，子目录 Audit |
| `src/Integration/MCMIntegration.cs` | ButterLib FQN 改公开类型 |
| `src/Evaluators/TroopClassifier.cs` | IsCultureAllowed 大小写不敏感 |

## 关键未访问资源（验证盲区）

- Nexus Mods IG/GDS 详情页（HTTP 403 / Cloudflare 拦截）→ 未一手核对它们的 SaveBaseId / PartyComponent 实现细节
- Patrols Reborn 源码（仓库未公开）
- Diplomacy `CampaignBehaviors/` 完整列表（GitHub API 401/404）

如需深度对比，建议从 Nexus 手动下载 IG/GDS 的 DLL 用 ilspycmd 反编译验证。
