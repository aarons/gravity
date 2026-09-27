# Publishing to Steam Workshop

## Setup

Use `workshop/image.png` for the main image and optionally add gallery images in
`workshop/previews/` (PNG/JPG/GIF, ordered by filename). Each image must be under
1 MB. Set tags, dependencies, and visibility in `workshop/settings.json`; edit
listing text in `workshop/localizations/`.

The publisher uses the Steam runtime from the checksum-verified Mega Crit uploader
v0.2.0 bundle, downloaded during preparation if missing. This runtime setup targets
macOS ARM64; Intel is untested. The managed Steamworks.NET library and its MIT
license are vendored under `tools/WorkshopLocalization/vendor/`.

## Prepare and release

Update `Gravity.json`'s version and the bilingual `changeNote` in
`workshop/settings.json` together. Then:

```sh
./install.sh                    # Test locally; restart the game.
./prepare.sh                    # Update translations and freeze the release.
./release.sh --dry-run          # Inspect it offline.
./release.sh                    # Publish with Steam running and signed in.
```

Use the same MSBuild game-path overrides with preparation as with installation.
See [Localization](localization.md) for translation options.

Preparation snapshots the build, metadata, images, translations, and publisher
runtime, and archives distinct builds as ZIPs under `archive/`. Failed builds
preserve the previous prepared release. Review `workshop/workshop.json`,
`workshop/content/`, and `workshop/prepared/localizations/`.

Edit source files and prepare again to make changes. Do not edit prepared files:
release verifies their hashes and never builds or translates. Preparation can be
rerun without uploading. Both commands use a checkout-local lock.

The first publication creates a **private** item and saves its ID automatically
in `workshop/mod_id.txt`. Commit and preserve that ID for subsequent releases.
After the first full release succeeds, set `"visibility": "public"`, prepare again,
inspect the dry run, and release to make it public. Reusing the initial snapshot
keeps it private.

## Retries and recovery

Keep `workshop/.release-state/` receipts. Retries reuse the saved item and skip
already verified content and listings when still current. Steam must be signed
in as the item's owner for updates.

- If Steam requires the Workshop agreement, accept it and retry; the item ID has
  already been saved.
- If item creation times out without returning an ID, further creation is blocked
  to prevent duplicates. Find the created item in your Workshop and save its ID in
  `workshop/mod_id.txt`. Remove `workshop/.release-state/creation.json` only if Steam
  confirms no item exists.
- Preparation recovers a legacy `workshop/first-upload/mod_id.txt` automatically.
  Conflicting saved IDs stop the workflow.

The publisher reads localized listings back and compares gallery filenames, order,
and complete downloaded image bytes with the prepared files. An identical gallery
is preserved across mod releases, including images repaired manually. Gallery
updates are submitted separately from mod content; changing a file's bytes triggers
an update even when its filename stays the same.

An existing gallery with matching filenames is checked before uploading mod content.
If an image is unavailable, the publisher retries verification every 15 seconds for
up to two minutes, then stops without reuploading it automatically. Retry release
to check again, or use `--previews-only` to explicitly reupload. Steam accepting an
upload does not guarantee that its image URL works. These checks do not establish
correct animation or public page rendering.

For targeted updates from the prepared release:

```sh
./release.sh --language japanese    # Listing text only.
./release.sh --previews-only         # Reupload the gallery.
```

Both modes support `--dry-run`.
