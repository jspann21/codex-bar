using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexBar;

internal static class ResetAnnouncementChecks
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    internal static string Status(object? scheduled = null, object? watch = null) => JsonSerializer.Serialize(new
    {
        data = new { scheduled_reset = scheduled, active_watch = watch }, meta = new { api_version = "v1" }
    });
    internal static object Scheduled(DateTimeOffset? time, bool banked = false) => new
    {
        status = "scheduled", reset_type = banked ? "banked" : "regular", scheduled_for = time?.ToString("O")
    };
    private static object Watch(int? chance, DateTimeOffset expiry) => new
    {
        level = "strong", reset_chance_percent = chance, forecast_window = "next 24 hours",
        observed_at = Now.ToString("O"), expires_at = expiry.ToString("O")
    };

    internal static void Run(Action<string, bool> check)
    {
        var none = ResetAnnouncements.Parse(Status());
        check("Empty tracker state is distinct from unavailable", !none.IsActive(Now) && none.Label(Now) == "No reset announced");
        var scheduled = ResetAnnouncements.Parse(Status(Scheduled(Now.AddHours(8))));
        check("Scheduled announcements have a countdown", scheduled.IsActive(Now) && scheduled.Label(Now) == "Reset Announced in ~8h");
        check("Passed schedule awaits confirmation instead of claiming execution", scheduled.IsActive(Now.AddHours(9)) &&
            scheduled.Label(Now.AddHours(9)) == "Reset Announced · pending");
        check("Missing scheduled time is explicitly announced without a countdown",
            ResetAnnouncements.Parse(Status(Scheduled(null))).Label(Now) == "Reset Announced");
        check("Banked credit announcements retain their meaning", ResetAnnouncements.Parse(Status(Scheduled(null, true))).Details(Now).Contains("Banked reset credit"));
        var forecast = ResetAnnouncements.Parse(Status(watch: Watch(65, Now.AddHours(12))));
        check("Possible resets are clearly estimates", forecast.IsActive(Now) && forecast.Label(Now) == "Possible reset · 65%" &&
            forecast.Details(Now).Contains("not an official commitment"));
        check("Expired watches are not advertised", !forecast.IsActive(Now.AddHours(12)) && forecast.Label(Now.AddHours(12)) == "No reset announced");
        check("Watch without probability remains possible", ResetAnnouncements.Parse(Status(watch: Watch(null, Now.AddHours(1)))).Label(Now) == "Possible reset");
        check("Announcement takes precedence over a watch", ResetAnnouncements.Parse(Status(Scheduled(null), Watch(90, Now.AddHours(1)))).Label(Now) == "Reset Announced");
        foreach (var invalid in new[]
        {
            ("missing fields", "{\"data\":{},\"meta\":{\"api_version\":\"v1\"}}"),
            ("unknown version", Status().Replace("v1", "v2")),
            ("unknown status", Status(Scheduled(null)).Replace("scheduled\"", "executed\"")),
            ("unknown reset kind", Status(Scheduled(null)).Replace("regular", "mystery")),
            ("unknown watch level", Status(watch: Watch(65, Now.AddHours(1))).Replace("strong", "certain")),
            ("invalid probability", Status(watch: Watch(101, Now.AddHours(1)))),
            ("invalid expiry", Status(watch: Watch(65, Now))),
            ("timestamp without zone", Status(new { status = "scheduled", reset_type = "regular", scheduled_for = "2026-10-02T09:00:00" })),
            ("malformed JSON", "not JSON")
        })
        {
            var rejected = false;
            try { ResetAnnouncements.Parse(invalid.Item2); }
            catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException) { rejected = true; }
            check("Rejects tracker " + invalid.Item1, rejected);
        }
        Task.Run(() => RunClientChecks(check)).GetAwaiter().GetResult();
    }

    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> read) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return read(request, cancellationToken);
        }
    }

    internal static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task RunClientChecks(Action<string, bool> check)
    {
        var sawEtag = false;
        var sequence = 0;
        using var conditionalHandler = new Handler((request, _) =>
        {
            if (++sequence == 1)
            {
                var response = JsonResponse(Status(Scheduled(Now.AddHours(8))));
                response.Headers.ETag = new EntityTagHeaderValue("\"fixture\"");
                return Task.FromResult(response);
            }
            sawEtag = request.Headers.IfNoneMatch.Any(tag => tag.Tag == "\"fixture\"");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });
        using var conditional = new ResetAnnouncementClient(conditionalHandler, () => Now);
        var first = await conditional.ReadAsync();
        check("Conditional requests reuse validated cached tracker data", ReferenceEquals(first, await conditional.ReadAsync()) && sawEtag && conditionalHandler.Calls == 2);
        using var empty304 = new ResetAnnouncementClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified))));
        check("304 without cached data is a failure", await Fails(() => empty304.ReadAsync()));

        var clock = Now;
        var retryCount = 0;
        using var retryHandler = new Handler((_, _) =>
        {
            if (++retryCount == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
                return Task.FromResult(response);
            }
            return Task.FromResult(JsonResponse(Status()));
        });
        using var throttled = new ResetAnnouncementClient(retryHandler, () => clock);
        check("Rate-limit failure is observable", await Fails(() => throttled.ReadAsync()));
        check("Retry-After prevents immediate repeat requests", await Fails(() => throttled.ReadAsync()) && retryHandler.Calls == 1);
        clock = Now.AddMinutes(61);
        check("Tracker reads recover after Retry-After", !(await throttled.ReadAsync()).IsActive(clock) && retryHandler.Calls == 2);

        foreach (var responseFactory in new Func<HttpResponseMessage>[]
        {
            () => new(HttpStatusCode.ServiceUnavailable),
            () => new(HttpStatusCode.OK) { Content = new StringContent("<html>offline</html>", Encoding.UTF8, "text/html") },
            () => JsonResponse("broken JSON"),
            () => JsonResponse(new string('x', 128 * 1024 + 1)),
            () => new(HttpStatusCode.OK) { Content = new UnboundedContent() }
        })
        {
            using var failed = new ResetAnnouncementClient(new Handler((_, _) => Task.FromResult(responseFactory())));
            check("Unavailable, malformed or oversized tracker response fails clearly", await Fails(() => failed.ReadAsync()));
        }
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = new ResetAnnouncementClient(new Handler(async (_, token) =>
        {
            pending.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return JsonResponse(Status());
        }));
        var read = cancelled.ReadAsync(cancellation.Token);
        await pending.Task;
        cancellation.Cancel();
        check("In-flight tracker reads cancel on shutdown", await Fails(() => read));
    }

    private sealed class UnboundedContent : HttpContent
    {
        internal UnboundedContent() => Headers.ContentType = new MediaTypeHeaderValue("application/json");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(new byte[128 * 1024 + 1]).AsTask();
    }

    private static async Task<bool> Fails(Func<Task<ResetAnnouncements>> action)
    {
        try { await action(); return false; }
        catch (Exception ex) when (ex is AnnouncementReadException or HttpRequestException or JsonException or OperationCanceledException) { return true; }
    }
}
