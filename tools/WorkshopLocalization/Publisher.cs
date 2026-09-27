using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WorkshopLocalization;

internal sealed record Preview(string Name, int Type, string Url = "");
internal sealed record RemoteItem(ulong ItemId, uint AppId, ulong Owner, string Title, string Description,
    string Metadata, ulong[] Dependencies, uint[] Descriptors, Preview[] Previews);
internal sealed record CreatedItem(ulong ItemId, bool NeedsLegalAgreement);

internal interface IWorkshopClient : IDisposable
{
    ulong UserId { get; }
    CreatedItem CreateItem();
    RemoteItem Read(ulong itemId, string language);
    void UploadContent(PreparedRelease release, RemoteItem previous);
    void UploadPreviews(PreparedRelease release, RemoteItem previous);
    string[] VerifyPreviewDownloads(Preview[] previews, Func<Preview[]> refresh);
    void ReconcileDependencies(ulong itemId, ulong[] previous, ulong[] desired);
    void UploadListing(ulong itemId, Listing listing);
}

internal sealed record PreparedRelease
{
    public required string Workspace { get; init; }
    public required ulong ItemId { get; init; }
    public required string ContentFingerprint { get; init; }
    public required JsonElement Settings { get; init; }
    public required List<Listing> Listings { get; init; }
    public string Marker => "sts2-mod-release:v1:" + ContentFingerprint;

    public static PreparedRelease Read(string workspace)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(workspace, "release-manifest.json")));
        var root = manifest.RootElement;
        if (root.GetProperty("version").GetInt32() != 2 || root.GetProperty("appId").GetUInt32() != 2868840)
            throw new InvalidDataException("Unsupported prepared release. Run ./prepare.sh again.");
        var item = ListingFiles.ReadItemId(workspace, required: false);
        if (root.GetProperty("publishedFileId").GetString() != item?.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidDataException("Prepared item ID changed.");
        var expected = root.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        var actual = Directory.GetFiles(workspace, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(workspace, path).Replace('\\', '/'))
            .Where(path => path != "release-manifest.json").ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected.Keys))
            throw new InvalidDataException("Prepared file inventory changed. Run ./prepare.sh again.");
        foreach (var path in actual)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(workspace, path))));
            if (hash != expected[path])
                throw new InvalidDataException($"Prepared file changed: {path}. Run ./prepare.sh again.");
        }
        var catalog = ListingFiles.ReadLanguages();
        var listings = ListingFiles.ReadListings(workspace, catalog);
        if (listings.Count != catalog.Count)
            throw new InvalidDataException("The prepared release must contain all supported languages.");
        using var settings = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(workspace, "workshop.json")));
        var english = listings.Single(listing => listing.Language == "english");
        if (settings.RootElement.GetProperty("title").GetString() != english.Title
            || settings.RootElement.GetProperty("description").GetString() != english.Description)
            throw new InvalidDataException("The prepared manifest must use the prepared English listing.");
        var fingerprint = root.GetProperty("contentFingerprint").GetString()!;
        if (fingerprint.Length != 64 || fingerprint.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw new InvalidDataException("Invalid prepared content fingerprint.");
        if (item == null && settings.RootElement.GetProperty("visibility").GetString() != "private")
            throw new InvalidDataException("The first release must be prepared with private visibility.");
        return new PreparedRelease { Workspace = workspace, ItemId = item ?? 0, ContentFingerprint = fingerprint,
            Settings = settings.RootElement.Clone(), Listings = listings };
    }
}

