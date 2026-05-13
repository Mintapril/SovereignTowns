# 第一阶段：资料调查清单

> 状态：**未开始**。本文件仅描述需要查阅的资料、需要确认的不确定点、以及风险预估。
> 在完成下列资料调查并产出"已验证 API 清单"之前，**不会编写任何实现代码**。

---

## 0. 当前项目实际状态

经检查 `C:\Users\rangt\Desktop\workspace`：

- 目录为空。
- **没有** Bannerlord Mod 项目骨架（无 `*.csproj`、无 `SubModule.xml`、无 `Module.xml`）。
- **没有** 对 `TaleWorlds.*` 程序集的引用。
- **没有** Harmony / MCM 依赖配置。
- **没有** 现有的 CampaignBehavior 代码可参考。

⚠️ **结论**：在做任何代码工作前，至少需要：
1. 确认本机是否安装了 Bannerlord 主程序（用于取得 `TaleWorlds.*.dll` 引用）。
2. 确认本机是否安装了 Visual Studio / .NET SDK（Bannerlord 当前主线版本基于 .NET Framework 4.7.2 编译，1.2.x 之后部分子模块迁移到 .NET 6/7 — 这点需要核实）。
3. 取得 Bannerlord 当前游戏版本号（API surface 在 1.0 → 1.2 → 1.3 之间有显著变化，必须先锁定目标版本）。

---

## 1. 需要读取的 Bannerlord 官方资料

> 注："官方"在 Bannerlord 语境下基本等同于 **TaleWorlds 自家发布的 Modding Documentation + 游戏 DLL 反编译结果**。
> TaleWorlds 没有像 Unity 那样完整的 API Reference 网站；很多内容只能从 DLL 反编译里来。

### 1.1 TaleWorlds 官方 Modding 文档（必读）
- TaleWorlds Modding Documentation 主站（docs.bannerlordmodding.lt 是社区站，官方资料相对稀疏；需要确认哪些章节最新）。
- TaleWorlds Forums 的 "Modding Discussion" 与 "Modding Guides" 板块（开发者会在这里贴变更说明）。
- Steam 创意工坊 / Nexus 上 TaleWorlds 自己发布的示例 Mod（如果有）。

❓ **不确定**：TaleWorlds 是否对外发布过 CampaignBehavior 的官方教程。需要在调查时确认资料新旧。

### 1.2 游戏自身 DLL（必须反编译查阅）
位于 `<Bannerlord 安装目录>/bin/Win64_Shipping_Client/`：

| DLL | 关注的命名空间 / 类（仅"我假设它在这里"，需要查证） |
|---|---|
| `TaleWorlds.CampaignSystem.dll` | `Campaign`, `CampaignBehaviorBase`, `MobileParty`, `Settlement`, `Town`, `Village`, `Hero`, `Clan`, `TroopRoster`, `PartyBase`, `CampaignTime`, `CampaignEvents` |
| `TaleWorlds.Core.dll` | `CharacterObject`, `ItemRoster`, `ItemObject`, `BasicCharacterObject` |
| `TaleWorlds.ObjectSystem.dll` | `MBObjectManager` — 存档关键 |
| `TaleWorlds.SaveSystem.dll` | `SaveableTypeDefiner`, `SaveableField`, `SyncData` — 自定义存档字段必读 |
| `TaleWorlds.Library.dll` | `InformationManager`, 数学工具 |
| `TaleWorlds.Engine.dll` | 引擎入口（一般不直接动） |
| `SandBox.dll` / `SandBoxCore.dll` | 沙盒侧实现细节（巡逻 / 招募 / AI 决策可能在这里） |
| `StoryMode.dll` | 主线相关，与本 Mod 大概率无关 |

工具：dnSpy / ILSpy / dotPeek。
❓ **不确定**：以上 DLL 的存在与命名以 1.2.x 为参考记忆。**必须以本机实际版本为准重新枚举**。

### 1.3 必须搞清楚的官方机制
按本 Mod 功能需要，依次确认：

