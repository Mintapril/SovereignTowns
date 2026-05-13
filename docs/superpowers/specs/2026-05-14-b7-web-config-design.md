# B7 — 网页前端 + 本地 HTTP server 取代游戏内控制面板

**日期**：2026-05-14
**触发**：用户判断 Gauntlet UI 设计天花板低、士兵模板编辑器太简陋；改用浏览器作为唯一控制面板入口，mod 端只读 JSON。
**范围**：删除所有 Gauntlet UI；新增本地 HTTP server + 静态网页前端 + 兵种 dump + MCM 简化。
**承接**：B6 的 5-tab Gauntlet 工作（已 land 到 master）会在 Phase 2 全部撤掉。

---

## 1. 目标

**架构调整**：游戏内不再有控制面板，玩家通过 MCM 一个按钮拉起浏览器 → 网页前端 fetch mod 内 HttpListener → 编辑 + Save → mod 重读 JSON → 下次 tick 生效。

### 用户明确决策（B7 brainstorm 三连答）
1. 网页↔mod 路径：**C. mod 内置 HTTP server**
2. 现有 Gauntlet UI：**全都删**
3. 启动方式：**先写 spec，设计完再动手**

---

## 2. 非目标

- 不动 Manager 层 / 决策逻辑 / 存档结构
- 不引入 .NET 包依赖（用 BCL 内置 `HttpListener`）
- 不做账户/多人/远程访问（127.0.0.1 only）
- Phase 1 不做前端工程化（不用 React/Vite，Alpine + Tailwind 都走 CDN，零 Node 构建链）
- 不动 LLM 子系统

---

## 3. 总体架构

```
┌─────────────────────────────────────┐
│  Mount & Blade II Bannerlord (game) │
│  ┌─────────────────────────────────┐│
│  │ SovereignTowns mod              ││
│  │  ┌──────────────────────────┐   ││
│  │  │ WebConfigServer          │   ││
│  │  │  HttpListener            │   ││
│  │  │  GET /api/config         │◄──┼───────┐
│  │  │  PUT /api/config         │   ││      │
│  │  │  GET /api/troops         │   ││      │
│  │  │  GET /api/settlements    │   ││      │
│  │  │  GET /api/status         │   ││      │
│  │  │  GET /webui/*            │   ││      │
│  │  └──────────────────────────┘   ││      │
│  │  ┌──────────────────────────┐   ││      │
│  │  │ ConfigurationManager     │   ││      │
│  │  │  Load() / Save()         │   ││      │
│  │  └──────────────────────────┘   ││      │
│  │  ┌──────────────────────────┐   ││      │
│  │  │ TroopDumper              │   ││      │
│  │  │  OnGameStart → troops.   │   ││      │
│  │  │  json                    │   ││      │
│  │  └──────────────────────────┘   ││      │
│  └─────────────────────────────────┘│      │
│                                     │      │
│  MCM:                               │      │
│    [总开关] [打开网页配置] [重载]   │      │
└─────────────────────────────────────┘      │
                                             │
            ┌────────────────────────────────┴───┐
            │ Browser (Chrome/Edge/Firefox)      │
            │   http://127.0.0.1:<port>/         │
            │   - Alpine.js + Tailwind           │
            │   - 5 tab + ExactTroop picker      │
            │   - TrainingTemplate Apply         │
            └────────────────────────────────────┘
```

---

## 4. mod 端：WebConfigServer

### 4.1 文件结构
```
SovereignTowns/src/WebConfig/
├── WebConfigServer.cs        # HttpListener 启动 / 关闭 / 路由
├── WebConfigEndpoints.cs     # 各 endpoint 处理函数
├── WebConfigAuth.cs          # token 生成与校验
└── TroopDumper.cs            # OnGameStart 时 dump troops.json
```

### 4.2 生命周期
- `SovereignTownsSubModule.OnSubModuleLoad`：构造 `WebConfigServer` 实例（不启动）
- `OnGameStart(game, starter)`：dump 兵种到 `troops.json`（即时数据，含玩家加载的其他 mod 兵种）
- `OnSessionLaunched`：启动 server（如果 MCM 没禁用），写 `port.txt` + `auth.txt`
- 游戏关闭（`OnSubModuleUnloaded`）：停止 server

### 4.3 端口策略
- 默认尝试 41763
- 如被占，逐步 +1 直到找到空闲（最多 50 次）
- 写入 `Documents/Mount and Blade II Bannerlord/Configs/SovereignTowns/port.txt`
- 前端 onLoad 先 fetch `file:///Users/.../port.txt`（实际通过 HttpListener serve）或硬编码端口
- 由于浏览器同源策略，前端无法直接读 file://，所以 server 自己 serve 一个固定 bootstrap：玩家从 MCM 点的 URL 已经含端口（`http://127.0.0.1:41763/`），后续 fetch 走相对路径即可
- **结论**：不需要 port.txt 给前端用，仅日志记录用

