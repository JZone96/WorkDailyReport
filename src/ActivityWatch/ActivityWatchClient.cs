using System.Text.Json;

namespace WorkDailyReport.ActivityWatch
{
    // ---- Interfaccia pubblica ------------------------------------------------
    public interface IActivityWatchClient
    {
        Task<IReadOnlyList<BucketInfo>> ListBucketsAsync(CancellationToken ct = default);
        Task<IReadOnlyList<AwEvent>> GetEventsAsync(string bucketId, DateTime start, DateTime end, CancellationToken ct = default);
        string BaseUrl { get; }
    }

    // ---- Implementazione base (da completare) --------------------------------
    public sealed class ActivityWatchClient : IActivityWatchClient
    {
        private readonly HttpClient _http;
        public string BaseUrl { get; }

        public ActivityWatchClient(HttpClient httpClient, string baseUrl)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            BaseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        // GET /api/0/buckets
        // GET /api/0/buckets
        public async Task<IReadOnlyList<BucketInfo>> ListBucketsAsync(CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/buckets";

        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        // Opzioni JSON tolleranti al case e con naming web
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, BucketInfoRaw>>(stream, jsonOpts, ct)
                ?? new Dictionary<string, BucketInfoRaw>();

        var list = new List<BucketInfo>(dict.Count);
        foreach (var kvp in dict)
        {
            var id = kvp.Key;
            var raw = kvp.Value;

            list.Add(new BucketInfo
            {
                Id = id,
                Type = raw?.Type ?? string.Empty,
                Client = raw?.Client,
                Hostname = raw?.Hostname,
                Created = raw?.Created
            });
        }

        // Ordinamento facoltativo per avere output stabile
        list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return list;
    }


        // GET /api/0/buckets/{bucketId}/events?start=...&end=...
        public async Task<IReadOnlyList<AwEvent>> GetEventsAsync(string bucketId, DateTime start, DateTime end, CancellationToken ct = default)
        {

            var startStr = new string(start.ToString("O"));
            var endStr = new string(end.ToString("O"));

            var url = $"{BaseUrl}/buckets/{bucketId}/events?start={startStr}&end={endStr}";
            Console.WriteLine($"{BaseUrl}/buckets/{bucketId}/events?start={startStr}&end={endStr}");


            if (string.IsNullOrWhiteSpace(bucketId)) throw new ArgumentException("bucketId required", nameof(bucketId));

            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);

            // Opzioni JSON tolleranti al case e con naming web
            var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

            var raws = await JsonSerializer.DeserializeAsync<List<AwEventRaw>>(stream, jsonOpts, ct);

            var list = new List<AwEvent>(raws.Count);

            foreach (var raw in raws)
            {

                list.Add(new AwEvent
                {
                    Id = (int)raw.Id,
                    Timestamp = raw.Timestamp,
                    Duration = (double)(raw?.Duration),
                    Data = MapData(bucketId, raw.Data),
                    SourceBucket = bucketId
                });
            }
            // TODO: build URL con start/end ISO8601 (start.ToString("O"))
            // TODO: GET e deserializza in List<AwEventRaw>, poi mappa in AwEvent
            // Ordinamento facoltativo per avere output stabile
            //list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        private static AwEventData MapData(string bucketId, JsonElement data)
        {
            string? Get(string name) => data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var result = new AwEventData
            {
                App = Get("app"),
                Title = Get("title"),
                Url = Get("url"),
                Status = Get("status"),
                Extras = new()
            };

            // hostname: se presente in data o derivato da Url
            result.Hostname = Get("hostname");
            if (result.Hostname is null && result.Url is string u && Uri.TryCreate(u, UriKind.Absolute, out var uri))
                result.Hostname = uri.Host;

            // salva chiavi non mappate in Extras
            foreach (var p in data.EnumerateObject())
            {
                if (p.NameEquals("app") || p.NameEquals("title") || p.NameEquals("url") ||
                    p.NameEquals("hostname") || p.NameEquals("status")) continue;

                result.Extras[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
            }

            return result;
        }

        // (facoltativo) helper per leggere JSON con JsonDocument/JsonSerializerOptions condivisi
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
    }

    // ---- Modelli/POCO --------------------------------------------------------

    // Bucket minimale “presentabile” all’app
    public sealed class BucketInfo
    {
        public string Id { get; init; } = string.Empty;          // es: aw-watcher-window_HOST
        public string Type { get; init; } = string.Empty;        // es: "event"
        public string? Client { get; init; }                     // es: "aw-watcher-window"
        public string? Hostname { get; init; }                   // es: "MYPC"
        public DateTime? Created { get; init; }
    }

    // Raw bucket così come torna dall’API (valori dentro al dizionario)
    public sealed class BucketInfoRaw
    {
        public string? Type { get; init; }
        public string? Client { get; init; }
        public string? Hostname { get; init; }
        public DateTime? Created { get; init; }
    }

    // Evento normalizzato per il resto dell’app
    public sealed class AwEvent
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; init; }           // inizio
        public double Duration { get; init; }                    // secondi (double come da AW)
        public AwEventData Data { get; init; } = new();          // payload specifico (window/web/afk)
        public string SourceBucket { get; init; } = string.Empty;
    }

    // Evento raw come da API (prima della mappatura)
    public sealed class AwEventRaw
    {
        public int? Id { get; init; }
        public DateTime Timestamp { get; init; }
        public double Duration { get; init; }
        public JsonElement Data { get; init; }                   // schema variabile → lo manteniamo grezzo
    }

    // Contenuto “data” esposto in modo sicuro (chiavi comuni più un bag generico)
    public sealed class AwEventData
    {
        // campi tipici dei watcher:
        public string? App { get; set; }                        // window: "Code", "devenv.exe", ...
        public string? Title { get; set; }                      // window: titolo finestra
        public string? Url { get; set; }                        // web: url completa (se non anonimizzata)
        public string? Hostname { get; set; }                   // web: dominio
        public string? Status { get; set; }                     // afk: "afk"/"not-afk"

        // bag generico per non perdere info non mappate
        public Dictionary<string, string?> Extras { get; init; } = new();
    }
}
