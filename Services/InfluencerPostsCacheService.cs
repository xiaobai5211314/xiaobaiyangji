using System.Text.Json;
using 小白养基.Models;

namespace 小白养基.Services
{
    public sealed class InfluencerPostsCacheService
    {
        private const string DefaultCachePath = "/var/lib/xiaobaiyangji/influencer-posts.json";
        private const int DefaultMaxDisplay = 20;
        private const int HardMaxDisplay = 20;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly IConfiguration _configuration;
        private readonly ILogger<InfluencerPostsCacheService> _logger;
        private IReadOnlyDictionary<string, string>? _privateEnv;

        public InfluencerPostsCacheService(IConfiguration configuration, ILogger<InfluencerPostsCacheService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<InfluencerPostsResult> GetLatestAsync(int? requestedLimit)
        {
            var cachePath = ResolveCachePath();
            var limit = ResolveLimit(requestedLimit);

            if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
            {
                return InfluencerPostsResult.Empty("missing", cachePath);
            }

            try
            {
                var json = await File.ReadAllTextAsync(cachePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return InfluencerPostsResult.Empty("empty", cachePath);
                }

                var document = DeserializeDocument(json);
                var items = (document?.Items ?? new List<InfluencerPostDto>())
                    .Where(IsUsable)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(limit)
                    .ToList();

                return new InfluencerPostsResult(
                    Status: items.Count > 0 ? "ok" : "empty",
                    CachePath: cachePath,
                    FetchedAt: document?.FetchedAt,
                    Items: items);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Influencer posts cache JSON is invalid: {CachePath}", cachePath);
                return InfluencerPostsResult.Empty("invalid", cachePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Influencer posts cache cannot be read: {CachePath}", cachePath);
                return InfluencerPostsResult.Empty("unavailable", cachePath);
            }
        }

        private string ResolveCachePath()
        {
            return FirstConfigured(
                "INFLUENCER_POSTS_CACHE_PATH",
                "InfluencerPosts:CachePath")
                ?? DefaultCachePath;
        }

        private int ResolveLimit(int? requestedLimit)
        {
            var configured = FirstConfigured(
                "INFLUENCER_POSTS_MAX_DISPLAY",
                "InfluencerPosts:MaxDisplay");

            var fallback = int.TryParse(configured, out var configuredLimit) && configuredLimit > 0
                ? configuredLimit
                : DefaultMaxDisplay;

            var limit = requestedLimit.GetValueOrDefault(fallback);
            return Math.Clamp(limit, 1, HardMaxDisplay);
        }

        private string? FirstConfigured(params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = _configuration[key];
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            var privateEnv = LoadPrivateEnv();
            foreach (var key in keys)
            {
                if (privateEnv.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private IReadOnlyDictionary<string, string> LoadPrivateEnv()
        {
            if (_privateEnv != null) return _privateEnv;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".secrets", "influencer.env");
            if (!File.Exists(envPath))
            {
                _privateEnv = values;
                return _privateEnv;
            }

            try
            {
                foreach (var rawLine in File.ReadAllLines(envPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || !line.Contains('='))
                    {
                        continue;
                    }

                    var parts = line.Split('=', 2);
                    var key = parts[0].Trim();
                    var value = StripEnvQuotes(parts[1]);
                    if (key.Length > 0 && value.Length > 0)
                    {
                        values[key] = value;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Influencer private env cannot be read: {EnvPath}", envPath);
            }

            _privateEnv = values;
            return _privateEnv;
        }

        private static string StripEnvQuotes(string value)
        {
            var text = value.Trim();
            if (text.Length >= 2 && text[0] == text[^1] && (text[0] == '\'' || text[0] == '"'))
            {
                return text[1..^1];
            }

            return text;
        }

        private static InfluencerPostsCacheDocument? DeserializeDocument(string json)
        {
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var items = JsonSerializer.Deserialize<List<InfluencerPostDto>>(json, JsonOptions);
                return items == null ? null : new InfluencerPostsCacheDocument { Items = items };
            }

            return JsonSerializer.Deserialize<InfluencerPostsCacheDocument>(json, JsonOptions);
        }

        private static bool IsUsable(InfluencerPostDto item)
        {
            return !string.IsNullOrWhiteSpace(item.ExternalId)
                && !string.IsNullOrWhiteSpace(item.Text)
                && item.CreatedAt != default;
        }
    }

    public sealed record InfluencerPostsResult(
        string Status,
        string CachePath,
        DateTimeOffset? FetchedAt,
        IReadOnlyList<InfluencerPostDto> Items)
    {
        public static InfluencerPostsResult Empty(string status, string cachePath)
        {
            return new InfluencerPostsResult(status, cachePath, null, Array.Empty<InfluencerPostDto>());
        }
    }
}
