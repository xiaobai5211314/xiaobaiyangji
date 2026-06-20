using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using 小白养基.Models;
using 小白养基.Services;

var json = """
{
  "targetHandle": "aleabitoreddit",
  "fetchedAt": "2026-06-20T00:00:00Z",
  "items": [
    {
      "id": "x:1",
      "externalId": "1",
      "text": "English",
      "translatedText": "中文",
      "translatedAt": "2026-06-20T00:01:00Z",
      "translationProvider": "custom",
      "translationStatus": "success",
      "createdAt": "2026-06-20T00:00:00Z",
      "url": "https://x.com/aleabitoreddit/status/1",
      "replies": [
        {
          "id": "2",
          "text": "Reply",
          "translatedText": "",
          "translatedAt": null,
          "translationProvider": "tencent",
          "translationStatus": "failed",
          "createdAt": "2026-06-20T00:02:00Z"
        }
      ]
    }
  ]
}
""";

var dto = JsonSerializer.Deserialize<InfluencerPostsCacheDocument>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
}) ?? throw new InvalidOperationException("cache document was not deserialized");
var post = dto.Items.Single();
if (post.TranslatedText != "中文" || post.TranslationProvider != "custom" || post.TranslationStatus != "success")
    throw new InvalidOperationException("translation fields were not deserialized");
if (post.Replies?.Single().TranslatedAt is not null)
    throw new InvalidOperationException("nullable reply translation timestamp was not deserialized");

var tempPath = Path.GetTempFileName();
try
{
    var items = Enumerable.Range(1, 25)
        .Select(index => new InfluencerPostDto
        {
            Id = $"x:{index}",
            ExternalId = index.ToString(),
            Text = $"post {index}",
            CreatedAt = DateTimeOffset.Parse("2026-06-20T00:00:00Z").AddMinutes(index),
            Url = $"https://x.com/aleabitoreddit/status/{index}"
        })
        .Reverse()
        .ToList();
    await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(new InfluencerPostsCacheDocument
    {
        TargetHandle = "aleabitoreddit",
        FetchedAt = DateTimeOffset.Parse("2026-06-20T01:00:00Z"),
        Items = items
    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InfluencerPosts:CachePath"] = tempPath,
            ["InfluencerPosts:MaxDisplay"] = "20"
        })
        .Build();
    var service = new InfluencerPostsCacheService(configuration, NullLogger<InfluencerPostsCacheService>.Instance);
    var result = await service.GetLatestAsync(100);

    if (result.Items.Count != 20)
        throw new InvalidOperationException($"expected hard maximum 20, actual {result.Items.Count}");
    if (result.Items[0].ExternalId != "25" || result.Items[^1].ExternalId != "6")
        throw new InvalidOperationException("posts were not returned newest first");
}
finally
{
    File.Delete(tempPath);
}

Console.WriteLine("Influencer posts regression passed.");
