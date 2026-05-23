# Localization audit — `src/Ui/` and `src/WebConfig/`

Audit scope: every C# file under `SovereignTowns/src/Ui/` and `SovereignTowns/src/WebConfig/`. The reference XML is `SovereignTowns/ModuleData/Languages/{EN,CNs}/std_sovereigntowns_strings.xml` (declares `ST_*` keys for game-side strings); the control panel uses an inline bilingual helper, `ControlPanelLoc.Tr(zh, en)`, that branches on the resolved `{=ST_WebUiLang}` value — both `en` and `zh` literals are baked into the C# source, so they are NOT keys-missing-translation. The WebUI is a separate surface that does its own `tr(zh,en)` in JS and only displays JSON values from `/api/*` raw on error paths.

The headline result: the in-scope source is in very good shape. The control panel surface is exhaustively translated through `ControlPanelLoc.Tr` (or, for the spec table, paired `LabelZh`/`LabelEn`/`HintZh`/`HintEn`); the vanilla game-menu and dialog surfaces consistently use `{=key}` TextObject literals that all resolve to keys present in both EN and CNs XML.

The only real findings are in `WebConfig`: HTTP error response `message` strings are English-only and the WebUI does render them raw to the user on `*Failed` / `bad_json` / etc. paths. These are P1 (rare paths, prefixed by a localized "Save failed:" / "Load failed:" wrapper on the WebUI side) — flagged so a Round-2 pass can either drop English from the message and key off `code` in WebUI, or accept the trade-off.

---

## `src/Ui/DiagnosticGameMenu.cs`

clean. All player-facing strings use `{=key}` TextObject literals (`ST_Menu_SetCapital`, `ST_Menu_OpenWebConfig`, `ST_Menu_CapitalStatus_Active/Other`, `ST_Msg_WebConfig_NotRunning/BrowserOpened/BrowserFailed`, `ST_Msg_Capital_Changed/ChangeFailed`, `ST_Common_Unset`), all keys are present in both EN and CNs XML. Menu option ids (`sovereign_towns_capital_status` etc.) are internal identifiers, not user-facing.

Minor non-issue: line 65 uses `{=!}{ST_CAPITAL_STATUS}` because the status text is injected via `MBTextManager.SetTextVariable`; the upstream template at line 191/192 IS keyed (`ST_Menu_CapitalStatus_Active/Other`). Working as designed.

## `src/Ui/STPartyDialogRegistration.cs`

clean. All dialog text uses `{=key}` TextObject literals (`ST_Dialog_PartyKind_*`, `ST_Common_Unknown`, `ST_Dialog_Greeting_Generic`, `ST_Dialog_Greeting_Patrol`, `ST_Dialog_PlayerClose`). NPC line text is injected via `MBTextManager.SetTextVariable("ST_PARTY_GREETING", greeting, false)` from already-keyed templates.

## `src/Ui/ControlPanel/ControlPanelLoc.cs`

clean (this IS the helper). Probes `{=ST_WebUiLang}en` once to detect game language, then `Tr(zh, en)` returns the language-appropriate literal. Caching is correct, exception is swallowed safely. The contract is "ControlPanel files inline both translations rather than declaring keys" — this is the project's documented architecture decision (`<summary>` at top: "不向 std_sovereigntowns_strings.xml 灌 100+ 个 key, 面板自带文案按当前游戏语言就地切换").

## `src/Ui/ControlPanel/ControlPanelScreen.cs`

clean. Contains no user-facing literals; all strings are internal identifiers (`"GauntletLayer"`, `"SovereignTownsControlPanel"`, `"GenericPanelGameKeyCategory"`, `"Exit"`) — sprite category names, prefab/movie names, hotkey ids. Logger messages excluded by scope rules.

## `src/Ui/ControlPanel/ControlPanelMapButtonView.cs`

clean. Contains no user-facing literals; identifiers only (`"GauntletLayer"`, `"SovereignTownsMapButton"`).

## `src/Ui/ControlPanel/MapButtonVM.cs`

clean. Sole user string at line 24: `ControlPanelLoc.Tr("控制面板", "Control Panel")`.

## `src/Ui/ControlPanel/ControlPanelData.cs`

clean. Sole user-visible string is the InvalidOperationException at line 22: `"GlobalConfig 深拷贝失败"`. Per scope rules, exception messages thrown internally do not count; on the call-site (`ControlPanelVM` line 241) this is caught and replaced with a properly localized warning. No action.

## `src/Ui/ControlPanel/ControlPanelVM.cs`

