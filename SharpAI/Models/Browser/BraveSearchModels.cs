using System.Text.Json.Serialization;

namespace SharpAI.Models.Browser;

public class BraveSearchResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public BraveQuery? Query { get; set; }

    [JsonPropertyName("web")]
    public BraveSearch? Web { get; set; }

    [JsonPropertyName("news")]
    public BraveNews? News { get; set; }

    [JsonPropertyName("mixed")]
    public BraveMixedResponse? Mixed { get; set; }
}

public class BraveQuery
{
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;

    [JsonPropertyName("show_strict_warning")]
    public bool ShowStrictWarning { get; set; }

    [JsonPropertyName("altered")]
    public string? Altered { get; set; }

    [JsonPropertyName("safesearch")]
    public bool SafeSearch { get; set; }

    [JsonPropertyName("is_navigational")]
    public bool IsNavigational { get; set; }

    [JsonPropertyName("local_decision")]
    public string? LocalDecision { get; set; }

    [JsonPropertyName("local_locations_idx")]
    public int? LocalLocationsIdx { get; set; }

    [JsonPropertyName("is_news_breaking")]
    public bool IsNewsBreaking { get; set; }

    [JsonPropertyName("spellcheck_off")]
    public bool SpellcheckOff { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("bad_results")]
    public bool BadResults { get; set; }

    [JsonPropertyName("should_fallback")]
    public bool ShouldFallback { get; set; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("header_country")]
    public string? HeaderCountry { get; set; }

    [JsonPropertyName("more_results_available")]
    public bool MoreResultsAvailable { get; set; }

    [JsonPropertyName("custom_location_label")]
    public string? CustomLocationLabel { get; set; }

    [JsonPropertyName("reddit_cluster")]
    public string? RedditCluster { get; set; }
}

public class BraveSearch
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("results")]
    public List<BraveSearchResult> Results { get; set; } = new();

    [JsonPropertyName("family_friendly")]
    public bool FamilyFriendly { get; set; }
}

public class BraveSearchResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("is_source_local")]
    public bool IsSourceLocal { get; set; }

    [JsonPropertyName("is_source_both")]
    public bool IsSourceBoth { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("page_age")]
    public string? PageAge { get; set; }

    [JsonPropertyName("page_fetched")]
    public string? PageFetched { get; set; }

    [JsonPropertyName("profile")]
    public BraveProfile? Profile { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("family_friendly")]
    public bool FamilyFriendly { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("meta_url")]
    public BraveMetaUrl? MetaUrl { get; set; }

    [JsonPropertyName("thumbnail")]
    public BraveThumbnail? Thumbnail { get; set; }

    [JsonPropertyName("age")]
    public string? Age { get; set; }

    [JsonPropertyName("extra_snippets")]
    public List<string>? ExtraSnippets { get; set; }
}

public class BraveProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("long_name")]
    public string LongName { get; set; } = string.Empty;

    [JsonPropertyName("img")]
    public string? Img { get; set; }
}

public class BraveMetaUrl
{
    [JsonPropertyName("scheme")]
    public string Scheme { get; set; } = string.Empty;

    [JsonPropertyName("netloc")]
    public string Netloc { get; set; } = string.Empty;

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("favicon")]
    public string? Favicon { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

public class BraveThumbnail
{
    [JsonPropertyName("src")]
    public string Src { get; set; } = string.Empty;

    [JsonPropertyName("original")]
    public string? Original { get; set; }

    [JsonPropertyName("logo")]
    public bool Logo { get; set; }
}

public class BraveNews
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("results")]
    public List<BraveNewsResult> Results { get; set; } = new();

    [JsonPropertyName("mutated_by_goggles")]
    public bool MutatedByGoggles { get; set; }
}

public class BraveNewsResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("age")]
    public string? Age { get; set; }

    [JsonPropertyName("page_age")]
    public string? PageAge { get; set; }

    [JsonPropertyName("breaking")]
    public bool Breaking { get; set; }

    [JsonPropertyName("profile")]
    public BraveProfile? Profile { get; set; }

    [JsonPropertyName("thumbnail")]
    public BraveThumbnail? Thumbnail { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("family_friendly")]
    public bool FamilyFriendly { get; set; }
}

public class BraveMixedResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("main")]
    public List<BraveMixedResult> Main { get; set; } = new();

    [JsonPropertyName("top")]
    public List<BraveMixedResult> Top { get; set; } = new();

    [JsonPropertyName("side")]
    public List<BraveMixedResult> Side { get; set; } = new();
}

public class BraveMixedResult
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("all")]
    public bool All { get; set; }
}

public class BraveSearchOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}