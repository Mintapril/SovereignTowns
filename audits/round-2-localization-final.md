# Round-2 Localization Audit — Final

**Date**: 2026-05-23
**Scope of Round 2**: only files changed in Round 1 (per advisor's narrow re-audit rule).
**Result**: 0 new P0, 0 new P1 — **terminate**.

## Round 1 → Round 2 delta

| Change | File | Verified |
| --- | --- | --- |
| New key `ST_ClanFinanceSettlement` (EN) | `SovereignTowns/ModuleData/Languages/EN/std_sovereigntowns_strings.xml` line 24 | ✓ grep confirms `text="Sovereign Towns treasury settlement"` |
| New key `ST_ClanFinanceSettlement` (CN) | `SovereignTowns/ModuleData/Languages/CNs/std_sovereigntowns_strings.xml` line 23 | ✓ grep confirms `text="主权城镇 金库结算"` |
| 3 fallback fixes | `src/Parties/StPartyComponent.cs` lines 284, 484, 593 | ✓ `TextObject(GetType().Name)` occurrences = 0; replaced with `TextObject("{=ST_Common_UnknownEntity}(unknown)")` |
| 17 bilingual reasons + IsChinese probe | `src/Economy/TreasuryUserActions.cs` | ✓ 17 `reason = Tr(...)` call sites; private `IsChinese` + `Tr` helpers present (lines 27–44); public method signatures unchanged |
| 99 bilingual reasons + IsChinese probe | `src/Configuration/ConfigurationManager.cs` | ✓ 99 `reason = Tr(...)` call sites; `IsChinese` at line 46; spot-checked validators read naturally (e.g. `Tr("ClanPatrol.EtaBufferHours 非法 (X)；范围 [0, 168]", ...)`); public method signatures unchanged |

`dotnet build SovereignTowns/src/SovereignTowns.csproj -c Debug` → **0 errors, 0 warnings**.

## Sanity re-checks

- `TextObject(GetType().Name)` in `src/Parties/StPartyComponent.cs` → 0 matches.
- `{=ST_*}` keys referenced in code that are NOT declared in XML → only one was `ST_ClanFinanceSettlement`, now declared in both locales.
- `reason = "literal"` or `reason = $"literal"` in `TreasuryUserActions.cs` / `ConfigurationManager.cs` → only 4 hits total, all `reason = ""` (success-path sentinel, not a user message).
- `IsChinese` probe pattern duplicated 4× now (`ActivityNarrator`, `ControlPanelLoc`, `TreasuryUserActions`, `ConfigurationManager`) — intentional, YAGNI'd. A 5th caller would justify promoting to `src/Common/STLoc.cs`.

## Backlog (deferred, not bugs)

See `audits/localization-hygiene-backlog.md`. Items:

- 22 rare-path WebConfig `WriteError` English messages (adversarial / internal-error paths; WebUI wraps with localized prefix).
- 4 prefab `Text="X"` close-glyph literals (icon convention).
- `SOVEREIGN TOWNS` brand title; `Tier` / `T1`/`T2`/... tier labels (vanilla EN+CN convention).
- `ST_Msg_AiOptIn` inline-fallback drift in `VanillaSuppressionManager.cs:106` (XML wins at runtime; cosmetic).
- CN punctuation style consistency (ASCII vs full-width).
- `ActivityNarrator.cs` `Tr(zh, en)` pattern — Round-1 core auditor flagged 17 "P0" findings; they are **not bugs** but the documented project convention mirroring `ControlPanelLoc.cs` (see backlog file for rationale).

## End-state localization coverage

Player-visible surfaces are now fully translated in both EN and zh-CN:

| Surface | Mechanism | Status |
| --- | --- | --- |
| Game-menu options (town/castle "Set capital" etc.) | BL `{=ST_*}` keys | ✓ all in XML |
| Party dialog lines (encounter with own parties) | BL `{=ST_*}` keys + `MBTextManager.SetTextVariable` | ✓ |
| Party names (Patrol/Recruiter/Sally/Transfer) | BL `{=ST_*}` keys | ✓ |
| Battle / disband / destroyed reports | BL `{=ST_*}` keys; defensive fallbacks now use `ST_Common_UnknownEntity` | ✓ |
| Daily summary, web-config notifications, config version-mismatch popup | BL `{=ST_*}` keys | ✓ |
| Incompatible-mods boot popup | BL `{=ST_*}` keys | ✓ |
| AI opt-in notification | BL `{=ST_*}` keys | ✓ |
| GameModel tooltip lines (party speed/wage/size limits + new ClanFinance) | BL `{=ST_*}` keys | ✓ (was 1 undeclared in Round 1, now declared) |
| In-game Control Panel (every label, button, group, hint, confirm dialog) | `ControlPanelLoc.Tr(zh, en)` | ✓ |
| Activity feed (15 decision narratives + 2 fallbacks) | `ActivityNarrator.Tr(zh, en)` | ✓ |
| WebUI panel | JS `tr(zh, en)` + WebUI-side spec table | ✓ |
| Treasury deposit/withdraw failure reasons | `Tr(zh, en)` in `TreasuryUserActions` | ✓ (was English-only in Round 1) |
| Config save / reload / validation failure reasons | `Tr(zh, en)` in `ConfigurationManager` | ✓ (was English-only in Round 1) |

Two-pattern coexistence is the deliberate project architecture:
- **BL i18n + `ST_*` XML keys** for surfaces vanilla touches (menus, dialogs, party names, MBTextManager templates).
- **Inline `Tr(zh, en)` helper** for mod-owned VM / JSON pipelines (Control Panel, activity feed, treasury and config error reasons) — avoids polluting `std_sovereigntowns_strings.xml` with 100+ ephemeral keys.

## Build verification (final)

```
ok dotnet build: 1 projects, 0 errors, 0 warnings
```

Runtime verification (load campaign, exercise treasury, switch language) is pending in-game test by the user.

## Closure

Termination criteria from advisor: 0 new P0 + ≤2 new P1 → met (0 + 0). No Round 3 needed.
