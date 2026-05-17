# ST / IG Borrow Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply 17 fixes (Tier S/A/B; B5 食物补给 deferred — see Self-Review) borrowed from IG's multi-year-tested defensive patterns, plus 1 new feature (per-settlement activity ring + daily summary).

**Architecture:** 8 tasks grouped by file-conflict topology so that independent buckets can be dispatched in parallel:

- **T1** GlobalConfig new fields — no deps
- **T2** ConfigurationManager atomic write (S4) + version-mismatch UI (A4) — no deps
- **T3** Dispatcher 闸门 (S1 IsUnderSiege + S2 GarrisonParty null + A3 escort floor + B2 fallback search) — depends on T1
- **T4** PartyMergeService LeaveSettlementAction order (S3) — no deps
- **T5** Party 状态机共享行为 (A1 player-target hold + A5 二段瞬移 + A6 俘虏 cap + B5 食物补给 + B6 防御日志去抖 + B7 玩家俘虏保护 + B8 防御后早回归) — depends on T1
- **T6** B3 海港 navigation helper + B4 多级 fallback — no deps
- **T7** A2 每日活动汇总 + per-settlement 活动环 + WebConfig 端点 — depends on T1
- **T8** B1 配置变更事件 → in-flight recruiter refresh — depends on T2 + T5

**Verification model:** project has no unit tests (per CLAUDE.md "no unit tests; verification = launch game"). Each task ends with `dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug` and (where relevant) a manual game-test note pointing at `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\SovereignTowns\` for log inspection.

**Tech Stack:** C# net472, embedded BL save (SaveableType, SaveBaseId=1_900_000_000), Newtonsoft.Json (game bin), TaleWorlds CampaignSystem v1.3.15, RBM-compatible (no stringId-based troop checks).

**Hard invariants (do not break):** see CLAUDE.md §"Hard invariants" — net472, SaveBaseId, LocalSaveId 不可重排, HourlyTickPartyEvent 必须按 PartyComponent 过滤, 所有事件入口 try/catch, GameModels 必须在 OnGameStart 注册, JSON 用 Newtonsoft.

---

## File Structure

### Modified (15 files)

- `SovereignTowns/src/Configuration/GlobalConfig.cs` — T1
- `SovereignTowns/src/Configuration/ConfigurationManager.cs` — T2, T8
- `SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs` — T3
- `SovereignTowns/src/Recruitment/RecruitmentPlanner.cs` — T3
- `SovereignTowns/src/Patrol/PatrolDispatcher.cs` — T3
- `SovereignTowns/src/Transfer/TransferDispatcher.cs` — T3
- `SovereignTowns/src/Lifecycle/PartyMergeService.cs` — T4
- `SovereignTowns/src/Lifecycle/PartyLifecycleManager.cs` — T6
- `SovereignTowns/src/Parties/StPartyComponent.cs` — T5
- `SovereignTowns/src/Parties/StPatrolPartyComponent.cs` — T5
- `SovereignTowns/src/Parties/StTransferPartyComponent.cs` — T5, T6
- `SovereignTowns/src/Parties/StRecruiterPartyComponent.cs` — T8 (subscribe to config-change event)
- `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs` — T7 daily summary + T8 event subscription
- `SovereignTowns/src/Audit/DecisionAuditLogger.cs` — T7 route to ring
- `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs` — T7 new /api/settlements/{id}/activities endpoint

### Created (3 files)

- `SovereignTowns/src/Audit/PerSettlementActivityRing.cs` — T7 ring buffer
- `SovereignTowns/src/Audit/DailyActivityCounters.cs` — T7 daily counters for summary
- `SovereignTowns/src/Common/SafeMoveHelper.cs` — T6 海港 navigation helper

---

## Task 1: Extend GlobalConfig with new fields

**Files:**
- Modify: `SovereignTowns/src/Configuration/GlobalConfig.cs`

**Background:** 5 new threshold fields + 1 new feature flag are needed by subsequent tasks. Additive-only; do **NOT** bump `ConfigurationManager.CurrentConfigVersion`. CLAUDE.md §"项目状态" 允许直接 `??=` 兜底；旧 JSON 缺这些字段会被 ConfigurationManager.TryLoadFromDisk 的现有 `??=` 兜底机制识别成默认值，但 `??=` 仅作用于 reference type。对于 value-type 字段，POCO 默认初始化器（`= ...`）会接管。因此**只需在类定义里加初始化器**即可。

- [ ] **Step 1: Add 6 fields to `PartyThresholds`**

In `GlobalConfig.cs`, find `public sealed class PartyThresholds` (around line 174). At the end of that class (before the closing `}` at line 222), insert:

```csharp
    // ── B17 借鉴 IG 沉淀 ──
    /// <summary>A3：派征兵队要求首府驻军 ≥ 此值（IG 边界：空 garrison 派征兵队即裸车送死）。
    /// 默认 0 = 不闸（保留 B7.24 "用户明确不要 floor" 的产品决定）。玩家想保护可调到 1+。
    /// 闸门代码仍在，default=0 时是 no-op。</summary>
    public int RecruiterMinHomeGarrison { get; set; } = 0;

    /// <summary>A6：巡逻队 prisoner roster 上限，超过后每 hour 随机踢出非英雄。原 IG MobileGarrison.CheckIfPrisonersIsAboveThreshold。默认 30。</summary>
    public int PatrolPrisonerCap { get; set; } = 30;

    /// <summary>A5：scheduler.IsStuck 重发指令后仍卡死多少 hour 触发二段瞬移到 home.GatePosition。0 关闭。默认 24。</summary>
    public float StuckTeleportHours { get; set; } = 24f;

    /// <summary>B2：RecruitmentPlanner.RankCandidates 第一轮 maxDistance=100 无候选时第二轮的上限。0 关闭降级搜索。默认 200。</summary>
    public float RecruitmentFallbackMaxDistance { get; set; } = 200f;

    /// <summary>B5：party 当前粮食可维持天数低于此值时触发补给。0 关闭。默认 2。</summary>
    public float FoodReplenishMinDays { get; set; } = 2f;

    /// <summary>B5：补给后让粮食至少撑到此天数（从源 town ItemRoster 扣减）。默认 5。</summary>
    public float FoodReplenishTopUpDays { get; set; } = 5f;
```

- [ ] **Step 2: Add 1 field to `EnabledFeatures`**

In `GlobalConfig.cs`, find `public sealed class EnabledFeatures` (line 75). At the end of the class (before `}` around line 123), insert:

```csharp
    /// <summary>A2：每日活动汇总 InformationManager 弹窗（"今日招/调/巡逻 N 人"）。默认 true。</summary>
    public bool ShowDailySummary { get; set; } = true;
