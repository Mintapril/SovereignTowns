# Round-1 Localization Audit — Core (non-UI, non-WebConfig)

**Scope**: all `src/**.cs` EXCEPT `src/Ui/**` and `src/WebConfig/**`.
**Reference**: `SovereignTowns/ModuleData/Languages/{EN,CNs}/std_sovereigntowns_strings.xml`.
**Audit date**: 2026-05-23.

Approach: grepped every common UI surface (`InformationMessage`, `AddQuickInformation`,
`new TextObject(...)`, `SetTextVariable`, `AddGameMenuOption`, `AddDialogLine`,
`SetCustomName`, `MBTextManager`, `ShowInquiry`, `MessageBox`, `<color=>` wraps),
then cross-checked every `{=ST_*}` key used in code against the language XMLs.

Most user-facing surfaces in scope already route through `TextObject("{=ST_*}…")`.
Three substantive issues were found.

---

## src/Audit/ActivityNarrator.cs — **P0** (the big one)

This file produces the player-visible activity-feed strings for the in-game Control
Panel and the WebUI feed (see `Audit/ActivityFeed.cs` and call site
`DecisionAuditLogger.cs:179`). It **bypasses the BL localization system** and ships
hardcoded bilingual literals via an in-C# `Tr(zh, en)` helper, choosing by detecting
language at runtime with `new TextObject("{=ST_WebUiLang}en").ToString() == "zh"`.

Every `Tr(...)` call below is a user-facing string with no entry in
`std_sovereigntowns_strings.xml`. Severity P0 because the player will read these
verbatim in the activity feed.

| Lines | Hardcoded string (EN side) | Context |
|---|---|---|
| 53-54 | `"Recruited {n} troops at {village}"` | case `RecruitFromVillage` |
| 60-61 | `"Sent a recruiter party from {home} to {target}"` | case `DispatchRecruiter` |
| 68-69 | `"Transferred {n} troops from {src} to {dst}"` | case `DispatchTransfer` |
| 76-77 | `"Capital logistics: {n} troops from {src} to {dst}"` | case `CapitalLogisticsMcmfTransfer` |
| 83-84 | `"{home} dispatched a patrol ({n} troops)"` | case `create_patrol_party` |
| 90-91 | `"{home} sallied out to engage the enemy ({n} troops)"` | case `create_sally_party` |
| 97-98 | `"Recruited {n} prisoners into the garrison at {town}"` | case `RecruitPrisoner` |
| 104-105 | `"Upgraded {n} garrison troops at {home}"` | case `UpgradeGarrison` |
| 111 | `"Capital {lost} has fallen"` | case `capital_lost` |
| 113 | `", capital moved to {newCap}"` | case `capital_lost` (suffix) |
| 119-120 | `"New capital designated: {newCap}"` | case `capital_restored` |
| 125-126 | `"Capital manually set to {newCap}"` | case `manual_set_capital` |
| 135-136 | `"Dispatcher assessment: {clan} garrison wage budget {budget} gold/day (~{cap} fully-paid troops)"` | case `DispatcherBudget` manual |
| 137-138 | `"Dispatcher: {clan} garrison wage budget set to {budget} gold/day (~{cap} fully-paid troops)"` | case `DispatcherBudget` auto |
| 144-145 | `"Dispatcher: disbanded {n} excess garrison troops at {place} (over budget)"` | case `DisbandExcessGarrison` |
| 164 | `"somewhere"` / `"某地"` | `Place()` fallback when settlement-id lookup fails |
| 192 | `"a clan"` / `"某氏族"` | `ClanName()` fallback when clan-id lookup fails |

Suggested fix shape: introduce `ST_Activity_*` keys per case
(`ST_Activity_RecruitFromVillage`, `ST_Activity_DispatchRecruiter`,
`ST_Activity_DispatchTransfer`, `ST_Activity_CapitalLogisticsMcmfTransfer`,
`ST_Activity_CreatePatrol`, `ST_Activity_CreateSally`, `ST_Activity_RecruitPrisoner`,
`ST_Activity_UpgradeGarrison`, `ST_Activity_CapitalLost`, `ST_Activity_CapitalLost_Suffix`,
`ST_Activity_CapitalRestored`, `ST_Activity_ManualSetCapital`,
`ST_Activity_DispatcherBudget_Manual`, `ST_Activity_DispatcherBudget_Auto`,
`ST_Activity_DisbandExcess`) and use `new TextObject("{=ST_Activity_...}…")` +
`SetTextVariable` instead of the `Tr()` helper. Then `_isZh` / `Tr` / the language
probe in this file become dead code and can be deleted.

For `Place()` / `ClanName()` fallbacks: `ST_Activity_Somewhere` and
`ST_Activity_SomeClan` (or reuse `ST_Common_Unknown` + `ST_Common_UnknownEntity`).