### 4.4 鉴权
**威胁模型**：本机其他进程（如其他 mod、其他应用、潜在 malware）能 fetch `http://127.0.0.1:port/` 并改写玩家配置。

**方案**：HMAC-style token
- server 启动时生成 random 32 字节 token（base64）
- 写入 `Documents/.../SovereignTowns/auth.txt`（仅当前 Windows 用户可读 ACL —— .NET 默认）
- 玩家从 MCM 点「打开网页配置」时，URL 带 token：`http://127.0.0.1:port/?t=<token>`
- 前端 onLoad 读 URL 参数，存入 sessionStorage，后续所有 fetch 加 header `X-ST-Token: <token>`
- 任何 API 请求 token 不匹配 → 401

注：玩家如果手动复制 URL 到其他浏览器，没有 token 就用不了 —— 必须从 MCM 入口进入。可接受。

### 4.5 API
所有 endpoint 都校验 token + content-type。

| Method | Path | Body | Response |
|---|---|---|---|
| GET  | `/api/config` | — | 当前 `global.json` 的 JSON object |
| PUT  | `/api/config` | full new JSON object | `{ ok: true }` 或 `{ ok: false, reason: "..." }` |
| GET  | `/api/troops` | — | 当前 `troops.json` 内容 |
| GET  | `/api/settlements` | — | `[{stringId, name, isCastle}...]` 玩家所拥有的城/堡 |
| GET  | `/api/status` | — | `{capital: "...", activeParties: 3, llmEnabled: false, ...}` 只读统计 |
| POST | `/api/reload` | — | 立即 `ConfigurationManager.Load()` 重读磁盘（PUT 已经会做，仅手动 retry 用） |
| GET  | `/` 或 `/webui/*` | — | 静态文件 serve from `Modules/SovereignTowns/WebUI/` |

**PUT 处理**：
1. 接收 JSON
2. 反序列化到 `GlobalConfig`，失败 → 400
3. 调 `ConfigurationManager.ValidateRule()`，失败 → 422 + reason
4. 写入磁盘（`ConfigurationManager.Save()`）
5. 立即 `ConfigurationManager.Load()`（触发 Manager 下一次 tick 读新值）
6. 返回 `{ ok: true }`

### 4.6 安全收窄
- HttpListener 前缀强制 `http://127.0.0.1:port/`（不能 + 也不能 *，否则需要 admin）
- 拒绝 `Host` header 非 `127.0.0.1:port` 的请求（防 DNS rebinding）
- 拒绝 `Origin` 非空且不是 `http://127.0.0.1:port` 的请求（防其他网页攻击）
- 所有非 token 请求一律 401，含 OPTIONS preflight

### 4.7 错误处理
- HttpListener.Start() 失败（端口冲突、防火墙弹窗、UAC 提示）→ Logger.Error + 不影响 mod 主体功能
- API 内部 throw → 500 JSON `{error: "..."}` + Logger.Error，绝不影响游戏主循环
- 整个 server 跑在独立 thread（HttpListener async 模型），与 Bannerlord 主线程隔离

---

## 5. 兵种 dump（TroopDumper）

### 5.1 触发
`OnGameStart(game, starter)`：每次进存档 dump 一次。

### 5.2 收集
```csharp
foreach (var co in MBObjectManager.Instance.GetObjectTypeList<CharacterObject>())
{
    if (!co.IsBasicTroop) continue;            // 跳过 hero / 玩家
    if (co.IsHero) continue;
    if (string.IsNullOrEmpty(co.StringId)) continue;
    list.Add(new {
        id = co.StringId,
        name = co.Name?.ToString() ?? co.StringId,
        culture = co.Culture?.StringId ?? "",
        tier = co.Tier,
        type = ClassifyTroopType(co),          // "infantry"/"archer"/"cavalry"/"crossbow"/"thrower"
        isMounted = co.IsMounted,
        isRanged = co.IsRanged,
    });
}
```

### 5.3 输出
`Documents/.../SovereignTowns/troops.json`：
```json
{
  "schemaVersion": 1,
  "dumpedAt": "2026-05-14T00:23:11Z",
  "troops": [
    { "id":"imperial_legionary","name":"帝国军团士兵","culture":"empire","tier":4,"type":"infantry","isMounted":false,"isRanged":false },
    ...
  ]
}
```

### 5.4 RBM/其他 mod 兼容
直接遍历 `CharacterObject.All` 自然包含其他 mod 加的兵种；ClassifyTroopType 使用 `IsMounted`/`IsRanged`/`UsesCrossbow` 等 vanilla API（B1/B2 已验证）。

---

## 6. 前端：单文件 HTML

