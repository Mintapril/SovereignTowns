# Phase 1:征兵村庄选择完全交给 MCMF — 设计计划 (2026-05-22)

## 目标

把"征兵队去哪个村"从 `RecruitmentPlanner.RankCandidates` 的独立打分,
改为 **Pass B MCMF (`SupplyDemandGraph`) 直接决定**。消除双层选村冲突
(MCMF 定 role / RankCandidates 定村)。征兵队保留多站巡回,但行程在派遣时
由 MCMF 输出静态确定,不再运行时重新打分。

## 现状根因(已核实)

- `SupplyDemandGraph` 的 `SourceKind.Village` 只在 `state.IsCapital` 时建,
  把首府**直属村**(`town.Villages`)全部志愿兵聚合成 per-role 桶,
  `SourceDef.Settlement`/`Town` 都填首府 —— 图里没有"哪个村"的粒度。
- `RecruiterPartyInstruction` 不带目标村;`Cost` 对 Village 距离恒 0。
- 征兵队靠 `RankCandidates` 独立打分 + `ClanRecruiterScheduler` 选村。

## 关键设计判定

- **距离费用复用现有口径**:`MatchPolicy.EdgeCost` 里 `distanceCost = round(distance)`,
  Garrison 转移已用同一欧氏距离。跨图村庄费用 ≈ distance(~1000) + overhead(100) +
  penalty ≈ 1250 < `McmfUnmetCost=2000` → MCMF 会为远村出边而非走 unmet。**无需调参**。
- **持久化零存档系统改动**:行程用 `List<Settlement>`,该容器
  (`ConstructContainerDefinition(typeof(List<Settlement>))`)已在 TypeDefiner 注册;
  `int` 字段 vanilla 原生支持。新增 `[SaveableField]` 槽位 25/26/27,删除 23
  (`_visitedThisTrip`)。**不动 `SovereignTownsTypeDefiner`**。
- **图规模有界**:新增 `RecruiterVillageCandidateCap`(默认 250 —— 原版全图约 210 村,
  默认即纳入全图),取距首府最近的 K 个合格村。真正限制征兵队跋涉距离的是边距离费用 +
  `McmfUnmetCost`;此 cap 仅为超大 mod 地图的求解规模安全阀。
- **巡回保留,打分删除**:MCMF 决定村集合;dispatch 层按地理最近邻打包成
  per-role 单队行程。删 `RankCandidates`/`ClanRecruiterScheduler`(独立打分),
  留 `BaseSettlementVisitScheduler`(Patrol 也继承它)。

## 任务分解(依赖序)

### T1 — `RecruiterPartyInstruction` 增 `TargetVillage`
`src/Algorithm/DispatchInstruction.cs`(~5 行):构造增 `Settlement targetVillage`
参数 + `TargetVillage` 属性。`ReturnSettlement` 仍为首府。

### T2 — 配置项 `RecruiterVillageCandidateCap`
`src/Configuration/GlobalConfig.cs` `PartyThresholds`(~2 行):`int = 250`。
`src/Ui/ControlPanel/ControlPanelSpecs.cs`:加 SpecEntry(双端单一来源)。

### T3 — `SupplyDemandGraph` per-village 改造(主手术,一个文件)
`src/Algorithm/SupplyDemandGraph.cs`:
- 新增 `EnumerateRecruitmentVillages(Town capital, Clan clan)`:全图 village,
  过滤(active / 非围城 / 非 Raided·Looted / faction 非交战 / 非 RecruitmentCooldown /
  非在飞征兵队目标),按距首府距离升序取 Top-`RecruiterVillageCandidateCap`。
- L314-315 的 `if (state.IsCapital)` 聚合块 → 改为遍历上述村集合,每村
  `AddCharacterSources(SourceKind.Village, village.Settlement, capitalTown, EnumerateVolunteerTroops(village.Settlement))`。
  `SourceDef.Settlement` = 村,`Town` = 首府 town。