**None of the call sites in scope produce these strings via `TextObject`** — every
narrate site builds the string in `Tr()` and returns it as a `string`, so the entire
narrator is one focused refactor.

---

## src/Models/STClanFinanceModel.cs — **P0**

| Line | Hardcoded string | Context |
|---|---|---|
| 32-33 | `new TextObject("{=ST_ClanFinanceSettlement}Sovereign Towns treasury settlement")` | Clan-finance breakdown row label shown in the clan-finance tooltip |

**Bug**: the key `ST_ClanFinanceSettlement` is used in code but is **NOT declared**
in either `EN/std_sovereigntowns_strings.xml` or `CNs/std_sovereigntowns_strings.xml`.
Players in `zh` will see the English fallback "Sovereign Towns treasury settlement"
inside the clan-finance tooltip.

Suggested fix: add
```xml
<string id="ST_ClanFinanceSettlement" text="Sovereign Towns treasury settlement" />
```
to both XMLs (with the Chinese translation in CNs).

---

## src/Parties/StPartyComponent.cs — **P1** (fallback only)

| Lines | Hardcoded string | Context |
|---|---|---|
| 284 | `new TextObject(GetType().Name)` | Battle-result message (`TryDisplayBattleResultMessage`) — used when `Name` is null. Would surface as e.g. `"StRecruiterPartyComponent"` in the {PARTY_NAME} slot of `ST_Msg_Battle_Report` |
| 484 | `new TextObject(GetType().Name)` | Disband-report message (`TryDisplayDisbandReport`) — same risk in `ST_Msg_Disband_Report` |
| 593 | `new TextObject(GetType().Name)` | Destroyed-fallback message (`TryDisplayDestroyedFallbackMessage`) — same risk in `ST_Msg_Destroyed_*` |

P1 not P0 because in practice `Name` returns a localized `TextObject` from the
component override (`StRecruiterPartyComponent.Name` etc.) and the null branch is
defensive. But if it ever fires, the player sees a raw C# class name.

Suggested fix: replace fallback with a generic localized key such as
`ST_Common_PartyNameFallback` (text = "party" / "队伍") or the existing
`ST_Common_UnknownEntity`.

---

## All other in-scope files — clean

The following files were inspected and either contain no user-facing strings, or
all user-facing strings go through `new TextObject("{=ST_*}…")` referencing a
declared key. No issues.

- `src/SovereignTownsSubModule.cs` — ✓ (`{=ST_Msg_IncompatibleMods}` at L100-101)
- `src/Campaign/SovereignTownsCampaignBehavior.cs` — ✓ (`{=ST_Msg_WebConfig_Started}` L340, `{=ST_Msg_WebConfig_StartFailed}` L347, `{=ST_Msg_DailySummary}` L413-414)
- `src/Configuration/ConfigurationManager.cs` — ✓ (`{=ST_Msg_Config_VersionMismatch}` L555-556)
- `src/Settlement/VanillaSuppressionManager.cs` — ✓ (`{=ST_Msg_AiOptIn}` L105-106; note: inline fallback text in code is slightly shorter than the EN XML version — non-blocking, XML wins at runtime)
- `src/Models/STPartyWageModel.cs` — ✓ (`{=ST_PartyWageZero}` L40)
- `src/Models/STPartySpeedModel.cs` — ✓ (`{=ST_PartySpeedBonus}` L49)
- `src/Models/STPartySizeLimitModel.cs` — ✓ (`{=ST_PartySizeLimit_*}` L36/44/52/62)
- `src/Models/STVolunteerModel.cs` — ✓ clean (no user strings)
- `src/Parties/StPatrolPartyComponent.cs` — ✓ (`{=ST_PatrolPartyName}`, `{=ST_Common_Unknown}`)
- `src/Parties/StRecruiterPartyComponent.cs` — ✓ (`{=ST_RecruiterPartyName}`, `{=ST_Common_Unknown}`)
- `src/Parties/StSallyPartyComponent.cs` — ✓ (`{=ST_SallyPartyName}`, `{=ST_Common_Unknown}`)
- `src/Parties/StTransferPartyComponent.cs` — ✓ (`{=ST_TransferPartyName}`, `{=ST_Common_Unknown}`)
- `src/Patches/PatrolSpawnSuppressionPatch.cs` — ✓ (empty `TextObject` used as harmless suppression reason, never displayed)
- `src/Algorithm/**` — ✓ clean (no user surfaces; pure solver code)
- `src/Audit/AuditHelpers.cs` — ✓ clean
- `src/Audit/DailyActivityCounters.cs` — ✓ clean
- `src/Audit/PerSettlementActivityRing.cs` — ✓ clean (internal audit)
- `src/Audit/DecisionAuditLogger.cs` — ✓ clean (audit-log strings are out of scope per spec; player-facing translation lives in `ActivityNarrator.cs`)
- `src/Audit/ActivityFeed.cs` — ✓ clean (storage only; text comes pre-translated from ActivityNarrator — see ActivityNarrator findings above)
- `src/Capital/**` (CapitalManager, CapitalRegistry) — ✓ clean
- `src/Common/**` (PartyNameFormatter, PartyReturnConditionChecker, PartyEconomyHelper, BuildingLevelReader, SafeMoveHelper, TroopTransferHelper, AsyncSimulator) — ✓ clean (only Logger strings)
- `src/Configuration/**` (GlobalConfig, FoodGuard, FiscalAutonomyConfig, AiCulturePresets, BranchRule, BuildingBonusConfig, GarrisonThresholdMath, TownGarrisonRule) — ✓ clean
- `src/Coordination/BaseSettlementVisitScheduler.cs` — ✓ clean
- `src/Economy/**` (ClanTreasury, ModTreasury, ModExpenseLedger, TreasuryUserActions) — ✓ clean
- `src/Evaluators/**` (RiskAssessmentService, TroopCompositionEvaluator, TroopClassifier, TroopTemplateMatcher, GenericTroopMatcher, GarrisonPowerEvaluator, HostilePartyScanner, HorizonForecast, EvaluatorCache) — ✓ clean
- `src/Lifecycle/**` (PartyLifecycleManager, PartyMergeService) — ✓ clean
- `src/Logging/Logger.cs` — ✓ clean (not user-facing)
- `src/Managers/CapitalLogisticsManager.cs` — ✓ clean
- `src/Patrol/**` (PatrolDispatcher, ClanPatrolScheduler) — ✓ clean
- `src/Recruitment/**` (RecruitmentDispatcher, CapitalInPlaceRecruiter, BranchInPlaceRecruiter, PrisonerRecruitmentManager, RecruitmentCooldown, StRecruitContext) — ✓ clean
- `src/SallyForth/SallyDispatcher.cs` — ✓ clean
- `src/SaveSystem/SovereignTownsTypeDefiner.cs` — ✓ clean (save-IDs only)
- `src/Settlement/VanillaPatrolSuppressor.cs` — ✓ clean
- `src/Templates/TroopTemplateModeService.cs` — ✓ clean
- `src/Transfer/**` (TransferDispatcher, TransferTask) — ✓ clean
- `src/Upgrades/**` (TroopUpgradeService, GarrisonXpInjector) — ✓ clean