clean. All user-facing strings go through `ControlPanelLoc.Tr`. Notes:

- Line 223 `TitleText => "SOVEREIGN TOWNS"` is the mod brand name — intentionally not translated. **P2 / no action**.
- Lines 322–347 (`ExecuteReload` confirm dialog) use `InformationManager.ShowInquiry` with all four text fields routed through `Tr`. Good.

## `src/Ui/ControlPanel/ControlPanelSpecs.cs`

clean. The entire spec table is a structured bilingual schema: every `SpecEntry` and `SpecGroup` carries paired `LabelZh`/`LabelEn`/`HintZh`/`HintEn`, consumed by `SettingsGroupVM` / `SliderRowVM` / `ToggleRowVM` via `ControlPanelLoc.Tr`. There is no English-only literal. Same payload is also served verbatim to the WebUI via `/api/specs` (which picks the active language client-side).

## `src/Ui/ControlPanel/Tabs/FeaturesTabVM.cs`

clean. Title, intro, every toggle label and hint goes through `ControlPanelLoc.Tr` (lines 15, 16, 19, 27, 30, 35, 40, 45, 50, 55, 60, 65). Both halves are present.

## `src/Ui/ControlPanel/Tabs/StrategyTabVM.cs`

clean. Title / intro / advanced toggle label / reset-group label all wrapped (lines 71–80). Group labels and slider/toggle labels are picked up from the bilingual SpecEntry/SpecGroup payload via `SettingsGroupVM` / `SliderRowVM`.

## `src/Ui/ControlPanel/Tabs/CompositionTabVM.cs`

clean. All UI strings — including the inline `CultureOptions` zh/en/hintZh/hintEn tuple table (lines 134–139), ratio sum legend (line 175 `"Σ = "` is a punctuation prefix, not localizable), tier range labels and reset labels — go through `Tr`. The `"Σ = "` static prefix at line 175 and the bare type strings (`"CavalryRatio"` etc.) are internal identifiers. The localization-by-key uses `LabelFor` in `TroopRowVM` (covered below).

## `src/Ui/ControlPanel/Tabs/TemplatesTabVM.cs`

clean. Every property string goes through `Tr`. The two `InformationManager.ShowInquiry` strings at lines 289–303 (clear-template confirm) are both bilingually wrapped. The two-line dialog options use `Tr("确定","OK")` / `Tr("取消","Cancel")`.

Borderline (P2, no action): line 50 `TierLabel => "Tier"` is a single English word used as a section header and is not localized — vanilla EN+CN saves keep it as "Tier" in practice, and `ChipVM` labels like `"T1"`/`"T2"` on line 598 follow the same convention. Acceptable.

## `src/Ui/ControlPanel/Tabs/BranchesTabVM.cs`

clean. Title / intro / `AutoModeNotice` / spec labels and hints (in inline `SpecEntry` literals on lines 55–70) all carry both `LabelZh`/`LabelEn`/`HintZh`/`HintEn`. Reset label wrapped at line 37.

## `src/Ui/ControlPanel/Tabs/ActivityTabVM.cs`

clean. Every column heading, status caption, button label, error / status message and the inline format strings (lines 183, 218–220, 231) goes through `Tr`. Borderline (P2): line 282 castle tag `" 〔堡〕"` / `" (castle)"` is bilingually wrapped via `Tr`. Treasury button labels (`Deposit 100` etc.) and confirm warnings are also wrapped.

## `src/Ui/ControlPanel/Items/SettingsGroupVM.cs`

clean. Group / slider / toggle labels read from the bilingual SpecEntry payload via `Tr` at lines 61–63 and 81–82.

## `src/Ui/ControlPanel/Items/SliderRowVM.cs`

clean. Label / Hint from bilingual SpecEntry; the reset-link label is wrapped at line 51 (`Tr("↺ 恢复默认 ", "↺ Reset ")`).

## `src/Ui/ControlPanel/Items/ToggleRowVM.cs`

clean. Constructor takes pre-localized `label`/`hint` strings — wrapping happens at the call site (`SettingsGroupVM` / `FeaturesTabVM`).

## `src/Ui/ControlPanel/Items/TroopRowVM.cs`

clean. `AddLabel` wrapped at line 42 (`Tr("✓ 已加","✓ Added")`/`Tr("＋ 加入","+ Add")`). `LabelFor(type)` returns localized labels at lines 80–84. The `GlyphFor` glyphs (`♞`, `↝`, `⚔`, `⤧`, `·`) are language-neutral pictograms.

