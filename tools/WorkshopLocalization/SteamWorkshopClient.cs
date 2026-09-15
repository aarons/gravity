using System.Diagnostics;
using System.Runtime.InteropServices;
using Steamworks;

namespace WorkshopLocalization;

/// <summary>Live Steam adapter. Constructed only by an explicit publish command without --dry-run.</summary>
internal sealed class SteamWorkshopClient : IWorkshopClient
{
    private static readonly AppId_t AppId = new(2868840);
    public ulong UserId { get; }

    public SteamWorkshopClient(string nativeLibrary)
    {
        if (!OperatingSystem.IsMacOS() || IntPtr.Size != 8 || !Packsize.Test())
            throw new InvalidOperationException("The prepared Steam publisher requires 64-bit macOS with compatible Steamworks struct packing.");
        NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly,
            (name, assembly, searchPath) => name.Contains("steam_api", StringComparison.Ordinal)
                ? NativeLibrary.Load(nativeLibrary) : IntPtr.Zero);
        if (SteamAPI.InitEx(out var initializationError) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            throw new InvalidOperationException($"Steam initialization failed: {initializationError}. Start Steam and sign in to the publishing account.");
        if (SteamUtils.GetAppID() != AppId || !SteamUser.BLoggedOn())
        {
            SteamAPI.Shutdown();
            throw new InvalidOperationException("Steam must be signed in with app ID 2868840.");
        }
        UserId = SteamUser.GetSteamID().m_SteamID;
    }

    public void Dispose() => SteamAPI.Shutdown();

    private static void Require(bool success, string operation)
    {
        if (!success) throw new InvalidOperationException($"Steam rejected {operation}.");
    }

    private static T Wait<T>(SteamAPICall_t call, string operation) where T : struct
    {
        if (call == SteamAPICall_t.Invalid)
            throw new InvalidOperationException($"Steam did not start {operation}.");
        var complete = false;
        var ioFailure = false;
        T value = default;
        using var result = CallResult<T>.Create((response, failed) =>
        {
            value = response;
            ioFailure = failed;
            complete = true;
        });
        result.Set(call);
        var timer = Stopwatch.StartNew();
        var nextProgress = TimeSpan.FromSeconds(10);
        while (!complete)
        {
            SteamAPI.RunCallbacks();
            if (timer.Elapsed > TimeSpan.FromMinutes(5))
                throw new TimeoutException($"Timed out during {operation}. Steam may still complete it; rerun release to reconcile before submitting again.");
            if (timer.Elapsed >= nextProgress)
            {
                Console.WriteLine($"Waiting for Steam: {operation} ({timer.Elapsed.TotalSeconds:F0}s)...");
                nextProgress += TimeSpan.FromSeconds(10);
            }
            if (!complete) Thread.Sleep(20);
        }
        if (ioFailure) throw new IOException($"Steam I/O failure during {operation}.");
        return value;
    }

    private static void Check(EResult result, string operation)
    {
        if (result != EResult.k_EResultOK)
            throw new InvalidOperationException($"Steam {operation} failed: {result}.");
    }