### 6.1 文件
`SovereignTowns/SovereignTowns/WebUI/index.html`（单文件，约 1500 行）

### 6.2 依赖（全部 CDN，无构建）
- Alpine.js v3（响应式 + 模板，约 14KB）
- Tailwind CSS v3 CDN 模式（约 100KB，可裁剪）
- 不引入 React/Vue/Vite

### 6.3 部署
- 静态文件由 mod 内 HttpListener serve
- 路径：`http://127.0.0.1:port/index.html` 或 `http://127.0.0.1:port/`
- 玩家从 MCM 点按钮 → `Process.Start("http://127.0.0.1:port/?t=" + token)`

### 6.4 UI 结构
和 B6 中的 5-tab 一致（用户已批准），但用 web 技术栈实现：
- **Tab 1 功能开关**：11 个 toggle，左右两列布局
- **Tab 2 数量与预算**：7 个 slider + 数字输入
- **Tab 3 兵种与 Tier 比例**：5 兵种 + 6 Tier，含 Σ 实时校验
- **Tab 4 模板与资源**：
  - 上半：**ExactTroopTemplate 高级编辑器** — 左右分栏，左是当前模板（兵种 + 数量），右是可选兵种 picker（按文化分组 + 搜索 + tier filter）。**这是最大的体验升级**：picker 不再受 Gauntlet 局限
  - 中间：3 个 TrainingTemplate Apply 按钮
  - 下半：6 个 numeric slider（食物/XP/Conformity/护卫/冷却/回首府）
- **Tab 5 按城堡覆盖**：可折叠卡片，每张卡显示城名 + 城/堡标签 + 「启用覆盖」+ 展开后含 toggle + 数值 + ExactTroopTemplate 链接

### 6.5 交互细节
- 编辑后**不自动保存**（避免误改）。顶部「保存到游戏」按钮，点击 → PUT /api/config
- 顶部右侧 status 区显示「server 在线 / 上次保存时间 / 当前 capital」
- 任意 slider 拖动 → 客户端 Σ 实时计算 + 红字警告
- 「应用预设」点击 → 弹 confirm dialog 列出将被覆盖的字段
- Tab 切换零成本，所有数据已加载

### 6.6 i18n
全中文，无英文 fallback（与 B6.1 一致）。Tooltip / placeholder 同样纯中文。

---

## 7. MCM 简化

### 7.1 留下的（与控制面板**不重叠**）
- **MasterEnable**（总开关 toggle）：所有 Manager 都接此开关；OFF 时所有 OnHourlyTick 立刻 return
- **Open Web Config**（按钮）：`Process.Start("http://127.0.0.1:port/?t=" + token)`，启动默认浏览器
- **Reload Config from Disk**（按钮）：强制 `ConfigurationManager.Load()` —— 用户手动复制 JSON 的备用场景
- **Disable Web Server**（toggle，默认 OFF）：禁用 HttpListener，整局不监听
- **Log Level**（dropdown）：Debug/Info/Warn/Error，默认 Info

### 7.2 删的（移到网页）
所有 EnabledFeatures.* toggle 11 个 + 所有 GlobalDefaults numeric + ExactTroopTemplate + 按城堡覆盖。

### 7.3 不变的
MCM 是软依赖，没装 MCM 的玩家仍能用（但只能从 `Documents/.../SovereignTowns/global.json` 手动开关，或访问 server 默认 URL）。

---

## 8. 删除清单（Phase 2 一次性砍掉）

| 文件 | 行数估计 | 备注 |
|---|---|---|
| `src/Ui/ConfigScreen/SovereignTownsConfigScreen.cs` | ~115 | dead code，先删 |
| `src/Ui/ConfigScreen/SovereignTownsConfigVM.cs` | ~700 | B6 重写后 |
| `src/Ui/ConfigScreen/ExactTroopTemplateEditor.cs` | ~40 | 入口 |
| `src/Ui/ConfigScreen/STTroopPickerScreen.cs` | ? | troop picker 容器 |
| `src/Ui/ConfigScreen/STTroopPickerVM.cs` | ? | troop picker VM |
| `src/Ui/ConfigScreen/Options/*.cs` | ~600 | 6 个 Option VM 文件 |
| `src/Ui/MapRibbon/*.cs` | ~250 | RibbonInjector + FloatingPanelWidget |
| `SovereignTowns/GUI/Prefabs/SovereignTownsConfigScreen.xml` | ~290 | dead code |
| `SovereignTowns/GUI/Prefabs/SovereignTownsRibbon.xml` | ~725 | 整个抽屉 |
| `SovereignTowns/GUI/Prefabs/STTroopPickerPopup.xml` | ? | picker 弹窗 |

**总计**：约 2500-3000 行 C# + 1100 行 XML 净删除。

---

## 9. 对架构契约的影响

