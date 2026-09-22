using System.Text.Json;
using WorkshopLocalization;

var tests = new (string Name, Action Test)[]
{
    ("preview delivery retries are bounded, spaced, refreshed, and respect backoff", PreviewDownloadTests.Run),
    ("first release creates privately, saves ID and retries without changing snapshot", () => WithFixture((release, steam, publisher, state) =>
    {
        release = FirstRelease(release);
        var settings = release.Settings.GetRawText();
        steam.FailLanguage = "japanese";
        Assert(publisher.Run(release, null, itemDirectory: state) == 1);
        Assert(File.ReadAllText(Path.Combine(state, "mod_id.txt")).Trim() == "123456789");
        Assert(!File.ReadAllText(Path.Combine(state, "creation.json")).Contains("\"completed\": true"));
        Assert(steam.Created == 1 && steam.Visibility == "private");
        Assert(release.ItemId == 0 && release.Settings.GetRawText() == settings);
        steam.FailLanguage = null;
        Assert(publisher.Run(release, null, itemDirectory: state) == 0);
        Assert(steam.Created == 1 && steam.ContentUploads == 1);
        Assert(File.ReadAllText(Path.Combine(state, "creation.json")).Contains("\"completed\": true"));
        File.Delete(Path.Combine(state, "mod_id.txt"));
        Assert(publisher.Run(release, null, itemDirectory: state) == 0);
        Assert(File.Exists(Path.Combine(state, "mod_id.txt")) && steam.Created == 1);
    })),
    ("ambiguous creation cannot create duplicates and can recover a known ID", () => WithFixture((release, steam, publisher, state) =>
    {
        release = FirstRelease(release);
        steam.LoseCreateResponse = true;
        Throws<TimeoutException>(() => publisher.Run(release, null, itemDirectory: state));
        Throws<InvalidOperationException>(() => publisher.Run(release, null, itemDirectory: state));
        Assert(steam.Created == 1 && steam.ContentUploads == 0);
        File.WriteAllText(Path.Combine(state, "mod_id.txt"), "123456789\n");
        Assert(publisher.Run(release, null, itemDirectory: state) == 0);
        Assert(steam.Created == 1 && steam.Visibility == "private");
    })),
    ("legal agreement preserves created ID before stopping", () => WithFixture((release, steam, publisher, state) =>
    {
        release = FirstRelease(release);
        steam.NeedsLegalAgreement = true;
        Throws<InvalidOperationException>(() => publisher.Run(release, null, itemDirectory: state));
        Assert(File.Exists(Path.Combine(state, "mod_id.txt")) && steam.ContentUploads == 0);
        Assert(publisher.Run(release, null, itemDirectory: state) == 0);
        Assert(steam.Created == 1);
    })),
    ("first release rejects partial modes and public visibility before creation", () => WithFixture((release, steam, publisher, state) =>
    {
        Throws<InvalidDataException>(() => publisher.Run(release with { ItemId = 0 }, null, itemDirectory: state));
        release = FirstRelease(release);
        Throws<InvalidOperationException>(() => publisher.Run(release, "japanese", itemDirectory: state));
        Throws<InvalidOperationException>(() => publisher.Run(release, null, previewsOnly: true, itemDirectory: state));
        Assert(steam.Created == 0 && steam.Calls.Count == 0);
    })),
    ("identity mismatch and account switch cannot redirect first release", () => WithFixture((release, steam, publisher, state) =>
    {
        release = FirstRelease(release);
        Assert(publisher.Run(release, null, itemDirectory: state) == 0);
        steam.Calls.Clear();
        File.WriteAllText(Path.Combine(state, "mod_id.txt"), "999\n");
        Throws<InvalidDataException>(() => publisher.Run(release, null, itemDirectory: state));
        File.WriteAllText(Path.Combine(state, "mod_id.txt"), "123456789\n");
        steam.Account = 999;
        Throws<InvalidOperationException>(() => publisher.Run(release, null, itemDirectory: state));
        Assert(steam.Calls.Count == 0 && steam.Created == 1);
    })),
    ("preview plan corrects old order and preserves video slots", () =>
    {
        Preview[] previous = [new("01-a.jpg", 0), new("video", 1), new("02-a.png", 0), new("01-b.jpg", 0), new("02-b.png", 0)];
        var plan = PreviewGallery.Plan(previous, ["02-b.png", "01-b.jpg", "02-a.png", "01-a.jpg"]).ToArray();
        Assert(plan.SequenceEqual(new PreviewChange[] { new(0, "01-a.jpg"), new(2, "01-b.jpg"), new(3, "02-a.png"), new(4, "02-b.png") }));
        Assert(PreviewGallery.Plan(previous, ["01-a.jpg"]).SequenceEqual(new PreviewChange[]
            { new(0, "01-a.jpg"), new(4, null), new(3, null), new(2, null) }));
        Assert(PreviewGallery.Plan([], ["02-a.png", "01-a.jpg"]).SequenceEqual(new PreviewChange[]
            { new(null, "01-a.jpg"), new(null, "02-a.png") }));
    }),
    ("preview-only retry uploads all images without content or translations", () => WithFixture((release, steam, publisher, state) =>
    {
        steam.Previews = [new("02-settings-b.png", 0), new("video", 1), new("01-legend-a.jpg", 0)];
        Assert(publisher.Run(release, null, previewsOnly: true) == 0);
        Assert(steam.Calls.SequenceEqual(new[] { "previews" }));
        Assert(steam.ContentUploads == 0 && steam.Metadata == "");
        Assert(steam.Previews.Any(p => p.Type == 1));
        Assert(!File.Exists(Path.Combine(state, "123456789-123.json")));
        Assert(publisher.Run(release, null, previewsOnly: true) == 0);
        Assert(steam.Calls.SequenceEqual(new[] { "previews", "previews" }));
        Throws<ArgumentException>(() => publisher.Run(release, "english", previewsOnly: true));
    })),
    ("missing previews repair despite unchanged content fingerprint", () => WithFixture((release, steam, publisher, state) =>
    {
        Assert(publisher.Run(release, null) == 0);
        steam.Previews = [];
        steam.Calls.Clear();
        Assert(publisher.Run(release, null) == 0);
        Assert(steam.Calls.SequenceEqual(new[] { "previews" }));
        Assert(steam.ContentUploads == 1);
    })),
    ("preview readback rejects mismatches and delivery failures", () => WithFixture((release, steam, publisher, state) =>
    {
        steam.IgnorePreviews = true;
        Throws<InvalidOperationException>(() => publisher.Run(release, null, previewsOnly: true));
        steam.IgnorePreviews = false;
        Assert(publisher.Run(release, null, previewsOnly: true) == 0);
        steam.FailPreviewDownloads = true;
        Throws<IOException>(() => publisher.Run(release, null, previewsOnly: true));
        steam.FailPreviewDownloads = false;
        Assert(publisher.Run(release, null) == 0);
        Assert(steam.Text.ContainsKey("japanese") && steam.Text.ContainsKey("french"));
    })),
    ("unchanged release checks delivery without automatic reuploads", () => WithFixture((release, steam, publisher, state) =>
    {
        Assert(publisher.Run(release, null) == 0);
        steam.Calls.Clear();
        steam.FailPreviewDownloads = true;
        var checks = steam.PreviewChecks;
        Throws<IOException>(() => publisher.Run(release, null));
        Assert(steam.PreviewChecks == checks + 1 && steam.Calls.Count == 0);
        Assert(publisher.Run(release, "japanese") == 0);
        Assert(steam.PreviewChecks == checks + 1);
    })),
    ("unchanged release skips all submissions", () => WithFixture((release, steam, publisher, state) =>
    {
        Assert(publisher.Run(release, null) == 0);
        var first = steam.Calls.ToArray();
        Assert(steam.ContentUploads == 1);
        Assert(first.Count(c => c.StartsWith("listing:")) == 2);
        Assert(publisher.Run(release, null) == 0);
        Assert(steam.Calls.SequenceEqual(first));
        Assert(Directory.GetFiles(Path.Combine(state, "backups"), "*.json", SearchOption.AllDirectories).Length == 4);
    })),
    ("partial listing failure continues and targeted retry excludes content", () => WithFixture((release, steam, publisher, state) =>
    {
        steam.FailLanguage = "japanese";
        Assert(publisher.Run(release, null) == 1);
        Assert(steam.Text.ContainsKey("french"));
        var receipt = File.ReadAllText(Path.Combine(state, "123456789-123.json"));
        Assert(!receipt.Contains("japanese"));
        steam.FailLanguage = null;
        steam.Calls.Clear();
        Assert(publisher.Run(release, "japanese") == 0);
        Assert(steam.Calls.SequenceEqual(new[] { "listing:japanese" }));
        Assert(steam.ContentUploads == 1);
        steam.Calls.Clear();
        Assert(publisher.Run(release, null) == 0);
        Assert(steam.Calls.Count == 0);
    })),
    ("lost content response reconciles the committed Steam fingerprint", () => WithFixture((release, steam, publisher, state) =>
    {
        steam.LoseContentResponse = true;
        Throws<TimeoutException>(() => publisher.Run(release, null));
        Assert(steam.ContentUploads == 1);
        steam.LoseContentResponse = false;
        Assert(publisher.Run(release, null) == 0);
        Assert(steam.ContentUploads == 1);
    })),
    ("failed dependency reconciliation retries without repeating content", () => WithFixture((release, steam, publisher, state) =>
    {
        steam.FailDependencies = true;
        Throws<IOException>(() => publisher.Run(release, null));
        steam.FailDependencies = false;
        Assert(publisher.Run(release, null) == 0);
        Assert(steam.ContentUploads == 1);
        Assert(steam.Dependencies.SequenceEqual(new ulong[] { 42 }));
    })),
    ("readback mismatch never records success", () => WithFixture((release, steam, publisher, state) =>
    {
        steam.IgnoreLanguage = "japanese";
        Assert(publisher.Run(release, "japanese") == 1);
        Assert(!File.Exists(Path.Combine(state, "123456789-123.json")));
        steam.IgnoreLanguage = null;
        Assert(publisher.Run(release, "japanese") == 0);
        Assert(steam.Calls.Count(c => c == "listing:japanese") == 2);
        Assert(steam.ContentUploads == 0);
    })),
    ("remote edits are reconciled despite a local receipt", () => WithFixture((release, steam, publisher, state) =>
    {
        Assert(publisher.Run(release, null) == 0);
        steam.Text["japanese"] = ("remote edit", "remote edit");
        steam.Calls.Clear();
        Assert(publisher.Run(release, "japanese") == 0);
        Assert(steam.Calls.SequenceEqual(new[] { "listing:japanese" }));
    })),
    ("fallback identical to English is explicitly published once", () => WithFixture((release, steam, publisher, state) =>
    {
        var english = release.Listings[0];
        release.Listings[1] = release.Listings[1] with { Title = english.Title, Description = english.Description };
        steam.Text["english"] = (english.Title, english.Description);
        Assert(publisher.Run(release, "japanese") == 0);
        Assert(steam.Calls.SequenceEqual(new[] { "listing:japanese" }));
        Assert(publisher.Run(release, "japanese") == 0);
        Assert(steam.Calls.Count == 1);
    })),
    ("wrong owner or app is rejected before mutations", () => WithFixture((release, steam, publisher, state) =>
    {
        steam.Owner = 999;
        Throws<InvalidOperationException>(() => publisher.Run(release, null));
        Assert(steam.Calls.Count == 0);
        steam.Owner = 123;
        steam.AppId = 480;
        Throws<InvalidOperationException>(() => publisher.Run(release, "japanese"));
        Assert(steam.Calls.Count == 0);
    })),
    ("unexpected English overwrite is detected", () => WithFixture((release, steam, publisher, state) =>
    {
        steam.OverwriteEnglish = true;
        Throws<InvalidOperationException>(() => publisher.Run(release, "japanese"));
    })),
    ("invalid selected language is rejected before mutations", () => WithFixture((release, steam, publisher, state) =>
    {
        Throws<ArgumentException>(() => publisher.Run(release, "unknown"));
        Assert(steam.Calls.Count == 0);
    })),
};
foreach (var (name, test) in tests)
{
    test();
    Console.WriteLine("PASS " + name);
}
Console.WriteLine($"{tests.Length} publisher regression checks passed without Steam.");