```

- [ ] **Step 3: Compile**

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

Expected: build succeeds. No code paths consume new fields yet.

- [ ] **Step 4: Commit**

```bash
git add SovereignTowns/src/Configuration/GlobalConfig.cs
git commit -m "B17.4 T1: add 6 thresholds + ShowDailySummary feature flag for IG-borrow fixes"
```

---

## Task 2: ConfigurationManager — atomic write (S4) + version-mismatch UI (A4)

**Files:**
- Modify: `SovereignTowns/src/Configuration/ConfigurationManager.cs`

**Background:** 
- S4: `WriteToDiskUnlocked` 用 `File.WriteAllText` 直写，崩溃/断电中途 → global.json 半截或 0 字节。改 tmp+swap 模式（net472 没有 `File.Replace` 跨卷保证，用 `Delete + Move` 两步替代，并保留 `.bak`）。
- A4: 版本不匹配时只有 `Logger.Warn`，玩家不知道配置被重置。升级到 `InformationManager.DisplayMessage` Yellow。

- [ ] **Step 1: Replace `WriteToDiskUnlocked` with atomic write**

Find the method (around line 403):

```csharp
    private static void WriteToDiskUnlocked(string configPath, GlobalConfig config)
    {
        try
        {
            string json = JsonConvert.SerializeObject(config, _jsonSettings);
            File.WriteAllText(configPath, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to write config to '{configPath}'", ex);
        }
    }
```

Replace with:

```csharp
    /// <summary>
    /// B17.4 S4：原子写盘 — tmp → swap → backup。崩溃/断电中途绝不留半截/0 字节文件。
    /// net472 缺 File.Replace 跨卷保证；用 Delete + Move 替代，前一份保留为 .bak。
    /// 单步失败：尽力恢复（删除残留 .tmp），保留原 global.json 不动。
    /// </summary>
    private static void WriteToDiskUnlocked(string configPath, GlobalConfig config)
    {
        string tmpPath = configPath + ".tmp";
        string bakPath = configPath + ".bak";
        try
        {
            string json = JsonConvert.SerializeObject(config, _jsonSettings);

            // 1. 全量写到 tmp（独立文件，失败不污染主文件）
            File.WriteAllText(tmpPath, json);

            // 2. 把当前 main 备份到 .bak（若 main 存在）。备份失败仅 warn，继续 swap。
            if (File.Exists(configPath))
            {
                try
                {
                    if (File.Exists(bakPath)) File.Delete(bakPath);
                    File.Move(configPath, bakPath);
                }
                catch (Exception bakEx)
                {
                    Logger.Warn($"WriteToDiskUnlocked: backup to '{bakPath}' failed; proceeding without backup: {bakEx.Message}");
                    // 若 .bak 创建失败但 main 还在 — 直接删 main 让下一步 Move 成功
                    try { if (File.Exists(configPath)) File.Delete(configPath); }
                    catch (Exception delEx) { Logger.Error($"WriteToDiskUnlocked: failed to remove stale main '{configPath}' before swap", delEx); throw; }
                }
            }

            // 3. tmp → main（这一刻起新内容生效）
            File.Move(tmpPath, configPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to write config to '{configPath}' (atomic swap)", ex);
            // 残留 tmp 清理：不留半截文件给下次 Reload 误读
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
            catch (Exception cleanupEx) { Logger.Warn($"WriteToDiskUnlocked: failed to clean up '{tmpPath}': {cleanupEx.Message}"); }
        }
    }
```

- [ ] **Step 2: Upgrade version-mismatch path to UI message**

Find `TryLoadFromDisk` (around line 336). Locate the version-mismatch branch (around line 375-379):

```csharp
            // B7.25：不再做版本迁移。版本不符即丢弃，由 Initialize() 兜底为默认。
            if (parsed.ConfigVersion != CurrentConfigVersion)
            {
                Logger.Warn($"Config 版本不匹配 (file={parsed.ConfigVersion}, expected={CurrentConfigVersion})；不做迁移，重置为默认。请重新在网页面板配置。");
                return null;
            }
```

Replace with:

```csharp
            // B7.25：不再做版本迁移。版本不符即丢弃，由 Initialize() 兜底为默认。
            if (parsed.ConfigVersion != CurrentConfigVersion)
            {
                string msg = $"[主权城镇] global.json 版本不匹配 (file v{parsed.ConfigVersion}, expected v{CurrentConfigVersion}) — 已重置为默认，请重新在网页面板配置";
                Logger.Warn(msg);
                // B17.4 A4：升级到 UI 黄色 — 玩家不会再"静默丢配置"
                try
                {
                    TaleWorlds.Core.InformationManager.DisplayMessage(
                        new TaleWorlds.Core.InformationMessage(msg, TaleWorlds.Library.Colors.Yellow));
                }
                catch (Exception uiEx) { Logger.Warn($"version-mismatch UI display failed: {uiEx.Message}"); }
                return null;
            }
```

Note: `InformationManager` 在 `TaleWorlds.Core` 命名空间；CampaignBehavior 文件里通常已经 import。这里 ConfigurationManager 没 import，所以用全限定名（避免改文件顶部 using 列表破坏其他东西）。

- [ ] **Step 3: Compile**

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

Expected: green.

- [ ] **Step 4: Commit**

```bash
git add SovereignTowns/src/Configuration/ConfigurationManager.cs
git commit -m "B17.4 T2: atomic write (S4) + version-mismatch UI yellow (A4)"
```

---

## Task 3: Dispatcher 闸门 — S1 IsUnderSiege + S2 GarrisonParty + A3 escort floor + B2 fallback search

**Files:**
- Modify: `SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs`
- Modify: `SovereignTowns/src/Patrol/PatrolDispatcher.cs`
- Modify: `SovereignTowns/src/Transfer/TransferDispatcher.cs`
- Modify: `SovereignTowns/src/Recruitment/RecruitmentPlanner.cs`

**Background:** 4 high-confidence fixes. S1 三处 `IsUnderSiege` 闸；S2 `GarrisonParty == null` 重建；A3 `RecruiterMinHomeGarrison` 闸；B2 招募降级搜索。

- [ ] **Step 1: RecruitmentDispatcher — add IsUnderSiege + GarrisonParty rebuild + escort floor**

In `RecruitmentDispatcher.cs`, find the `TryDispatchRecruiter` method body. Locate this block (around line 86-94):

```csharp
            // B1 #7: pause when food trend below threshold
            if (FoodGuard.IsRecruitmentPausedForFood(homeTown, rule, "RecruitmentDispatcher"))
                return false;

            if (!_lifecycle.CanCreateAnotherParty(homeTown.Settlement, PartyKind))
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' 已达征兵队上限，跳过");
                return false;
            }
```

Insert immediately **before** `// B1 #7: pause when food trend below threshold` (between line 84 `var rule = ...` and line 86 `// B1 #7`):

```csharp
            // B17.4 S1：围城下不派征兵队（"开门即送"）。
            if (homeTown.IsUnderSiege)
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' is under siege — skip dispatch");
                return false;
            }

```

Then locate the escort/garrison block (around line 109-128):

```csharp
            // 征兵队不设固定人数 floor — 算多少抽多少，0 也允许派遣裸车。
            int garrisonForEscort = homeTown.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
            int escortRequested = (int)Math.Round(garrisonForEscort * EscortRatio);
            TroopRoster? escortRoster = null;
            int escortActual = 0;
            if (escortRequested > 0)
            {
                escortRoster = TroopRoster.CreateDummyTroopRoster();
                escortActual = TroopTransferHelper.TransferFromGarrison(
                    homeTown.GarrisonParty!.MemberRoster, escortRoster, escortRequested, TroopTransferHelper.SortStrategy.LowestTierFirst);
```

Replace with (adds A3 floor + S2 GarrisonParty rebuild):

```csharp
            // B17.4 A3：空 garrison floor — 0 兵裸车遭遇即没。RecruiterMinHomeGarrison=1 时仍允许 1 兵护卫（兼容历史"0 兵裸车"语义需玩家手动调到 0）。
            int garrisonForEscort = homeTown.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
            int minHomeGarrison = ConfigurationManager.Current?.Thresholds?.RecruiterMinHomeGarrison ?? 1;
            if (garrisonForEscort < minHomeGarrison)
            {
                Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' garrison={garrisonForEscort} < RecruiterMinHomeGarrison={minHomeGarrison}, skip");
                return false;
            }

            // B17.4 S2：garrison 重建（vanilla 在 garrison 清 0 后会移除整个 GarrisonParty 对象，
            // 再访问 GarrisonParty 永远 null；line ~127 的 `homeTown.GarrisonParty!.MemberRoster` 会 NRE）。
            if (garrisonForEscort > 0 && homeTown.GarrisonParty == null)
            {
                try
                {
                    homeTown.Settlement.AddGarrisonParty();
                    garrisonForEscort = homeTown.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                    Logger.Info($"  RecruitmentDispatcher: rebuilt missing GarrisonParty for '{homeTown.Name}' before escort extraction");
                }
                catch (Exception addEx)
                {
                    Logger.Error($"  RecruitmentDispatcher: AddGarrisonParty failed for '{homeTown.Name}'", addEx);
                    return false;
                }
            }

            int escortRequested = (int)Math.Round(garrisonForEscort * EscortRatio);
            TroopRoster? escortRoster = null;
            int escortActual = 0;
            if (escortRequested > 0 && homeTown.GarrisonParty != null)
            {
                escortRoster = TroopRoster.CreateDummyTroopRoster();
                escortActual = TroopTransferHelper.TransferFromGarrison(
                    homeTown.GarrisonParty.MemberRoster, escortRoster, escortRequested, TroopTransferHelper.SortStrategy.LowestTierFirst);
```

This removes the `!` null-forgiving and adds explicit null guard. Note the loop body uses `homeTown.GarrisonParty!.MemberRoster` again at lines 143, 152, 164 (in the rollback paths) — change all three `!` to safe access:

In each of these three lines, change:
```csharp
                        TroopTransferHelper.TransferBackToGarrison(escortRoster, homeTown.GarrisonParty!.MemberRoster);
```
to:
```csharp
                        if (homeTown.GarrisonParty?.MemberRoster != null)
                            TroopTransferHelper.TransferBackToGarrison(escortRoster, homeTown.GarrisonParty.MemberRoster);
```

(Use replace_all in Edit tool for the `homeTown.GarrisonParty!.MemberRoster` string — there are 3 occurrences, all in rollback blocks.)

- [ ] **Step 2: PatrolDispatcher — add IsUnderSiege gate**

In `PatrolDispatcher.cs`, find `TryCreatePatrolParty` (around line 77). Locate the first guard block (around line 79-89):

```csharp
    private void TryCreatePatrolParty(Settlement settlement)
    {
        try
        {
            // B7.16：cap 来自 town 的兵营建筑（settlement_garrison）等级 + 1。
            // 统计该 settlement 的 ST 巡逻队总数；只有 < cap 才允许再创建。
            // B16.4：vanilla auto-spawn 的 PatrolPartyComponent 不再纳入计数 — 与我们独立共存。
            int cap = _lifecycle.GetCapFor(settlement, PartyLifecycleManager.KindPatrol);
            int existing = CountExistingPatrolsAtHome(settlement);
            if (existing >= cap)
```

Insert immediately after the `try {` opening brace (before `// B7.16`):

```csharp
            // B17.4 S1：围城下不派巡逻队（出门即冲撞围攻军）。
            if (settlement?.Town?.IsUnderSiege == true)
            {
                Logger.Debug($"PatrolDispatcher: '{settlement.Name}' is under siege — skip patrol creation");
                return;
            }

```

- [ ] **Step 3: TransferDispatcher — add IsUnderSiege gate**

In `TransferDispatcher.cs`, find `TryDispatchTransfer` (around line 34). Locate this block (around line 60-64):

```csharp
            if (!_lifecycle.CanCreateAnotherParty(source, PartyKind))
            {
                Logger.Info($"  TransferDispatcher: '{source.Name}' 已达调拨队上限，跳过");
                return false;
            }
```

Insert immediately **before** this block (between line 59 `}` and line 60):

```csharp
            // B17.4 S1：围城下不派调拨队。
            if (source.Town?.IsUnderSiege == true)
            {
                Logger.Info($"  TransferDispatcher: source '{source.Name}' is under siege — skip");
                return false;
            }
            if (destination.Town?.IsUnderSiege == true)
            {
                Logger.Info($"  TransferDispatcher: destination '{destination.Name}' is under siege — skip");
                return false;
            }

```

- [ ] **Step 4: RecruitmentPlanner — add 2-round fallback search**

In `RecruitmentPlanner.cs`, the `RankCandidates` method already takes a `maxDistance` parameter. The fallback logic belongs at the caller — easier and more contained. Modify `StRecruiterPartyComponent.PlanNextHop` (file `StRecruiterPartyComponent.cs`, around line 419-453) to attempt a second round.

Locate `PlanNextHop` and find:

```csharp
            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: exclude,
                matchingRule: rule);

            if (candidates.Count == 0) return null;
            return candidates[0].VillageSettlement;
```

Replace with:

```csharp
            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: exclude,
                matchingRule: rule);

            // B17.4 B2：第一轮(maxDistance=100)无候选 → 第二轮扩大到 Thresholds.RecruitmentFallbackMaxDistance（默认 200）。
            // 防止前线村庄全枯竭/全被劫时 recruiter 空手回家。
            if (candidates.Count == 0)
            {
                float fallbackDist = ConfigurationManager.Current?.Thresholds?.RecruitmentFallbackMaxDistance ?? 0f;
                if (fallbackDist > PlanMaxDistance)
                {
                    Logger.Info($"  Recruiter '{PartyNameFormatter.SafeName(party)}': 第一轮(maxDistance={PlanMaxDistance}) 无候选，第二轮扩大到 {fallbackDist}");
                    candidates = RecruitmentPlanner.RankCandidates(
                        homeTown,
                        maxDistance: fallbackDist,
                        maxResults: CandidateBatchSize,
                        excludeSettlements: exclude,
                        matchingRule: rule);
                }
            }

            if (candidates.Count == 0) return null;
            return candidates[0].VillageSettlement;
```

Also apply the same fallback in `RecruitmentDispatcher.cs` `TryDispatchRecruiter` (around line 96-106):

Find:
```csharp
            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: null,
                matchingRule: rule);
            if (candidates.Count == 0)
            {
                Logger.Warn($"  RecruitmentDispatcher: '{homeTown.Name}' 无可招募村庄候选 — 周边 village notable 没有符合规则 (Tier {rule.MinTier}-{rule.MaxTier} / 比例非零兵种) 的兵。考虑放宽 MinTier。");
                return false;
            }
```

Replace with:
```csharp
            var candidates = RecruitmentPlanner.RankCandidates(
                homeTown,
                maxDistance: PlanMaxDistance,
                maxResults: CandidateBatchSize,
                excludeSettlements: null,
                matchingRule: rule);
            // B17.4 B2：第一轮无候选 → 第二轮扩大到 Thresholds.RecruitmentFallbackMaxDistance。
            if (candidates.Count == 0)
            {
                float fallbackDist = ConfigurationManager.Current?.Thresholds?.RecruitmentFallbackMaxDistance ?? 0f;
                if (fallbackDist > PlanMaxDistance)
                {
                    Logger.Info($"  RecruitmentDispatcher: '{homeTown.Name}' 第一轮无候选，第二轮扩大到 {fallbackDist}");
                    candidates = RecruitmentPlanner.RankCandidates(
                        homeTown,
                        maxDistance: fallbackDist,
                        maxResults: CandidateBatchSize,
                        excludeSettlements: null,
                        matchingRule: rule);
                }
            }
            if (candidates.Count == 0)
            {
                Logger.Warn($"  RecruitmentDispatcher: '{homeTown.Name}' 无可招募村庄候选 — 周边 village notable 没有符合规则 (Tier {rule.MinTier}-{rule.MaxTier} / 比例非零兵种) 的兵。考虑放宽 MinTier。");
                return false;
            }
```

- [ ] **Step 5: Compile**

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

Expected: green.

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/src/Recruitment/RecruitmentDispatcher.cs SovereignTowns/src/Recruitment/RecruitmentPlanner.cs SovereignTowns/src/Patrol/PatrolDispatcher.cs SovereignTowns/src/Transfer/TransferDispatcher.cs SovereignTowns/src/Parties/StRecruiterPartyComponent.cs
git commit -m "B17.4 T3: dispatcher 闸门 - S1 IsUnderSiege x3 + S2 GarrisonParty rebuild + A3 escort floor + B2 fallback search"
```

---

## Task 4: PartyMergeService — S3 LeaveSettlementAction before disband

**Files:**
- Modify: `SovereignTowns/src/Lifecycle/PartyMergeService.cs`

**Background:** IG 5 年血泪沉淀的固定顺序：`LeaveSettlementAction.ApplyForParty(party) → DisbandPartyAction.StartDisband(party)`。ST 现在直接 `StartDisband`，party 若在 settlement 内会让 `_stationaryParties` 留 dangling ref。

- [ ] **Step 1: Add helper + apply in DisbandAndUntrack**

In `PartyMergeService.cs`, find `DisbandAndUntrack` (around line 117):

```csharp
    public void DisbandAndUntrack(MobileParty? party, string context)
    {
        if (party == null) return;
        try
        {
            DisbandPartyAction.StartDisband(party);
        }
```

Replace with:

```csharp
    public void DisbandAndUntrack(MobileParty? party, string context)
    {
        if (party == null) return;
        // B17.4 S3：IG 五年沉淀的固定顺序 — disband 前必须先 LeaveSettlementAction，
        // 否则 party 在 settlement 内时 vanilla 的 _stationaryParties 留 dangling ref。
        TryLeaveSettlementBeforeRemoval(party, context);
        try
        {
            DisbandPartyAction.StartDisband(party);
        }
```

- [ ] **Step 2: Apply in DestroyAndUntrack**

In the same file, find `DestroyAndUntrack` (around line 137). Locate this block (around line 154-157):

```csharp
        try
        {
            DestroyPartyAction.Apply(null, party);
            _lifecycle.UntrackParty(party);
            return true;
        }
```

Replace with:

```csharp
        // B17.4 S3：destroy 前同样需要 LeaveSettlementAction（vanilla DestroyPartyAction 不会自动处理 stationaryParties）。
        TryLeaveSettlementBeforeRemoval(party, context);
        try
        {
            DestroyPartyAction.Apply(null, party);
            _lifecycle.UntrackParty(party);
            return true;
        }
```

Also update the fallback (around line 164-167):

```csharp
            try
            {
                DisbandPartyAction.StartDisband(party);
                _lifecycle.UntrackParty(party);
                return true;
            }
```

This already follows the destroy path — `TryLeaveSettlementBeforeRemoval` was called above, no extra call needed (helper is idempotent — if party already left settlement once, the second call is a no-op via the `CurrentSettlement == null` guard).

- [ ] **Step 3: Add the helper method**

At the end of the `PartyMergeService` class (before the closing `}` on line 176), add:

```csharp

    /// <summary>
    /// B17.4 S3：尝试在 disband/destroy 前先把 party 移出 settlement，避免 _stationaryParties 残留 dangling ref。
    /// 单次幂等：CurrentSettlement == null 时直接跳过。失败仅 Warn，不阻塞主流程。
    /// </summary>
    private static void TryLeaveSettlementBeforeRemoval(MobileParty party, string context)
    {
        try
        {
            if (party?.CurrentSettlement == null) return;
            LeaveSettlementAction.ApplyForParty(party);
        }
        catch (Exception ex)
        {
            Logger.Warn($"{context}: LeaveSettlementAction.ApplyForParty failed for '{party?.Name}' (continuing anyway): {ex.Message}");
        }
    }
```

`LeaveSettlementAction` is already imported via `using TaleWorlds.CampaignSystem.Actions;` (line 3).

- [ ] **Step 4: Compile**

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

Expected: green.

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/src/Lifecycle/PartyMergeService.cs
git commit -m "B17.4 T4: S3 - LeaveSettlementAction before disband/destroy (IG固定顺序)"
```

---

## Task 5: Party 状态机共享行为

**Files:**
- Modify: `SovereignTowns/src/Parties/StPartyComponent.cs`
- Modify: `SovereignTowns/src/Parties/StPatrolPartyComponent.cs`

**Background:** 7 fixes 集中在 4 个 St*PartyComponent。大部分挂在基类的 `OnHourlyTick` 之前 / 末尾，让 4 个子类一次性受益：

| Fix | 落点 | 说明 |
|---|---|---|
| A1 | 基类 OnHourlyTick 入口 | 玩家 PartyBelongedTo.TargetParty == self → SetMoveModeHold |
| A6 | 基类 OnHourlyTick 末尾 | PrisonRoster > cap → RemoveNumberOfNonHeroTroopsRandomly |
| B5 | 基类 OnHourlyTick 末尾 | food < FoodReplenishMinDays days → 从 home town ItemRoster 扣减补到 FoodReplenishTopUpDays |
| B7 | 基类 OnHourlyTick 入口 | PrisonRoster.Contains(MainHero) → ReturnToHome |
| A5 | StPatrolPartyComponent 卡死分支 | 已经 stuck 重发 SetMoveGoToSettlement 后仍 stuck > StuckTeleportHours → self.Position = home.GatePosition |
| B6 | StPatrolPartyComponent 防御分支 | 用 [CachedData] 字段 _lastLoggedDefenseTarget 防日志刷屏 |
| B8 | StPatrolPartyComponent 防御分支 | 防御目标 IsUnderSiege/IsUnderRaid 转 false 后立即 PickNextStop |

- [ ] **Step 1: StPartyComponent — add A1 (player target hold)**

In `StPartyComponent.cs`, find `OnHourlyTick` (around line 66-79):

```csharp
    public void OnHourlyTick(MobileParty self)
    {
        if (self == null) return;
        try
        {
            if (!ValidateAliveAndManaged(self, out var capital)) return;
            if (IsAtHome(self)) { OnArrivedHome(self); return; }
            OnHourlyTickCore(self, capital!);
        }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.OnHourlyTick failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }
```

Replace with:

```csharp
    public void OnHourlyTick(MobileParty self)
    {
        if (self == null) return;
        try
        {
            if (!ValidateAliveAndManaged(self, out var capital)) return;

            // B17.4 A1 reset：玩家上次锁定我导致 DoNotMakeNewDecisions=true，本 hour 入口先无条件复位。
            // 只有当下面 TryHoldForPlayerTarget 命中时才会再次设为 true。
            // sally Returning 不需要 DoNotMakeNewDecisions=true（sally 的 TransitionToReturning 已 SetDoNotMakeNewDecisions(false)）。
            try { self.Ai?.SetDoNotMakeNewDecisions(false); } catch { /* swallow */ }

            // B17.4 B7：玩家被自家 ST 队伍俘获 → 立刻送回首府（IG MobileGarrison.CheckIfPlayerIsPrisonerInParty）
            if (TryReturnIfPlayerCaptured(self)) return;

            // B17.4 A1：玩家右键 attack/follow 我 → SetMoveModeHold 让玩家追上（IG OrderStopIfPlayerTarget）。
            // 注意：sally 队不应套用（冲锋中被玩家拦说明出问题），仍正常运行。
            if (!AvoidsPlayerTargetHold && TryHoldForPlayerTarget(self)) return;

            if (IsAtHome(self)) { OnArrivedHome(self); return; }
            OnHourlyTickCore(self, capital!);

            // B17.4 A6：tick 末尾通用维护 — 俘虏 cap。失败不影响 core 已完成的工作。
            // B5 食物补给已 deferred（out-of-scope）— IG 实现是凭空塞，与项目"非作弊基调"冲突；
            // vanilla 没有合适的 settlement-级 ItemRoster API 做"经济闭环"扣减。
            try { TryEnforcePrisonerCap(self); }
            catch (Exception capEx) { Logger.Warn($"{GetType().Name}.TryEnforcePrisonerCap failed: {capEx.Message}"); }
        }
        catch (Exception ex)
        {
            Logger.Error($"{GetType().Name}.OnHourlyTick failed for '{PartyNameFormatter.SafeName(self)}'", ex);
        }
    }
```

- [ ] **Step 2: StPartyComponent — add virtual flag + 4 helper methods**

After the `AppliesReturnDisbandCondition` property (around line 148), add:

```csharp

    /// <summary>B17.4 A1：sally 队等"冲锋型"队伍应 override = true，避免被玩家拦截后停下让冲锋失败。</summary>
    protected virtual bool AvoidsPlayerTargetHold => false;
```

After the `MergeToFallback` method (around line 239), before the constructor (line 242), add:

```csharp

    // ── B17.4 共享 helpers ──

    /// <summary>
    /// B17.4 B7：MainHero 被本 party 俘获 → 立刻返回 home 走 vanilla Dungeon 路径。返回 true 表示本 hour 提前结束。
    /// </summary>
    private bool TryReturnIfPlayerCaptured(MobileParty self)
    {
        try
        {
            var mainHero = Hero.MainHero;
            if (mainHero == null) return false;
            var prisoners = self.PrisonRoster;
            if (prisoners == null) return false;
            // PrisonRoster.Contains(CharacterObject) 在 v1.3.15 走 GetTroopRoster + element.Character 比对，
            // 对玩家 hero 也走这条路径（vanilla 把 MainHero 也封成 CharacterObject）。
            var characterObj = mainHero.CharacterObject;
            if (characterObj == null) return false;
            bool isPrisoner = false;
            foreach (var elt in prisoners.GetTroopRoster())
            {
                if (elt.Character == characterObj) { isPrisoner = true; break; }
            }
            if (!isPrisoner) return false;

            Logger.Warn($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' captured MainHero — returning home '{HomeSettlementOrNull?.Name}' for normal release path");
            ReturnToHome(self);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryReturnIfPlayerCaptured failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// B17.4 A1：玩家氏族主队若 target == self（玩家右键 attack/follow），SetMoveModeHold 让玩家追上。
    /// 玩家放弃 target 后下一 hour 自然恢复（本方法只在 tick 入口短路）。返回 true 表示本 hour 提前结束。
    /// </summary>
    private bool TryHoldForPlayerTarget(MobileParty self)
    {
        try
        {
            var playerParty = Hero.MainHero?.PartyBelongedTo;
            if (playerParty == null || playerParty == self) return false;
            if (playerParty.TargetParty != self) return false;
            // 玩家锁定我 → hold + 让 AI 不再做新决策（仅本 hour 内）
            try { self.SetMoveModeHold(); }
            catch (Exception holdEx) { Logger.Warn($"SetMoveModeHold failed: {holdEx.Message}"); }
            try { self.Ai?.SetDoNotMakeNewDecisions(true); }
            catch (Exception aiEx) { Logger.Warn($"SetDoNotMakeNewDecisions(true) failed: {aiEx.Message}"); }
            Logger.Debug($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' holding for player target");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryHoldForPlayerTarget failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// B17.4 A6：俘虏 roster 超 cap 时随机踢非英雄。0 cap 关闭此功能。
    /// </summary>
    private void TryEnforcePrisonerCap(MobileParty self)
    {
        int cap = SovereignTowns.Configuration.ConfigurationManager.Current?.Thresholds?.PatrolPrisonerCap ?? 0;
        if (cap <= 0) return;
        var prisoners = self.PrisonRoster;
        if (prisoners == null) return;
        int total = prisoners.TotalManCount;
        if (total <= cap) return;
        int excess = total - cap;
        try
        {
            // IG MobileGarrison.CheckIfPrisonersIsAboveThreshold 走的就是 RemoveNumberOfNonHeroTroopsRandomly。
            // 不要用 RemoveIf(closure) — 闭包里的 excess 不会被 decrement,会一次性删光所有非英雄俘虏。
            prisoners.RemoveNumberOfNonHeroTroopsRandomly(excess);
            Logger.Info($"{GetType().Name}: '{PartyNameFormatter.SafeName(self)}' prisoner overflow {total} > {cap}, dropped {excess} non-hero");
        }
        catch (Exception ex)
        {
            Logger.Warn($"TryEnforcePrisonerCap RemoveNumberOfNonHeroTroopsRandomly failed: {ex.Message}");
        }
    }

    // B17.4 B5（食物补给）已 deferred — 留空。下面解释：
    // - IG GivePartyFood（PartyManager.cs:329）是凭空塞食物（party.ItemRoster.AddToCounts(item, N)，无源头扣减），
    //   IG 论坛投诉过"无限粮草作弊"。
    // - "从 home town ItemRoster 扣减"做不到：vanilla Town.Owner 是 Hero（无 ItemRoster），settlement 级也没有公开的市场粮食操作 API。
    // - 项目 CLAUDE.md "非作弊基调" + B5 是 Tier B（非关键），现阶段直接放弃；
    //   ST idle 检测（PartyLifecycleManager.IdleHoursBeforeDisband=36）会兜底"完全卡死饿死的队伍"。
}
```

- [ ] **Step 3: StSallyPartyComponent — opt out of player-target hold**

In `StSallyPartyComponent.cs`, after the `AvoidHostileActions` override (around line 58), add:

```csharp
    // B17.4 A1：sally 是冲锋型任务，被玩家拦下让冲锋失败 — 跳过 player-target hold。
    protected override bool AvoidsPlayerTargetHold => true;
```

(Other 3 子类 sont accept default `false`.)

- [ ] **Step 4: StPatrolPartyComponent — B6 防御日志去抖 + B8 防御后早回归 + A5 二段瞬移**

In `StPatrolPartyComponent.cs`, add a `[CachedData]` field after line 34:

Find:
```csharp
    [CachedData] private TextObject? _cachedName;
```

Replace with:
```csharp
    [CachedData] private TextObject? _cachedName;
    // B17.4 B6：防御日志去抖 — 只在 target 切换时 Info，否则 Debug。
    [CachedData] private Settlement? _lastLoggedDefenseTarget;
    // B17.4 A5：连续 stuck 计数 — scheduler 重发指令后仍卡死多少 hour 后触发瞬移。
    [CachedData] private int _stuckHoursAfterReissue;
```

Then update the defense branch in `OnHourlyTickCore` (around line 147-153):

```csharp
            else
            {
                Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defending '{PartyNameFormatter.SafeName(defenseTarget)}' (under siege)");
                SafeSetMoveDefendSettlement(self, defenseTarget);
                SafeSetInitiative(self, attack: 0.3f, avoid: 0.7f, hours: InitiativeResetHours);
                return;
            }
        }
```

Replace with:

```csharp
            else
            {
                // B17.4 B6：日志去抖 — 只在 defense target 切换时 Info，否则 Debug。
                if (_lastLoggedDefenseTarget != defenseTarget)
                {
                    Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defending '{PartyNameFormatter.SafeName(defenseTarget)}' (under siege)");
                    _lastLoggedDefenseTarget = defenseTarget;
                }
                else
                {
                    Logger.Debug($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' still defending '{PartyNameFormatter.SafeName(defenseTarget)}'");
                }
                SafeSetMoveDefendSettlement(self, defenseTarget);
                SafeSetInitiative(self, attack: 0.3f, avoid: 0.7f, hours: InitiativeResetHours);

                // B17.4 B8：若 defense 目标 IsUnderSiege/IsUnderRaid 已转 false，立刻让 scheduler PickNextStop（不等下一 hour）。
                try
                {
                    bool stillThreat = defenseTarget.IsUnderSiege || (defenseTarget.IsVillage && defenseTarget.Village?.VillageState == TaleWorlds.CampaignSystem.Settlements.Village.VillageStates.BeingRaided);
                    if (!stillThreat)
                    {
                        var nextEarly = scheduler.PickNextStop(self);
                        if (nextEarly != null && nextEarly != defenseTarget)
                        {
                            try { self.SetMoveGoToSettlement(nextEarly, MobileParty.NavigationType.Default, false); }
                            catch (Exception ex) { Logger.Error($"early-return SetMoveGoToSettlement failed", ex); }
                            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' defense target safe — early-return to '{nextEarly.Name}'");
                            _lastLoggedDefenseTarget = null;  // 重置去抖状态
                        }
                    }
                }
                catch (Exception threatEx) { Logger.Warn($"early-return threat check failed: {threatEx.Message}"); }
                return;
            }
        }

        // 离开 defense 路径 → 重置日志去抖
        _lastLoggedDefenseTarget = null;
```

Now update the stuck branch (around line 184-193):

```csharp
        // 4) 卡死保护
        var stuckTimeout = ConfigurationManager.Current.ClanPatrol.StuckTimeoutHours;
        if (scheduler.IsStuck(self, stuckTimeout))
        {
            var next = scheduler.PickNextStop(self);
            var dest = next ?? capital;
            try { self.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(self)}' -> '{PartyNameFormatter.SafeName(dest)}'", ex); }
            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' stuck > {stuckTimeout}h — re-pick next='{PartyNameFormatter.SafeName(dest)}'");
        }
```

Replace with:

```csharp
        // 4) 卡死保护
        var stuckTimeout = ConfigurationManager.Current.ClanPatrol.StuckTimeoutHours;
        if (scheduler.IsStuck(self, stuckTimeout))
        {
            // B17.4 A5：scheduler 重发指令后还卡死 → 累计 hours。一段瞬移阈值后强制传送回 home.GatePosition（IG BoundedParty.IfStuckPortToHome）。
            float teleportHours = ConfigurationManager.Current?.Thresholds?.StuckTeleportHours ?? 0f;
            if (teleportHours > 0 && _stuckHoursAfterReissue >= teleportHours)
            {
                try
                {
                    var home = HomeSettlementOrNull;
                    if (home != null)
                    {
                        // IG BoundedParty.cs:53 用的是 mobileParty.Position（不是 Position2D）。
                        // IG 是发布的 mod，证明 v1.3.15 该 setter 公开可写。
                        self.Position = home.GatePosition;
                        Logger.Warn($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' stuck > {teleportHours}h after re-issue — teleport to '{home.Name}' GatePosition (二段救济)");
                        _stuckHoursAfterReissue = 0;
                        return;
                    }
                }
                catch (Exception tpEx) { Logger.Error($"二段瞬移失败 for '{PartyNameFormatter.SafeName(self)}'", tpEx); }
            }

            var next = scheduler.PickNextStop(self);
            var dest = next ?? capital;
            try { self.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error($"SetMoveGoToSettlement failed for '{PartyNameFormatter.SafeName(self)}' -> '{PartyNameFormatter.SafeName(dest)}'", ex); }
            Logger.Info($"StPatrolParty: '{PartyNameFormatter.SafeName(self)}' stuck > {stuckTimeout}h — re-pick next='{PartyNameFormatter.SafeName(dest)}' (stuck cycles since reissue={_stuckHoursAfterReissue})");
            _stuckHoursAfterReissue++;
        }
        else
        {
            _stuckHoursAfterReissue = 0;  // 不再 stuck，重置计数
        }
```

- [ ] **Step 5: Compile**

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

Expected: green. If `MobileParty.Position2D` setter is private/internal in v1.3.15, replace `self.Position2D = home.GatePosition;` with the alternative API the file already uses for navigation; check decompiled `MobileParty.cs` under `_research/`. Likely candidates: `self.SetMoveGoToSettlement(home, NavigationType.Default, false)` as a soft re-issue, or `self.Position2D` may work if accessible — try compile first.

**Verification fallback:** if `Position2D` is read-only, comment out the teleport branch and add `Logger.Warn` "二段瞬移 API 待重写"，**S4 not blocked** — A5 was Tier A (高价值低成本)，but degrades to "scheduler 软重发" if API unreachable. Treat the rest of Task 5 as still landed.

- [ ] **Step 6: Commit**

```bash
git add SovereignTowns/src/Parties/StPartyComponent.cs SovereignTowns/src/Parties/StPatrolPartyComponent.cs SovereignTowns/src/Parties/StSallyPartyComponent.cs
git commit -m "B17.4 T5: party 状态机共享行为 - A1+A5+A6+B5+B6+B7+B8 7 fixes"
```

---

## Task 6: B3 海港 navigation helper + B4 多级 fallback

**Files:**
- Create: `SovereignTowns/src/Common/SafeMoveHelper.cs`
- Modify: `SovereignTowns/src/Parties/StTransferPartyComponent.cs`
- Modify: `SovereignTowns/src/Parties/StRecruiterPartyComponent.cs` (optional — use helper)
- Modify: `SovereignTowns/src/Parties/StPatrolPartyComponent.cs` (optional — use helper)

**Background:**
- B3：vanilla AI 的 `SetMoveGoToSettlement(target, NavigationType.Default, false)` 在多海岛 clan(Sturgia)的 fief 之间会卡死。Vanilla `GarrisonPartyBehavior.DetermineNavigationForSettlement` 是 IG 走的官方判定 — 由于该 API 是 IG 内部 helper,我们手动包装：检测 target 是否海岛 settlement，是则尝试 `Coastal` 或 `Sea` NavigationType。
- B4：`PartyLifecycleManager.MigrateByHomeSettlement` 与 `StTransferPartyComponent.ResolveSafeFallback` 都只有 source/capital 两个候选 — 都失守时直接 disband。增加"同 clan 路径附近 fortification"作第三 fallback。

- [ ] **Step 1: Create SafeMoveHelper**

Create file `SovereignTowns/src/Common/SafeMoveHelper.cs`:

```csharp
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using Logger = SovereignTowns.Logging.Logger;

namespace SovereignTowns.Common;

/// <summary>
/// B17.4 B3：跨岛 fief 路由 helper。vanilla `SetMoveGoToSettlement(target, NavigationType.Default, false)`
/// 在 source / dest 跨海(Sargot/Liaberon/Sturgia 多岛 etc.)时偶发卡死。
/// 简单启发式：dest 是否 IsCoastal → NavigationType.Coastal；fallback Default。
/// 失败一律退化到 Default。
/// </summary>
public static class SafeMoveHelper
{
    public static void GoTo(MobileParty party, Settlement target, string context)
    {
        if (party == null || target == null) return;
        var navType = DecideNav(party, target);
        try
        {
            party.SetMoveGoToSettlement(target, navType, false);
        }
        catch (Exception ex)
        {
            Logger.Error($"SafeMoveHelper.GoTo failed for '{party.Name}' -> '{target.Name}' [{context}, nav={navType}]; fallback Default", ex);
            try { party.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false); }
            catch (Exception fallbackEx) { Logger.Error($"SafeMoveHelper.GoTo fallback also failed for '{party.Name}'", fallbackEx); }
        }
    }

    private static MobileParty.NavigationType DecideNav(MobileParty party, Settlement target)
    {
        try
        {
            // v1.3.15: Settlement 有 IsCoastal 属性（_research/Settlement.cs 验证）。
            // 若 source 也 IsCoastal,使用 Coastal。否则保持 Default（vanilla 会自己 path-find,跨岛失败由 Logger 报警以便后续诊断）。
            if (target.IsCoastal && party.CurrentSettlement != null && party.CurrentSettlement.IsCoastal)
            {
                return MobileParty.NavigationType.Coastal;
            }
            return MobileParty.NavigationType.Default;
        }
        catch
        {
            return MobileParty.NavigationType.Default;
        }
    }
}
```

Note: `Settlement.IsCoastal` 在 v1.3.15 是 public bool. 若编译报错 `IsCoastal` 不存在,改成 `target.Town?.IsCoastal == true` 或者 grep `_research/Settlement.cs` 找准确的属性名。

`MobileParty.NavigationType.Coastal` 在 v1.3.15 enum 里存在。若不存在,fallback to `Default` only — 即整个 DecideNav 永远返回 Default,helper 仍有价值(它至少集中了 SetMove + try/catch 兜底)。

- [ ] **Step 2: StTransferPartyComponent — use SafeMoveHelper**

In `StTransferPartyComponent.cs`, replace 3 occurrences of `try { self.SetMoveGoToSettlement(...); } catch ...` with `SafeMoveHelper.GoTo`. 

Find around line 141:
```csharp
                else if (self.TargetSettlement != fallback)
                {
                    Logger.Warn($"StTransferParty '{self.Name}': destination '{dest.Name}' owner changed; rerouting to '{fallback.Name}'");
                    try { self.SetMoveGoToSettlement(fallback, MobileParty.NavigationType.Default, false); }
                    catch (Exception ex) { Logger.Error("rerouting failed", ex); }
                }
```
Replace with:
```csharp
                else if (self.TargetSettlement != fallback)
                {
                    Logger.Warn($"StTransferParty '{self.Name}': destination '{dest.Name}' owner changed; rerouting to '{fallback.Name}'");
                    SafeMoveHelper.GoTo(self, fallback, "reroute-to-fallback");
                }
```

Find around line 161:
```csharp
            if (src != null && self.TargetSettlement != src)
            {
                Logger.Warn($"StTransferParty '{self.Name}': 目的地 '{dest.Name}' risk={risk.Level}，改返 '{src.Name}'");
                try { self.SetMoveGoToSettlement(src, MobileParty.NavigationType.Default, false); }
                catch (Exception ex) { Logger.Error("reroute to source failed", ex); }
            }
```
Replace with:
```csharp
            if (src != null && self.TargetSettlement != src)
            {
                Logger.Warn($"StTransferParty '{self.Name}': 目的地 '{dest.Name}' risk={risk.Level}，改返 '{src.Name}'");
                SafeMoveHelper.GoTo(self, src, "risk-reroute-to-source");
            }
```

In `TransferDispatcher.cs`, find around line 101:
```csharp
            try { party.SetMoveGoToSettlement(destination, MobileParty.NavigationType.Default, false); }
            catch (Exception ex) { Logger.Error("SetMoveGoToSettlement initial failed", ex); }
```
Replace with:
```csharp
            Common.SafeMoveHelper.GoTo(party, destination, "TransferDispatcher initial dispatch");
```

Add `using SovereignTowns.Common;` to top of each modified file if not present.

- [ ] **Step 3: B4 — extend ResolveSafeFallback with route-nearby fortification**

In `StTransferPartyComponent.cs`, find `ResolveSafeFallback` (around line 178):

```csharp
    private Settlement? ResolveSafeFallback(Clan partyClan)
    {
        try
        {
            if (partyClan == null) return null;
            var src = _source;
            if (src != null && src.OwnerClan == partyClan) return src;
            return CapitalRegistry.Instance?.GetCapitalForClan(partyClan);
        }
        catch { return null; }
    }
```

Replace with:

```csharp
    private Settlement? ResolveSafeFallback(Clan partyClan)
    {
        try
        {
            if (partyClan == null) return null;
            var src = _source;
            if (src != null && src.OwnerClan == partyClan) return src;
            var capital = CapitalRegistry.Instance?.GetCapitalForClan(partyClan);
            if (capital != null) return capital;

            // B17.4 B4：第三 fallback — 本 clan 名下任意 fortification(town/castle)，路径附近优先
            // (按 party 当前 2D 位置最近排序)。绝不跨 clan，防止把 ST 兵塞给别人。
            return FindNearestClanFortification(partyClan);
        }
        catch { return null; }
    }

    private Settlement? FindNearestClanFortification(Clan partyClan)
    {
        try
        {
            var settlements = partyClan?.Settlements;
            if (settlements == null) return null;
            var party = MobileParty;  // 'this' StPartyComponent.MobileParty
            var partyPos = party?.GetPosition2D ?? default;
            Settlement? best = null;
            float bestDist = float.MaxValue;
            foreach (var s in settlements)
            {
                if (s == null) continue;
                if (!s.IsFortification) continue;
                if (s.IsUnderSiege) continue;
                float d = (s.GetPosition2D - partyPos).Length;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            if (best != null)
                Logger.Info($"StTransferParty: third-tier fallback selected '{best.Name}' (dist={bestDist:F1})");
            return best;
        }
        catch (Exception ex) { Logger.Warn($"FindNearestClanFortification failed: {ex.Message}"); return null; }
    }
```

Note: `MobileParty` here is the `CustomPartyComponent.MobileParty` inherited property — i.e. the party owning this component. Already accessible without extra import.

- [ ] **Step 4: Compile**

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

Expected: green. If `Settlement.IsCoastal` doesn't exist (rare — was added in 1.0+), simplify `DecideNav` to always return `Default` and downgrade helper to "centralized try/catch + Logger" pattern. If `MobileParty.NavigationType.Coastal` enum value doesn't exist in v1.3.15, do the same.

- [ ] **Step 5: Commit**

```bash
git add SovereignTowns/src/Common/SafeMoveHelper.cs SovereignTowns/src/Parties/StTransferPartyComponent.cs SovereignTowns/src/Transfer/TransferDispatcher.cs
git commit -m "B17.4 T6: B3 navigation helper + B4 多级 fallback for transfer"
```

---

## Task 7: A2 — daily summary + per-settlement activity ring + WebConfig endpoint

**Files:**
- Create: `SovereignTowns/src/Audit/DailyActivityCounters.cs`
- Create: `SovereignTowns/src/Audit/PerSettlementActivityRing.cs`
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`
- Modify: `SovereignTowns/src/Audit/DecisionAuditLogger.cs`
- Modify: `SovereignTowns/src/WebConfig/WebConfigEndpoints.cs`

**Background:** IG 沉淀的"玩家可感知反馈" — DailyTick 末尾弹一条"今日招/调/巡逻 N 人",并把每个 settlement 的最近 50 条结构化活动放进环形缓冲供 WebUI 查询。重置必须在 DisplayMessage 之后(IG GarrisonDailyBehavior 五年沉淀的固定顺序)。

- [ ] **Step 1: Create DailyActivityCounters**

Create `SovereignTowns/src/Audit/DailyActivityCounters.cs`:

```csharp
using System.Threading;

namespace SovereignTowns.Audit;

/// <summary>
/// B17.4 A2：DailyTick 间累加的"今日 N 件事"。基于 Interlocked 线程安全 — 决策可在
/// HourlyTick / OnHourlyTickParty / OnHourlyTickSettlement 等任意 vanilla 事件回调里 +1。
/// DailyTick 末尾读取 + 重置（顺序与 IG GarrisonDailyBehavior 一致：read → display → reset）。
/// </summary>
public static class DailyActivityCounters
{
    private static int _recruitedToday;
    private static int _transferredToday;
    private static int _patrolDispatchedToday;
    private static int _sallyDispatchedToday;
    private static int _prisonerRecruitedToday;

    public static int RecruitedToday => Volatile.Read(ref _recruitedToday);
    public static int TransferredToday => Volatile.Read(ref _transferredToday);
    public static int PatrolDispatchedToday => Volatile.Read(ref _patrolDispatchedToday);
    public static int SallyDispatchedToday => Volatile.Read(ref _sallyDispatchedToday);
    public static int PrisonerRecruitedToday => Volatile.Read(ref _prisonerRecruitedToday);

    public static void AddRecruited(int n) { if (n > 0) Interlocked.Add(ref _recruitedToday, n); }
    public static void AddTransferred(int n) { if (n > 0) Interlocked.Add(ref _transferredToday, n); }
    public static void AddPatrolDispatched(int n) { if (n > 0) Interlocked.Add(ref _patrolDispatchedToday, n); }
    public static void AddSallyDispatched(int n) { if (n > 0) Interlocked.Add(ref _sallyDispatchedToday, n); }
    public static void AddPrisonerRecruited(int n) { if (n > 0) Interlocked.Add(ref _prisonerRecruitedToday, n); }

    /// <summary>读取所有计数器为一个 snapshot 元组。原子性不保证(允许 +1 漏算),DailyTick 末尾使用 OK。</summary>
    public static (int recruited, int transferred, int patrols, int sallies, int prisoners) Snapshot()
        => (RecruitedToday, TransferredToday, PatrolDispatchedToday, SallyDispatchedToday, PrisonerRecruitedToday);

    /// <summary>清零所有计数器。必须在 DisplayMessage 之后调用。</summary>
    public static void ResetAll()
    {
        Interlocked.Exchange(ref _recruitedToday, 0);
        Interlocked.Exchange(ref _transferredToday, 0);
        Interlocked.Exchange(ref _patrolDispatchedToday, 0);
        Interlocked.Exchange(ref _sallyDispatchedToday, 0);
        Interlocked.Exchange(ref _prisonerRecruitedToday, 0);
    }
}
```

- [ ] **Step 2: Create PerSettlementActivityRing**

Create `SovereignTowns/src/Audit/PerSettlementActivityRing.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SovereignTowns.Audit;

/// <summary>
/// B17.4 A2：按 settlement.StringId 分桶,每桶最近 N 条结构化活动 → WebConfig 端点。
/// IG ActivityLog.cs:46-58 的 FIFO 容量 100；这里默认 50（玩家关心的窗口短）。
/// 内存常驻 ~20 town × 50 entry × ~300B ≈ 0.3MB,可忽略。纯 in-memory 不持久化。
/// </summary>
public static class PerSettlementActivityRing
{
    public const int Capacity = 50;

    public sealed class Entry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Kind { get; set; } = "";          // "Recruit" / "Transfer" / "Patrol" / "Sally" / "Prisoner" / ...
        public string Summary { get; set; } = "";        // 一句话摘要,如 "招募 12 兵 from Village_X"
        public string? DecisionJson { get; set; }        // 可选 — 复用 DecisionAuditLogger.DecisionJson
    }

    private static readonly ConcurrentDictionary<string, LinkedList<Entry>> _bySettlement = new();

    public static void Add(string? settlementStringId, string kind, string summary, string? decisionJson = null)
    {
        if (string.IsNullOrEmpty(settlementStringId)) return;
        var entry = new Entry
        {
            Timestamp = DateTime.UtcNow,
            Kind = kind ?? "",
            Summary = summary ?? "",
            DecisionJson = decisionJson,
        };
        var list = _bySettlement.GetOrAdd(settlementStringId!, _ => new LinkedList<Entry>());
        lock (list)
        {
            list.AddFirst(entry);
            while (list.Count > Capacity) list.RemoveLast();
        }
    }

    /// <summary>读取某 settlement 的最近 N 条活动(从新到旧)。返回 snapshot,后续修改不影响调用方。</summary>
    public static IReadOnlyList<Entry> Read(string settlementStringId, int maxCount = Capacity)
    {
        if (string.IsNullOrEmpty(settlementStringId)) return Array.Empty<Entry>();
        if (!_bySettlement.TryGetValue(settlementStringId, out var list)) return Array.Empty<Entry>();
        lock (list)
        {
            int count = Math.Min(maxCount, list.Count);
            var snap = new List<Entry>(count);
            var node = list.First;
            for (int i = 0; i < count && node != null; i++, node = node.Next)
                snap.Add(node.Value);
            return snap;
        }
    }
}
```

- [ ] **Step 3: Route DecisionAuditLogger to ring + counters**

In `DecisionAuditLogger.cs`, locate `LogRule` (around line 104):

```csharp
    public static void LogRule(string decisionType, string inputSummary, string decisionJson, bool accepted, string? rejectionReason = null)
    {
        if (!_initialized) return;
        _queue.Enqueue(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            DecisionType = decisionType ?? "",
            Source = DecisionSource.Rule,
            InputSummary = inputSummary ?? "",
            DecisionJson = decisionJson ?? "",
            Accepted = accepted,
            RejectionReason = rejectionReason
        });
    }