- `CanConnect`:Village 对非首府 demand → false;对首府 role-demand / stockpile →
  role 匹配即连(不再 `source.Settlement==capital` 等值判定)。
- `Cost`:`distance` 分支纳入 Village → `Distance(village, capitalSettlement)`。
- `Decode`:Village → `new RecruiterPartyInstruction(capitalTown, capitalSettlement, source.Settlement, role, count)`。
- 在飞排除:扫 `MobileParty.AllCustomParties` 取本 clan 征兵队待访问村集合。

### T4 — `StRecruiterPartyComponent` 行程驱动状态机
`src/Parties/StRecruiterPartyComponent.cs`:
- 新增 `[SaveableField(25)] List<Settlement> _itinerary`、`[SaveableField(26)] int _itineraryIndex`、
  `[SaveableField(27)] int _tripCountTarget`;删 `[SaveableField(23)] _visitedThisTrip`
  + `VisitedThisTrip`/`MarkVisited`。doc 槽位 [20,28),23 空置。
- `SetItinerary(IReadOnlyList<Settlement>, int tripTarget)`。
- `PlanNextHop` → `AdvanceItinerary`(index++,跳过失效村,越界/达标 → Returning);
  删 `ClanRecruiterScheduler`/`RankCandidates` 调用。`ResolveDepartureTarget` 简化为
  `_itinerary` 首个有效项。
- 返回条件:`_recruitedThisTrip >= _tripCountTarget` 或行程耗尽。
- 暴露 `IEnumerable<Settlement> PendingVillages`(供 T3 在飞排除)。
- `RecruitFromTargetVillage`:`_assignedRole != Unknown` 时只招该 role。

### T5 — `RecruitmentDispatcher.TryDispatchRecruiter` 接收行程
`src/Recruitment/RecruitmentDispatcher.cs`:
- 新签名 `TryDispatchRecruiter(Town homeTown, IReadOnlyList<Settlement> itinerary, GenericTroopRole role, int tripTarget, string reason)`。
- 删 `RankCandidates` 调用 + `RecruiterScheduler` 簿记块。`target = itinerary[0]`。
- 保留围城/食物/cap/护卫校验。`rp.SetItinerary(...)`。

### T6 — `CapitalLogisticsManager` 按 role 打包行程
`src/Managers/CapitalLogisticsManager.cs`:
- 收 `RecruiterPartyInstruction` 按 `Role` 分组;每组 `TargetVillage` 集合按
  距首府最近邻排序成 `List<Settlement>`,`tripTarget = Σ count`,派一支队。

### T7 — 删除死代码
- 删 `src/Recruitment/ClanRecruiterScheduler.cs`、`src/Recruitment/RecruitmentPlanner.cs`
  (`RankCandidates`/`VillageRecruitOption` 全文)。
- `src/Capital/CapitalManager.cs`:删 `_recruiterScheduler` 字段/构造/`RecruiterScheduler` 属性。
- `src/Lifecycle/PartyLifecycleManager.cs`:删 `RecruiterScheduler.NotifyPartyDestroyed` 调用。
- `ClanRecruiterConfig` / `GlobalConfig.ClanRecruiter` / `RecruitmentCandidateBatchSize`:
  实现时 grep,确无 spec/WebUI 引用则删,否则留。

### T8 — Build + 修复 + 验证
`dotnet build -c Debug`,迭代到 0 error。运行验证留给用户游戏内测试。

## 不在本 Phase(留待后续)

- **Phase 2 — Prisoner 接入 MCMF**:`PrisonerConvertInstruction` + `ExecuteMcmfInstruction`
  switch case 已留钩子;加 `SourceKind.Prisoner` + 兵源枚举器 + 执行器。独立提交。
- **Backlog — Patrol 抽兵预扣**:MCMF 算完目标头数被 hourly patrol 抽走,无同-tick
  对账。与本重构正交。
- Sally:事件驱动战术响应,不适合建模进资源分配 MCMF。