static void Assert(bool condition)
{
    if (!condition) throw new Exception("Assertion failed");
}

static PreparedRelease FirstRelease(PreparedRelease release) => release with
{
    ItemId = 0,
    Settings = JsonSerializer.SerializeToElement(new { visibility = "private", dependencies = new[] { 42UL } })
};

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

static void WithFixture(Action<PreparedRelease, FakeSteam, Publisher, string> test)
{
    var state = Path.Combine(Path.GetTempPath(), "sts2-mod-publish-test-" + Guid.NewGuid().ToString("N"));
    var workspace = Path.Combine(state, "workspace");
    Directory.CreateDirectory(Path.Combine(workspace, "previews"));
    foreach (var name in new[] { "02-settings-b.png", "01-legend-b.jpg", "01-legend-a.jpg", "02-settings-a.png" })
        File.WriteAllText(Path.Combine(workspace, "previews", name), "fake image");
    var release = new PreparedRelease
    {
        Workspace = workspace, ItemId = 123456789, ContentFingerprint = new string('a', 64),
        Settings = JsonSerializer.SerializeToElement(new { visibility = "public", dependencies = new[] { 42UL } }),
        Listings = [new("english", "eng", "My Mod", "English description"),
            new("japanese", "jpn", "マップガイド", "Japanese description"),
            new("french", "fra", "Guide", "French description")]
    };
    var steam = new FakeSteam();
    var publisher = new Publisher(steam, state, _ => { });
    try { test(release, steam, publisher, state); }
    finally { if (Directory.Exists(state)) Directory.Delete(state, recursive: true); }
}

