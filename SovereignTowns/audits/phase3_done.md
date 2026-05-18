# Phase 3 — 对齐执行汇总

> 日期：2026-05-18
> 执行原则：「全部按照文档来」 + 最小改动 + 每项独立 diff + build 验证
> 涉及 5 个 drift 项（D1-D5），全部完成；每项 build 通过。

---

## 改动清单

### D5 — XP 城/堡 1.5/0.5 系数（最早完成）

**文件**：[GarrisonXpInjector.cs:96–123](SovereignTowns/src/Upgrades/GarrisonXpInjector.cs:96)

**改动**：在 `townBonus = round(baseXp × mult)` 之后，扫 `town.OwnerClan.Settlements` 计 town/castle 数，乘以 `townCount × 1.5 + castleCount × 0.5` 系数。

**diff 摘要**（+18 行）：
```csharp
townBonus = MathF.Round(baseXp * mult);
// doc §13.4：按首府拥有者所属城镇/城堡数量额外乘算（城 1.5、堡 0.5）
if (townBonus > 0)
{
    int townCount = 0, castleCount = 0;
    var ownerClan = town.OwnerClan;
    if (ownerClan?.Settlements != null)
    {
        foreach (var s in ownerClan.Settlements)
        {
            if (s == null) continue;
            if (s.IsTown) townCount++;
            else if (s.IsCastle) castleCount++;
        }
    }
    float ownerMult = townCount * 1.5f + castleCount * 0.5f;
    if (ownerMult > 0f) townBonus = MathF.Round(townBonus * ownerMult);
}
```

**边界保护**：`ownerClan?.Settlements != null` null-safe；`ownerMult > 0f` 才相乘（避免归零）。

### D3 — 巡逻被劫村响应

**文件**：[ClanPatrolScheduler.cs:89–142](SovereignTowns/src/Patrol/ClanPatrolScheduler.cs:89)

**改动**：`GetDefenseTarget` 现在同时收集 `IsUnderSiege` 与 `Village.VillageState == BeingRaided`。优先级：**首府围攻 > 非首府最近围攻 > 最近被劫村**。

**diff 摘要**（+15 行）：
- 增加 `raidedVillages` list
- 遍历 `_clan.Settlements` 时分类到 `besieged` / `raidedVillages`
- 围攻结束后 fallthrough 到被劫村

**调用方早已兼容**：[StPatrolPartyComponent.cs:310](SovereignTowns/src/Parties/StPatrolPartyComponent.cs:310) 的 `stillThreat = IsUnderSiege || (IsVillage && BeingRaided)` 检查表明上游已预期此场景，本次 drift 是调度器侧的功能缺失。

### D4 — Sally 优先支援被劫村

**文件**：[SallyDispatcher.cs](SovereignTowns/src/SallyForth/SallyDispatcher.cs)

**改动**：
1. 新增 `FindRaiderTargetingBoundVillage(settlement)` helper（+44 行）— 扫 `town.Villages` 中 `BeingRaided` 状态的村庄，在 30m 半径内找敌方 party 作为出击目标
2. `OnHourlyTickSettlement` 在前置检查后**先**调 `FindRaiderTargetingBoundVillage`，找到则跳过持续可见性 gate 但保留冷却 gate（doc:849 / doc:855），直接 `TryCreateSallyParty`

**diff 摘要**：
```csharp
// doc §10.5：优先支援被劫掠的下辖村庄
var raidTarget = FindRaiderTargetingBoundVillage(settlement);
if (raidTarget != null) {
    if (_lastSallyEndedAt.TryGetValue(settlement, out var raidLastEnd) &&
        (CampaignTime.Now - raidLastEnd).ToHours < SallyCooldownHours) {
        // 冷却拦截
        return;
    }
    TryCreateSallyParty(settlement, garrison!, garrisonCount, raidTarget);
    return;
}
// 普通敌方目标（原有路径）
```

**冷却复用代码**：故意保留两处冷却检查（raid 路径 + 普通敌方路径），未抽函数（按 Phase 3 "Rule of Three"：仅 2 处不抽）。

### D2 — 移除征兵候选 100m 距离硬过滤

**文件**：[RecruitmentPlanner.cs](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs)

**改动**：第 2 类（其他同氏族 town 的村庄）调用 `TryAdd` 时由 `includeDistanceFilter: true` 改为 `false`。XML 文档注释更新以反映 doc §7.2「无距离要求」语义。

**diff 摘要**：
- L143（before）：`includeDistanceFilter: true,`
- L143（after）：`includeDistanceFilter: false,`
- L70 XML 注释由「（仅当 ≤ maxDistance）」改为「doc §7.2 "无距离要求"」
- L74 `maxDistance` 参数注释改为「保留参数 … 距离不再用作硬过滤」

