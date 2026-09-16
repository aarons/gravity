Update only $target for the game's locale '$game_language' (Steam language '$steam_language').
Treat $source as the authoritative source for keys and meaning. The current
English reference is included below. Complete the edits now, leaving them uncommitted.

This is a Slay the Spire 2 mod. Read README.md and relevant code under src/ for
feature context, where each string appears, and available UI space.
src/Localization.cs implements embedded translations and English fallback.

Write natural, easy-to-read wording that speakers of this language would expect
in a game. Prefer familiar words and concise sentences. Avoid overly pedantic,
formal, or literal phrasing. Preserve meaning without adding explanations.

Read the existing translation (create it if missing). Review ALL values, even
when keys have not changed. Preserve good translations and established game
terminology. Translate additions and changed meanings; remove obsolete keys.
Match English's key order and preserve placeholders, markup, and line breaks.
Write a UTF-8 JSON object with nonempty string values and no duplicate keys.
Follow regional conventions: esp/latam is Latin American Spanish, while
spa/spanish is Castilian Spanish. supported-languages.json defines the mapping.
Consult git history if useful; current English remains authoritative.

Do not edit English, other locales, review fingerprints, code, or documentation.
Do not commit, prepare releases, publish, or invoke update-localizations.sh. Verify this
file's keys exactly match English. Other locales may still be stale; the caller
validates this file and records its fingerprints after your successful run.
Preserve correct text unchanged when no edit is needed. Summarize changes and
any translation uncertainties in your final response.

English reference ($source):
$english_json

Additional release context:
$context