    public RemoteItem Read(ulong itemId, string language)
    {
        var handle = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(itemId)], 1);
        if (handle == UGCQueryHandle_t.Invalid)
            throw new InvalidOperationException("Steam did not create the listing query.");
        try
        {
            Require(SteamUGC.SetLanguage(handle, language), "query language");
            Require(SteamUGC.SetReturnLongDescription(handle, true), "full description query");
            Require(SteamUGC.SetReturnMetadata(handle, true), "metadata query");
            Require(SteamUGC.SetReturnChildren(handle, true), "dependency query");
            Require(SteamUGC.SetReturnAdditionalPreviews(handle, true), "preview query");
            Require(SteamUGC.SetAllowCachedResponse(handle, 0), "uncached query");
            var result = Wait<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(handle), $"read {language}");
            Check(result.m_eResult, $"read {language}");
            if (result.m_unNumResultsReturned != 1)
                throw new InvalidOperationException("Steam did not return exactly one Workshop item.");
            Require(SteamUGC.GetQueryUGCResult(handle, 0, out var details), "query result");
            Check(details.m_eResult, "item details");
            // Empty metadata is valid for an item published before this workflow existed.
            SteamUGC.GetQueryUGCMetadata(handle, 0, out var metadata, 5001);
            var children = new PublishedFileId_t[details.m_unNumChildren];
            if (children.Length > 0)
                Require(SteamUGC.GetQueryUGCChildren(handle, 0, children, (uint)children.Length), "dependency results");
            var descriptors = new EUGCContentDescriptorID[5];
            var descriptorCount = SteamUGC.GetQueryUGCContentDescriptors(handle, 0, descriptors, 5);
            var previews = new List<Preview>();
            var count = SteamUGC.GetQueryUGCNumAdditionalPreviews(handle, 0);
            for (uint i = 0; i < count; i++)
            {
                Require(SteamUGC.GetQueryUGCAdditionalPreview(handle, 0, i, out var url, 10000,
                    out var name, 10000, out var type), "preview results");
                previews.Add(new Preview(name, (int)type, url));
            }
            return new RemoteItem(details.m_nPublishedFileId.m_PublishedFileId, details.m_nConsumerAppID.m_AppId,
                details.m_ulSteamIDOwner, details.m_rgchTitle, details.m_rgchDescription, metadata ?? "",
                children.Select(id => id.m_PublishedFileId).ToArray(),
                descriptors.Take((int)descriptorCount).Select(d => (uint)d).ToArray(), previews.ToArray());
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(handle); }
    }

    private static UGCUpdateHandle_t Start(ulong itemId)
    {
        var handle = SteamUGC.StartItemUpdate(AppId, new PublishedFileId_t(itemId));
        if (handle == UGCUpdateHandle_t.Invalid)
            throw new InvalidOperationException("Steam did not create an item update.");
        return handle;
    }

    private static void Submit(UGCUpdateHandle_t handle, string? note, string operation)
    {
        var result = Wait<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(handle, note), operation);
        Check(result.m_eResult, operation);
        if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            throw new InvalidOperationException("Steam requires acceptance of the Workshop legal agreement before this release is available.");
    }

    public void UploadListing(ulong itemId, Listing listing)
    {
        var handle = Start(itemId);
        // Always explicit, including English. Never submit content, previews, or change notes here.
        Require(SteamUGC.SetItemUpdateLanguage(handle, listing.Language), "listing language");
        Require(SteamUGC.SetItemTitle(handle, listing.Title), "listing title");
        Require(SteamUGC.SetItemDescription(handle, listing.Description), "listing description");
        Submit(handle, null, $"publish {listing.Language}");
    }

    public void UploadContent(PreparedRelease release, RemoteItem previous)
    {
        var settings = release.Settings;
        var handle = Start(release.ItemId);
        var visibility = settings.GetProperty("visibility").GetString() switch
        {
            "public" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
            "private" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
            "unlisted" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
            "friends_only" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
            _ => throw new InvalidDataException("Unsupported prepared visibility.")
        };
        Require(SteamUGC.SetItemVisibility(handle, visibility), "visibility");
        if (settings.TryGetProperty("tags", out var tags))
            Require(SteamUGC.SetItemTags(handle, tags.EnumerateArray().Select(t => t.GetString()!).ToList()), "tags");
        if (settings.TryGetProperty("contentDescriptors", out var descriptors))
        {
            var desired = descriptors.EnumerateArray().Select(d => d.GetString() switch
            {
                "nudity" => EUGCContentDescriptorID.k_EUGCContentDescriptor_NudityOrSexualContent,
                "frequent_violence" => EUGCContentDescriptorID.k_EUGCContentDescriptor_FrequentViolenceOrGore,
                "adult_only" => EUGCContentDescriptorID.k_EUGCContentDescriptor_AdultOnlySexualContent,
                "gratuitous_nudity" => EUGCContentDescriptorID.k_EUGCContentDescriptor_GratuitousSexualContent,
                "general_mature" => EUGCContentDescriptorID.k_EUGCContentDescriptor_AnyMatureContent,
                _ => throw new InvalidDataException("Unsupported prepared content descriptor.")
            }).ToHashSet();
            foreach (var descriptor in desired.Where(d => !previous.Descriptors.Contains((uint)d)))
                Require(SteamUGC.AddContentDescriptor(handle, descriptor), "add content descriptor");
            foreach (var descriptor in previous.Descriptors.Select(d => (EUGCContentDescriptorID)d).Except(desired))
                Require(SteamUGC.RemoveContentDescriptor(handle, descriptor), "remove content descriptor");
        }
        Require(SteamUGC.SetRequiredGameVersions(handle,
            settings.TryGetProperty("minBranch", out var min) ? min.GetString()! : "",
            settings.TryGetProperty("maxBranch", out var max) ? max.GetString()! : ""), "required game versions");
        Require(SteamUGC.SetItemContent(handle, Path.Combine(release.Workspace, "content")), "mod content");
        Require(SteamUGC.SetItemPreview(handle, Path.Combine(release.Workspace, "image.png")), "thumbnail");
        SetPreviews(handle, release, previous);
        // This marker is committed with the content. A lost local receipt cannot cause a repeat upload.
        Require(SteamUGC.SetItemMetadata(handle, release.Marker), "release fingerprint");
        Submit(handle, settings.TryGetProperty("changeNote", out var note) ? note.GetString() : null, "upload shared content");
    }

    private static void SetPreviews(UGCUpdateHandle_t handle, PreparedRelease release, RemoteItem previous)
    {
        foreach (var change in PreviewGallery.Plan(previous.Previews, PreviewGallery.Files(release)))
        {
            if (change.Path == null)
                Require(SteamUGC.RemoveItemPreview(handle, (uint)change.Index!.Value), "remove preview");
            else if (change.Index is int index)
                Require(SteamUGC.UpdateItemPreviewFile(handle, (uint)index, change.Path), $"update preview {change.Path}");
            else
                Require(SteamUGC.AddItemPreviewFile(handle, change.Path, EItemPreviewType.k_EItemPreviewType_Image), $"add preview {change.Path}");
        }
    }

    public void UploadPreviews(PreparedRelease release, RemoteItem previous)
    {
        var handle = Start(release.ItemId);
        SetPreviews(handle, release, previous);
        Submit(handle, null, "upload shared previews");
    }

    public void VerifyPreviewDownloads(Preview[] previews)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        foreach (var preview in previews.Where(p => p.Type == PreviewGallery.ImageType))
        {
            try
            {
                if (!Uri.TryCreate(preview.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                    throw new IOException("Steam returned no HTTPS image URL");
                using var response = http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var stream = response.Content.ReadAsStream();
                var header = new byte[16];
                stream.ReadExactlyAsync(header, timeout.Token).AsTask().GetAwaiter().GetResult();
                if (!(header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff)
                    && !header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                    && !header.AsSpan(0, 6).SequenceEqual("GIF87a"u8)
                    && !header.AsSpan(0, 6).SequenceEqual("GIF89a"u8))
                    throw new IOException("Steam URL did not return a supported image");
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
            {
                throw new IOException($"Preview '{preview.Name}' could not be downloaded: {error.Message}. "
                    + "Steam may still be processing it; retry ./release.sh --previews-only if it remains missing.", error);
            }
        }
    }

    public void ReconcileDependencies(ulong itemId, ulong[] previous, ulong[] desired)
    {
        var item = new PublishedFileId_t(itemId);
        foreach (var id in desired.Except(previous))
            Check(Wait<AddUGCDependencyResult_t>(SteamUGC.AddDependency(item, new PublishedFileId_t(id)), "add dependency").m_eResult, "add dependency");
        foreach (var id in previous.Except(desired))
            Check(Wait<RemoveUGCDependencyResult_t>(SteamUGC.RemoveDependency(item, new PublishedFileId_t(id)), "remove dependency").m_eResult, "remove dependency");
    }
}