1. **CampaignBehaviorBase 生命周期**
   - 注册方式（`CampaignGameStarter.AddBehavior`）
   - 事件订阅 API：`CampaignEvents.DailyTickEvent`、`HourlyTickEvent`、`OnSettlementOwnerChangedEvent`、`OnPartyDestroyedEvent` 等
   - `SyncData` 存档机制
2. **真实招募 API**
   - `Recruit*` 类、村庄/城镇里 `NotablesRoster` / `RecruitableTroops` 之类的字段
   - 玩家招募走的同一条接口（必须找到）
3. **MobileParty 创建**
   - `MobileParty.CreateParty` / `MBObjectManager.Instance.CreateObject<MobileParty>()`
   - "Custom Party" 模板：`PartyTemplateObject`
   - 是否能创建脱离任何 `Clan`/`Hero` 的"无主队伍" — **关键不确定点**
4. **驻军（Garrison）**
   - `Town.GarrisonParty` 的类型与可写性
   - 增减驻军是否走 `MobilePartyHelper` 还是直接改 `TroopRoster`
5. **Party AI**
   - `MobilePartyAi`、`AiBehavior` 枚举、`SetMoveGoToSettlement` 等
6. **存档系统**
   - `SaveableTypeDefiner` 实现
   - 自定义类如何被序列化
   - 删除 Mod 后存档兼容性
7. **MCM (Mod Configuration Menu)**
   - 是否兼容当前游戏版本
   - 注册配置项的方式

---

## 2. 需要分析的现有 Mod（按功能分类）

> 目标：找出真实可用的实现模式，避免重新踩坑。逐个反编译 / 读源码确认。

### 2.1 自动招募 / 自动驻军
- **Recruiter** by Vermilion（如果还有人在维护 1.2 版本）
- **Auto Recruiter** / **Garrison Recruiter**（搜索名时可能多版本）
- **Improved Garrisons**（如果作者放出 1.2 适配）
- **Garrison Do Something**（小工具，常用于学 API）

### 2.2 巡逻 / 防御队
- **Bannerlord Tweaks** 系列里的巡逻队相关条目
- **Calradia Awaits**（含巡逻 AI 行为）
- **Patrols of Calradia** 或类似名（需逐个核对，名字可能漂移）

### 2.3 城镇 / 王国管理增强
- **Better Garrisons / Bigger Garrisons**
- **Realistic Battle Mod**（虽是战斗 Mod，但内部 CampaignBehavior 写法值得借鉴）
- **Diplomacy**（成熟项目，存档迁移、行为注册、MCM 接入都规范）

### 2.4 后勤 / 补给
- **Recruit Everywhere** / **Recruit From Garrisons**
- **Calradia Expanded Kingdoms**（也接触兵种文化过滤）

### 2.5 Party AI 模板
- **Bannerlord Coop**（多人协作 Mod，反复操作 MobileParty，参考价值高）
- **Custom Spawns API**（明确处理"凭空生成 vs 真实生成"边界）

❓ **不确定**：以上 Mod 的当前维护状态、是否还兼容目标 1.2.x 版本。调查阶段必须逐个去 Nexus / GitHub 确认最近更新时间。

### 2.6 反面教材（要看出"它们错在哪"）
- 任何使用 `AddTroopToCounts` 直接给驻军塞兵的 Mod — 我们要明确知道为什么这违反"真实招募"原则、以及它在 Patch Notes 里造成过什么问题。
- 任何把 `MobileParty` 创建得太多导致大地图卡顿的 Mod — 用来定我们的 PartyLifecycle 上限。

---

## 3. 当前项目缺少的依赖

需要在 MVP 0 阶段先解决以下任意之一才能开工：

1. **Bannerlord 游戏本体**（用于 `bin\Win64_Shipping_Client\TaleWorlds.*.dll`）。
2. **Bannerlord Module Loader 项目骨架**：
   - `Modules/<ModName>/SubModule.xml`
   - `Modules/<ModName>/bin/Win64_Shipping_Client/<ModName>.dll`
