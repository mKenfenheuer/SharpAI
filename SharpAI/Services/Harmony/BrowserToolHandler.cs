using System.Text.Json;
using SharpAI.Models.Browser;
using SharpAI.Services.Browser;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Handles browser tool function calls by routing them to the appropriate BrowserService methods.
/// Supports search, open, and find operations with markdown response formats.
/// </summary>
public class BrowserToolHandler
{
    private readonly BrowserService _browserService;
    private readonly ILogger<BrowserToolHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the BrowserToolHandler.
    /// </summary>
    /// <param name="browserService">Browser service for performing web operations</param>
    /// <param name="logger">Logger for tool handler operations</param>
    public BrowserToolHandler(BrowserService browserService, ILogger<BrowserToolHandler> logger)
    {
        _browserService = browserService;
        _logger = logger;
    }

    /// <summary>
    /// Handles browser tool function calls by routing to appropriate service methods.
    /// </summary>
    /// <param name="functionName">Name of the browser function to execute</param>
    /// <param name="arguments">JSON arguments for the function</param>
    /// <returns>Function execution result or error message</returns>
    public async Task<string> HandleBrowserToolCall(string functionName, string arguments)
    {
        _logger.LogInformation("Handling browser tool call: {FunctionName}", functionName);

        try
        {
            return functionName switch
            {
                "search" => await HandleSearchAsync(arguments),
                "open" => await HandleOpenAsync(arguments),
                "find" => await HandleFindAsync(arguments),
                _ => $"Error: Unknown browser function '{functionName}'"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling browser tool call: {FunctionName}", functionName);
            return $"Error: Failed to execute browser function '{functionName}': {ex.Message}";
        }
    }

    /// <summary>
    /// Handles search function calls.
    /// </summary>
    /// <param name="arguments">JSON arguments for search</param>
    /// <returns>Markdown-formatted search results</returns>
    private async Task<string> HandleSearchAsync(string arguments)
    {
        var args = JsonSerializer.Deserialize<BrowserSearchArgs>(arguments);
        if (args == null)
        {
            _logger.LogWarning("Invalid search arguments provided");
            return "Error: Invalid search arguments";
        }

        return await _browserService.SearchAsync(args);
    }

    /// <summary>
    /// Handles page open function calls.
    /// </summary>
    /// <param name="arguments">JSON arguments for opening a page</param>
    /// <returns>Markdown-formatted page content</returns>
    private async Task<string> HandleOpenAsync(string arguments)
    {
        var args = JsonSerializer.Deserialize<BrowserOpenArgs>(arguments);
        if (args == null)
        {
            _logger.LogWarning("Invalid open arguments provided");
            return "Error: Invalid open arguments";
        }

        return await _browserService.OpenAsync(args);
    }

    /// <summary>
    /// Handles find function calls.
    /// </summary>
    /// <param name="arguments">JSON arguments for finding text</param>
    /// <returns>Find results</returns>
    private async Task<string> HandleFindAsync(string arguments)
    {
        var args = JsonSerializer.Deserialize<BrowserFindArgs>(arguments);
        if (args == null)
        {
            _logger.LogWarning("Invalid find arguments provided");
            return "Error: Invalid find arguments";
        }

        return await _browserService.FindAsync(args);
    }

    /// <summary>
    /// Determines if a recipient string indicates a browser tool call.
    /// </summary>
    /// <param name="recipient">Recipient string to check</param>
    /// <returns>True if this is a browser tool call</returns>
    public static bool IsBrowserToolCall(string recipient)
    {
        return recipient.StartsWith("browser.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the browser function name from a recipient string.
    /// </summary>
    /// <param name="recipient">Recipient string</param>
    /// <returns>Browser function name or empty string</returns>
    public static string ExtractBrowserFunction(string recipient)
    {
        if (recipient.StartsWith("browser.", StringComparison.OrdinalIgnoreCase))
        {
            return recipient.Substring("browser.".Length);
        }
        return "";
    }
}