using System.Text.Json.Serialization;

namespace SharpAI.Models.Browser;

public class BrowserSearchArgs
{
    [JsonPropertyName("query")]
    public required string Query { get; set; }

    [JsonPropertyName("topn")]
    public int? TopN { get; set; } = 10;

    [JsonPropertyName("source")]
    public string? Source { get; set; } = "web";
}

public class BrowserOpenArgs
{
    [JsonPropertyName("id")]
    public object? Id { get; set; } = -1; // Can be int or string

    [JsonPropertyName("cursor")]
    public int? Cursor { get; set; } = -1;

    [JsonPropertyName("loc")]
    public int? Loc { get; set; } = -1;

    [JsonPropertyName("num_lines")]
    public int? NumLines { get; set; } = -1;

    [JsonPropertyName("view_source")]
    public bool? ViewSource { get; set; } = false;

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

public class BrowserFindArgs
{
    [JsonPropertyName("pattern")]
    public required string Pattern { get; set; }

    [JsonPropertyName("cursor")]
    public int? Cursor { get; set; } = -1;
}

public class BrowserSearchResult
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public class BrowserPage
{
    public int Cursor { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Lines { get; set; } = new();
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
}

public class BrowserFindResult
{
    public int LineNumber { get; set; }
    public string LineContent { get; set; } = string.Empty;
    public int Position { get; set; }
}