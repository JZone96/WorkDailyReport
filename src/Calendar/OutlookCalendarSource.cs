using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkDailyReport.Config;

namespace WorkDailyReport.Calendar;

public interface ICalendarEventSource
{
    Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(DateTimeOffset since, DateTimeOffset untilExclusive, CancellationToken ct);
}

public sealed class OutlookCalendarSource : ICalendarEventSource
{
    private readonly OutlookCalendarOptions _options;
    private readonly ILogger<OutlookCalendarSource> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _icsFilePath;
    private readonly Uri? _icsUri;
    private readonly TimeZoneInfo _defaultTz;

    public OutlookCalendarSource(
        IOptions<WorkReportOptions> opt,
        ILogger<OutlookCalendarSource> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _options = opt.Value.Calendar?.Outlook ?? new OutlookCalendarOptions();
        _httpClientFactory = httpClientFactory;

        var configured = _options.IcsFile?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) &&
            Uri.TryCreate(configured, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            _icsUri = uri;
        }
        else
        {
            _icsFilePath = string.IsNullOrWhiteSpace(configured)
                ? string.Empty
                : (Path.IsPathRooted(configured)
                    ? configured
                    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured)));
        }

        _defaultTz = ResolveTimeZone(_options.TimeZone) ?? TimeZoneInfo.Local;
    }

    public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(DateTimeOffset since, DateTimeOffset untilExclusive, CancellationToken ct)
    {
        if (!_options.Enabled)
            return Array.Empty<CalendarEventDto>();

        string? content;
        if (_icsUri is not null)
        {
            content = await DownloadIcsAsync(ct);
        }
        else if (!string.IsNullOrWhiteSpace(_icsFilePath) && File.Exists(_icsFilePath))
        {
            await using var stream = File.OpenRead(_icsFilePath);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            content = await reader.ReadToEndAsync();
        }
        else
        {
            _logger.LogWarning("File ICS Outlook non trovato: {Path}", _icsFilePath);
            return Array.Empty<CalendarEventDto>();
        }

        if (string.IsNullOrEmpty(content))
            return Array.Empty<CalendarEventDto>();

        var parsedEvents = ParseEvents(content).ToList();

        if (parsedEvents.Count == 0)
        {
            var source = _icsUri?.ToString() ?? _icsFilePath ?? "<non specificato>";
            _logger.LogWarning("Nessun evento trovato nel file Outlook ICS {Path}", source);
        }

        return parsedEvents
            .Where(e => e.End > since && e.Start < untilExclusive)
            .OrderBy(e => e.Start)
            .ToList();
    }

    private IEnumerable<CalendarEventDto> ParseEvents(string ics)
    {
        var normalizedLines = NormalizeLines(ics);
        var current = new CalendarEventBuilder();

        foreach (var line in normalizedLines)
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new CalendarEventBuilder();
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current.IsValid)
                    yield return current.ToDto();

                current = new CalendarEventBuilder();
                continue;
            }

            current.ProcessLine(line, _defaultTz);
        }
    }

    private static IEnumerable<string> NormalizeLines(string ics)
    {
        using var reader = new StringReader(ics);
        string? current = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith(" ") || line.StartsWith("\t"))
            {
                current += line.TrimStart();
                continue;
            }

            if (current is not null)
                yield return current;

            current = line;
        }

        if (current is not null)
            yield return current;
    }

    private static TimeZoneInfo? ResolveTimeZone(string? tzId)
    {
        if (string.IsNullOrWhiteSpace(tzId))
            return null;

        try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { return null; }
    }

    private async Task<string?> DownloadIcsAsync(CancellationToken ct)
    {
        if (_icsUri is null)
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(OutlookCalendarSource));
            using var response = await client.GetAsync(_icsUri, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Download ICS Outlook fallito ({Status}): {Url}", response.StatusCode, _icsUri);
                return null;
            }

#if NET8_0_OR_GREATER
            return await response.Content.ReadAsStringAsync(ct);
#else
            return await response.Content.ReadAsStringAsync();
#endif
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Errore durante il download dell'ICS Outlook da {Url}", _icsUri);
            return null;
        }
    }

    private static readonly string[] UtcFormats =
    {
        "yyyyMMdd'T'HHmmss'Z'",
        "yyyyMMdd'T'HHmm'Z'",
        "yyyyMMdd'T'HH'Z'"
    };

    private static readonly string[] LocalDateTimeFormats =
    {
        "yyyyMMdd'T'HHmmss",
        "yyyyMMdd'T'HHmm",
        "yyyyMMdd'T'HH"
    };

    private static readonly string[] DateOnlyFormats = { "yyyyMMdd" };

    private sealed class CalendarEventBuilder
    {
        private DateTimeOffset? _start;
        private DateTimeOffset? _end;
        private string? _summary;
        private string? _location;

        public bool IsValid => _start is not null && _end is not null;

        public CalendarEventDto ToDto() =>
            new CalendarEventDto(_start!.Value, _end!.Value, _summary ?? string.Empty, _location);

        public void ProcessLine(string line, TimeZoneInfo fallbackTz)
        {
            var split = line.Split(':', 2);
            if (split.Length != 2)
                return;

            var key = split[0];
            var value = split[1];
            string? tz = null;

            if (key.Contains(';'))
            {
                var keyParts = key.Split(';', 2);
                key = keyParts[0];
                var param = keyParts[1];
                if (param.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))
                    tz = param.Substring("TZID=".Length);
            }

            switch (key.ToUpperInvariant())
            {
                case "DTSTART":
                    _start = ParseDateTime(value, tz, fallbackTz);
                    break;
                case "DTEND":
                    _end = ParseDateTime(value, tz, fallbackTz);
                    break;
                case "SUMMARY":
                    _summary = value;
                    break;
                case "LOCATION":
                    _location = value;
                    break;
            }
        }

        private static DateTimeOffset? ParseDateTime(string raw, string? tzId, TimeZoneInfo fallbackTz)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (raw.EndsWith("Z", StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.TryParseExact(
                    raw,
                    UtcFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var utcDto))
            {
                return utcDto;
            }

            var tz = OutlookCalendarSource.ResolveTimeZone(tzId) ?? fallbackTz;

            if (DateTime.TryParseExact(raw, LocalDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDateTime))
            {
                var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
                var offset = tz.GetUtcOffset(unspecified);
                return new DateTimeOffset(unspecified, offset);
            }

            if (DateTime.TryParseExact(raw, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            {
                var unspecified = DateTime.SpecifyKind(dateOnly, DateTimeKind.Unspecified);
                var offset = tz.GetUtcOffset(unspecified);
                return new DateTimeOffset(unspecified, offset);
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed;

            return null;
        }
    }
}

public sealed record CalendarEventDto(DateTimeOffset Start, DateTimeOffset End, string Title, string? Location);
