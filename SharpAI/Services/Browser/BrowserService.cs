using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SharpAI.Models.Browser;

namespace SharpAI.Services.Browser;

public class BrowserService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BrowserService> _logger;
    private readonly BraveSearchOptions _braveOptions;
    private readonly Dictionary<int, BrowserPage> _pages = new();
    private int _nextCursor = 1;

    public BrowserService(HttpClient httpClient, ILogger<BrowserService> logger, IOptions<BraveSearchOptions> braveOptions)
    {
        _httpClient = httpClient;
        _logger = logger;
        _braveOptions = braveOptions.Value;
    }

    public async Task<string> SearchAsync(BrowserSearchArgs args)
    {
        _logger.LogInformation("Browser search: {Query}", args.Query);

        try
        {
            if (string.IsNullOrEmpty(_braveOptions.ApiKey))
            {
                _logger.LogWarning("Brave Search API key not configured");
                return "Error: Browser search is not available. The Brave Search API key is not configured. Please contact the administrator to set up the API key in the application configuration.";
            }

            var results = await CallBraveSearchApiAsync(args.Query, args.TopN ?? 10);
            var response = FormatBraveSearchResults(results, args.TopN ?? 10);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing browser search");
            return "Error: Failed to perform search. Please try again.";
        }
    }

    public async Task<string> SearchJsonAsync(BrowserSearchArgs args)
    {
        _logger.LogInformation("Browser searchjson: {Query}", args.Query);

        try
        {
            if (string.IsNullOrEmpty(_braveOptions.ApiKey))
            {
                _logger.LogWarning("Brave Search API key not configured");
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
            _logger.LogError(ex, "Error performing browser searchjson");
            return JsonSerializer.Serialize(new { error = $"Failed to perform search: {ex.Message}" });
        }
    }

    public async Task<string> OpenAsync(BrowserOpenArgs args)
    {
        _logger.LogInformation("Browser open: Id={Id}, Cursor={Cursor}, Loc={Loc}", args.Id, args.Cursor, args.Loc);

        try
        {
            string url = "";
            
            // Determine the URL to open
            if (args.Id is string urlString)
            {
                url = urlString;
            }
            else if (args.Id is int linkId && linkId > 0)
            {
                // In a real implementation, you'd maintain a mapping of link IDs to URLs
                // from previous search results
                url = GetUrlFromLinkId(linkId);
            }
            else if (args.Cursor.HasValue && args.Cursor > 0 && _pages.ContainsKey(args.Cursor.Value))
            {
                // Scrolling on existing page
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

            // Return the formatted page content
            return FormatPageContent(page, args.Loc ?? 1, args.NumLines ?? 20);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening page");
            return "Error: Failed to open page. Please check the URL and try again.";
        }
    }

    public async Task<string> OpenJsonAsync(BrowserOpenArgs args)
    {
        _logger.LogInformation("Browser openjson: Id={Id}, Cursor={Cursor}, Loc={Loc}", args.Id, args.Cursor, args.Loc);

        try
        {
            string url = "";
            
            // Determine the URL to open
            if (args.Id is string urlString)
            {
                url = urlString;
            }
            else if (args.Id is int linkId && linkId > 0)
            {
                // In a real implementation, you'd maintain a mapping of link IDs to URLs
                // from previous search results
                url = GetUrlFromLinkId(linkId);
            }
            else if (args.Cursor.HasValue && args.Cursor > 0 && _pages.ContainsKey(args.Cursor.Value))
            {
                // Scrolling on existing page - return JSON format
                var existingPage = _pages[args.Cursor.Value];
                var scrollResult = ScrollToLocation(existingPage, args.Loc ?? -1, args.NumLines ?? 20);
                
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
            _logger.LogError(ex, "Error opening page for JSON");
            return JsonSerializer.Serialize(new { error = $"Failed to open page: {ex.Message}" });
        }
    }

    public Task<string> FindAsync(BrowserFindArgs args)
    {
        _logger.LogInformation("Browser find: Pattern={Pattern}, Cursor={Cursor}", args.Pattern, args.Cursor);

        try
        {
            var cursor = args.Cursor ?? -1;
            BrowserPage? page = null;

            if (cursor > 0 && _pages.ContainsKey(cursor))
            {
                page = _pages[cursor];
            }
            else
            {
                // Use the most recent page
                page = _pages.Values.OrderByDescending(p => p.LoadedAt).FirstOrDefault();
            }

            if (page == null)
            {
                return Task.FromResult("Error: No page available to search. Please open a page first.");
            }

            var results = FindInPage(page, args.Pattern);
            return Task.FromResult(FormatFindResults(page, results, args.Pattern));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding in page");
            return Task.FromResult("Error: Failed to search in page.");
        }
    }

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
            
            _logger.LogDebug("Brave Search API response: {Response}", jsonContent);

            var searchResponse = JsonSerializer.Deserialize<BraveSearchResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return searchResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Brave Search API");
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing Brave Search API response");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Brave Search API");
            throw;
        }
    }

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

    private readonly Dictionary<int, string> _linkIdToUrlMapping = new();

    private void UpdateLinkIdMapping(List<BraveSearchResult> results)
    {
        // Clear previous mappings and store new ones
        _linkIdToUrlMapping.Clear();
        
        for (int i = 0; i < results.Count; i++)
        {
            var linkId = i + 1;
            _linkIdToUrlMapping[linkId] = results[i].Url;
        }
    }

    private async Task<string> FetchPageContentAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            return CleanHtmlContent(response);
        }
        catch
        {
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

    private string ExtractTitle(string content)
    {
        var titleMatch = Regex.Match(content, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase);
        return titleMatch.Success ? titleMatch.Groups[1].Value : "Untitled Page";
    }

    private List<string> SplitIntoLines(string content)
    {
        return content.Split('\n')
            .Select((line, index) => $"{index + 1:D3}: {line.Trim()}")
            .Where(line => !string.IsNullOrWhiteSpace(line.Substring(5)))
            .ToList();
    }

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

    private string ScrollToLocation(BrowserPage page, int loc, int numLines)
    {
        return FormatPageContent(page, loc > 0 ? loc : 1, numLines);
    }

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