Update only $target for the Steam Workshop language '$steam_language'
(game locale '$game_language'). Treat $source as the authoritative source for
both title and description. The current English reference is included below.
Complete the edits now, leaving them uncommitted.

This is the public Steam Workshop listing for this Slay the Spire 2 mod.
The title names the mod; the description explains what it does and how to use it
to players browsing Workshop. This is listing copy, not compact settings UI.

Write natural, approachable language that is easy for native speakers to read.
Prefer familiar wording and clear sentences. Avoid overly pedantic, formal, or
word-for-word translations. Adapt sentence structure and idioms naturally while
preserving English's meaning, examples, and friendly tone. Do not invent features,
promises, or extra technical explanations. Keep the title recognizable as the mod's name.

Read the existing translation (create it if missing). Review BOTH title and
description in full, preserving good translations and established terminology.
For context, read README.md; you may consult
localization/$game_language.json for consistent game terminology. Current Workshop
English is authoritative for listing content; do not substitute the
generated workshop/workshop.json or expand the listing with unrelated README details.
Use git history for $source if needed to understand changes.

For every non-English listing, end the description with a separate paragraph
that naturally translates this note into the target language:
"The English text is the source, and these translations were provided by AI.
Corrections and improvements are always welcome!"
This note is required even though it is absent from the English reference.
Preserve a good existing translation of the note, keep it as the final paragraph,
and include it exactly once.

Write exactly the JSON keys title and description, in English's order, with
nonempty string values, UTF-8 encoding, and no duplicate keys or NUL characters.
Preserve placeholders, Steam BBCode/markup, link URLs, and paragraph breaks.
The title must fit 128 UTF-8 bytes; the description must fit 7999 UTF-8 bytes.
Follow regional conventions: latam/esp is Latin American Spanish, while
spanish/spa is Castilian Spanish. supported-languages.json defines the mapping.

Do not edit English, in-game translations, other locales, review fingerprints,
workshop/workshop.json, workshop/settings.json, changelogs, code, or documentation. Do not commit, prepare releases,
publish, connect to Steam, or invoke update-localizations.sh. The caller validates
this file and records its fingerprints after your successful run; other locales
may still be stale. Preserve correct text unchanged when no edit is needed.
Summarize changes and any translation uncertainties in your final response.

English reference ($source):
$english_json

Additional release context:
$context