internal sealed class PublishState
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Languages { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class Publisher(IWorkshopClient client, string stateDirectory, Action<string> log)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static string Fingerprint(Listing listing) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(listing.Language + "\0" + listing.Title + "\0" + listing.Description)));

    internal static bool Matches(RemoteItem remote, Listing listing) => remote.Title == listing.Title && remote.Description == listing.Description;

    private RemoteItem Read(ulong item, string language)
    {
        var remote = client.Read(item, language);
        if (remote.ItemId != item || remote.AppId != 2868840 || remote.Owner != client.UserId)
            throw new InvalidOperationException("Workshop item, game, or publishing account does not match. Nothing may be published to this target.");
        return remote;
    }

    private bool PreviewsMatch(PreparedRelease release, RemoteItem remote)
    {
        if (!PreviewGallery.Matches(release, remote))
            return false;
        var hashes = client.VerifyPreviewDownloads(remote.Previews, () =>
        {
            var refreshed = Read(release.ItemId, "english");
            if (!PreviewGallery.Matches(release, refreshed))
                throw new InvalidOperationException("Steam gallery changed during verification. Retry ./release.sh --previews-only.");
            return refreshed.Previews;
        });
        return hashes.SequenceEqual(PreviewGallery.Files(release)
            .Select(path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))));
    }

    private void VerifyPreviews(PreparedRelease release, RemoteItem remote)
    {
        if (!PreviewsMatch(release, remote))
            throw new InvalidOperationException("Steam preview filenames, order, or image bytes did not match the prepared gallery. Retry ./release.sh --previews-only.");
        log($"Verified {PreviewGallery.Files(release).Length} shared previews: filenames, order, and complete image bytes.");
    }

    public int Run(PreparedRelease release, string? language, bool previewsOnly = false, string? itemDirectory = null)
    {
        if (previewsOnly && language != null)
            throw new ArgumentException("--previews-only cannot be combined with --language: the gallery is shared.");
        if (language != null && !release.Listings.Any(l => l.Language == language))
            throw new ArgumentException($"No prepared translation for {language}.");
        Directory.CreateDirectory(stateDirectory);
        if (itemDirectory != null)
        {
            // Serialize creation and publication, including direct publisher invocations.
            using var identityLock = new FileStream(Path.Combine(stateDirectory, "creation.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var identity = new WorkshopIdentity(itemDirectory, stateDirectory);
            release = identity.Resolve(release, client, language == null && !previewsOnly, log);
            var result = Run(release, language, previewsOnly);
            if (result == 0 && language == null && !previewsOnly) identity.Complete();
            return result;
        }
        if (release.ItemId == 0)
            throw new InvalidOperationException("Creating an item requires --item-directory outside the prepared workspace.");
        var statePath = Path.Combine(stateDirectory, $"{release.ItemId}-{client.UserId}.json");
        // Also lock direct invocations, independently of the shell workflow lock.
        using var stateLock = new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var state = File.Exists(statePath)
            ? JsonSerializer.Deserialize<PublishState>(File.ReadAllText(statePath), JsonOptions)
                ?? throw new InvalidDataException("Invalid release receipt.")
            : new PublishState();
        if (state.Version != 1 || state.Languages == null)
            throw new InvalidDataException("Unsupported release receipt.");
        var englishBefore = Read(release.ItemId, "english");
        var englishExpected = new Listing("english", "eng", englishBefore.Title, englishBefore.Description);

        if (previewsOnly)
        {
            Backup(release, "previews", englishBefore);
            client.UploadPreviews(release, englishBefore);
            var after = Read(release.ItemId, "english");
            VerifyPreviews(release, after);
            if (!Matches(after, englishExpected) || after.Metadata != englishBefore.Metadata)
                throw new InvalidOperationException("Listing or content metadata changed during preview-only publication.");
            return 0;
        }

        if (language == null)
        {
            // Verify before changing shared content. A healthy, byte-identical gallery
            // (including a manual repair) must survive ordinary mod releases untouched.
            var previewsMatch = PreviewsMatch(release, englishBefore);
            if (englishBefore.Metadata == release.Marker)
            {
                log("Skip shared content: Steam already has this prepared content fingerprint.");
            }
            else
            {
                Backup(release, "content", englishBefore);
                client.UploadContent(release, englishBefore);
                if (Read(release.ItemId, "english").Metadata != release.Marker)
                    throw new InvalidOperationException("Content upload could not be verified. Rerun release to reconcile with Steam.");
                log("Verified shared content upload.");
            }
            if (previewsMatch)
                log("Skip shared previews: Steam already serves the prepared image bytes in order.");
            else
            {
                var current = Read(release.ItemId, "english");
                Backup(release, "previews", current);
                client.UploadPreviews(release, current);
                VerifyPreviews(release, Read(release.ItemId, "english"));
            }
            if (release.Settings.TryGetProperty("dependencies", out var dependencies))
            {
                var current = Read(release.ItemId, "english");
                var desired = dependencies.EnumerateArray().Select(d => d.GetUInt64()).Distinct().ToArray();
                if (!current.Dependencies.ToHashSet().SetEquals(desired))
                {
                    Backup(release, "dependencies", current);
                    client.ReconcileDependencies(release.ItemId, current.Dependencies, desired);
                    if (!Read(release.ItemId, "english").Dependencies.ToHashSet().SetEquals(desired))
                        throw new InvalidOperationException("Dependency changes could not be verified. Rerun release.");
                }
            }
        }

        var failures = new List<string>();
        foreach (var listing in release.Listings.Where(l => language == null || l.Language == language)
                     .OrderBy(l => l.Language != "english").ThenBy(l => l.Language, StringComparer.Ordinal))
        {
            try
            {
                var current = Read(release.ItemId, listing.Language);
                var fingerprint = Fingerprint(listing);
                // Steam can return English fallback for an absent translation. Require a receipt
                // before skipping identical non-English text so every language is explicitly set.
                if (Matches(current, listing) && (listing.Language == "english"
                    || state.Languages.GetValueOrDefault(listing.Language) == fingerprint))
                    log($"Skip {listing.Language}: verified current on Steam.");
                else
                {
                    Backup(release, listing.Language, current);
                    client.UploadListing(release.ItemId, listing);
                    if (!Matches(Read(release.ItemId, listing.Language), listing))
                        throw new InvalidOperationException("Uploaded text did not match readback.");
                    log($"Verified {listing.Language}.");
                }
                if (listing.Language == "english") englishExpected = listing;
                state.Languages[listing.Language] = fingerprint;
                AtomicWrite(statePath, state);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
            {
                failures.Add(listing.Language);
                log($"FAILED {listing.Language}: {error.Message}");
            }
        }
        // Detect accidentally updating English while submitting a different language.
        if (!Matches(Read(release.ItemId, "english"), englishExpected))
            throw new InvalidOperationException("English changed unexpectedly during localized publishing; inspect the saved backups before retrying.");
        if (failures.Count > 0)
        {
            log($"Incomplete languages: {string.Join(", ", failures)}. Rerun ./release.sh or ./release.sh --language CODE.");
            return 1;
        }
        log("Release verified. No rebuilding or translation generation was performed.");
        return 0;
    }

    private void Backup(PreparedRelease release, string operation, RemoteItem previous)
    {
        var directory = Path.Combine(stateDirectory, "backups", release.ItemId.ToString());
        Directory.CreateDirectory(directory);
        AtomicWrite(Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{operation}-{Guid.NewGuid():N}.json"),
            new { operation, previous, release.ContentFingerprint });
    }

    internal static void AtomicWrite(string path, object value)
    {
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions) + "\n"));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
