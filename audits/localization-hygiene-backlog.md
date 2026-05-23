# Localization hygiene backlog

Deferred items from the 2026-05-23 localization audit. None block the "no
hardcoded localized strings" gate — they are either rare paths, internal-only,
or intentional convention.

## Defended-path WebConfig error messages (P1, ~22 strings)

`src/WebConfig/WebConfigServer.cs` and `src/WebConfig/WebConfigEndpoints.cs`
produce English-only `message` fields on error responses. The WebUI consumes
these via `r.body.message`, wrapping with a localized `tr("保存失败：", "Save
failed: ")` prefix before display.

All of the messages below are reached only on adversarial requests, malformed
input, or transient errors. They are defensible to leave English because:

- The WebUI prefixes them with a localized wrapper, so the player still sees a
  recognisable failure context in their language.
- The exception text (`ex.Message`) is from .NET BCL and is intrinsically
  English; translating only the surrounding prefix produces a confusing mix.
- Maintaining bilingual translations of `host_mismatch` / `origin_forbidden`
  etc. has no observable user benefit in any realistic install.

The clean fix, if ever desired, is a WebUI-side `code → localized template`
lookup table — keyed on the stable `code` field rather than the English
`message`. Out of scope for this audit pass.

Specific messages:

| File | line | message | code |
|---|---|---|---|
| WebConfigServer.cs | 190 | `Host must be {expectedHost}` | host_mismatch |
| WebConfigServer.cs | 199 | `Origin '{origin}' not allowed` | origin_forbidden |
| WebConfigServer.cs | 234 | `Missing or invalid X-ST-Token` | unauthorized |
| WebConfigServer.cs | 279 | `No route for {method} {path}` | not_found |
| WebConfigServer.cs | 284,366 | `ex.Message` | internal_error |
| WebConfigServer.cs | 325 | `Path traversal not allowed` | bad_path |
| WebConfigServer.cs | 334 | `ModuleHelper returned empty path` | module_path_unresolved |
| WebConfigServer.cs | 344 | `Static path escaped WebUI root` | outside_webroot |
| WebConfigServer.cs | 350 | `Static file not found: {path}` | not_found |
| WebConfigEndpoints.cs | 65,465 | `Content-Type must be application/json (got '{contentType}')` | unsupported_media_type |
| WebConfigEndpoints.cs | 76 | `Content-Length header required` | length_required |
| WebConfigEndpoints.cs | 82 | `Request body {declared} bytes exceeds limit ...` | payload_too_large |
| WebConfigEndpoints.cs | 90,472 | `PUT/POST requires a JSON body` | empty_body |
| WebConfigEndpoints.cs | 101 | `jex.Message` (Newtonsoft parse error) | bad_json |
| WebConfigEndpoints.cs | 107 | `Body deserialized to null` | null_config |
| WebConfigEndpoints.cs | 149 | `ex.Message` | internal_error |
| WebConfigEndpoints.cs | 196 | `troops.json not yet generated; load a campaign save first` | troops_not_dumped |
| WebConfigEndpoints.cs | 240 | `URL must include /api/settlements/{stringId}/activities` | missing_settlement_id |
| WebConfigEndpoints.cs | 485 | `amount must be a positive integer` | invalid_amount |
| WebConfigEndpoints.cs | 506 | `main-thread action did not complete within {X}ms (campaign tick stalled?)` | main_thread_timeout |

## Prefab "X" close glyphs (P2, 4 occurrences)

`Text="X"` used as a close/remove icon. Same convention vanilla Bannerlord
uses. Culturally neutral.

- `GUI/Prefabs/SovereignTownsControlPanel.xml:43` (panel close)
- `GUI/Prefabs/SovereignTownsControlPanel.xml:229` (warning dismiss)
- `GUI/Prefabs/SovereignTownsControlPanel.xml:250` (success dismiss)
- `GUI/Prefabs/STCPTroopTemplateRow.xml:60` (per-row remove)

If a unified close-glyph treatment is ever wanted, add `ST_Common_CloseGlyph`
and bind from a VM.

## Brand and tier strings (P2, no action)

- `ControlPanelVM.TitleText => "SOVEREIGN TOWNS"` — mod brand, intentionally
  not translated (matches vanilla `MOUNT & BLADE II BANNERLORD` treatment).
- `TemplatesTabVM.TierLabel => "Tier"` and `ChipVM` `"T1"`/`"T2"`/... —
  single-token tier labels follow the BL EN+CN convention of leaving "Tier" /
  "T1" untranslated in CN saves.

## ST_Msg_AiOptIn inline-fallback drift (P2)

The C# fallback in `src/Settlement/VanillaSuppressionManager.cs:106` is a
shorter abbreviation of the longer XML-declared text. At runtime
`MBTextManager` always prefers the keyed XML text, so the player only sees the
canonical long form. The drift is purely a confusing-to-read code artefact for
future maintainers.

Fix when convenient: collapse the inline default to a sentinel like `"…"` (so
no one mistakes it for the canonical wording).

## CN punctuation style inconsistency (P2)

`CNs/std_sovereigntowns_strings.xml` mixes ASCII commas/colons with full-width
`，`/`：`/`「」` inconsistently. Style only — pick one convention in a future
pass.

## ActivityNarrator / ControlPanelLoc `Tr(zh, en)` pattern (NOT a bug)

The Round-1 core audit flagged 17 strings in `src/Audit/ActivityNarrator.cs`
as P0 because the file does not use BL `{=ST_*}` keys. This is **not a bug**
— it is the documented project architecture, mirrored from
`src/Ui/ControlPanel/ControlPanelLoc.cs`:

> 不向 std_sovereigntowns_strings.xml 灌 100+ 个 key,
> 面板自带文案按当前游戏语言就地切换。

Both consumers of `ActivityNarrator.Narrate` output (in-game `ActivityRowVM`
display + WebUI JS feed) render the string verbatim — they never round-trip
through BL `MBTextManager`. Routing the strings through `{=ST_*}` keys would
produce identical output and add maintenance overhead.

Decision rationale captured here so future audits don't re-flag it.
