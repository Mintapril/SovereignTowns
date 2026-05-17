# SovereignTowns Round 3 Audit — Final / Termination

**审计日期**: 2026-05-17
**审计基线**: master @ e173f8d (B17.3 修复后)
**审计方式**: 2 个 sonnet agent 窄范围验证 — lifecycle/null-safety + WebConfig/validation 维度
**目的**: 确认 B17.3 6 项 P1 must-fix 修复正确 + 无引入新问题

---

## 终止判定 — 符合 advisor 终止条件 ✅

| 终止条件 | 状态 |
|---|---|
| Round 1 P0 全部 verified fixed | ✅ (Round 2 audit 验证,Round 3 复核确认仍生效) |
| Round 3 audit 新 P0 数 | **0** (≤ 0 要求) |
| Round 3 audit 新 P1 数 | **0** (≤ 2 要求) |
| 是否在 advisor 5 轮硬封顶内 | ✅ Round 3 完成 |

**结论**: 迭代终止。剩余 P2 项 + Round 2 advisor 判定延后的 P1 项 → 转入 B18 hygiene backlog (audits/b18-hygiene-backlog.md)。

---

## B17.3 6 项 must-fix 验证矩阵

| Fix # | 修改文件:行 | audit 验证 | 副作用 |
|---|---|---|---|
| **Fix 1**: HandlePlayerClanSwap 兵蒸发 + oldClan==newClan 防御 | `CapitalRegistry.cs:332-351` | ✅ 调用顺序正确(GetCapitalForClan→Remove→EnsureForClan),无回访风险,Logger.Info 完整 | edge case: oldClan 失最后 town 触发 swap 时 fall through 蒸发,advisor 已认可 |
| **Fix 2**: StPatrolPartyComponent.Name HomeSettlementOrNull | `StPatrolPartyComponent.cs:41` | ✅ 语义完全等价,正常存档行为不变,损坏存档不再 throw | 无 |
| **Fix 3**: STPartySizeLimitModel ComputeSallyLimit HomeSettlementOrNull | `STPartySizeLimitModel.cs:95` | ✅ ActualGarrisonCount(null) 已确认返 0,损坏存档下从 throw 改为返合理 fallback | 无 |
| **Fix 4**: PUT /api/config chunked encoding 早 reject 411 | `WebConfigEndpoints.cs:69-77` | ✅ declared < 0 精确匹配 ContentLength64==-1,顺序在 1MB 检查之前正确 | P2: Logger.Warn 不含客户端 IP (本地威胁模型可忽略) |
| **Fix 5**: OnWarDeclared target.MapFaction null check | `SovereignTownsCampaignBehavior.cs:410` | ✅ **关键检查**: `?.IsAtWarWith(...) != true` 写法无语义反转。null/false → continue,true → 撤回。正向逻辑正确 | 无 |
| **Fix 6**: ValidateRule IsFiniteFloat + FoodSafetyThreshold [-1000,1000] | `ConfigurationManager.cs:504,584` | ✅ Helper 私有,IsFinite 实现正确,[-1000,1000] 范围对默认 -2.0 + vanilla 典型 [-20,+15] 都合理宽松 | 无 |

---

## Round 1 P0 全部状态 (累计)

| Round 1 P0 | Commit | 当前状态 |
|---|---|---|
| P0-1 Patrol daily fallback | B17.1 | ✅ Resolved |
| P0-2 OnDestroyed 下沉 | B17.2 | ✅ Resolved |
| P0-3 WarDeclared (Recruiter only) | B17.2 + B17.3 (null check) | ✅ Resolved |
| P0-4 OnHeroChangedClanEvent + HandlePlayerClanSwap | B17.2 + B17.3 (兵蒸发修复) | ✅ Resolved |
| P0-5 ValidateRule 上限校验 | B17.1 + B17.3 (FoodSafetyThreshold) | ✅ Resolved (主要字段) |
| P0-6 SettlementsSnapshot HTTP 线程安全 | B17.1 | ✅ Resolved (性能优化进 B18) |

---

## Round 2/3 累计修复的 P1 (16 项已修)

Round 2 修复批 (B17.1 + B17.2):
- P1-1 AI toggle UI confirm
- P1-2 _visitedThisTrip SaveableField
- P1-4 AllowedCultureIds 接通
- P1-5 PUT payload 上限 + Content-Type
- P1-6 Castle menu 注册
- P1-7 HomeSettlement throw + OrNull accessor (Recruiter/Sally)
- P1-8 scheduler NotifyPartyDestroyed 接线
- P1-9 Sally silent catch → Logger.Warn
- H2 早 return 移入 try
- L6-4 token chat 不泄漏
- POST /api/reload UI 接线
- 死字段清理 (AllowLowTierFiller / RestrictToFactionCultures / ClanRecruiterConfig.StuckTimeoutHours)

