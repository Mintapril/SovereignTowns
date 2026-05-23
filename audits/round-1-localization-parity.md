# Round-1 Localization Parity Audit — SovereignTowns

**Date**: 2026-05-23
**Scope**: ST_* localization keys across EN/CNs XML + C# references + Gauntlet prefabs + WebUI.
**Files audited**:
- `SovereignTowns/SovereignTowns/ModuleData/Languages/EN/std_sovereigntowns_strings.xml` (44 keys)
- `SovereignTowns/SovereignTowns/ModuleData/Languages/CNs/std_sovereigntowns_strings.xml` (44 keys)
- `SovereignTowns/SovereignTowns/ModuleData/Languages/EN/language_data.xml`
- `SovereignTowns/SovereignTowns/ModuleData/Languages/CNs/language_data.xml`
- `SovereignTowns/src/**/*.cs` (all C# sources)
- `SovereignTowns/SovereignTowns/GUI/Prefabs/*.xml` (8 Gauntlet prefab files)
- `SovereignTowns/SovereignTowns/WebUI/index.html`

**Severity legend**:
- **P0**: Code references an id that is not declared in either locale → runtime falls back to inline default, intent is masked OR variable cannot be reloaded by translators.
- **P0**: Placeholder set differs EN vs CN at same id → silent breakage in one locale.
- **P1**: Key in one locale but not the other.
- **P2**: Dead key or cosmetic drift.

---

## Summary

| Check | Result |
| --- | --- |
| 1. Key parity (EN vs CN) | PASS — 44 keys identical, zero placeholder mismatches |
| 2. `language_data.xml` validity | PASS for both files |
| 3. Unused keys (dead) | PASS — every declared key is referenced |
| 4. Code references undeclared id | **1 P0** — `ST_ClanFinanceSettlement` is used in `STClanFinanceModel.cs` but missing from both XMLs |
| 5. Encoding sanity | PASS — both files are valid no-BOM UTF-8, no mojibake, Chinese well-formed |

**Net action items**: add one missing string id (`ST_ClanFinanceSettlement`) to both locale XMLs. Optional P2 cleanup on `ST_Msg_AiOptIn` inline-fallback drift.

---

## 1. Key parity — EN vs CNs

### EN-only keys
*(none)*

### CNs-only keys
*(none)*

### Placeholder-set mismatches at same id
*(none)*

All 44 ids declared in both locales, and every `{PLACEHOLDER}` token set matches between EN and CN.

### Declared id list (44)

```
ST_Battle_Verdict_Damaged          ST_Menu_CapitalStatus_Active        ST_Msg_WebConfig_BrowserFailed
ST_Battle_Verdict_Heavy            ST_Menu_CapitalStatus_Other         ST_Msg_WebConfig_BrowserOpened
ST_Battle_Verdict_Won              ST_Menu_OpenWebConfig               ST_Msg_WebConfig_NotRunning
ST_Common_CapitalOwner             ST_Menu_SetCapital                  ST_Msg_WebConfig_Started
ST_Common_Unknown                  ST_Msg_AiOptIn                      ST_Msg_WebConfig_StartFailed
ST_Common_UnknownEntity            ST_Msg_Battle_Report                ST_PartySizeLimit_Patrol
ST_Common_Unset                    ST_Msg_Capital_Changed              ST_PartySizeLimit_Recruit
ST_Dialog_Greeting_Generic         ST_Msg_Capital_ChangeFailed         ST_PartySizeLimit_Sally
ST_Dialog_Greeting_Patrol          ST_Msg_Config_VersionMismatch       ST_PartySizeLimit_Transfer
ST_Dialog_PartyKind_Generic        ST_Msg_DailySummary                 ST_PartySpeedBonus
ST_Dialog_PartyKind_Recruiter      ST_Msg_Destroyed_NoRefund           ST_PartyWageZero
ST_Dialog_PartyKind_Sally          ST_Msg_Destroyed_WithRefund         ST_PatrolPartyName
ST_Dialog_PartyKind_Transfer       ST_Msg_Disband_Report               ST_RecruiterPartyName
ST_Dialog_PlayerClose              ST_Msg_IncompatibleMods             ST_SallyPartyName
                                                                       ST_TransferPartyName
                                                                       ST_WebUiLang
```

---

## 2. `language_data.xml` validity

### EN (`Languages/EN/language_data.xml`)

```xml
<LanguageData id="English">
  <LanguageFile xml_path="EN/std_sovereigntowns_strings.xml" />
</LanguageData>
```

- `id="English"` — matches vanilla Bannerlord language id (vanilla `Modules/Native/ModuleData/Languages/EN/language_data.xml` uses the same). PASS.
- `xml_path` resolves to the file we audited. PASS.
- No `name=`/`iso=`/`subtitle_extension=` attributes — these are optional in vanilla and only set by the locale's own `language_data.xml`, not by a content module. PASS (consistent with vanilla pattern for content-only modules).

### CNs (`Languages/CNs/language_data.xml`)

```xml
<LanguageData id="简体中文">
  <LanguageFile xml_path="CNs/std_sovereigntowns_strings.xml" />
</LanguageData>
```

- `id="简体中文"` — matches vanilla `Modules/Native/ModuleData/Languages/SChinese/language_data.xml` `id` (note: vanilla's *folder* name is `SChinese`, but the `id` attribute is the Chinese text `简体中文`. Our folder is `CNs` per existing convention, and `LanguageData id` matches by id-string, not by folder). PASS.
- `xml_path="CNs/std_sovereigntowns_strings.xml"` is relative to `ModuleData/Languages/`, resolves correctly. PASS.

Both files declare `encoding="utf-8"` in the XML preamble.

---

## 3. Unused-key check

For each of the 44 declared keys, grepped `src/**/*.cs`, `GUI/Prefabs/*.xml`, and `WebUI/index.html` for the bare key string. Every key is referenced from C#:

| Key | Referenced in |
| --- | --- |
| ST_Battle_Verdict_Heavy / Damaged / Won | `src/Parties/StPartyComponent.cs:281-283` |
| ST_Common_CapitalOwner | `src/Parties/StPartyComponent.cs:483` |
| ST_Common_Unknown | `src/Parties/StPartyComponent.cs:485`, `StPatrolPartyComponent.cs:59`, `StRecruiterPartyComponent.cs:103`, `StSallyPartyComponent.cs:56`, `StTransferPartyComponent.cs:40`, `Ui/STPartyDialogRegistration.cs:89,91,109,111` |
| ST_Common_UnknownEntity | `src/Parties/StPartyComponent.cs:591` |
| ST_Common_Unset | `src/Ui/DiagnosticGameMenu.cs:187` |
| ST_Dialog_Greeting_Generic / Patrol | `src/Ui/STPartyDialogRegistration.cs:94,113` |
| ST_Dialog_PartyKind_* (4) | `src/Ui/STPartyDialogRegistration.cs:79-82` |
| ST_Dialog_PlayerClose | `src/Ui/STPartyDialogRegistration.cs:48` |
| ST_Menu_CapitalStatus_Active / Other | `src/Ui/DiagnosticGameMenu.cs:191-192` |
| ST_Menu_OpenWebConfig | `src/Ui/DiagnosticGameMenu.cs:87` |
| ST_Menu_SetCapital | `src/Ui/DiagnosticGameMenu.cs:75` |
| ST_Msg_AiOptIn | `src/Settlement/VanillaSuppressionManager.cs:106` (see drift note in §4.B) |
| ST_Msg_Battle_Report | `src/Parties/StPartyComponent.cs:286` |
| ST_Msg_Capital_Changed / ChangeFailed | `src/Ui/DiagnosticGameMenu.cs:253,259` |
| ST_Msg_Config_VersionMismatch | `src/Configuration/ConfigurationManager.cs:556` |
| ST_Msg_DailySummary | `src/Campaign/SovereignTownsCampaignBehavior.cs:414` |
| ST_Msg_Destroyed_WithRefund / NoRefund | `src/Parties/StPartyComponent.cs:598,604` |
| ST_Msg_Disband_Report | `src/Parties/StPartyComponent.cs:487` |
| ST_Msg_IncompatibleMods | `src/SovereignTownsSubModule.cs:101` |
| ST_Msg_WebConfig_NotRunning / BrowserOpened / BrowserFailed | `src/Ui/DiagnosticGameMenu.cs:127,142,153` |
| ST_Msg_WebConfig_Started / StartFailed | `src/Campaign/SovereignTownsCampaignBehavior.cs:340,347` |
| ST_PartySizeLimit_Recruit / Transfer / Sally / Patrol | `src/Models/STPartySizeLimitModel.cs:36,44,52,62` |
| ST_PartySpeedBonus | `src/Models/STPartySpeedModel.cs:49` |
| ST_PartyWageZero | `src/Models/STPartyWageModel.cs:40` |
| ST_PatrolPartyName | `src/Parties/StPatrolPartyComponent.cs:57,105` |
| ST_RecruiterPartyName | `src/Parties/StRecruiterPartyComponent.cs:101,172` |
| ST_SallyPartyName | `src/Parties/StSallyPartyComponent.cs:54,115` |
| ST_TransferPartyName | `src/Parties/StTransferPartyComponent.cs:41,86` |
| ST_WebUiLang | `src/Audit/ActivityNarrator.cs:28`, `src/Ui/ControlPanel/ControlPanelLoc.cs:34`, `src/WebConfig/WebConfigEndpoints.cs:554` |

Result: **no dead keys**. Gauntlet prefabs (`GUI/Prefabs/*.xml`) and `WebUI/index.html` contain zero `ST_*` references — the only "ST_" matches in WebUI are `ST_WORKER_ID` inside the bundled `vendor/tailwindcss.js`, which is unrelated (Tailwind worker constant).

---

## 4. Reference parity — `{=ST_…}` in code

### A. P0 — Referenced but NOT declared in either XML

| Severity | Id | Location | Inline fallback in code |
| --- | --- | --- | --- |
| **P0** | `ST_ClanFinanceSettlement` | `src/Models/STClanFinanceModel.cs:33` | `"Sovereign Towns treasury settlement"` |

```csharp
// src/Models/STClanFinanceModel.cs:32-33
private static readonly TextObject TreasuryLine =
    new TextObject("{=ST_ClanFinanceSettlement}Sovereign Towns treasury settlement");
```

This `TextObject` is shown as a line in the clan-finance breakdown tooltip (vanilla `ExplainedNumber` description path). Because the id is undeclared in both EN and CN, at runtime Bannerlord falls back to the inline default text in *every* locale — Chinese players see "Sovereign Towns treasury settlement" in English. Fix: add the following to both XMLs.

- EN: `<string id="ST_ClanFinanceSettlement" text="Sovereign Towns treasury settlement" />`
- CN: `<string id="ST_ClanFinanceSettlement" text="主权城镇 金库结算" />` (suggested — confirm wording with project owner)

### B. P2 — Inline-fallback drift on declared id

| Severity | Id | Issue |
| --- | --- | --- |
| P2 | `ST_Msg_AiOptIn` | The inline fallback in `src/Settlement/VanillaSuppressionManager.cs:106` is **shorter** than the declared text in the XMLs. |

- Declared EN text: `"[Sovereign Towns] AI clans that own a capital are now managed by Sovereign Towns for capital / garrison / recruitment / transfer / sally / prisoner / patrol. Their lords can no longer recruit volunteers via vanilla; ST mod channels (capital in-place / recruiter parties / prisoner recruitment) are the only troop source. Recruitment is restricted to same-culture troops."`
- Code fallback: `"[Sovereign Towns] AI clans that own a capital are now managed by Sovereign Towns for capital / garrison / recruitment / transfer / sally / prisoner / patrol; AI recruitment is restricted to same-culture troops."`

Runtime users see the **declared XML text** (vanilla's `MBTextManager` always prefers the keyed text), so this is purely cosmetic — but the divergence will confuse anyone reading the C# fallback expecting it to match. Pick one canonical wording and sync (either expand the inline fallback to match the XML, or trim the XML to match the code). Lowest-risk: keep XML as the canonical long form, shorten the inline fallback to a placeholder like `"…"`.

### C. Non-localization patterns (verified safe)

The following code patterns also matched `ST_…` but are **not** localization ids — they are MBTextManager *variable names* injected at runtime via `SetTextVariable`:

- `src/Ui/DiagnosticGameMenu.cs:65,175,195` — `"{=!}{ST_CAPITAL_STATUS}"` (the `{=!}` disables localization on the wrapper, `ST_CAPITAL_STATUS` is the variable name set via `MBTextManager.SetTextVariable`)
- `src/Ui/STPartyDialogRegistration.cs:38,75,97,115` — `"{=!}{ST_PARTY_GREETING}"` (same pattern)

These correctly do not require XML entries.

---

## 5. Encoding sanity

| File | Encoding (declared) | Actual bytes | BOM | Strict UTF-8 decode | Mojibake / U+FFFD |
| --- | --- | --- | --- | --- | --- |
| EN/std_sovereigntowns_strings.xml | utf-8 | UTF-8 | none | OK | none |
| CNs/std_sovereigntowns_strings.xml | utf-8 | UTF-8 | none | OK | none |
| EN/language_data.xml | utf-8 | UTF-8 | none | OK | none |
| CNs/language_data.xml | utf-8 | UTF-8 | none | OK | none |

- All four files declare `encoding="utf-8"` and contain no BOM.
- Strict-UTF-8 decode (with `throwOnInvalidBytes: true`) succeeds on every file.
- Scan of CN strings.xml for U+FFFD replacement chars: 0 hits.
- Scan for stray `?` characters in CN content: 2 hits, both in the XML preamble `<?xml ... ?>` — expected, not mojibake.
- Spot-checked Chinese strings (e.g. `主权城镇`, `首府`, `网页控制面板`, `重创`, `祝你顺利。`) — all render correctly under strict UTF-8, no half-width replacements, no broken combining marks.

Cosmetic note (P2, optional): the CNs file mixes regular ASCII commas/colons (e.g. `Documents\Mount and Blade II Bannerlord\Configs\SovereignTowns\auth.txt 读取 token。`) with full-width punctuation (`，`, `：`, `「」`) inconsistently. Not a bug — typography preference only.

---

## Recommended fixes (ordered)

1. **P0** — Add `ST_ClanFinanceSettlement` to both EN and CN `std_sovereigntowns_strings.xml`. Drop-in entries:
   - EN: `<string id="ST_ClanFinanceSettlement" text="Sovereign Towns treasury settlement" />`
   - CN: `<string id="ST_ClanFinanceSettlement" text="主权城镇 金库结算" />` (confirm wording)
2. **P2** — Sync the inline fallback in `VanillaSuppressionManager.cs:106` with the XML-declared text for `ST_Msg_AiOptIn`, or trim the XML to match the code. Pick one canonical form.
3. **P2 (optional)** — Pass over CNs strings to unify ASCII vs. full-width punctuation per project style. No functional impact.

No P1 issues. No dead keys to delete.