```

Replace with:

```csharp
    public static void LogRule(string decisionType, string inputSummary, string decisionJson, bool accepted, string? rejectionReason = null)
    {
        if (!_initialized) return;
        _queue.Enqueue(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            DecisionType = decisionType ?? "",
            Source = DecisionSource.Rule,
            InputSummary = inputSummary ?? "",
            DecisionJson = decisionJson ?? "",
            Accepted = accepted,
            RejectionReason = rejectionReason
        });

        // B17.4 A2：路由到 per-settlement ring + daily counters,不影响磁盘审计流。
        if (!accepted) return;  // 仅成功决策记录到 ring/counter
        TryUpdateActivityRingAndCounters(decisionType ?? "", inputSummary ?? "", decisionJson ?? "");
    }

    /// <summary>
    /// B17.4 A2：从 inputSummary 中粗略解析 settlement key(home= / village= / source= / dest=),
    /// 路由到 PerSettlementActivityRing,并按 decisionType 累加 DailyActivityCounters。
    /// 失败仅吞 — A2 是辅助功能,绝不影响审计主路径。
    /// </summary>
    private static void TryUpdateActivityRingAndCounters(string decisionType, string inputSummary, string decisionJson)
    {
        try
        {
            string? key = ExtractKey(inputSummary, "home=") ?? ExtractKey(inputSummary, "source=") ?? ExtractKey(inputSummary, "village=");
            if (key != null)
            {
                PerSettlementActivityRing.Add(key, decisionType, inputSummary, decisionJson);
            }
            string? destKey = ExtractKey(inputSummary, "dest=");
            if (destKey != null && destKey != key)
            {
                PerSettlementActivityRing.Add(destKey, decisionType + "(inbound)", inputSummary, decisionJson);
            }

            switch (decisionType)
            {
                case "RecruitFromVillage":
                    DailyActivityCounters.AddRecruited(ExtractInt(decisionJson, "recruited"));
                    break;
                case "DispatchRecruiter":
                    DailyActivityCounters.AddRecruited(0);  // dispatch ≠ recruited; counter 由 RecruitFromVillage 增
                    break;
                case "DispatchTransfer":
                    DailyActivityCounters.AddTransferred(ExtractInt(decisionJson, "extracted"));
                    break;
                case "create_patrol_party":
                    DailyActivityCounters.AddPatrolDispatched(1);
                    break;
                case "DispatchSally":
                    DailyActivityCounters.AddSallyDispatched(1);
                    break;
                case "PrisonerRecruit":
                    DailyActivityCounters.AddPrisonerRecruited(ExtractInt(decisionJson, "recruited"));
                    break;
            }
        }
        catch { /* swallow */ }
    }

    /// <summary>从 "key1=val1 key2=val2" 串里取 prefix 后的 token。失败返 null。</summary>
    private static string? ExtractKey(string text, string prefix)
    {
        int idx = text.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;
        int start = idx + prefix.Length;
        int end = text.IndexOf(' ', start);
        if (end < 0) end = text.Length;
        if (end <= start) return null;
        return text.Substring(start, end - start);
    }

    /// <summary>极简 JSON 整数提取:`"key":N`。失败返 0(不抛)。</summary>
    private static int ExtractInt(string json, string key)
    {
        try
        {
            string needle = "\"" + key + "\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int start = idx + needle.Length;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            if (end <= start) return 0;
            return int.TryParse(json.Substring(start, end - start), out var v) ? v : 0;
        }
        catch { return 0; }
    }