sealed class FakeSteam : IWorkshopClient
{
    public ulong Account = 123;
    public ulong UserId => Account;
    public ulong Owner = 123;
    public uint AppId = 2868840;
    public Dictionary<string, (string Title, string Description)> Text = new()
    { ["english"] = ("old bilingual title", "old bilingual description") };
    public List<string> Calls = [];
    public string Metadata = "";
    public ulong[] Dependencies = [];
    public int ContentUploads;
    public Preview[] Previews = [];
    public bool IgnorePreviews;
    public string? FailLanguage, IgnoreLanguage;
    public bool LoseContentResponse, FailDependencies, OverwriteEnglish;
    public bool LoseCreateResponse, NeedsLegalAgreement;
    public int Created;
    public string? Visibility;
    public CreatedItem CreateItem()
    {
        Created++;
        if (LoseCreateResponse) throw new TimeoutException("Simulated lost creation response");
        return new CreatedItem(123456789, NeedsLegalAgreement);
    }
    public RemoteItem Read(ulong itemId, string language)
    {
        var text = Text.GetValueOrDefault(language, Text["english"]);
        return new(itemId, AppId, Owner, text.Title, text.Description, Metadata, Dependencies, [], Previews);
    }
    public void UploadContent(PreparedRelease release, RemoteItem previous)
    {
        Calls.Add("content"); ContentUploads++; Metadata = release.Marker;
        Visibility = release.Settings.GetProperty("visibility").GetString();
        var english = release.Listings.Single(listing => listing.Language == "english");
        Text["english"] = (english.Title, english.Description);
        SetPreviews(release);
        if (LoseContentResponse) throw new TimeoutException("Simulated committed upload with lost response");
    }
    private void SetPreviews(PreparedRelease release)
    {
        if (IgnorePreviews) return;
        Previews = Previews.Where(p => p.Type != 0).Concat(PreviewGallery.Files(release)
            .Select(p => new Preview(Path.GetFileName(p), 0))).ToArray();
    }
    public void UploadPreviews(PreparedRelease release, RemoteItem previous)
    {
        Calls.Add("previews"); SetPreviews(release);
    }
    public bool FailPreviewDownloads;
    public int PreviewChecks;
    public void VerifyPreviewDownloads(Preview[] previews, Func<Preview[]> refresh)
    {
        PreviewChecks++;
        if (FailPreviewDownloads) throw new IOException("Simulated preview 404");
    }
    public void ReconcileDependencies(ulong itemId, ulong[] previous, ulong[] desired)
    {
        Calls.Add("dependencies");
        if (FailDependencies) throw new IOException("Simulated dependency failure");
        Dependencies = desired;
    }
    public void UploadListing(ulong itemId, Listing listing)
    {
        Calls.Add("listing:" + listing.Language);
        if (FailLanguage == listing.Language) throw new IOException("Simulated submission failure");
        if (IgnoreLanguage != listing.Language) Text[listing.Language] = (listing.Title, listing.Description);
        if (OverwriteEnglish) Text["english"] = (listing.Title, listing.Description);
    }
    public void Dispose() { }
}
