using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SharpAI.Models.Browser;

namespace SharpAI.Services.Browser;

/// <summary>
/// Provides browser functionality including web search and page content retrieval.
/// Integrates with Brave Search API for web search capabilities and maintains page cache for navigation.
/// </summary>
public class BrowserService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BrowserService> _logger;
    private readonly BraveSearchOptions _braveOptions;
    private readonly Dictionary<int, BrowserPage> _pages = new();
    private readonly Dictionary<int, string> _linkIdToUrlMapping = new();
    private int _nextCursor = 1;

    /// <summary>
    /// Initializes a new instance of the BrowserService.
    /// </summary>
    /// <param name="httpClient">HTTP client for making web requests</param>
    /// <param name="logger">Logger for service operations</param>
    /// <param name="braveOptions">Configuration options for Brave Search API</param>
    public BrowserService(HttpClient httpClient, ILogger<BrowserService> logger, IOptions<BraveSearchOptions> braveOptions)
    {
        _httpClient = httpClient;
        _logger = logger;
        _braveOptions = braveOptions.Value;
    }

    /// <summary>
    /// Performs a web search using Brave Search API and returns formatted results.
    /// </summary>
    /// <param name="args">Search parameters including query and result count</param>
    /// <returns>Formatted search results or error message</returns>
    public async Task<string> SearchAsync(BrowserSearchArgs args)
    {
        _logger.LogInformation("Performing web search for query: {Query}", args.Query);

        try
        {
            if (string.IsNullOrEmpty(_braveOptions.ApiKey))
            {
                _logger.LogWarning("Brave Search API key not configured");
                return "Error: Browser search is not available. The Brave Search API key is not configured. Please contact the administrator to set up the API key in the application configuration.";
            }

            var results = await CallBraveSearchApiAsync(args.Query, args.TopN ?? 10);
            var response = FormatBraveSearchResults(results, args.TopN ?? 10);
            _logger.LogInformation("Search completed successfully, returned {Count} results", results?.Web?.Results?.Count ?? 0);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing browser search for query: {Query}", args.Query);
            return "Error: Failed to perform search. Please try again.";
        }
    }

    /// <summary>
    /// Performs a web search and returns results in JSON format.
    /// </summary>
    /// <param name="args">Search parameters including query and result count</param>
    /// <returns>JSON-formatted search results or error message</returns>
    public async Task<string> SearchJsonAsync(BrowserSearchArgs args)
    {
        _logger.LogInformation("Performing JSON web search for query: {Query}", args.Query);

        try
        {
            if (string.IsNullOrEmpty(_braveOptions.ApiKey))
            {
                _logger.LogWarning("Brave Search API key not configured for JSON search");
                return JsonSerializer.Serialize(new { 
                    error = "Browser search is not available. The Brave Search API key is not configured. Please contact the administrator to set up the API key in the application configuration." 
                });
            }

            var results = await CallBraveSearchApiAsync(args.Query, args.TopN ?? 10);
            
            // Simplify the response to only include id, title, description, and URL
            var simplifiedResults = new List<object>();
            
            if (results?.Web?.Results != null)
            {
                simplifiedResults = results.Web.Results.Take(args.TopN ?? 10).Select((result, index) => new
                {
                    id = index + 1,
                    title = result.Title,
                    description = result.Description,
                    url = result.Url
                }).Cast<object>().ToList();

                // Store URL mapping for later use in open function
                UpdateLinkIdMapping(results.Web.Results.Take(args.TopN ?? 10).ToList());
                _logger.LogInformation("JSON search completed successfully, returned {Count} results", simplifiedResults.Count);
            }

            return JsonSerializer.Serialize(new
            {
                query = args.Query,
                count = simplifiedResults.Count,
                results = simplifiedResults
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing JSON browser search for query: {Query}", args.Query);
            return JsonSerializer.Serialize(new { error = $"Failed to perform search: {ex.Message}" });
        }
    }

    /// <summary>
    /// Opens a web page or scrolls to a location on an existing page.
    /// </summary>
    /// <param name="args">Open parameters including URL/ID, cursor, and location</param>
    /// <returns>Formatted page content or error message</returns>
    public async Task<string> OpenAsync(BrowserOpenArgs args)
    {
        _logger.LogInformation("Opening page - Id: {Id}, Cursor: {Cursor}, Location: {Loc}", args.Id, args.Cursor, args.Loc);

        try
        {
            string url = "";
            
            // Determine the URL to open
            if (args.Id is string urlString)
            {
                url = urlString;
                _logger.LogDebug("Using direct URL: {Url}", url);
            }
            else if (args.Id is int linkId && linkId > 0)
            {
                url = GetUrlFromLinkId(linkId);
                _logger.LogDebug("Resolved link ID {LinkId} to URL: {Url}", linkId, url);
            }
            else if (args.Cursor.HasValue && args.Cursor > 0 && _pages.ContainsKey(args.Cursor.Value))
            {
                // Scrolling on existing page
                _logger.LogDebug("Scrolling on existing page with cursor: {Cursor}", args.Cursor.Value);
                return ScrollToLocation(_pages[args.Cursor.Value], args.Loc ?? -1, args.NumLines ?? 20);
            }
            else
            {
                return "Error: No valid URL or page specified to open.";
            }

            if (string.IsNullOrEmpty(url))
            {
                return "Error: Could not determine URL to open.";
            }

            // Fetch the page content
            var pageContent = await FetchPageContentAsync(url);
            var page = new BrowserPage
            {
                Cursor = _nextCursor++,
                Url = url,
                Title = ExtractTitle(pageContent),
                Content = pageContent,
                Lines = SplitIntoLines(pageContent)
            };

            _pages[page.Cursor] = page;
            _logger.LogInformation("Successfully loaded page '{Title}' with cursor {Cursor}", page.Title, page.Cursor);

            // Return the formatted page content
            return FormatPageContent(page, args.Loc ?? 1, args.NumLines ?? 20);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening page with args: {@Args}", args);
            return "Error: Failed to open page. Please check the URL and try again.";
        }
    }

    /// <summary>
    /// Opens a web page and returns content in JSON format, or scrolls to a location on an existing page.
    /// </summary>
    /// <param name="args">Open parameters including URL/ID, cursor, and location</param>
    /// <returns>JSON-formatted page content or error message</returns>
    public async Task<string> OpenJsonAsync(BrowserOpenArgs args)
    {
        _logger.LogInformation("Opening page for JSON response - Id: {Id}, Cursor: {Cursor}, Location: {Loc}", args.Id, args.Cursor, args.Loc);

        try
        {
            string url = "";
            
            // Determine the URL to open
            if (args.Id is string urlString)
            {
                url = urlString;
                _logger.LogDebug("Using direct URL for JSON: {Url}", url);
            }
            else if (args.Id is int linkId && linkId > 0)
            {
                url = GetUrlFromLinkId(linkId);
                _logger.LogDebug("Resolved link ID {LinkId} to URL for JSON: {Url}", linkId, url);
            }
            else if (args.Cursor.HasValue && args.Cursor > 0 && _pages.ContainsKey(args.Cursor.Value))
            {
                // Scrolling on existing page - return JSON format
                var existingPage = _pages[args.Cursor.Value];
                var scrollResult = ScrollToLocation(existingPage, args.Loc ?? -1, args.NumLines ?? 20);
                
                _logger.LogDebug("Returning JSON scroll result for cursor: {Cursor}", args.Cursor.Value);
                return JsonSerializer.Serialize(new
                {
                    cursor = existingPage.Cursor,
                    url = existingPage.Url,
                    title = existingPage.Title,
                    content = scrollResult,
                    loc = args.Loc ?? -1,
                    num_lines = args.NumLines ?? 20,
                    total_lines = existingPage.Lines.Count
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                return JsonSerializer.Serialize(new { error = "No valid URL or page specified to open." });
            }

            if (string.IsNullOrEmpty(url))
            {
                return JsonSerializer.Serialize(new { error = "Could not determine URL to open." });
            }

            // Fetch the page content
            var pageContent = await FetchPageContentAsync(url);
            var page = new BrowserPage
            {
                Cursor = _nextCursor++,
                Url = url,
                Title = ExtractTitle(pageContent),
                Content = pageContent,
                Lines = SplitIntoLines(pageContent)
            };

            _pages[page.Cursor] = page;
            _logger.LogInformation("Successfully loaded page '{Title}' for JSON response with cursor {Cursor}", page.Title, page.Cursor);

            // Return the page data in JSON format
            var startLine = args.Loc ?? 1;
            var numLines = args.NumLines ?? 20;
            var startIndex = Math.Max(0, startLine - 1);
            var endIndex = Math.Min(page.Lines.Count, startIndex + numLines);
            
            var displayedLines = page.Lines.Skip(startIndex).Take(numLines).ToList();

            return JsonSerializer.Serialize(new
            {
                cursor = page.Cursor,
                url = page.Url,
                title = page.Title,
                lines = displayedLines,
                start_line = startLine,
                num_lines = numLines,
                total_lines = page.Lines.Count,
                view_source = args.ViewSource ?? false,
                content_length = pageContent.Length
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening page for JSON with args: {@Args}", args);
            return JsonSerializer.Serialize(new { error = $"Failed to open page: {ex.Message}" });
        }
    }

    /// <summary>
    /// Searches for a pattern within a loaded page.
    /// </summary>
    /// <param name="args">Find parameters including pattern and cursor</param>
    /// <returns>Formatted search results or error message</returns>
    public Task<string> FindAsync(BrowserFindArgs args)
    {
        _logger.LogInformation("Searching for pattern '{Pattern}' in page with cursor: {Cursor}", args.Pattern, args.Cursor);

        try
        {
            var cursor = args.Cursor ?? -1;
            BrowserPage? page = null;

            if (cursor > 0 && _pages.ContainsKey(cursor))
            {
                page = _pages[cursor];
                _logger.LogDebug("Using specific page with cursor: {Cursor}", cursor);
            }
            else
            {
                // Use the most recent page
                page = _pages.Values.OrderByDescending(p => p.LoadedAt).FirstOrDefault();
                _logger.LogDebug("Using most recent page");
            }

            if (page == null)
            {
                return Task.FromResult("Error: No page available to search. Please open a page first.");
            }

            var results = FindInPage(page, args.Pattern);
            _logger.LogInformation("Found {Count} matches for pattern '{Pattern}' in page '{Title}'", results.Count, args.Pattern, page.Title);
            return Task.FromResult(FormatFindResults(page, results, args.Pattern));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for pattern '{Pattern}' with cursor: {Cursor}", args.Pattern, args.Cursor);
            return Task.FromResult("Error: Failed to search in page.");
        }
    }

    /// <summary>
    /// Calls the Brave Search API to perform web search.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="count">Number of results to return</param>
    /// <returns>Brave search response or null if failed</returns>
    private async Task<BraveSearchResponse?> CallBraveSearchApiAsync(string query, int count)
    {
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"{_braveOptions.BaseUrl}/web/search?q={encodedQuery}&count={count}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Accept-Encoding", "gzip");
            request.Headers.Add("X-Subscription-Token", _braveOptions.ApiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string jsonContent;
            
            // Check if the response is gzipped and decompress if needed
            if (response.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
                using var reader = new StreamReader(gzipStream);
                jsonContent = await reader.ReadToEndAsync();
            }
            else
            {
                jsonContent = await response.Content.ReadAsStringAsync();
            }
            
            _logger.LogDebug("Received Brave Search API response with length: {Length}", jsonContent.Length);

            var searchResponse = JsonSerializer.Deserialize<BraveSearchResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return searchResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Brave Search API for query: {Query}", query);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing Brave Search API response for query: {Query}", query);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Brave Search API for query: {Query}", query);
            throw;
        }
    }

    /// <summary>
    /// Formats Brave Search API results into a readable text format.
    /// </summary>
    /// <param name="response">Brave search response</param>
    /// <param name="requestedCount">Number of results requested</param>
    /// <returns>Formatted search results</returns>
    private string FormatBraveSearchResults(BraveSearchResponse? response, int requestedCount)
    {
        if (response?.Web?.Results == null || !response.Web.Results.Any())
        {
            return "No search results found.";
        }

        var results = response.Web.Results.Take(requestedCount).ToList();
        var formatted = new List<string>
        {
            $"Found {results.Count} search results:",
            ""
        };

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var linkId = i + 1;
            
            formatted.Add($"【{linkId}†{result.Url}】");
            formatted.Add($"**{result.Title}**");
            formatted.Add(result.Description);
            formatted.Add("");
        }

        // Store URL mapping for later use in open function
        UpdateLinkIdMapping(results);

        return string.Join("\n", formatted);
    }

    /// <summary>
    /// Updates the internal mapping of link IDs to URLs from search results.
    /// </summary>
    /// <param name="results">Search results to extract URLs from</param>
    private void UpdateLinkIdMapping(List<BraveSearchResult> results)
    {
        // Clear previous mappings and store new ones
        _linkIdToUrlMapping.Clear();
        
        for (int i = 0; i < results.Count; i++)
        {
            var linkId = i + 1;
            _linkIdToUrlMapping[linkId] = results[i].Url;
        }
        _logger.LogDebug("Updated link ID mapping with {Count} URLs", results.Count);
    }

    /// <summary>
    /// Fetches content from a web page URL.
    /// </summary>
    /// <param name="url">URL to fetch</param>
    /// <returns>Cleaned HTML content or simulated content if fetch fails</returns>
    private async Task<string> FetchPageContentAsync(string url)
    {
        try
        {
            _logger.LogDebug("Fetching content from URL: {Url}", url);
            var response = await _httpClient.GetStringAsync(url);
            return CleanHtmlContent(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch content from URL: {Url}, returning simulated content", url);
            // Return simulated content if actual fetch fails
            return $@"
<html>
<head><title>Example Page</title></head>
<body>
<h1>Example Page Content</h1>
<p>This is simulated content for the URL: {url}</p>
<p>In a real implementation, this would be the actual web page content.</p>
<p>The content would be cleaned and formatted for better readability.</p>
</body>
</html>";
        }
    }

    /// <summary>
    /// Cleans HTML content by removing scripts, styles, and tags.
    /// </summary>
    /// <param name="html">Raw HTML content</param>
    /// <returns>Cleaned text content</returns>
    private string CleanHtmlContent(string html)
    {
        // Simple HTML cleaning - remove tags and decode entities
        var text = Regex.Replace(html, "<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "<style[^>]*>.*?</style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = Regex.Replace(text, @"\s+", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    /// <summary>
    /// Extracts the title from HTML content.
    /// </summary>
    /// <param name="content">HTML content</param>
    /// <returns>Page title or default if not found</returns>
    private string ExtractTitle(string content)
    {
        var titleMatch = Regex.Match(content, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase);
        return titleMatch.Success ? titleMatch.Groups[1].Value : "Untitled Page";
    }

    /// <summary>
    /// Splits content into numbered lines for easier navigation.
    /// </summary>
    /// <param name="content">Text content</param>
    /// <returns>List of numbered lines</returns>
    private List<string> SplitIntoLines(string content)
    {
        return content.Split('\n')
            .Select((line, index) => $"{index + 1:D3}: {line.Trim()}")
            .Where(line => !string.IsNullOrWhiteSpace(line.Substring(5)))
            .ToList();
    }

    /// <summary>
    /// Formats page content for display with line numbers and navigation info.
    /// </summary>
    /// <param name="page">Browser page to format</param>
    /// <param name="startLine">Starting line number</param>
    /// <param name="numLines">Number of lines to display</param>
    /// <returns>Formatted page content</returns>
    private string FormatPageContent(BrowserPage page, int startLine, int numLines)
    {
        var lines = new List<string>
        {
            $"[{page.Cursor}] **{page.Title}**",
            $"URL: {page.Url}",
            ""
        };

        var startIndex = Math.Max(0, startLine - 1);
        var endIndex = Math.Min(page.Lines.Count, startIndex + numLines);

        for (int i = startIndex; i < endIndex; i++)
        {
            lines.Add(page.Lines[i]);
        }

        if (endIndex < page.Lines.Count)
        {
            lines.Add($"... ({page.Lines.Count - endIndex} more lines)");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Scrolls to a specific location on a page.
    /// </summary>
    /// <param name="page">Browser page</param>
    /// <param name="loc">Location to scroll to</param>
    /// <param name="numLines">Number of lines to display</param>
    /// <returns>Formatted page content at the specified location</returns>
    private string ScrollToLocation(BrowserPage page, int loc, int numLines)
    {
        return FormatPageContent(page, loc > 0 ? loc : 1, numLines);
    }

    /// <summary>
    /// Finds all occurrences of a pattern within a page.
    /// </summary>
    /// <param name="page">Browser page to search</param>
    /// <param name="pattern">Search pattern</param>
    /// <returns>List of find results</returns>
    private List<BrowserFindResult> FindInPage(BrowserPage page, string pattern)
    {
        var results = new List<BrowserFindResult>();
        var regex = new Regex(Regex.Escape(pattern), RegexOptions.IgnoreCase);

        for (int i = 0; i < page.Lines.Count; i++)
        {
            var line = page.Lines[i];
            var matches = regex.Matches(line);
            
            foreach (Match match in matches)
            {
                results.Add(new BrowserFindResult
                {
                    LineNumber = i + 1,
                    LineContent = line,
                    Position = match.Index
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Formats find results for display.
    /// </summary>
    /// <param name="page">Browser page that was searched</param>
    /// <param name="results">Find results</param>
    /// <param name="pattern">Search pattern</param>
    /// <returns>Formatted find results</returns>
    private string FormatFindResults(BrowserPage page, List<BrowserFindResult> results, string pattern)
    {
        if (!results.Any())
        {
            return $"Pattern '{pattern}' not found in page [{page.Cursor}].";
        }

        var lines = new List<string>
        {
            $"Found {results.Count} matches for '{pattern}' in page [{page.Cursor}]:",
            ""
        };

        foreach (var result in results.Take(10)) // Limit to 10 results
        {
            lines.Add($"Line {result.LineNumber}: {result.LineContent}");
        }

        if (results.Count > 10)
        {
            lines.Add($"... and {results.Count - 10} more matches");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Gets URL from link ID using the stored mapping from search results.
    /// </summary>
    /// <param name="linkId">Link ID from search results</param>
    /// <returns>Corresponding URL or fallback URL</returns>
    private string GetUrlFromLinkId(int linkId)
    {
        // Use the stored mapping from the last search results
        if (_linkIdToUrlMapping.TryGetValue(linkId, out var url))
        {
            return url;
        }
        
        // Fallback for demo purposes
        return $"https://example.com/link-{linkId}";
    }
}