---

## Cross-checks performed (negative results — nothing found)

- `InformationManager.DisplayMessage(new InformationMessage("literal"`)` — none.
- `MBInformationManager.AddQuickInformation` — none.
- `<color=…>` literal tags wrapping localized text — none.
- `GameTexts.FindText(...)` calls referencing undeclared ids — none in scope.
- `new TextObject("text-without-{=id}")` patterns reaching UI — only the three
  `new TextObject(GetType().Name)` fallbacks above; one
  `new TextObject(string.Empty)` in `PatrolSpawnSuppressionPatch` (never displayed);
  and the `ActivityNarrator` `Tr()` strings (not `TextObject` but the same problem).
- `SetTextVariable` with a literal string as value where it should be a `TextObject`
  or runtime entity name — none. All values are entity `.Name`s, numeric counts, or
  pre-built `TextObject`s.
- `.SetCustomName(...)` / `.SetName(...)` calls with literal C# strings — none;
  party display names are sourced from the `override TextObject Name` getters.

---

## Severity tally

- **P0**: 17 strings (16 in `ActivityNarrator.cs` + 1 missing key in `STClanFinanceModel.cs`)
- **P1**: 3 strings (the three `new TextObject(GetType().Name)` fallbacks in `StPartyComponent.cs`)
- **P2**: 0

## Suggested follow-up (single PR)

1. **Add keys** to both `EN/std_sovereigntowns_strings.xml` and
   `CNs/std_sovereigntowns_strings.xml`:
   - `ST_ClanFinanceSettlement`
   - `ST_Activity_*` (15 keys for narrator cases, see ActivityNarrator section)
   - `ST_Activity_Somewhere` + `ST_Activity_SomeClan` (or alias to existing
     `ST_Common_Unknown` / `ST_Common_UnknownEntity`)
   - `ST_Common_PartyNameFallback` (or reuse `ST_Common_UnknownEntity`)
2. **Rewrite** `ActivityNarrator.Narrate` so each case builds a `TextObject` with
   `SetTextVariable` and `.ToString()`s at the end; delete `_isZh` / `IsChinese` /
   `Tr` once `WebUiLang` is no longer needed here.
3. **Replace** `new TextObject(GetType().Name)` at `StPartyComponent.cs:284, 484, 593`
   with `new TextObject("{=ST_Common_PartyNameFallback}party")` (or whichever key
   you pick).

After this round the entire non-UI / non-WebConfig surface should be fully
localizable.
