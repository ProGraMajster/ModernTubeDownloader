# Localization coverage — 1.3.0 candidate

## Implemented

The existing localization service uses one canonical registry for 16 locales. Legacy `en` and `pl` values remain stable; `en-US`/`pl-PL` aliases normalize to them. Each locale declares its culture, native name, translated display-name key and region cue. Dictionaries are validated and cached lazily, so startup does not load every language.

Setup and Settings share the native searchable locale picker, with stable automation IDs, selected state, keyboard navigation and full accessible labels. Region icons are locally drawn vectors; complex flags are simplified regional cues. English uses a neutral globe. No emoji font, external asset request or bitmap cache is needed. The existing Web Remote selector uses the same registry and resource keys; it remains the existing web client and runtime.

## Structural coverage

Snapshot: 2026-11-14; 1387 keys per locale, including organizer, Batch automation, startup updates and Web Remote. The final artifact checker found zero missing keys, unknown keys, placeholder token/count differences, empty values, decoding residue or replacement characters. This is a structural audit, not native-speaker approval.

| Locale | Keys | Missing / extra | Explicit reviewed overrides | Unchanged strings |
|---|---:|---:|---:|---:|
| en | 1387 | 0 / 0 | Source | — |
| pl | 1387 | 0 / 0 | Existing Polish source | — |
| cs-CZ | 1387 | 0 / 0 | 134 | 29 |
| de-DE | 1387 | 0 / 0 | 624 | 58 |
| es-ES | 1387 | 0 / 0 | 221 | 38 |
| fr-FR | 1387 | 0 / 0 | 53 | 52 |
| hu-HU | 1387 | 0 / 0 | 143 | 24 |
| it-IT | 1387 | 0 / 0 | 186 | 30 |
| ja-JP | 1387 | 0 / 0 | 624 | 20 |
| ko-KR | 1387 | 0 / 0 | 138 | 17 |
| nl-NL | 1387 | 0 / 0 | 63 | 48 |
| pt-BR | 1387 | 0 / 0 | 173 | 30 |
| ru-RU | 1387 | 0 / 0 | 132 | 19 |
| sk-SK | 1387 | 0 / 0 | 132 | 30 |
| tr-TR | 1387 | 0 / 0 | 224 | 25 |
| zh-CN | 1387 | 0 / 0 | 135 | 17 |

Unchanged strings are retained product names, format identifiers, technical units or terms also used in the target language (for example French «Source», German «Downloads», MP4, LIVE and URL). They are actual resource values, not missing-key fallbacks. English fallback remains available only for an unforeseen key.

## Translation provenance and review

The base translations were generated offline using official Argos Open Tech models with CTranslate2 4.6.0 and SentencePiece 0.2.0. The Czech BPE model additionally used Sacremoses 0.1.1 and subword-nmt 0.3.8. Models were downloaded to an ignored, isolated artifact directory; no text, URLs, paths, cookies or user data were sent to a translation service. Model weights and Python tooling are not application dependencies and are not shipped. Compressed models total 1367569445 bytes.