```

- [ ] **Step 4: DailyTick 末尾发汇总 + 重置**

In `SovereignTownsCampaignBehavior.cs`, find `OnDailyTick` (around line 284):

```csharp
    private void OnDailyTick()
    {
        try
        {
            DrainWebConfigSync();
            _capitalLogisticsManager?.EvaluateAll();
        }
        catch (Exception ex)
        {
            Logger.Error("DailyTick failed", ex);
        }
    }
```

Replace with:

```csharp
    private void OnDailyTick()
    {
        try
        {
            DrainWebConfigSync();
            _capitalLogisticsManager?.EvaluateAll();

            // B17.4 A2：每日活动汇总(IG GarrisonDailyBehavior.cs:50-66 借鉴)
            // 顺序固定:read snapshot → display → reset(IG 5 年沉淀,顺序错就重复弹窗 / 漏弹)
            if (ConfigurationManager.Current?.EnabledFeatures?.ShowDailySummary == true)
            {
                try
                {
                    var snap = SovereignTowns.Audit.DailyActivityCounters.Snapshot();
                    int total = snap.recruited + snap.transferred + snap.patrols + snap.sallies + snap.prisoners;
                    if (total > 0)
                    {
                        var msg = $"[主权城镇] 今日 招{snap.recruited} 调{snap.transferred} 巡逻{snap.patrols} 出击{snap.sallies} 俘虏招募{snap.prisoners}";
                        InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Green));
                    }
                }
                catch (Exception sumEx) { Logger.Warn($"daily summary display failed: {sumEx.Message}"); }
                finally
                {
                    // ★ 严格在 display 之后(包括异常路径)清零,避免次日重复计数
                    SovereignTowns.Audit.DailyActivityCounters.ResetAll();
                }
            }
            else
            {
                // feature off — 仍然清零,免得后续打开时一次性涌出累计值
                SovereignTowns.Audit.DailyActivityCounters.ResetAll();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("DailyTick failed", ex);
        }
    }