3. **`MBSubModuleBase` 入口类**（在 `TaleWorlds.MountAndBlade.dll`）。
4. **Harmony**（`0Harmony.dll`，社区一般用 `Bannerlord.Harmony` 子模块发布）—— 只有在确认必须 Patch 时才引入。
5. **MCMv5**（`MCMv5` / `MCMv4` 取决于游戏版本）—— 配置 UI 用。
6. **Mod Builder**（`Bannerlord.BuildResources` / `Bannerlord.ReferenceAssemblies` NuGet）—— 可以省去手动复制 DLL 的麻烦。

❓ **需要用户确认**：
- 目标 Bannerlord 版本号？（决定 .NET 运行时和 MCM 大版本）
- 是否只考虑原版战役？还是必须兼容某些大型 Mod（Realm of Thrones / Calradia Expanded Kingdoms 等）？
- 是否同意引入 Harmony？

---

## 4. 存在不确定性的系统

下列点 **在没有反编译 / 没看到实际 API 之前**，绝不会写代码：

| # | 不确定项 | 为什么关键 |
|---|---|---|
| U1 | 是否能创建"无主 MobileParty"（没有 owner clan 也合法） | 决定"征兵队 / 巡逻队"能不能脱离玩家氏族独立存在；如果不行，必须挂靠玩家氏族，会影响家族容量上限 |
| U2 | `Town.GarrisonParty` 的写入路径 | 决定"返回城镇后兵员加入驻军"如何实现；走错了就是"虚空 AddTroop" |
| U3 | 真实招募 API 的最小调用单元 | 必须找到"玩家在村庄按下招募按钮"实际调用的方法链；否则"真实招募"就只是口号 |
| U4 | 自定义 Behavior 的 `SyncData` 边界 | 决定哪些字段能存盘、哪些只能在内存。错误会导致存档膨胀或读档丢失 |
| U5 | `CampaignTime` 调度精度 | DailyTick / HourlyTick 的实际开销，决定我们能不能每小时做风险评估 |
| U6 | `PartyAi.SetMoveGoToSettlement` 在路径阻塞时的行为 | 决定征兵队会不会卡死 |
| U7 | 围城 / 战时事件触发器 | 决定"敌军接近时禁止调拨"的判定如何挂钩官方事件 |
| U8 | "兵种文化 / 阵营过滤"的真实数据来源 | `CharacterObject.Culture` 是否就是兵种文化？还是要追溯到 `BasicCharacterObject` / `Occupation`？ |
| U9 | 卸载 Mod 后的存档处置 | 我们写入的 `MobileParty` 和 `CampaignBehavior` 字段，移除 Mod 后是否安全（TW 的存档系统对"找不到的类型"如何处理） |
| U10 | MCM 在目标版本是否可用 | 决定第一版要不要走 XML / JSON 兜底 |
| U11 | LLM 的网络调用是否会阻塞 Tick | C# 这边必须异步，不能在 Tick 线程同步等待 HTTP |

---

## 5. 哪些功能可能需要 Harmony Patch

> 原则：能用事件就不用 Patch；Patch 越少越好。下表是**预判**，不是承诺。

| 功能 | 是否需要 Harmony？ | 理由 |
|---|---|---|
| 监听城镇所有者变化 | 否 | `CampaignEvents.OnSettlementOwnerChangedEvent` 存在的话直接订阅 |
| 创建自定义 MobileParty | 否（**待验证**） | 应当直接走 `MBObjectManager.Instance.CreateObject<MobileParty>` |
| 修改驻军 TroopRoster | 否 | 直接操作 `Town.GarrisonParty.MemberRoster` — 待验证 |
| "真实招募"流程嵌入 | 可能否 | 如果存在公开的 `RecruitTroopsFromNotable(...)` 类 API |
| 阻止驻军被官方 AI 抽空 | **可能需要** | 如果官方 AI 仍在我们的征兵队回程时拉走兵 — 需要 Patch `SettlementGarrisonController` 之类的内部方法 |
| 自定义 Party 不被原版征兵 AI 接管 | **可能需要** | 防止 TW 内部 AI 把我们的征兵队按"小队需补给"逻辑误调度 |
| 围城时禁止调拨 | 否 | 可通过 `MapEvent` 状态判断 |
| MCM 接入 | 否 | MCM 自有 API |
| LLM 接入 | 否 | 纯外部网络层 |

