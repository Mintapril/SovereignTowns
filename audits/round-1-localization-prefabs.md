# Round-1 Localization Audit — Gauntlet UI Prefabs

**Date**: 2026-05-23
**Scope**: All `.xml` under `SovereignTowns/GUI/Prefabs/`
**Method**: regex sweep for `Text="…"` non-binding values, `Tooltip=`, `Parameter.Text=`, Chinese characters inside attribute values; visual review of every `TextWidget`/`ButtonWidget`/`Standard.Button` text surface.

## Headline

The prefabs are in very good shape. Every meaningful user-facing label (tab titles, section headers, button text, intro paragraphs, hints, banner messages, log entries, finance rows, slider values, chip labels, toggle labels, capital nameplate) is bound via `Text="@VmProperty"` or a sub-prefab DataSource. Localization is therefore deferred to the C# ViewModel layer (`ControlPanelVM`, `*TabVM`, `*RowVM`).

The **only** literal text strings appearing in any prefab are four occurrences of `Text="X"` used as close / remove button glyphs. These are P2 (glyph-as-icon) — see findings below.

No Chinese strings appear inside any attribute value; the Chinese text matched by ripgrep is all in `<!-- … -->` developer comments, which do not render.

No `Tooltip=` attributes are present in any prefab. (Tooltips, if any, are constructed VM-side via `HintViewModel`.)

---

## SovereignTownsControlPanel.xml (1179 lines)

### P2 — Close button glyph (literal "X")

- **L43** `Text="X"` — header close button (top-right of panel)
  - Context: outer panel close `ButtonWidget` (`Command.Click="ExecuteClose"`).
- **L229** `Text="X"` — warning banner dismiss (`Command.Click="ExecuteDismissWarning"`)
- **L250** `Text="X"` — success banner dismiss (`Command.Click="ExecuteDismissSuccess"`)

**Assessment**: "X" is a culturally neutral close-glyph in both EN and zh-CN UIs; same convention used by vanilla Bannerlord. Strictly a literal but functionally an icon. **Recommend leaving as-is** unless the team adopts a unified close-glyph approach (e.g., bind to `@CloseGlyph` returning "X" / "✕" / sprite swap). If localization purity is desired, add `ST_Common_CloseGlyph="X"` and bind, but no observable user impact.

### Verified clean (representative)

All of the following are correctly VM-bound — no action needed:

- `@TitleText`, `@CapitalName` (header + capital nameplate, L33/L58)
- `@Tab0Label … @Tab5Label` (six tab buttons, L85–L141) — VM source: `ControlPanelVM.Tab*Label`
- `@ActivityLogLabel`, `@Timestamp`, `@Message` (left-rail log)
- `@Warning`, `@Success` (banners)
- Features tab: `@Title`, `@Intro1` (L270/L272)
- Strategy tab: `@Title`, `@Intro1`, `@AdvancedToggleLabel`, `@ActiveGroupLabel`, `@ActiveGroupHint`, `@ResetGroupLabel`
- Composition tab: `@Title`, `@Intro`, `@GenericModeLabel`/`Desc`, `@ExactModeLabel`/`Desc`, `@ExactModeCardTitle`, `@ExactModeCardDesc1`, `@ExactModeCardNoEffect`, `@GoToTemplatesLabel`, `@CultureSectionLabel`, `@CultureFilterHint`, `@RatioSectionLabel`, `@RatioSumText`, `@RatioHintText`, `@TierSectionLabel`, `@MinTierLabel`, `@MaxTierLabel`, `@TierRangeText`
- Templates tab: `@Title`, `@SelectedCountText`, `@ClearLabel`, `@Intro`, `@CatalogSectionLabel`, `@SearchText` (`EditableTextWidget` placeholder), `@MatchCountText`, `@SelectedSectionLabel`, `@RatioSumPercentText`, `@EmptyListText`
- Branches tab: `@Title`, `@ResetAllLabel`, `@Intro`, `@AutoModeNotice`
- Activity tab: `@Title`, `@Intro`, `@ClanTitle`, `@EmptyFiscalText`, `@TreasuryActionTitle`, `@TreasuryActionHint`, `@DepositSmallLabel`/`Medium`/`Large`/`All`, `@WithdrawSmallLabel`/`Medium`/`Large`/`All`, `@TreasuryActionStatus`, `@HoldingsTitle`, `@TodayLabel`, `@Value`, `@Caption`, `@FeedTitle`, `@EmptyText`, `@When`, `@Text`
- Footer: `@DirtyLabel`, `@ReloadLabel`, `@SaveLabel` (L1160, L1163, L1165 via `Standard.Button Parameter.Text`)

