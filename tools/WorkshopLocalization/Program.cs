using System.Text.Encodings.Web;
using System.Text.Json;
using WorkshopLocalization;

try
{
    if (args.Length == 0 || args is ["--help"] or ["-h"])
    {
        Console.WriteLine("""
            Usage: WorkshopLocalization <validate|dry-run|publish> [--workspace PATH] [--require-all] [--language CODE]
            validate: check local listing files; missing translations are reported, not substituted.
            dry-run: print the item ID and proposed localized text as JSON (requires mod_id.txt).
            --workspace defaults to ./workshop. --language selects one Steam API code for dry-run.
            --require-all fails unless all 14 supported Workshop translations exist.
            validate and dry-run inspect source listing files offline.
            publish: publish a frozen workspace created by package.sh (all languages by default).
            publish --language CODE: update only that language, without uploading shared content.
            publish --previews-only: reupload all shared gallery images in filename order.
            publish --dry-run: inspect the frozen release offline; no Steam connection or writes.
            publish requires --state-directory PATH outside the prepared workspace for receipts/backups.
            Prefer ./release.sh, which also checks source freshness and the English review copy.
            """);
        return args.Length == 0 ? 1 : 0;
    }

    var command = args[0];
    if (command is not ("validate" or "dry-run" or "publish"))
        throw new ArgumentException($"Unknown command: {command}. Use validate, dry-run, or publish.");
    var workspace = Path.GetFullPath("workshop");
    string? selectedLanguage = null;
    var requireAll = false;
    var offline = false;
    var previewsOnly = false;
    string? stateDirectory = null;
    var seen = new HashSet<string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i++)
    {
        var option = args[i];
        if (!seen.Add(option))
            throw new ArgumentException($"Repeated option: {option}.");
        switch (option)
        {
            case "--require-all": requireAll = true; break;
            case "--dry-run": offline = true; break;
            case "--previews-only": previewsOnly = true; break;
            case "--workspace":
            case "--state-directory":
            case "--language":
                if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--"))
                    throw new ArgumentException($"{option} requires a value.");
                if (option == "--workspace") workspace = Path.GetFullPath(args[i]);
                else if (option == "--state-directory") stateDirectory = Path.GetFullPath(args[i]);
                else selectedLanguage = args[i];
                break;
            default: throw new ArgumentException($"Unknown option: {option}.");
        }
    }
    if (selectedLanguage != null && command == "validate")
        throw new ArgumentException("--language is only supported with dry-run or publish.");
    if (command != "publish" && (offline || stateDirectory != null || previewsOnly))
        throw new ArgumentException("--dry-run, --previews-only and --state-directory require publish.");
    if (previewsOnly && selectedLanguage != null)
        throw new ArgumentException("--previews-only cannot be combined with --language: the gallery is shared.");

    if (command == "publish")
    {
        var release = PreparedRelease.Read(workspace);
        if (selectedLanguage != null && !release.Listings.Any(l => l.Language == selectedLanguage))
            throw new ArgumentException($"No prepared translation for {selectedLanguage}.");
        if (offline)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "offline-prepared-release", appId = 2868840,
                publishedFileId = release.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                sharedContent = selectedLanguage == null && !previewsOnly ? "compare Steam fingerprint, upload only if different" : "skip",
                previews = new { mode = previewsOnly ? "reupload" : selectedLanguage == null ? "reconcile and verify" : "skip",
                    files = PreviewGallery.Files(release).Select(Path.GetFileName) },
                release.ContentFingerprint,
                updates = release.Listings.Where(l => !previewsOnly && (selectedLanguage == null || l.Language == selectedLanguage)),
                note = "Live differences and ownership are checked only when publishing. No Steam connection was made."
            }, Publisher.JsonOptions));
            return 0;
        }
        if (stateDirectory == null || stateDirectory == workspace
            || stateDirectory.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("publish requires --state-directory outside the prepared workspace.");
        var native = Path.Combine(workspace, "publisher/libsteam_api.dylib");
        using var client = new SteamWorkshopClient(native);
        return new Publisher(client, stateDirectory, Console.WriteLine).Run(release, selectedLanguage, previewsOnly);
    }

    var catalog = ListingFiles.ReadLanguages();
    var listings = ListingFiles.ReadListings(workspace, catalog);
    var missing = catalog.Values.Except(listings.Select(listing => listing.Language)).Order(StringComparer.Ordinal).ToArray();
    if (requireAll && missing.Length > 0)
        throw new InvalidDataException($"Missing Workshop translations: {string.Join(", ", missing)}.");
    if (selectedLanguage != null && !listings.Any(listing => listing.Language == selectedLanguage))
        throw new ArgumentException($"No supported translation file for {selectedLanguage}.");
    var itemId = ListingFiles.ReadItemId(workspace, required: command == "dry-run");

    if (command == "validate")
        Console.WriteLine($"Validated {listings.Count}/{catalog.Count} Workshop translations.");
    else
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode = "offline-dry-run",
            appId = 2868840,
            // A string preserves the complete uint64 ID in JavaScript/JSON consumers.
            publishedFileId = itemId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            missingLanguages = missing,
            updates = listings.Where(listing => selectedLanguage == null || listing.Language == selectedLanguage)
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    if (missing.Length > 0)
        Console.Error.WriteLine($"Pending Workshop translations: {string.Join(", ", missing)}.");
    return 0;
}
catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException
    or JsonException or InvalidOperationException or TimeoutException or DllNotFoundException or EntryPointNotFoundException
    or BadImageFormatException or KeyNotFoundException)
{
    Console.Error.WriteLine($"Workshop localization failed: {error.Message}");
    return 1;
}
