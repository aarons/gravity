namespace WorkshopLocalization;

internal sealed record PreviewChange(int? Index, string? Path);

internal static class PreviewGallery
{
    internal const int ImageType = 0;

    internal static string[] Files(PreparedRelease release) =>
        Directory.GetFiles(Path.Combine(release.Workspace, "previews")).Order(StringComparer.Ordinal).ToArray();

    internal static bool Matches(PreparedRelease release, RemoteItem remote) =>
        remote.Previews.Where(p => p.Type == ImageType).Select(p => p.Name)
            .SequenceEqual(Files(release).Select(Path.GetFileName), StringComparer.Ordinal);

    internal static IEnumerable<PreviewChange> Plan(Preview[] previous, IEnumerable<string> files)
    {
        var desired = files.Order(StringComparer.Ordinal).ToArray();
        var slots = Enumerable.Range(0, previous.Length).Where(i => previous[i].Type == ImageType).ToArray();
        // Steam retains slot order. Matching by filename would retain an old, unsorted gallery.
        for (var i = 0; i < desired.Length; i++)
            yield return new PreviewChange(i < slots.Length ? slots[i] : null, desired[i]);
        foreach (var index in slots.Skip(desired.Length).Reverse())
            yield return new PreviewChange(index, null);
    }
}