Source: [Argos package index](https://github.com/argosopentech/argospm-index/blob/main/index.json), [Argos tokenizer implementation](https://github.com/argosopentech/argos-translate/blob/master/argostranslate/tokenizer.py). Sentence-level translation avoids dropped multi-sentence messages; placeholders and product names were protected. A final checker compares placeholder multiplicity as well as indices.

Review corrected short action labels, untranslated English fragments, literal CSV/JSON field names, startup opt-in explanations, global queue resume consent and organizer no-overwrite/recovery/undo confirmations. German and Japanese each have 624 hand-authored overrides, including all onboarding and organizer text. Other locale review is targeted and partial; full semantic/native-speaker review of all generated prose remains **NOT QUALIFIED**. Technical coverage does not establish idiomatic translation quality.

The 2026-11-14 incremental review added Updates.StartupAbortBlocked directly in all 16 languages. Each message explicitly states that download startup and normal application exit are blocked; only this new key was added to the existing resources. The structural audit preserves all 1386 previous values per locale.

The first generation exposed residual SentencePiece markers in Portuguese, HTML entities in Spanish, a repeated Chinese placeholder and incorrect short-label translations. Review corrected dangerous Undo labels in Russian/Slovak, an inverted valid-path instruction in Chinese, omitted JSON-requirement wording, and an unrelated FAQ label on the Korean automatic-update checkbox. Sidecar descriptions, queued-task destination guarantees and retained-partial-recording settings were also reviewed. Decoder cleanup, reviewed overrides and strict assertions fixed these identified defects. Original generation artifacts and review maps remain available; tests reject decoder/HTML residue and require unchanged literal CSV field names and recovery command-line syntax.

| Model | Package | SHA-256 |
|---|---|---|
| en → zh | 1.9 | `433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab` |
| en → cs | 1.9.6 | `f2f5b93fc5a5aa3aa6bece99e0fa9f80487bb9a2ea4e0c3def58d6a4af37c81f` |
| en → nl | 1.8 | `91406f040619fb519e16af9c304709e9f3195297e626eec6924d62c7c981a33b` |
| en → fr | 1.9 | `3a65ed83364f4e7b06e30f9dd823db1934899ed3ce839e63f46dc7b09dc797b4` |
| en → de | 1.3 | `6cd847f0c06c9c66013e6b0932e07fd54a6d90894659c02bf6c5247b72fb25b1` |
| en → hu | 1.9 | `63cd48e94fce5041fb0b3ef6394900a5f4ea014f90f9509e57bf10d79d8bbb10` |
| en → it | 1.0 | `dde2180001a47904ecbbd688a41e35db8a040e4fd5b52e4f29b4bb499516ab32` |
| en → ja | 1.1 | `16300cc4eaa85320520cabcf433b63d01be40ef6966251de72043a083408f716` |
| en → ko | 1.1 | `e03d8e65e6d44525ec5808c3409fcf8728c76c2c76925372b6d3dc3278de17fc` |
| en → pt | 1.9 | `0c5350a2fa5b923de1346edc0d42e08e38bab2e33cede3a0b9a48eb4281ad8a9` |
| en → ru | 1.9 | `591d743ae103752b88ffc38785c50421320f4eff93c8967e0d3d2e14d4e27811` |
| en → sk | 1.9 | `937a73ea56435772a60623959b4f3873dbe65c3de7446f3462919a32c62fd788` |
| en → es | 1.0 | `d698d0ef87ad70d5d184b7fa6965905bf4368f09a2bb9ffb165a79bac96af0c4` |
| en → tr | 1.5 | `2d553a00880a0c21dea5b1c375535659e6bf18c9bf2ce9847c2a2fd2ff50316a` |

## Validation and remaining qualification

- Static artifact audit: **PASS**, all 14 new JSON resources have the exact 1387-key baseline and format tokens. The application never reconstructs placeholders.
- Added focused tests: full key/format/culture validation, duplicate JSON keys, invalid Unicode/format values, alias compatibility, persistence across reload, searchable picker at 100/150/200%, and existing onboarding language changes. Execution results belong to the iteration implementation report and original test logs; test presence does not imply PASS.
- Native screenshots and CJK/font/DPI observations belong to the controlled capture run in the implementation report. Full native/UIA/DPI matrices and release-package qualification remain deferred.
- All generated languages require full native-speaker editorial review before claiming release-quality wording. Japanese/Chinese/Korean punctuation, Brazilian Portuguese terminology and long labels deserve particular review.
- Region icons are intentionally simplified at small sizes, not reproductions of official coats of arms.

Local reproducible evidence is retained in `artifacts/batch-organizer-20261112/translation-tools/`: the exact package-index snapshot, model SHA-256 manifest, generation cache, manual override maps, token audit and final coverage JSON. German/Japanese hand translations are in `artifacts/batch-organizer-20261112/de-ja-translations.tsv`. Existing user data was not used.