## `src/Ui/ControlPanel/Items/TroopTemplateRowVM.cs`

clean. `EstimatedCount` at lines 47–50 uses `Tr("≈ " + est + " 人","≈ " + est + " men")` — slightly awkward concat-in-Tr but functionally correct. `TierText` ("T?" fallback at line 79) is fine — vanilla "T1/T2/..." is project-wide convention.

## `src/Ui/ControlPanel/Items/ChipVM.cs`

clean. Label passed in pre-localized by the caller.

## `src/Ui/ControlPanel/Items/LogEntryVM.cs`

clean. Message is pre-localized by caller. `ColorHint` is `"info"`/`"ok"`/`"err"` — internal tone tag, mapped to colors by prefab, not displayed.

## `src/Ui/ControlPanel/Items/FinanceRowVM.cs`

clean. Pure data container; columns are pre-localized by `ActivityTabVM`.

## `src/Ui/ControlPanel/Items/ActivityRowVM.cs`

clean. Pure data container; `Text` is the activity feed item — its content comes from `SovereignTowns.Audit.ActivityFeed` (out of audit scope, in `src/Audit/`).

---

## `src/WebConfig/WebConfigAuth.cs`

clean. No user-facing strings (token generation + ACL-by-filesystem-permission helper).

## `src/WebConfig/WebConfigGameThreadSync.cs`

clean. Only Logger calls (excluded by scope).

## `src/WebConfig/FinancialSnapshot.cs`

clean. Pure DTO struct + Volatile read/write helpers. No literals reach the user; `ClanName` / `Name` fields are populated by the writer with `Clan.Name.ToString()` (vanilla-localized).

## `src/WebConfig/SettlementsSnapshot.cs`

clean. DTO + writer; `name` / `ownerClanName` come from vanilla `Settlement.Name` / `Clan.Name` (vanilla-localized).

## `src/WebConfig/TroopDumper.cs`

clean. Sets `name` / `cultureName` from `CharacterObject.Name.ToString()` / `Culture.Name.ToString()` — these are vanilla-localized for whatever language the game is in (lines 124, 129). The `type` field (`"cavalry"`, `"infantry"`, `"horsearcher"`, `"ranged"`) is a stable internal identifier consumed by `TroopRowVM.LabelFor` (which localizes for display).

## `src/WebConfig/WebConfigServer.cs`

Mostly clean. All error responses are P1 (see WebConfigEndpoints below — same `WriteError` channel). One internal-only error in this file feeds the same path:

| # | line | string | severity | suggested |
|---|------|--------|----------|-----------|
| 1 | 190 | `"Host must be {expectedHost}"` (`code="host_mismatch"`) | P1 | WebUI never reaches this (browser sends correct Host); only triggered by non-browser/curl probes. Acceptable to leave English. |
| 2 | 199 | `"Origin '{origin}' not allowed"` (`code="origin_forbidden"`) | P1 | Same as #1 — defends against attacker-controlled localhost pages. Not reached by legit WebUI. |
| 3 | 234 | `"Missing or invalid X-ST-Token"` (`code="unauthorized"`) | P1 | Only on token tampering. Acceptable. |
| 4 | 279 | `$"No route for {method} {path}"` (`code="not_found"`) | P1 | 404 fallback; never reached by legit WebUI. Acceptable. |
| 5 | 284, 366 | `ex.Message` (`code="internal_error"`) | P1 | Unhandled exception text. WebUI prefixes with localized "Save failed:" / "Load failed:" and appends the raw English message. Acceptable as-is; consumers see localized prefix + cryptic exception detail. |
| 6 | 325 | `"Path traversal not allowed"` (`code="bad_path"`) | P1 | Only on adversarial requests. |
| 7 | 334 | `"ModuleHelper returned empty path"` (`code="module_path_unresolved"`) | P1 | Fatal install-broken state. Rare. |
| 8 | 344 | `"Static path escaped WebUI root"` (`code="outside_webroot"`) | P1 | Adversarial probe. |
| 9 | 350 | `$"Static file not found: {path}"` (`code="not_found"`) | P1 | Stale browser cache or 404. |

These are all stable shapes; the WebUI consumes `r.body.message` raw (e.g., index.html lines 1313, 1356, 1373, 1388, 1505, 1528). If we want them localized, the canonical pattern is: keep the English `message`, switch the WebUI to look up by `code` first and fall back to `message` for unknown codes. That is a WebUI-side change and out of scope here. **Flag and defer**.

## `src/WebConfig/WebConfigEndpoints.cs`

