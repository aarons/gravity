# Localization

Feature work starts in English. Keep the [README](../README.md) current so reviewers
have context for the mod's features and UI, then run the dedicated translation pass.

| File | Role |
| --- | --- |
| `localization/eng.json` | English game text. |
| `workshop/localizations/english.json` | Independent English Workshop listing. |
| `supported-languages.json` | The 14 game-to-Steam locale mappings. |
| `scripts/prompts/` | Separate review instructions for game UI and Workshop listings. |
| `localization-review.json` | Generated review fingerprints; commit with translations. |

`esp`/`latam` is Latin American Spanish; `spa`/`spanish` is Castilian Spanish.

## Review workflow

```sh
./update-localizations.sh
./update-localizations.sh --check
```

The updater creates or reviews stale non-English files in Codex sessions, with up
to four running at once. Game reviews finish before Workshop reviews begin so the
listings can use the updated game terminology. Useful options:

- `--jobs 2` reduces concurrency; `--jobs 1` runs sequentially.
- `--model MODEL` overrides the configured model.
- `--context "What changed"` provides additional review guidance.
- `--force` reviews every translation, including after prompt-policy changes.

A file is skipped only when validation passes and its English and translation
hashes match the review record. English edits invalidate their group; translation
edits invalidate that file. Do not manufacture review records. Review the diff
and check fonts and layout in-game: fingerprints track freshness, not quality.

Only one updater can run per checkout. On failure, it finishes active reviews and
preserves successful results; rerun after resolving the problem. Ctrl-C stops
active sessions while keeping completed reviews.

Local installation validates English only. Release preparation requires all 14
locales in both groups with current reviews. To prepare with fewer translation
workers, run the updater with `--jobs 2`, then `./prepare.sh --skip-localizations`.
That flag skips model calls but still requires valid, reviewed translations.

Workshop translations include an AI translation note governed by
`scripts/prompts/workshop-localization.md`. Changelogs are maintained separately
in `workshop/settings.json`, in English and Simplified Chinese, following
[AGENTS.md](../AGENTS.md); the updater does not translate them.

Changing the supported language set also requires updating the expected counts
in `scripts/validate_localization.py`, `tools/WorkshopLocalization/ListingFiles.cs`,
and their tests.
