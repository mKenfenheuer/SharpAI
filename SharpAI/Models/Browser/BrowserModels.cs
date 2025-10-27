using System.Text.Json.Serialization;

namespace SharpAI.Models.Browser;

/// <summary>
/// Arguments for browser search operations through the browser tool handler.
/// </summary>
public class BrowserSearchArgs
{
    /// <summary>
    /// The search query string to execute.
    /// </summary>
    [JsonPropertyName("query")]
    public required string Query { get; set; }

    /// <summary>
    /// Maximum number of search results to return (default: 10).
    /// </summary>
    [JsonPropertyName("topn")]
    public int? TopN { get; set; } = 10;

    /// <summary>
    /// The search source to use (default: "web").
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; } = "web";
}

/// <summary>
/// Arguments for browser open operations to view web pages or navigate content.
/// </summary>
public class BrowserOpenArgs
{
    /// <summary>
    /// The ID or URL to open. Can be an integer (search result ID) or string (URL).
    /// </summary>
    [JsonPropertyName("id")]
    public object? Id { get; set; } = -1; // Can be int or string

    /// <summary>
    /// Starting position cursor for content viewing (-1 for default).
    /// </summary>
    [JsonPropertyName("cursor")]
    public int? Cursor { get; set; } = -1;

    /// <summary>
    /// Specific location to navigate to within the page (-1 for default).
    /// </summary>
    [JsonPropertyName("loc")]
    public int? Loc { get; set; } = -1;

    /// <summary>
    /// Number of lines to display from the cursor position (-1 for default).
    /// </summary>
    [JsonPropertyName("num_lines")]
    public int? NumLines { get; set; } = -1;

    /// <summary>
    /// Whether to view the raw HTML source of the page instead of processed content.
    /// </summary>
    [JsonPropertyName("view_source")]
    public bool? ViewSource { get; set; } = false;

    /// <summary>
    /// The source system to use for opening the content.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

/// <summary>
/// Arguments for finding content within browser pages using pattern matching.
/// </summary>
public class BrowserFindArgs
{
    /// <summary>
    /// The search pattern to find within the page content.
    /// </summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; set; }

    /// <summary>
    /// Starting cursor position for the search (-1 for beginning).
    /// </summary>
    [JsonPropertyName("cursor")]
    public int? Cursor { get; set; } = -1;
}

/// <summary>
/// Represents a single search result from browser search operations.
/// </summary>
public class BrowserSearchResult
{
    /// <summary>
    /// Unique identifier for this search result.
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// The title of the web page or search result.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// The URL of the web page.
    /// </summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>
    /// A summary or snippet of the page content.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Represents a loaded web page with its content and navigation state.
/// </summary>
public class BrowserPage
{
    /// <summary>
    /// Current cursor position within the page content.
    /// </summary>
    public int Cursor { get; set; }
    /// <summary>
    /// The URL of the loaded page.
    /// </summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>
    /// The title of the web page.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// The processed text content of the page.
    /// </summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>
    /// The page content split into individual lines for navigation.
    /// </summary>
    public List<string> Lines { get; set; } = new();
    /// <summary>
    /// Timestamp when the page was loaded.
    /// </summary>
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the result of a find operation within a browser page.
/// </summary>
public class BrowserFindResult
{
    /// <summary>
    /// The line number where the pattern was found.
    /// </summary>
    public int LineNumber { get; set; }
    /// <summary>
    /// The content of the line containing the match.
    /// </summary>
    public string LineContent { get; set; } = string.Empty;
    /// <summary>
    /// The character position within the line where the pattern was found.
    /// </summary>
    public int Position { get; set; }
}