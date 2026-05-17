# SovereignTowns Round 2 Audit — 修复后复审

**审计日期**: 2026-05-17
**审计基线**: master @ ad6c919 (B17.2 修复后)
**审计方式**: 4 个 sonnet agent 并行 — events / webconfig / lifecycle / boundaries 维度
**目的**: 验证 B17.1 + B17.2 的修复正确性 + 扫描是否引入新问题

## 总体结论

**P0**: **0 个新发现**。Round 1 的所有 P0 (P0-1/P0-2/P0-3/P0-4/P0-5/P0-6) 均经验证修复生效。

**P1**: **10 个新发现** (去重后) — 多于 advisor 终止条件 ≤2,但其中多数是单行修复或一批校验扩展,可在一个小 commit 内解决。

**关键判定**: 需 advisor 帮判断剩余 P1 是该 round 3 全修,还是降级到 P2 hygiene 集中到最后一轮。

---

## Round 1 修复验证矩阵(全部生效)

| 修复项 | 状态 | 验证 |
|---|---|---|
| P0-1 Patrol daily 兜底 | ✅ | OnDailyTickSettlement:338 已加 patrol dispatcher 调用,内部 cap 检查保证幂等 |
| P0-2 OnDestroyed 下沉 | ✅ | base class 实现救援逻辑 (home → capital → 蒸发),Sally override 改为 base + finally NotifyDispatcherEnded,无双重 merge |
| P0-3 WarDeclared 订阅 | ✅ (限于 Recruiter) | Sub + handler 都正确,签名验证通过。Transfer/Patrol/AI Recruiter 按用户决策不覆盖。**注意**: audit 认为 Transfer 在敌方版图穿越需处理 — 与用户决策冲突,需 reconcile |
| P0-4 OnHeroChangedClanEvent 订阅 | ✅ | Hero.MainHero 过滤,registry.HandlePlayerClanSwap 调用链正确。**但**: 实现细节有 bug,见 P1 #1 |
| P0-5 ValidateRule 上限 | ✅ | 7 项上限校验 + MinTier/MaxTier 改 [1,6]。**但**: 仍有 8+ 字段未校验,见 P1 #6 |
| P0-6 SettlementsSnapshot | ✅ | DTO + Volatile atomic + Refresh 调用点完整。**但**: 性能担忧,见 #11 |
| P1-1 UI confirm 弹窗 | ✅ | setToggleValue 拦截,confirm 取消时 \$nextTick 写回。**但**: 后端可绕过 + UI 竞态,见 P1 #4 / #5 |
| P1-2 _visitedThisTrip SaveableField | ✅ | List<Settlement> + TypeDefiner ContainerDefinition,lazy-init 处理旧存档 null |
| P1-4 AllowedCultureIds 接通 | ✅ | MatchesRule 中 culture filter 位置合理 (tier 之后、role 之前),OrdinalIgnoreCase 比较,null-culture 处理正确 |
| P1-5 PUT payload 上限 | ✅ (有缝隙) | 1MB + Content-Type 校验,**但**: chunked encoding 绕过,见 P1 #3 |
| P1-6 castle menu | ✅ | foreach `["town","castle"]` 注册,IsSetCapitalAvailable 仍仅 IsTown(设计正确) |
| P1-7 HomeSettlement throw | ✅ (有 4 处遗漏) | RebuildFromCampaign 已用 OrNull。**但**: StPatrolPartyComponent.Name + 多处仍裸调,见 P1 #7/#10 |
| P1-8 scheduler NotifyPartyDestroyed | ✅ | UntrackParty 内同时通知 Patrol+Recruiter scheduler |
| P1-9 silent catch → Warn | ✅ | 2 处都改对了 |
| L6-4 token chat 不泄漏 | ✅ | 改为只显 127.0.0.1:PORT + 提示 auth.txt |
| POST /api/reload UI 接线 | ✅ | reloadAll 前先 POST,soft-fail。**但**: init() 每次开页强制 reload,UX 担忧 |
| H2 早 return 移入 try | ✅ | OnHourlyTickParty 整体在 try 内 |

---

## Round 2 新 P1 清单(10 条,去重)

### P1 #1 — HandlePlayerClanSwap 兵蒸发 (events P1-B + boundaries P1-C 部分)
**文件**: `SovereignTowns/src/Capital/CapitalRegistry.cs:332-351`