| Hard invariant | 影响 |
|---|---|
| net472 | 不变；HttpListener 是 BCL 一部分 |
| SaveBaseId / LocalSaveId | 不变；HTTP server 无序列化字段 |
| try/catch 包裹事件入口 | server 内每个 endpoint handler 必须 try/catch，绝不让异常上升到 HttpListener 主循环 |
| HourlyTickPartyEvent 首行 PartyComponent 过滤 | 无关 |
| LLM 禁即时路径 | 无关；HTTP 异步处理与 game tick 隔离 |
| SafeUninstall 覆盖自定义 component | 无关，删 UI 不动 Component 类型 |
| Newtonsoft.Json | 仍用，server 序列化全部走 Newtonsoft |

**新增不变量（B7 锁定）**：
10. **WebConfigServer 必须 127.0.0.1 绑定**；不得 `+`/`*`/`0.0.0.0`，否则触发 UAC 或暴露公网
11. **任何 API endpoint 必须 token 校验**（除静态文件 serve 走 token 也可选，但建议统一）
12. **所有 HTTP handler 内部 try/catch**，异常仅返回 500 JSON，不上升

---

## 10. 实施顺序（Phase 1 + 2）

### Phase 1（MVP，1-2 天）
1. **B7.1 TroopDumper**：写 `TroopDumper.cs` + OnGameStart 集成
2. **B7.2 配置路径切换**：`ConfigurationManager` 读写路径改 `Documents/.../SovereignTowns/global.json`，写 `ConfigMigrator` 拷旧路径过来
3. **B7.3 WebConfigServer 骨架**：HttpListener 启动 + token + 4 API endpoint（不 serve 静态文件）+ MCM「打开浏览器」按钮（先指向 about:blank 测连接）
4. **B7.4 静态前端骨架**：单 HTML，纯 Vanilla JS，只做「fetch /api/config + 显示 JSON + 编辑后 PUT」
5. **B7.5 进游戏跑通 round-trip**：游戏→MCM→浏览器→改→保存→reload→生效

如果 Phase 1 五步走通，再做 Phase 2。

### Phase 2（完整迁移，3-7 天）
6. **B7.6 删 Gauntlet UI**：按 §8 清单一次性删
7. **B7.7 MCM 简化**：按 §7 重写 MCM 集成（如有）
8. **B7.8 前端完整 UI**：Alpine + Tailwind + 5 tab + ExactTroop 高级 picker
9. **B7.9 i18n 验证 + UX 抛光**

---

## 11. 回滚

每步独立 commit。如果 Phase 1 走不通：
- B7.5 失败 → revert B7.3 / B7.4，HttpListener 路线作废，回到 Gauntlet UI 或讨论别的方案
- B7.6 是不可逆删除，但 git 保留所有提交；revert commit 即可恢复 Gauntlet UI

存档兼容：POCO 不变 → 存档不受影响。

---

## 12. 风险与开放问题

### 已识别风险
1. **HttpListener 在 Bannerlord 进程内的稳定性**：游戏沙盒、Steam 防火墙弹窗、AntiVirus 误报。需要 Phase 1.3 进游戏实测
2. **多次启动 Bannerlord 端口冲突**：自动 +1 策略可以兜底
3. **玩家 Windows Defender / Firewall**：仅 127.0.0.1 监听应该不会触发弹窗，但首次启动可能 prompt 一次
4. **浏览器 mixed content**：前端走 http://，不能跨向 https:// fetch；mod API 都同源走相对路径，避免
5. **兵种 dump 后玩家加 mod**：dump 滞后于 mod 加载顺序，但 OnGameStart 时所有 SubModule 已 OnSubModuleLoad，应该 OK

### 开放问题（spec land 后再讨论）
- A: ExactTroopTemplate picker 是否需要支持「按 culture 分组的多列布局」还是「一个长 list + 搜索」？
- B: TrainingTemplate Apply 后要不要让玩家「撤销到上一版本」（实现：mod 在 PUT 时把旧 config 备份到 `global.json.bak.1` ~ `global.json.bak.5` 滚动）？
- C: WebUI 是否需要响应式（手机/平板访问）？默认 MVP 仅桌面 1920x1080

---

## 13. 已确认的设计选择

- HTTP server 绑定 `127.0.0.1:port`，default port 41763 + auto-fallback
- Token 鉴权（启动随机生成，玩家从 MCM 入口）
- 前端 Alpine.js + Tailwind（全 CDN，零构建）
- 兵种 dump OnGameStart 写文档目录
- 配置路径从 `Modules/.../Configs/global.json` 迁移到 `Documents/.../SovereignTowns/global.json`
- MCM 只保留 5 项与控制面板不重叠的运行时控制
- Phase 1 先跑通 round-trip，Phase 2 再删 Gauntlet UI