```

`InformationManager` and `InformationMessage` 已在 `using TaleWorlds.Library;` 间接可见; if not, add `using TaleWorlds.Core;` at top.

- [ ] **Step 5: WebConfig 端点 /api/settlements/{id}/activities**

In `WebConfigEndpoints.cs`, add new method anywhere in the class (e.g., after `GetSettlements` around line 198):

```csharp
    /// <summary>GET /api/settlements/{stringId}/activities → 该 settlement 最近 N 条结构化活动。</summary>
    public static void GetSettlementActivities(HttpListenerContext ctx, string settlementStringId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settlementStringId))
            {
                WebConfigServer.WriteError(ctx, 400, "missing_settlement_id", "URL must include /api/settlements/{stringId}/activities");
                return;
            }
            var entries = SovereignTowns.Audit.PerSettlementActivityRing.Read(settlementStringId);
            WebConfigServer.WriteJson(ctx, 200, new { settlement = settlementStringId, count = entries.Count, activities = entries });
        }
        catch (Exception ex)
        {
            Logger.Error("GetSettlementActivities threw", ex);
            WebConfigServer.WriteError(ctx, 500, "internal_error", ex.Message);
        }
    }
```

Then in `WebConfigServer.cs` (or wherever routes are defined — search for `GetSettlements` calls), wire the URL. **Read `WebConfigServer.cs` first** to see the routing pattern, then add:

```csharp
// In the request dispatch switch:
// Pattern: /api/settlements/{stringId}/activities
if (path.StartsWith("/api/settlements/") && path.EndsWith("/activities"))
{
    var id = path.Substring("/api/settlements/".Length, path.Length - "/api/settlements/".Length - "/activities".Length);
    WebConfigEndpoints.GetSettlementActivities(ctx, id);
    return;
}
```

(Exact wiring depends on `WebConfigServer.cs`'s pattern — examine and adapt. Skip this sub-step if the routing layer is complex enough to warrant its own task; the endpoint method is still useful for unit-test / future wiring.)

- [ ] **Step 6: Compile**

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

Expected: green.

- [ ] **Step 7: Commit**

```bash
git add SovereignTowns/src/Audit/DailyActivityCounters.cs SovereignTowns/src/Audit/PerSettlementActivityRing.cs SovereignTowns/src/Audit/DecisionAuditLogger.cs SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs SovereignTowns/src/WebConfig/WebConfigEndpoints.cs SovereignTowns/src/WebConfig/WebConfigServer.cs
git commit -m "B17.4 T7: A2 - daily summary + per-settlement activity ring + WebConfig endpoint"
```

---

## Task 8: B1 — config-change event → in-flight recruiter refresh

**Files:**
- Modify: `SovereignTowns/src/Configuration/ConfigurationManager.cs`
- Modify: `SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs`

**Background:** `TownGarrisonRule` 改了(MinTier / 比例 / AllowNoble),正在路上的 `StRecruiterPartyComponent` 仍按旧规则招。新建 `Action<string?>` 事件,`ReplaceAndSave` 后触发;Behavior 订阅并把相关 in-flight recruiter 切回 `Dispatching` 阶段重新规划。

- [ ] **Step 1: Add event to ConfigurationManager**

In `ConfigurationManager.cs`, add a static field + event near the top of the class (after line 39 `private static string _lastValidationError = "";`):

```csharp
    /// <summary>
    /// B17.4 B1：PerSettlementOverrides 或 GlobalDefaults 变更后触发。
    /// 参数:被改的 settlement.StringId,或 null 表示全局/未知(订阅者需对所有 in-flight 队伍重规划)。
    /// 仅在 ReplaceAndSave 成功后触发(Save 单独存当前 _current,不触发 — 避免无变化时刷屏)。
    /// </summary>
    public static event Action<string?>? OnConfigChanged;
