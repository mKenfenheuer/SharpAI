using System.Text.Json;
using SharpAI.Models.Browser;
using SharpAI.Services.Browser;

namespace SharpAI.Services.Harmony;

public class BrowserToolHandler
{
    private readonly BrowserService _browserService;
    private readonly ILogger<BrowserToolHandler> _logger;

    public BrowserToolHandler(BrowserService browserService, ILogger<BrowserToolHandler> logger)
    {
        _browserService = browserService;
        _logger = logger;
    }

    public async Task<string> HandleBrowserToolCall(string functionName, string arguments)
    {
        _logger.LogInformation("Handling browser tool call: {FunctionName}", functionName);

        try
        {
            return functionName switch
            {
                "search" => await HandleSearchAsync(arguments),
                "searchjson" => await HandleSearchJsonAsync(arguments),
                "open" => await HandleOpenAsync(arguments),
                "openjson" => await HandleOpenJsonAsync(arguments),
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

    private async Task<string> HandleSearchAsync(string arguments)
    {
        var args = JsonSerializer.Deserialize<BrowserSearchArgs>(arguments);
        if (args == null)
        {
            return "Error: Invalid search arguments";
        }

        return await _browserService.SearchAsync(args);
    }

    private async Task<string> HandleSearchJsonAsync(string arguments)
    {
        var args = JsonSerializer.Deserialize<BrowserSearchArgs>(arguments);
        if (args == null)
        {
            return JsonSerializer.Serialize(new { error = "Invalid search arguments" });
        }

        return await _browserService.SearchJsonAsync(args);
    }

    private async Task<string> HandleOpenAsync(string arguments)
    {
        var args = JsonSerializer.Deserialize<BrowserOpenArgs>(arguments);
        if (args == null)
        {
            return "Error: Invalid open arguments";
        }

        return await _browserService.OpenAsync(args);
    }

    private async Task<string> HandleOpenJsonAsync(string arguments)
    {
        var args = JsonSerializer.Deserialize<BrowserOpenArgs>(arguments);
        if (args == null)
        {
            return JsonSerializer.Serialize(new { error = "Invalid open arguments" });
        }

        return await _browserService.OpenJsonAsync(args);
    }

    private async Task<string> HandleFindAsync(string arguments)
    {
        var args = JsonSerializer.Deserialize<BrowserFindArgs>(arguments);
        if (args == null)
        {
            return "Error: Invalid find arguments";
        }

        return await _browserService.FindAsync(args);
    }

    public static bool IsBrowserToolCall(string recipient)
    {
        return recipient.StartsWith("browser.", StringComparison.OrdinalIgnoreCase);
    }

    public static string ExtractBrowserFunction(string recipient)
    {
        if (recipient.StartsWith("browser.", StringComparison.OrdinalIgnoreCase))
        {
            return recipient.Substring("browser.".Length);
        }
        return "";
    }
}