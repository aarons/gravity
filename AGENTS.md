# Working in this mod

- Start with README.md. If the root project is still `MyMod.csproj`, this is an
  unnamed template; use `scripts/setup_mod.py` when the user supplies the identity.
- Keep mod behavior under `src/`. Use the existing localization helper and literal
  `Localize("key")` calls for player-facing text. Keep the scaffolding simple.
- Update the README with actual features and UI context before localization review.
- `./install.sh` validates English and installs for local testing; restart the game.
  `dotnet build -c Release` builds without installing. Honor game-path overrides.
- Use `./update-localizations.sh` for the dedicated translation pass. Commit its
  review fingerprints with translations; do not manufacture current review records.
- Use `./package.sh` to prepare a release and `./release.sh --dry-run` to inspect it.
  `./release.sh` is live publication; use it when publishing is requested. Preparation
  alone is not a publication request. Never edit prepared files or rebuild in release.
- A new item needs the one-time official uploader workflow documented in README.md.
  Preserve `workshop/mod_id.txt`; never reuse an ID from another mod.
- Update the root mod JSON's version and the changelog together for releases.
- When changing tooling, run `python3 -m unittest discover -s tests -v`. These tests
  use fake translation sessions and Steam clients. Test mod behavior in-game as needed.

# General Principles

- This mod has localizations for the game, as well as Steam Workshop entries. For new work, focus on making changes in English. A dedicated prompt will be used when we are ready to update localizations, except for changelogs as described below.
- Keep player-facing copy concise. Trust players to infer details that are clear
  from the UI or normal play; explain exceptions only when they help the player
  make a decision or avoid likely confusion. Avoid exhaustive qualifications.
  For example, the encounter tooltip needs only "Encounters visited this act.
  Visit {0} to unlock the boss." Omit "The starting Ancient and bosses do not
  count." The counter and boss unlock flow make that detail apparent in play.

# Changelog Format

When modifying `changeNote` in `workshop/settings.json`:

- Start with the version number on its own line, followed by a blank line.
- Write a concise, high-level one-line summary in English. For releases with multiple items, follow it with a blank line and detailed `- ` bullet points, ordered by player impact and importance, biggest changes first.
- Separate the English and Simplified Chinese text with `────────────────────` on its own line, with a blank line before and after it.
- Follow the separator with the Simplified Chinese summary and corresponding bullet points. Keep both languages in sync with the same entries and order, using clear, natural wording and existing in-game Chinese terminology where applicable.
- Do not include language labels such as "English" or "简体中文", and do not italicize the Chinese text.
- For a release with only one item, use a short paragraph in each language and omit the bullet points.
- Update both languages whenever the changelog changes; no separate localization prompt is needed.

Multiple-item layout (placeholders below should be replaced with release text):

```text
1.1.0

High-level English summary.

- Most important detail.
- Additional detail.

────────────────────

简体中文更新概述。

- 最重要的详细更新内容。
- 其他详细更新内容。
```

Single-item layout:

```text
1.3.0

Added a highlight color slider in settings. Choose your own color, reset to gold, or turn highlighting off.

────────────────────

设置中新增高亮颜色滑块，可自选高亮颜色、恢复默认金色，或关闭高亮。
```