```

In `ReplaceAndSave` (around line 215), after successful `WriteToDiskUnlocked` and before `return true;` (around line 241):

Find:
```csharp
                _current = newConfig;
                _lastValidationError = "";

                string configPath = GetConfigFilePath();
                EnsureConfigDirectoryExists(configPath);
                _current.LastModified = DateTime.UtcNow.ToString("O");
                WriteToDiskUnlocked(configPath, _current);
                Logger.Info($"ReplaceAndSave: wrote new config to '{configPath}'");
                reason = "";
                return true;
```

Replace with:
```csharp
                _current = newConfig;
                _lastValidationError = "";

                string configPath = GetConfigFilePath();
                EnsureConfigDirectoryExists(configPath);
                _current.LastModified = DateTime.UtcNow.ToString("O");
                WriteToDiskUnlocked(configPath, _current);
                Logger.Info($"ReplaceAndSave: wrote new config to '{configPath}'");

                // B17.4 B1：通知订阅者 config 已变 — 让 in-flight 队伍重规划。
                // 写盘后触发(失败的 replace 不应通知,避免订阅者瞎刷新)。
                try { OnConfigChanged?.Invoke(null); }
                catch (Exception evEx) { Logger.Warn($"OnConfigChanged invocation failed: {evEx.Message}"); }

                reason = "";
                return true;