**副作用**：调用方的 100→200 二轮 fallback 模式现在两轮返回相同结果（dead code）。**未触动调用方**（Phase 3 不夹带重构），详见 [phase3_followups.md](audits/phase3_followups.md#F1)。

### D1 — 招募候选第 3 类（友军/中立非同氏族村庄）

**文件**：[RecruitmentPlanner.cs](SovereignTowns/src/Recruitment/RecruitmentPlanner.cs)

**改动**：
1. `TryAdd` 阵营过滤从「`MapFaction != homeFaction → reject`」改为「跨 faction 时必须双方非 null 且 not-at-war」（+7 行）
2. `RankCandidates` 添加第 3 类扫描块：遍历 `Settlement.All` 取 `IsVillage`，排除已在前两类的同氏族村，让 `TryAdd` 的 not-at-war 过滤生效（+19 行）

**diff 摘要**：
```csharp
// TryAdd 内
if (villageFaction != homeFaction) {
    if (homeFaction == null) return;  // 边界：保守拒绝
    if (homeFaction.IsAtWarWith(villageFaction)) return;
}

// RankCandidates 内（cat 2 之后）
foreach (var s in Settlement.All) {
    if (!s.IsVillage) continue;
    if (!seen.Add(s)) continue;
    if (s.OwnerClan == homeClan) continue;  // 已在前两类
    TryAdd(options, s, homeSettlement, homeFaction, homePos, maxDistance,
           includeDistanceFilter: false, excludeSet, matchingRule);
}
```

**边界保护**：`homeFaction == null` 时跨 faction 拒绝（保守，保持原行为）。

---

## 验证

### Build 验证（每项独立）

| 步骤 | 命令 | 结果 |
| --- | --- | --- |
| D5 后 | `dotnet build -c Debug` | 0 errors, 2 pre-existing warnings |
| D3 后 | 同上 | 0 errors, 2 warnings |
| D4 后 | 同上 | 0 errors, 2 warnings |
| D2 后 | 同上 | 0 errors, 2 warnings |
| D1 后 | 同上 | 0 errors, 2 warnings |
| D1 hotfix（faction null 守卫） | 同上 | 0 errors, 2 warnings |

**全程编译 clean**。2 个 warnings (CS8604) 是 pre-existing，详见 [phase3_followups.md](audits/phase3_followups.md#F2)。

### 静态回归测试

按你的指令已删除 `tests/static-regression.ps1` —— 不参与本轮验证。

### 运行时验证

按 [CLAUDE.md](CLAUDE.md)「There are no unit tests. Verification = launch the game」—— 待你在游戏中手测。验证清单建议：
- **D5**：选一个氏族首府，对比改动前后驻军每日 XP 注入是否变化（开 1 城应×1.5，开 1 城 1 堡应×2.0，2 城 2 堡应×4.0 等）
- **D3**：让一个氏族非首府村庄被劫掠，看本氏族巡逻队是否前往支援
- **D4**：让一个氏族下辖村庄被劫，看其某个 town 是否派出 sally 救援（应跳过 3 小时持续可见 gate）
- **D2**：派征兵队，看候选村庄是否能跨距离选到 100m 以外的同氏族其他城的村
- **D1**：在友军（同阵营其他氏族）或中立（非同阵营但和平）境内放征兵队，看是否能选其村作为候选

---

## 未处理项（Phase 3 范围外）

按 Phase 3 "对齐执行（最小改动）" 原则，以下项目**未触动**：

1. **§20 #1 重构（PartyEconomyHelper 分叉统一）** — 留给 Phase 4
2. **W1（TransferBranchToBranchPenalty 命名反差）** — 用户 Q-P2.7 默认保留现状（doc 已承认）
3. **F1（招募 2-轮距离 fallback 成为 dead code）** — Phase 4 重构候选
4. **F2（2 个 pre-existing CS8604 警告）** — Phase 5 健壮性
5. **b1-hygiene-backlog P1/P2/P3** — 未纳入本轮（用户未明示）

详见 [phase3_followups.md](audits/phase3_followups.md)。

---

## 下一阶段

Phase 4 重构主战场：§20 #1（统一队伍粮食 / 自资金）。Phase 4 入口需要：
- 重构计划卡（按用户 prompt 要求"计划卡需我点头才能动手"）
- 当前实现位置（详查每个 Component 的食物/资金逻辑）
- 目标实现（接口签名 + 调用方迁移清单）
- 风险与回滚点
- 验证方式

— Phase 3 报告完
