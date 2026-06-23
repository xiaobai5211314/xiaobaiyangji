namespace 小白养基.Models
{
    public sealed class InfluencerPostDto
    {
        public string Id { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string AuthorHandle { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public DateTimeOffset? TranslatedAt { get; set; }
        public string TranslationProvider { get; set; } = "none";
        public string TranslationStatus { get; set; } = "skipped";
        public DateTimeOffset CreatedAt { get; set; }
        public string Url { get; set; } = string.Empty;
        public long LikeCount { get; set; }
        public long RetweetCount { get; set; }
        public long ReplyCount { get; set; }
        public long QuoteCount { get; set; }
        public List<string> MediaUrls { get; set; } = new();
        public List<InfluencerReplyDto>? Replies { get; set; }
        public string ReplyFetchStatus { get; set; } = "pending";
        public string ReplyFetchMessage { get; set; } = string.Empty;
        public DateTimeOffset? ReplyFetchedAt { get; set; }
        public int ReplyFetchCount { get; set; }
        public string Source { get; set; } = "twscrape";
    }

    public sealed class InfluencerReplyDto
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public string? TranslatedText { get; set; }
        public DateTimeOffset? TranslatedAt { get; set; }
        public string TranslationProvider { get; set; } = "none";
        public string TranslationStatus { get; set; } = "skipped";
        public DateTimeOffset CreatedAt { get; set; }
        public string? AuthorName { get; set; }
        public string? AuthorUsername { get; set; }
        public long LikeCount { get; set; }
        public string? Url { get; set; }
    }

    public sealed class InfluencerPostsCacheDocument
    {
        public string TargetHandle { get; set; } = string.Empty;
        public DateTimeOffset? FetchedAt { get; set; }
        public List<InfluencerPostDto> Items { get; set; } = new();
    }
}