Round 3 修复批 (B17.3):
- R2-P1-#1 + #10 HandlePlayerClanSwap 兵蒸发 + 同对象防御
- R2-P1-#7 StPatrolPartyComponent.Name OrNull
- R2-P1-#8 STPartySizeLimitModel OrNull
- R2-P1-#3 chunked encoding 拒绝
- R2-P1-#9 target.MapFaction null check
- R2-P1-#6 FoodSafetyThreshold IsFinite

---

## Round 3 新发现 P2 (2 项,B18 backlog)

1. **STPartyDialogRegistration.cs:84,92,95 使用 `HomeSettlement?.X`**
   - 损坏存档下会 throw,但被外层 try-catch 包,不崩游戏
   - 一致性优化:改为 `HomeSettlementOrNull?.X`

2. **WebConfigEndpoints.cs:74 411 拒绝消息无客户端 IP**
   - 调试时不知道是谁发的请求
   - 加 `ctx.Request.RemoteEndPoint?.Address`

---

## 迭代历程

| 周期 | 范围 | 结果 |
|---|---|---|
| Round 1 audit | 6 个 general-purpose agent 并行 (hardcoded / config-ui / events / lifecycle / boundaries / webconfig) | 6 个 P0 + ~30 P1 |
| B17.1 (Wave 1, Bucket A) | 3 个 sonnet agent 并行修自主可行项 | 12 项 P0/P1/P2 修复 |
| B17.2 (Wave 2, Bucket B) | 1 个 sonnet agent 实施用户决策政策 | 5 项 P0/P1 (含 P0-2/P0-3/P0-4/P1-1/P1-4) |
| Round 2 audit | 4 个 sonnet agent 并行 (events / webconfig / lifecycle / boundaries) | 0 P0 + 10 新 P1 |
| Advisor calibration | 三 bucket triage: 6 must-fix / 4+ defer to B18 / WarDeclared 不扩展(用户决策已敲定) | — |
| B17.3 (Round 3 must-fix) | 1 个 sonnet agent ~22 行修 6 项 | 6 P1 closed |
| Round 3 audit | 2 个 sonnet agent 窄范围验证 | 0 P0 + 0 P1 + 2 P2 |

**总 commit 数**: 3 (B17.1 / B17.2 / B17.3)
**总修复**: 6 P0 + 16+ P1 + 多项 P2 hygiene
**Build status**: 全部 dotnet build -c Debug pass (0 errors, 0 warnings)
**Runtime verification**: pending in-game test by the user (无单元测试,CLAUDE.md 明确)

---

## B18 backlog (待办)

详见 `audits/b18-hygiene-backlog.md`。摘要:

- **WarDeclared 扩展**: 用户原决策"只有征兵队",audit 建议扩到 Transfer/Patrol/AI Recruiter — 需用户重新决策
- **UI/UX**: $nextTick race / init() 每次开页 reload / Refresh() 每 settlement-tick 全图扫
- **校验扩展**: ClanPatrolConfig 5 字段 / ClanRecruiterConfig 3 字段 / VillageCooldownHours 上限 / PerSettlementOverrides 大小 / 字符串长度 cap
- **PUT 后端通知**: SyncFromConfig 解散 AI 时加 Logger.Info / DisplayMessage 通知玩家
- **HomeSettlement OrNull 一致性**: STPartyDialogRegistration 3 处 + 多个 try-catch 内 `HomeSettlement?.X`
- **硬编码配置化**: Sally 4 项 + Idle 2 项 等(完整清单见 Round 1 audit_hardcoded 报告)
- **性能**: SettlementsSnapshot.Refresh 频率优化
- **诊断**: 411 拒绝消息加客户端 IP

---

## 致谢 / 流程总结

本次审计 + 修复迭代由 6+3+1+2 = 12 个 subagent 完成,在 ~2 小时内闭环。advisor 在 Round 1 收尾 + Round 2 收尾两次校准方向,避免了"无限收敛"的陷阱。

最终 commit chain: **7622e61 (B16.4a) → 9a6b292 (B17.1) → ad6c919 (B17.2) → e173f8d (B17.3)**。
