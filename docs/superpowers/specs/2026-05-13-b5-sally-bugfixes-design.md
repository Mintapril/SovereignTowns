# B5 — SallyForth 行为 bug 修（micro batch）

**日期**：2026-05-13
**触发**：API + 社区核对发现两处行为层问题（API 调用全部正确）。
**范围**：仅 2 个改动 + 1 项 verify。
**前置 verify**：F3（SafeUninstallMenu 扫 `SallyForthPartyComponent`）已在 B1 Task 4.C `commit 2b3f9e9` 完成（`SafeUninstallMenu.cs:110`），本批跳过。

## 1. 目标

把 SovereignTowns sally 行为里两处社区已知坑修掉：
- **F1**：sally party 在 `ShouldJoinPlayerBattles` 默认 true 下会强行加入玩家战斗，把出击队从首府辖区拽到大地图任意战场。改设为 false。
- **F2**：vanilla 已知 bug —— `SetMoveEngageParty` 的目标进入 settlement 后，追击队在外面找不到目标干等。我方现有"target null/IsActive=false"检测覆盖不到这种情况（target 进了 settlement 仍 IsActive=true）。加 `target.CurrentSettlement != null` 检测，命中即立即回家。

## 2. 非目标

- 不引入决策门槛（GuardHouse / 概率 / NightOnly / 文化加成）—— design choice，留 B6+。
- 不动 wage / Aggressiveness / ActualClan —— vanilla 默认在 12h sally 寿命内可接受。
- 不动 SafeUninstall（B1 已修）。
- 不动 spec/plan 文档以外的 sally 任何代码 —— 不在本批 scope。

## 3. 改动

### F1 — `ShouldJoinPlayerBattles = false`

**文件**：`SovereignTowns/src/SallyForth/SallyForthManager.cs`

`TryCreateSallyParty` 内，在 `SetMoveEngageParty(target, ...)` 之前（或之后，同 try 块内）加：

```csharp
try { sallyParty.ShouldJoinPlayerBattles = false; }
catch (Exception flagEx) { Logger.Error("ShouldJoinPlayerBattles=false failed", flagEx); }
```

放进现有 `try { sallyParty.Ai?.SetDoNotMakeNewDecisions(true); sallyParty.SetMoveEngageParty(...); } catch ...` 同一块内最合适，避免一次 sally 创建出现两个独立 try。

### F2 — 目标进入 settlement 后立即回家

**文件**：`SovereignTowns/src/SallyForth/SallyForthManager.cs`

`OnHourlyTickParty` 内，**第 4 分支**当前是"target == null || !target.IsActive → 释放 AI"。在该分支之**前**加一个新检测："target 还活着但已经躲进 settlement → 强制回家"。位置：line ~150-160 区间。

```csharp
// 新分支：vanilla 已知 bug — target 进 settlement 后追击队在外卡住。
// 12h 超时兜底太久，立即回家。
var target = sp.TargetParty;
if (target != null && target.IsActive && target.CurrentSettlement != null)
{
    Logger.Info($"SallyForthManager: '{SafeName(party)}' target '{SafeName(target)}' entered '{target.CurrentSettlement.Name}', returning home '{home.Name}'");
    ReleaseAiAndReturnHome(party, home);
    return;
}

// 否则继续 engage（创建时已 SetMoveEngageParty + SetDoNotMakeNewDecisions）
//   如果当前目标已死亡/失效，让 vanilla AI 接管
if (target == null || !target.IsActive) { ... 原逻辑 ... }
```

注意：原来的 `var target = sp.TargetParty;` 这一行会被新分支吃掉，原 if 内的 `target` 变量重用即可。重构后 `target` 的提取只发生一次。

### F3 — verified, no-op

`grep` 已确认 `SafeUninstallMenu.cs:110` 含 `SallyForthPartyComponent`（B1 Task 4.C commit `2b3f9e9` 加的）。本批无改动。

## 4. 对架构契约的影响

| Hard invariant | 影响 |
|---|---|
| net472 / SaveBaseId / LocalSaveId | 无 |
| try/catch 包裹 | F1 加自己的 try-catch；F2 在已有 `OnHourlyTickParty` 外层 try 内 |
| HourlyTickPartyEvent 首行 PartyComponent 过滤 | 保持：F2 在 `sp == null return` 之后 |
| LLM 禁即时路径 | 无关 |
| SafeUninstall 覆盖自定义 component | F1+F2 不引入新 component |
| Newtonsoft.Json | 无关 |

## 5. 验证（无单测，靠日志 + 游戏内）

| 改动 | 命令/操作 | 预期 |
|---|---|---|
| Build | `dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug` | `0 Error(s)`，**3** baseline warnings 不变 |
| F1 in-game | 出击队在外时玩家进入战斗（任何战斗） | sally party **不**冲过来；日志无 "joining player battle" |
| F2 in-game | 出击队追击的 AI 党进入某 settlement | 下一个 hourly tick，日志见 `target ... entered '<settlement>', returning home` + party 折返 |

## 6. 实施顺序

- Step 1: F1 改动（1 行 + try-catch 共 3 行）+ build + commit
- Step 2: F2 改动（重构 OnHourlyTickParty 第 4 分支 ~6 行）+ build + commit

两步独立，失败不影响下一步。

## 7. 回滚

每步都是"加 case / 加 try-catch"，签名不变。回滚 = revert 该 commit。无存档兼容问题。