❓ **不确定**：是否存在更隐蔽的 Patch 需求（比如官方代码中"驻军最低阈值"硬编码）—— 需要反编译后定位。

---

## 6. 性能 / 存档风险预判

### 6.1 性能风险
- **R-P1** 每小时 Tick 时遍历所有玩家自有城镇 + 全部 MobileParty 做风险评估 → **O(N×M)** 风险。
  - 缓解：分桶 / 增量评估 / 仅在事件触发时局部重算。
- **R-P2** 征兵队 + 巡逻队 + 调拨队同时存在 → MobileParty 总数膨胀，大地图寻路压力上升。
  - 缓解：硬上限（例如每个城镇最多 1 征兵队 + 1 巡逻队 + 队伍全局上限 N）。
- **R-P3** LLM 调用必须异步 + 节流，不能阻塞主线程。
- **R-P4** 调试日志若按 Tick 写入会拖性能，必须分级 + 异步落盘。

### 6.2 存档风险
- **R-S1** 在 `CampaignBehavior` 里直接持有 `MobileParty` 引用：必须用 `SyncData` 包裹，不能用 C# 默认序列化。
- **R-S2** 自定义类必须由 `SaveableTypeDefiner` 注册并分配 **稳定 ID**。ID 冲突会导致整个存档无法读取。
- **R-S3** 移除 Mod 后：所有我们创建的 `MobileParty` 会成为"未知 owner clan"或孤儿；必须确认 TW 存档系统是否会自动剔除。
- **R-S4** "城镇 → 城堡调兵"中转期间，如果存档保存：必须有"中转中兵员"的明确归属，否则丢人。
- **R-S5** 配置文件版本迁移：MVP 5 之后会有多版本配置共存，必须从一开始就写 `configVersion` 字段。

### 6.3 兼容性风险
- **R-C1** 与 Diplomacy / Realm 系大型 Mod 的事件订阅顺序问题。
- **R-C2** 与其他驻军 Mod 同时启用：双重招募 / 双重调拨会把驻军拉穿。

---

## 7. 下一步行动建议（请用户确认后再继续）

在我开始第二阶段（可行性报告）之前，需要用户先回答：

1. **目标 Bannerlord 版本号**？（例：1.2.12 / 1.3.x）
2. **Bannerlord 安装路径**？我需要那里的 `TaleWorlds.*.dll` 来做反编译核对。
3. **是否同意引入 Harmony**？（默认否，仅在必要点引入）
4. **是否同意引入 MCM 依赖**？还是先用 XML/JSON 走兜底？
5. **是否需要兼容大型 Overhaul Mod**（Realm of Thrones / CEK 等）？
6. **LLM 提供商**？（本地 Ollama / OpenAI 兼容接口 / Anthropic / 关闭）
7. 是否同意在 `workspace/` 下建一个 **空的 Mod 骨架**（仅 `SubModule.xml` + 空 `MBSubModuleBase`），用来验证编译 + 加载链路通畅，**不包含任何业务逻辑**？

只有 1、2、7 是阻塞项；3–6 可以延后到 MVP 选项里再选。

---

## 附录 A：调查阶段的 Deliverable

资料调查阶段结束时，会产出：

- `RESEARCH_FINDINGS.md` — 已验证 API 清单 + 源码引用位置 + 版本号锚定
- `UNCERTAINTY_LOG.md` — U1–U11 每项的查证结论或仍存疑标记
- `MOD_SURVEY.md` — 第 2 节列出的 Mod 逐个看完后的笔记
- `RISK_REGISTER.md` — R-P*/R-S*/R-C* 的最终版

完成上述四份文档之前，不进入第二阶段（可行性报告），更不进入代码。