**问题链**:
1. HandlePlayerClanSwap 调 `_lifecycle?.MigrateAllOrDisband(oldClan, null)` (newCapital=null)
2. MigrateAllOrDisband 内 if (newCapital != null) merge → 跳过 merge
3. DisbandAndUntrack → 触发 vanilla DisbandPartyAction → 异步 disband chain
4. 最终 MobilePartyDestroyed → 路由到 OnDestroyed
5. **但**: oldClan 已被 `_managers.Remove(oldClan)` 移除 (line 339)
6. OnDestroyed 内 `IsManagedClanWithCapital(partyClan)` = false,`GetCapitalForClan(partyClan)` = null
7. rescueTarget = null → 蒸发

**触发**: 玩家通过 mod/console/quest 换 clan,所有 oldClan in-flight 兵员被全部丢失。

**修复**:
```csharp
public void HandlePlayerClanSwap(Clan? oldPlayerClan, Clan? newPlayerClan)
{
    if (oldPlayerClan != null && oldPlayerClan == newPlayerClan) return; // P1-C 防御
    Settlement? oldCapital = oldPlayerClan != null 
        ? GetCapitalForClan(oldPlayerClan) 
        : null;
    if (oldPlayerClan != null)
    {
        _lifecycle?.MigrateAllOrDisband(oldPlayerClan, oldCapital); // 传 capital 而非 null
        _managers.Remove(oldPlayerClan);
    }
    // ...
}
```

### P1 #2 — WarDeclared 仅覆盖 Recruiter(events N-A + lifecycle N-4)
**文件**: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:389-431`

**用户 Round 1 决策**: "只有征兵队的行为会改变"
**Audit 反馈**: Transfer 在敌方版图穿越被截杀也需处理

**需用户 reconcile**: 是按原决策不扩展,还是延伸到 Transfer? AI Recruiter 是否也需要?

### P1 #3 — chunked encoding 绕过 1MB 上限(webconfig P1-A)
**文件**: `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs:68-76 + 233-238`

**问题**: ContentLength64 == -1 (chunked transfer encoding) 直接 fall through 到 ReadBody,无长度上限,可 OOM

**修复**: ReadBody 内加 LimitedStream 包装,读超 1MB 截断 + 返回 413/400

### P1 #4 — setToggleValue \$nextTick 写回竞态(webconfig P1-B)
**文件**: `SovereignTowns/SovereignTowns/WebUI/index.html:1219-1221`

**问题**: 写 false 再 $nextTick 写 true,极快双击+save() 时,中间 microtask 窗口配置可能被 false 写盘
**修复**: 不要先写 false,直接 \$nextTick 强制刷新 true

### P1 #5 — PUT /api/config 绕过 UI confirm + 后端无 INFO 通知(webconfig P1-C)
**文件**: `SovereignTowns/src/Capital/CapitalRegistry.cs:SyncFromConfig`

**问题**: UI confirm 仅 UI 路径;PUT JSON 或编辑 global.json 都可绕过,后端解散 AI in-flight 时无日志告知

**修复**: SyncFromConfig 移除 AI manager 路径加 `Logger.Info` + 可选 `InformationManager.DisplayMessage`

### P1 #6 — ValidateConfig 校验仍漏(webconfig + boundaries 合并)
**文件**: `SovereignTowns/src/Configuration/ConfigurationManager.cs`

**漏校验字段清单**:
| 字段 | 缺失约束 |
|---|---|
| `FoodSafetyThreshold` | 任意 float 含 NaN/Inf |
| `ClanPatrolConfig` 5 个 float (EtaBufferHours/StuckTimeoutHours/MinVisitGapHours/DistanceWeightHoursPerTile/SupportEtaThresholdHours) | 全部无校验 |
| `ClanRecruiterConfig` 3 个 float (同名)| 同上 |
| `VillageCooldownHours` | 仅 `>= 0`,无上限(720 hr = 30 天合理) |
| `PerSettlementOverrides` 字典 | 大小无上限,key 长度无上限 |
| `AllowedCultureIds` 列表 | 大小 + 元素长度无上限 |
| `ExactTroopTemplate` key | 长度无上限 |
| `PriorityTroopIds` / `BannedTroopIds` | 同上 |

**修复**: ValidateConfig 内增加 IsFinite 检查 (排 NaN/Inf) + 字符串长度 + 字典/列表大小上限

### P1 #7 — StPatrolPartyComponent.Name 仍裸 HomeSettlement(lifecycle N-2 + boundaries P1-A)
**文件**: `SovereignTowns/src/Parties/StPatrolPartyComponent.cs:41`

**问题**: `HomeSettlement?.Name?.ToString() ?? "未知"` 中 `?.` 只能 null,不能 throw。当 _homeSettlement==null 时 HomeSettlement getter throw,? 操作符无效,Name getter 抛 InvalidOperationException

**修复**: 改为 `HomeSettlementOrNull?.Name?.ToString() ?? "未知"`

**优先**: Name 被 vanilla 各路径(UI/log/PartyNameFormatter)高频调用,**是唯一能让异常逃出 ST 边界的点**

### P1 #8 — STPartySizeLimitModel.cs:95 裸 HomeSettlement(lifecycle N-3c)
**文件**: `SovereignTowns/src/Models/STPartySizeLimitModel.cs:95`

**问题**: ComputeSallyLimit 内 `sally.HomeSettlement`,外层无 try-catch,异常逃出到 vanilla GetPartyMemberSizeLimit

**修复**: 改为 `sally.HomeSettlementOrNull?` 或在 model 加 try-catch 兜底

### P1 #9 — OnWarDeclared 内 target.MapFaction 可 null(boundaries P1-B)
**文件**: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:410`

