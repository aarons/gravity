using System.Net;
using WorkshopLocalization;

internal static class PreviewDownloadTests
{
    internal static void Run()
    {
        Check((call, _) => call < 3 ? Response(404) : Image(), succeeds: true, expectedCalls: 3, expectedWaits: 2);
        Check((_, _) => Response(404), succeeds: false, expectedCalls: 7, expectedWaits: 6, expectedRefreshes: 1);
        Check((_, request) => request.RequestUri!.AbsolutePath == "/fresh" ? Image() : Response(404),
            succeeds: true, expectedCalls: 7, expectedWaits: 6, expectedRefreshes: 1);
        Check((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>missing image</html>") },
            succeeds: false, expectedCalls: 7, expectedWaits: 6, expectedRefreshes: 1);
        Check((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("GIF") },
            succeeds: false, expectedCalls: 7, expectedWaits: 6, expectedRefreshes: 1);
        Check((_, _) => Response(429), succeeds: false, expectedCalls: 1, expectedWaits: 0);
        Check((_, _) =>
        {
            var response = Response(503);
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(5));
            return response;
        }, succeeds: false, expectedCalls: 1, expectedWaits: 0);
        Check((_, _) => Image(), succeeds: false, expectedCalls: 0, expectedWaits: 6, expectedRefreshes: 1, missingUrl: true);

        // Successful URLs and video previews are not polled along with the failing image.
        var clock = TimeSpan.Zero;
        var counts = new Dictionary<string, int>();
        using var handler = new Handler((_, request) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            counts[path] = counts.GetValueOrDefault(path) + 1;
            return path == "/good" || counts[path] == 3 ? Image() : Response(404);
        });
        using var http = new HttpClient(handler);
        Preview[] gallery = [new("good.gif", 0, "https://example.test/good"), new("late.gif", 0, "https://example.test/late"), new("video", 1)];
        PreviewDownloads.Verify(gallery, () => gallery, http, _ => { }, delay => clock += delay, () => clock);
        Require(counts["/good"] == 1 && counts["/late"] == 3 && clock == TimeSpan.FromSeconds(30));

        // Request time counts against the budget, not just sleeps.
        clock = TimeSpan.Zero;
        using var slowHandler = new Handler((_, _) => { clock += TimeSpan.FromSeconds(15); throw new TaskCanceledException("timeout"); });
        using var slowHttp = new HttpClient(slowHandler);
        try
        {
            PreviewDownloads.Verify([gallery[0]], () => [gallery[0]], slowHttp, _ => { }, delay => clock += delay, () => clock);
            throw new Exception("Expected timeout failure");
        }
        catch (IOException) { }
        Require(clock <= TimeSpan.FromMinutes(2) && slowHandler.Calls == 4);
    }

    private static void Check(Func<int, HttpRequestMessage, HttpResponseMessage> respond, bool succeeds,
        int expectedCalls, int expectedWaits, int expectedRefreshes = 0, bool missingUrl = false)
    {
        using var handler = new Handler(respond);
        using var http = new HttpClient(handler);
        var clock = TimeSpan.Zero;
        var waits = 0;
        var refreshes = 0;
        Exception? failure = null;
        try
        {
            PreviewDownloads.Verify([new("test.gif", 0, missingUrl ? "" : "https://example.test/old")],
                () => { refreshes++; return [new("test.gif", 0, missingUrl ? "" : "https://example.test/fresh")]; },
                http, _ => { }, delay =>
                {
                    Require(delay == TimeSpan.FromSeconds(15));
                    waits++; clock += delay;
                }, () => clock);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException) { failure = error; }
        Require((failure == null) == succeeds);
        Require(handler.Calls == expectedCalls && waits == expectedWaits && refreshes == expectedRefreshes);
        if (failure is IOException)
            Require(failure.Message.Contains("test.gif") && failure.Message.Contains("--previews-only"));
    }

    private static HttpResponseMessage Response(int status) => new((HttpStatusCode)status);
    private static HttpResponseMessage Image() => new(HttpStatusCode.OK)
    { Content = new ByteArrayContent("GIF89a0123456789abcdef"u8.ToArray()) };
    private static void Require(bool condition)
    {
        if (!condition) throw new Exception("Preview download assertion failed");
    }
    private sealed class Handler(Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(++Calls, request));
    }
}