---

## SovereignTownsMapButton.xml (37 lines)

Clean. No text content at all — pure icon button using vanilla `GameMenu.Extend.Button` brush + arrow brush. No literal labels.

---

## STCPChip.xml (38 lines)

Clean. Two `TextWidget` instances, both bound to `@Label` (one hidden alpha-zero spacer for layout, one visible).

---

## STCPFinanceRow.xml (57 lines)

Clean. Four `TextWidget` columns, all bound to `@Col1`/`@Col2`/`@Col3`/`@Col4`. The VM (`FinanceRowVM`) owns localization.

---

## STCPSliderRow.xml (67 lines)

Clean. All text bound: `@Label`, `@Hint`, `@ResetLabel`, `@ValueText`. Slider min/max/value are numeric bindings (not text).

---

## STCPToggleRow.xml (35 lines)

Clean. Single `TextWidget` bound to `@Label`. The checkbox indicator is brush-based, no text.

---

## STCPTroopCatalogRow.xml (44 lines)

Clean. All five text fields bound: `@TierText`, `@Name`, `@CultureName`, `@TypeGlyph`, `@AddLabel`. The `@TypeGlyph` is a single-character emoji/symbol coming from VM — VM-side concern, not a prefab finding.

---

## STCPTroopTemplateRow.xml (91 lines)

### P2 — Remove button glyph (literal "X")

- **L60** `Text="X"` — per-row remove button (`Command.Click="ExecuteRemove"`)
  - Same rationale as the three "X" instances in `SovereignTownsControlPanel.xml`: functions as an icon. **Recommend leaving as-is**.

### Verified clean

`@Name`, `@CultureName`, `@TierText`, `@TypeLabel`, `@RatioPercent`, `@EstimatedCount`, `@Ratio` — all bound.

---

## Cross-cutting observations

1. **No `{=id}fallback` syntax used anywhere in prefabs.** All localization flows through C# ViewModels which presumably call `TextObject` against `std_sovereigntowns_strings.xml`. This is a deliberate architectural choice and works fine; just worth flagging as the project's convention so future contributors don't mix the two styles.

2. **Tab labels and tooltips are not declared in `std_sovereigntowns_strings.xml`.** The strings file contains 41 entries, all of which are for non-prefab surfaces (game-menu options, party names, dialog lines, log/error messages, battle reports). Every prefab-side label (`@Tab0Label`, `@TitleText`, `@CapitalName`, `@ReloadLabel`, `@SaveLabel`, `@DepositSmallLabel`, etc.) must therefore originate from C# in one of two places:
   - hardcoded English strings in VM properties (BAD — would not translate to zh-CN), or
   - `TextObject` lookups against a different / additional localization XML or against `new TextObject("{=…}…")` literals.
   
   **This audit cannot verify VM-side localization from the prefabs alone.** Recommend a Round-2 audit on `src/Ui/ControlPanel/**/*.cs` to confirm every `@…Label` / `@…Text` / `@…Title` property is backed by a `TextObject.ToString()` call and that the underlying ids are declared in the EN + CNs XML pair. If many are hardcoded English literals, this is where the real localization debt sits.

3. **`EditableTextWidget` placeholder (L704–706, search box).** `Text="@SearchText"` — the user-typed search query. VM owns initial/empty value. Confirm `SearchText`'s initial value is empty (not a localized "Search…" placeholder leaking English) when reviewing VM.

4. **`Tooltip=`-style tooltips: none.** If the design eventually wants hover-tooltips on tab buttons, deposit buttons, etc., these will be new `HintViewModel`-bound `Hint=` attributes — track for future rounds.

---

## Severity counts

| Severity | Count | Notes |
|----------|-------|-------|
| **P0**   | 0     | No literal user-visible labels in any prefab. |
| **P1**   | 0     | No literal tooltips or secondary labels. |
| **P2**   | 4     | All four are `Text="X"` close/remove button glyphs (icon usage). |

## Recommendation

**Prefabs require no localization changes.** All four "X" literals are conventional close-glyph icons and may be left as-is.

**The real localization risk lives in the C# ViewModels** that supply the bound properties (`ControlPanelVM`, `*TabVM`, `*RowVM`, `ControlPanelSpecs.cs`, `ControlPanelLoc.cs` if present). A Round-2 audit on `src/Ui/ControlPanel/**` is the necessary next step to certify the panel is actually translatable.
