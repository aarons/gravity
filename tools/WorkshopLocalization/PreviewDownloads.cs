using System.Diagnostics;
using System.Security.Cryptography;

namespace WorkshopLocalization;

/// <summary>Checks delivery separately from Steam's gallery metadata. Never uploads.</summary>
internal static class PreviewDownloads
{
    internal static string[] Verify(Preview[] previews, Func<Preview[]> refresh, HttpClient http,
        Action<string> log, Action<TimeSpan>? wait = null, Func<TimeSpan>? elapsed = null)
    {
        var timer = Stopwatch.StartNew();
        elapsed ??= () => timer.Elapsed;
        wait ??= Thread.Sleep;
        var deadline = TimeSpan.FromMinutes(2);
        var interval = TimeSpan.FromSeconds(15);
        var verified = new Dictionary<Preview, string>();
        string failure = "Preview availability could not be confirmed";
        for (var attempt = 0; attempt < 7; attempt++)
        {
            if (attempt > 0)
            {
                if (deadline - elapsed() <= interval) break;
                log($"{failure}. Waiting 15 seconds before checking again.");
                wait(interval);
            }
            // Refresh once near the end, rather than querying Steam on every HTTP retry.
            var finalAttempt = attempt == 6 || deadline - elapsed() <= TimeSpan.FromSeconds(30);
            if (finalAttempt) previews = refresh();
            var pending = previews.Where(p => p.Type == PreviewGallery.ImageType && !verified.ContainsKey(p)).ToArray();
            foreach (var preview in pending)
            {
                var remaining = deadline - elapsed();
                if (remaining <= TimeSpan.Zero) break;
                try
                {
                    if (!Uri.TryCreate(preview.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                        throw new IOException("Steam returned no HTTPS image URL");
                    using var timeout = new CancellationTokenSource(remaining < interval ? remaining : interval);
                    using var response = http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                        .GetAwaiter().GetResult();
                    // Do not keep polling a server that explicitly asks us to back off.
                    if ((int)response.StatusCode == 429 || response.Headers.RetryAfter != null)
                        throw new InvalidOperationException($"Preview '{preview.Name}' ({preview.Url}): HTTP {(int)response.StatusCode}; server requested backoff. Retry verification later.");
                    response.EnsureSuccessStatusCode();
                    using var stream = response.Content.ReadAsStream(timeout.Token);
                    var header = new byte[16];
                    stream.ReadExactlyAsync(header, timeout.Token).AsTask().GetAwaiter().GetResult();
                    if (!(header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff)
                        && !header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                        && !header.AsSpan(0, 6).SequenceEqual("GIF87a"u8)
                        && !header.AsSpan(0, 6).SequenceEqual("GIF89a"u8))
                        throw new IOException("URL did not return a supported image");
                    // Compare complete image bytes, not filenames or CDN URL conventions.
                    // Bound downloads so an unexpected response cannot consume unlimited memory.
                    using var data = new MemoryStream();
                    data.Write(header);
                    var buffer = new byte[8192];
                    int count;
                    while ((count = stream.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult()) > 0)
                    {
                        if (data.Length + count >= 1_000_000)
                            throw new IOException("Image exceeds the prepared gallery size limit");
                        data.Write(buffer, 0, count);
                    }
                    verified.Add(preview, Convert.ToHexStringLower(SHA256.HashData(data.ToArray())));
                }
                catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
                {
                    failure = $"Preview '{preview.Name}' ({preview.Url}): {error.Message}";
                }
            }
            if (previews.Where(p => p.Type == PreviewGallery.ImageType).All(verified.ContainsKey))
                return previews.Where(p => p.Type == PreviewGallery.ImageType).Select(p => verified[p]).ToArray();
            if (finalAttempt) break;
        }
        throw new IOException($"Steam gallery image availability verification failed. {failure}. "
            + "No automatic reupload was attempted. Retry ./release.sh to check again, or ./release.sh --previews-only to reupload the gallery.");
    }
}