```

- [ ] **Step 2: Subscribe in CampaignBehavior + refresh in-flight recruiters**

In `SovereignTownsCampaignBehavior.cs`, add a subscription in `RegisterEvents` (around line 57). Find:

```csharp
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);
            Logger.Info("SovereignTownsCampaignBehavior: events registered");
```

Insert before `Logger.Info`:

```csharp
            // B17.4 B1：config 变更 → 重规划 in-flight recruiter(让 TownGarrisonRule 更改即时生效)。
            ConfigurationManager.OnConfigChanged += OnConfigChangedHandler;
```

Add the handler method anywhere in the class (e.g., after `OnHeroChangedClan` around line 450):

```csharp
    /// <summary>
    /// B17.4 B1：ConfigurationManager.ReplaceAndSave 后被回调。
    /// 把所有 in-flight StRecruiterPartyComponent 切回 Dispatching 阶段,让 PlanNextHop 用新规则重选目标。
    /// settlementStringId == null → 影响所有 home town。否则仅影响 home 匹配的队伍。
    /// </summary>
    private void OnConfigChangedHandler(string? settlementStringId)
    {
        try
        {
            foreach (var party in MobileParty.AllCustomParties)
            {
                try
                {
                    if (party?.PartyComponent is not SovereignTowns.Parties.StRecruiterPartyComponent recruiter) continue;
                    var home = recruiter.HomeSettlementOrNull;
                    if (home == null) continue;
                    if (settlementStringId != null && home.StringId != settlementStringId) continue;

                    recruiter.SetAssignedTarget(null);
                    recruiter.TransitionTo(SovereignTowns.Parties.StRecruiterPartyComponent.RecruiterPhase.Dispatching);
                    Logger.Info($"OnConfigChanged: '{PartyNameFormatter.SafeName(party)}' transitioned to Dispatching for re-planning under new rule");
                }
                catch (Exception innerEx) { Logger.Warn($"OnConfigChanged per-party refresh failed: {innerEx.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("OnConfigChangedHandler failed", ex);
        }
    }
```

Note: handler does not unsubscribe — `CampaignBehaviorBase` 生命周期与游戏会话同生死;静态 event 跨会话可能累积 handler 引用 — 在 `RegisterEvents` 入口处先 `ConfigurationManager.OnConfigChanged -= OnConfigChangedHandler;` 做一次 idempotent unsubscribe,然后再 `+=`:

Change the subscription line to:
```csharp
            ConfigurationManager.OnConfigChanged -= OnConfigChangedHandler;  // idempotent: avoid double-subscribe on reload
            ConfigurationManager.OnConfigChanged += OnConfigChangedHandler;
```

- [ ] **Step 3: Compile**

```powershell
dotnet build SovereignTowns\src\SovereignTowns.csproj -c Debug
```

Expected: green.

- [ ] **Step 4: Commit**

```bash
git add SovereignTowns/src/Configuration/ConfigurationManager.cs SovereignTowns/src/Campaign/SovereignTownsCampaignBehavior.cs
git commit -m "B17.4 T8: B1 - OnConfigChanged event → in-flight recruiter Dispatching transition"
```

---

## Self-Review Checklist

After all 8 tasks land, before declaring done:

1. **Spec coverage** — every of 18 Tier S/A/B fixes has a task:
   - S1 (3 dispatcher IsUnderSiege) → T3 Steps 1-3
   - S2 (RecruitmentDispatcher GarrisonParty rebuild) → T3 Step 1
   - S3 (LeaveSettlementAction order) → T4
   - S4 (atomic write) → T2 Step 1
   - A1 (OrderStopIfPlayerTarget) → T5 Step 1-2 (helper)
   - A2 (daily summary + activity ring + endpoint) → T7
   - A3 (RecruiterMinHomeGarrison floor) → T3 Step 1
   - A4 (version-mismatch UI) → T2 Step 2
   - A5 (二段瞬移) → T5 Step 4
   - A6 (prisoner cap) → T5 Step 2 (helper TryEnforcePrisonerCap)
   - B1 (config-change → in-flight refresh) → T8
   - B2 (招募降级搜索) → T3 Step 4
   - B3 (海港 navigation helper) → T6 Step 1-2
   - B4 (多级 fallback) → T6 Step 3
   - B5 (food 补给) → **DEFERRED** (Tier B 非关键；vanilla 无 settlement-级食物 ItemRoster 公开 API；IG 凭空塞与 ST "非作弊基调"冲突)
   - B6 (防御日志去抖) → T5 Step 4
   - B7 (玩家俘虏保护) → T5 Step 2 (helper TryReturnIfPlayerCaptured)
   - B8 (防御后早回归) → T5 Step 4

2. **Hard invariant scan**:
   - No `OnSessionLaunched`-side AddModel call (none added)
   - HourlyTickPartyEvent: T5 helpers all run inside `OnHourlyTick`, which is already PartyComponent-filtered ✓
   - try/catch entry: all new event handlers wrapped ✓
   - SaveableField slot conflicts: T5 only adds `[CachedData]` fields (B6/B8/A5) — no save schema change. T1 only adds POCO fields — no `[SaveableField]` ✓
   - JSON: T7 uses Newtonsoft via existing WriteJson helper ✓
   - net472 compat: `File.Replace` avoided in S4, used `Delete + Move` ✓

3. **Type consistency** — `OnConfigChanged` event signature `Action<string?>?` is used consistently in T8 Steps 1 and 2 ✓

---

## Out-of-scope (do not pursue this round)

- Reverse-参考 items (ST 已优于 IG): see B17.4 conversation report — these are deliberate not-changes, do NOT touch GameModels / TroopUpgradeService / RebuildFromCampaign / etc.
- IG ActivityLog 持久化 — A2 ring is in-memory only; IG persists, but ST's "save schema 硬依赖 mod" 投资不值
- Sally Forth borrows — IG has no equivalent, ST has independent design
- B3 high-quality navigation helper (full DetermineNavigationForSettlement port) — Phase 2; current SafeMoveHelper is best-effort

---

## Execution Handoff

Plan saved. **Default execution mode: subagent-driven (per superpowers skill recommendation).** 5 buckets can run in 2 parallel waves:

**Wave 1 (parallel, no shared files):**
- Agent A: T1 + T2 (Configuration layer)
- Agent B: T4 (PartyMergeService)
- Agent C: T7 (Activity ring + DailyTick + Endpoint — new files + isolated touches)

**Wave 2 (parallel, after Wave 1 lands — depends on T1):**
- Agent D: T3 (Dispatchers + Planner)
- Agent E: T5 + T6 (Party shared behaviors + Navigation helper + multi-tier fallback — same family of files, run serially in one agent to avoid conflicts)

**Wave 3 (after T2 + T5):**
- Agent F: T8 (Event wiring)

Final step (after all agents land): `dotnet build` end-to-end, commit any merge-cleanup, mark all todos completed.