Same `WriteError` channel as above. All findings P1, all consumed by WebUI as `r.body.message`. Listing only the messages, since the `code` strings are internal identifiers:

| # | line | message | code | severity |
|---|------|---------|------|----------|
| 10 | 65 | `Content-Type must be application/json (got '{contentType}')` | `unsupported_media_type` | P1 |
| 11 | 76 | `Content-Length header required` | `length_required` | P1 |
| 12 | 82 | `Request body {declared} bytes exceeds limit {MaxConfigPayloadBytes} bytes` | `payload_too_large` | P1 |
| 13 | 90 | `PUT /api/config requires a JSON body` | `empty_body` | P1 |
| 14 | 101 | `jex.Message` (Newtonsoft error) | `bad_json` | P1 |
| 15 | 107 | `Body deserialized to null` | `null_config` | P1 |
| 16 | 125 | `reason` (validation message bubbled up from `ConfigurationManager.ReplaceAndSave`) | `validation_failed` | **P0** if the underlying validation reasons end up in English and surface in the normal save-flow — needs cross-check, but `ConfigurationManager` is outside this agent's scope. **Flag for a Configuration audit.** |
| 17 | 149 | `ex.Message` | `internal_error` | P1 |
| 18 | 163 | `reloadReason` (from `ConfigurationManager.TryReload`) | `reload_failed` | P1 (same as #16: depends on upstream) |
| 19 | 196 | `"troops.json not yet generated; load a campaign save first"` | `troops_not_dumped` | P1 |
| 20 | 240 | `"URL must include /api/settlements/{stringId}/activities"` | `missing_settlement_id` | P1 |
| 21 | 465 | `Content-Type must be application/json (got '{contentType}')` | `unsupported_media_type` | P1 |
| 22 | 472 | `POST requires a JSON body {"amount":N}` | `empty_body` | P1 |
| 23 | 485 | `"amount must be a positive integer"` | `invalid_amount` | **P1 → potentially P0** — this one IS reachable from the treasury deposit/withdraw flow in the control panel & WebUI if amount=0 ever slips through client-side validation. The control panel preset buttons (lines 122–129 of ActivityTabVM) only send positive integers, so unreachable from the in-game panel; the WebUI may or may not also pre-validate. Worth localizing or letting the WebUI handle by `code`. |
| 24 | 506 | `$"main-thread action did not complete within {TreasuryActionTimeoutMs}ms (campaign tick stalled?)"` | `main_thread_timeout` | P1 |
| 25 | 515 | `reason` (from `TreasuryUserActions.TryDeposit/TryWithdraw`) | (no error code; field `reason` directly on payload) | **P0** — this one is in the success/failure pair of the deposit/withdraw flow the user will actually exercise. The control panel currently reads `body.reason` AND `body.message` (index.html line 1480: `const reason = r.body?.reason || r.body?.message || '?';`). If `TreasuryUserActions.TryDeposit`/`TryWithdraw` produces English-only `reason` strings, every failed deposit/withdraw surfaces English in the panel for CN players. **`TreasuryUserActions` is `src/Economy/`, outside this agent's scope — flag for the next audit.** Note: the control panel's own `ActivityTabVM.DoDeposit/DoWithdraw` (line 188, 236) emits `ControlPanelLoc.Tr("存款失败:", "Deposit failed: ") + reason` — same problem on the in-game side. |

**No untagged `new TextObject("...")` literals, no `GameTexts.FindText`, no hardcoded color tags around localized text. No `InformationManager.DisplayMessage` calls in WebConfig.**

---

## Severity count

- **P0** (definitely seen in non-English locale as English): **2**, both flagged but root cause is **outside this agent's scope** — `ConfigurationManager.ReplaceAndSave` (#16) and `TreasuryUserActions.TryDeposit/TryWithdraw` (#25). Both feed user-visible error strings through the in-scope plumbing without being themselves in scope. Action: trigger a follow-up audit on `src/Configuration/` and `src/Economy/` before declaring localization complete.
- **P1** (rare-path / fallback English error messages): **~22**, all flowing through `WebConfigServer.WriteError`. The cleanest fix is a WebUI-side `code → localized text` table with `message` as the technical fallback; not in scope for this agent.
- **P2** (punctuation / brand / borderline): **3** — `"SOVEREIGN TOWNS"` brand title, single-word "Tier" header, "T1/T2/..." tier glyphs. No action.

The in-scope C# is essentially complete for localization on its own. The remaining gaps live in callees this agent was scoped out of.
