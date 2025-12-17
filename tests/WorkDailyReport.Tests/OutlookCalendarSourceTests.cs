using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkDailyReport.Calendar;
using WorkDailyReport.Config;
using Xunit;

namespace WorkDailyReport.Tests;

public class OutlookCalendarSourceTests
{
    [Fact]
    public async Task ParseIcs_ReturnsEventsWithinRange()
    {
        var icsPath = Path.Combine(AppContext.BaseDirectory, "TestData", "outlook-test.ics");
        Assert.True(File.Exists(icsPath), $"ICS di test non trovata: {icsPath}");

        var options = Options.Create(new WorkReportOptions
        {
            Calendar = new CalendarOptions
            {
                Outlook = new OutlookCalendarOptions
                {
                    Enabled = true,
                    IcsFile = icsPath,
                    TimeZone = "Europe/Rome"
                }
            }
        });

        var source = new OutlookCalendarSource(
            options,
            NullLogger<OutlookCalendarSource>.Instance,
            new TestHttpClientFactory(new ThrowingHandler()));
        var since = new DateTimeOffset(2025, 12, 16, 0, 0, 0, TimeSpan.FromHours(1));
        var until = since.AddDays(1);

        var events = await source.GetEventsAsync(since, until, CancellationToken.None);

        Assert.Single(events);
        Assert.Equal("Test Meeting", events[0].Title);
        Assert.Equal("Room 1", events[0].Location);
    }

    [Fact]
    public async Task DownloadIcs_ReturnsEventsWithinRange()
    {
        const string ics = """
BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
DTSTART:20251220T070000Z
DTEND:20251220T080000Z
SUMMARY:Remote Test
END:VEVENT
END:VCALENDAR
""";

        var options = Options.Create(new WorkReportOptions
        {
            Calendar = new CalendarOptions
            {
                Outlook = new OutlookCalendarOptions
                {
                    Enabled = true,
                    IcsFile = "https://example.com/calendar.ics",
                    TimeZone = "Europe/Rome"
                }
            }
        });

        var handler = new FixedResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ics)
        });

        var source = new OutlookCalendarSource(
            options,
            NullLogger<OutlookCalendarSource>.Instance,
            new TestHttpClientFactory(handler));

        var since = new DateTimeOffset(2025, 12, 20, 0, 0, 0, TimeSpan.FromHours(1));
        var until = since.AddDays(1);

        var events = await source.GetEventsAsync(since, until, CancellationToken.None);

        Assert.Single(events);
        Assert.Equal("Remote Test", events[0].Title);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler, disposeHandler: true);
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP call non atteso per test ICS locale");
    }

    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FixedResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
