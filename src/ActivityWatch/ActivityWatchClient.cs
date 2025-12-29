using System.Net.Http.Json;
using System.Text.Json;

namespace WorkDailyReport.ActivityWatch;

public interface IActivityWatchClient
{
    Task<IReadOnlyList<BucketDto>> ListBucketsAsync(CancellationToken ct);
    Task<IReadOnlyList<EventDto>> GetEventsAsync(string bucketId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
}

public sealed class ActivityWatchClient : IActivityWatchClient
{
    private readonly HttpClient _http;

    public ActivityWatchClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<BucketDto>> ListBucketsAsync(CancellationToken ct)
    {
        // /buckets -> { "<bucketId>": { "id": "...", "client": "...", ... }, ... }
        var map = await _http.GetFromJsonAsync<Dictionary<string, JsonElement>>("buckets", ct);
        if (map is null || map.Count == 0)
            return new List<BucketDto>();

        var list = new List<BucketDto>(map.Count);
        foreach (var kv in map)
        {
            var el = kv.Value;
            string id = el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                        ? idProp.GetString() ?? kv.Key
                        : kv.Key;

            string client = el.TryGetProperty("client", out var clProp) && clProp.ValueKind == JsonValueKind.String
                        ? clProp.GetString() ?? string.Empty
                        : string.Empty;

            list.Add(new BucketDto(id, client));
        }
        return list;
    }

    public async Task<IReadOnlyList<EventDto>> GetEventsAsync(string bucketId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var url = "buckets/" + Uri.EscapeDataString(bucketId) + "/events"
                + "?start=" + Uri.EscapeDataString(start.ToString("O"))
                + "&end=" + Uri.EscapeDataString(end.ToString("O"));

        var res = await _http.GetFromJsonAsync<List<EventDto>>(url, ct);
        return res ?? new List<EventDto>();
    }
}

public sealed record BucketDto(string Id, string Client);
public sealed record EventDto(DateTimeOffset Timestamp, double? Duration, EventData Data);
public sealed record EventData(string? App, string? Title, string? Url, string? Status);
