using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAI.Services;

/// <summary>
/// Service for logging requests and responses to a JSONL file for tracking and debugging purposes.
/// Each log entry is a JSON object on a single line with request ID tracking.
/// </summary>
public class RequestLogger
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    public RequestLogger(IConfiguration configuration)
    {
        var logDirectory = configuration["Logging:RequestLogDirectory"] ?? "Data";
        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, "requests.jsonl");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Logs a request event to the JSONL file.
    /// </summary>
    /// <param name="requestId">Unique identifier for the request</param>
    /// <param name="eventType">Type of event (e.g., "request_received", "prompt_formatted", "model_response", "final_response")</param>
    /// <param name="data">Event data to log</param>
    public async Task LogEventAsync(string requestId, string eventType, object data)
    {
        var logEntry = new
        {
            Timestamp = DateTimeOffset.UtcNow,
            RequestId = requestId,
            EventType = eventType,
            Data = data
        };

        await WriteLogEntryAsync(logEntry);
    }

    /// <summary>
    /// Logs the incoming request.
    /// </summary>
    public async Task LogRequestAsync(string requestId, object request)
    {
        await LogEventAsync(requestId, "request_received", request);
    }

    /// <summary>
    /// Logs the formatted prompt sent to the model.
    /// </summary>
    public async Task LogFormattedPromptAsync(string requestId, string prompt, object metadata = null)
    {
        await LogEventAsync(requestId, "prompt_formatted", new
        {
            Prompt = prompt,
            Metadata = metadata
        });
    }

    /// <summary>
    /// Logs the raw response from the model.
    /// </summary>
    public async Task LogModelResponseAsync(string requestId, string rawResponse, object metadata = null)
    {
        await LogEventAsync(requestId, "model_response", new
        {
            RawResponse = rawResponse,
            Metadata = metadata
        });
    }

    /// <summary>
    /// Logs the final response sent to the client.
    /// </summary>
    public async Task LogFinalResponseAsync(string requestId, object response)
    {
        await LogEventAsync(requestId, "final_response", response);
    }

    /// <summary>
    /// Logs an error event.
    /// </summary>
    public async Task LogErrorAsync(string requestId, string errorMessage, string stackTrace = null)
    {
        await LogEventAsync(requestId, "error", new
        {
            ErrorMessage = errorMessage,
            StackTrace = stackTrace
        });
    }

    /// <summary>
    /// Writes a log entry to the JSONL file thread-safely.
    /// </summary>
    private async Task WriteLogEntryAsync(object logEntry)
    {
        await _semaphore.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(logEntry, _jsonOptions);
            await File.AppendAllTextAsync(_logFilePath, json + Environment.NewLine);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