**问题**: settlement 易主进行中 `target.MapFaction` 可能 null → NRE
**修复**: `target.MapFaction?.IsAtWarWith(playerFaction) == true`

### P1 #10 — HandlePlayerClanSwap 缺 oldClan==newClan 防御(boundaries P1-C)
**文件**: `SovereignTowns/src/Capital/CapitalRegistry.cs:332`

**修复**: 方法入口加 `if (oldPlayerClan != null && oldPlayerClan == newPlayerClan) return;`(可与 P1 #1 一起改)

---

## 性能 / UX 观察(非 P1,但建议处理)

### #11 — SettlementsSnapshot.Refresh() 在 OnDailyTickSettlement 全图扫描
**文件**: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs:358`

OnDailyTickSettlement 对每个 settlement 触发一次 (100+ 城/天),每次 Refresh 全扫 Town.AllTowns (70-80 条目),总约 12000+ Town 读取/天

**建议**: 改为 OnDailyTick (campaign-level,无 settlement 参数) 中每天 1 次,或用 dirty flag 仅在易主/PUT 时触发

### #12 — init() 每次开页强制后端 reload(webconfig 4-B)
**文件**: `SovereignTowns/SovereignTowns/WebUI/index.html:1128`

每次浏览器刷新强制后端从磁盘重读,会覆盖玩家未保存的浏览器端修改

**建议**: init() 不要自动 POST /api/reload,只由"↻ 重读"按钮触发

---

## Round 1 已确认状态变化

| Round 1 项 | Round 2 状态 |
|---|---|
| H3 (HourlyTickParty 名存实亡) | 设计层注释清楚保留 DrainWebConfigSync,无需修 |
| H4 (CapitalRegistry.Instance 时序) | **Resolved** — events_r2 确认 Initialize() L92 在 OnSessionLaunched 线性流末尾,无并发订阅 |
| H5 (MapEventEnded 双重路由) | **Resolved** — B16.4 重构后单一路由 |
| L4-4 ConfigurationManager.Current race | **Resolved** — Current getter 已 lock(_gate) |
| 5.4 Transfer 无饿死保护 | 未修,设计取舍 |
| 5.5 IsAtHome 首跳问题 | 未修,实践规避 |
| 5.6 Sally target 进 settlement | 未修,设计取舍 |
| L2-2 auth.txt ACL | 未修,Windows 默认 ACL 足够 |
| L2-3 query string token | 未修,实际风险低 |

---

## Round 3 建议(待 advisor 校准)

按 advisor 的硬终止条件:
- P0 全部修复 ✅
- 新 P0: 0 ✅
- **新 P1: 10 ❌** (远超 ≤2)

**两个走向**:
- **A. 再做 Round 3**: 修复 P1 #1 (ClanSwap 兵蒸发,最严重) + #3 (chunked 绕过) + #6 (校验完整化) + #7/8 (HomeSettlement getter 兼容) — 共 ~4 个 commit。然后 Round 3 复审,期望产出 ≤2 P1。
- **B. 收尾**: 接受 P1 现状,把所有 10 个 P1 + 性能 #11/#12 + 硬编码配置化合并到 B18 hygiene 周期,本轮以"P0 已修复"收尾。

advisor 之前说"P2 hygiene 应该集中到最后一轮,不要追求收敛"。但 P1 #1 (ClanSwap 兵蒸发) 严重度接近 P0,#7 (Name getter throw 逃出 mod 边界) 也是触及 invariant 的事 — 这些不是纯 hygiene。

需 advisor 帮判定: 哪些 P1 属于 "must fix before stop", 哪些可延后。

---

## 提交说明

本报告基于 Round 2 4 个 sonnet agent 并行复审整合产出。所有引用的 file:line 来自 ad6c919 (B17.2) 状态。
