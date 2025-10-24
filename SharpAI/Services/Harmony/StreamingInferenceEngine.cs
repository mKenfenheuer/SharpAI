using LLama;
using LLama.Common;
using SharpAI.Models;
using SharpAI.Services.Harmony;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpAI.Services.Harmony;

public class StreamingInferenceEngine
{
    private readonly ILogger<StreamingInferenceEngine> _logger;
    private readonly HarmonyFormatter _formatter;
    private readonly HarmonyParser _parser;

    public StreamingInferenceEngine(
        ILogger<StreamingInferenceEngine> logger,
        HarmonyFormatter formatter,
        HarmonyParser parser)
    {
        _logger = logger;
        _formatter = formatter;
        _parser = parser;
    }

    public async IAsyncEnumerable<StreamingInferenceResult> GenerateStreamAsync(
        List<ChatMessage> messages,
        ChatCompletionRequest request,
        ModelInstance modelInstance,
        InferenceParams inferenceParams,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting unified streaming inference for model: {Model}", request.Model);

        // Check if tools are available and should be used
        var shouldUseFunctions = request.Tools?.Any() == true && 
                                 (request.ToolChoice != ToolChoice.None);

        // Apply harmony format to the prompt
        var formattedPrompt = _formatter.FormatPrompt(
            messages, 
            shouldUseFunctions, 
            request.ReasoningEffort ?? ReasoningEffortLevel.Medium, 
            request.Tools,
            request.ResponseFormat);

        _logger.LogDebug("Formatted prompt: {Prompt}", formattedPrompt);

        // Initialize streaming state
        var streamState = new HarmonyStreamState();
        var responseBuilder = new StringBuilder();

        // Generate response using the stateless executor
        await foreach (var token in modelInstance.Executor!.InferAsync(formattedPrompt, inferenceParams))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Streaming inference cancelled");
                break;
            }

            responseBuilder.Append(token);
            var currentResponse = responseBuilder.ToString();

            // Parse incremental response to get new content
            var delta = _parser.ParseIncrementalResponse(streamState, token);

            if (delta != null)
            {
                yield return new StreamingInferenceResult
                {
                    Delta = delta,
                    IsComplete = false,
                    RawResponse = currentResponse
                };
            }

            // Check if we hit a completion stop token
            if (currentResponse.Contains("<|call|>") || currentResponse.Contains("<|return|>"))
            {
                _logger.LogDebug("Hit completion stop token, ending inference");
                break;
            }
        }

        // Parse final complete response
        var completeResponse = responseBuilder.ToString().Trim();
        _logger.LogDebug("Complete raw response: {Response}", completeResponse);

        var parsedResponse = _parser.ParseCompleteResponse(completeResponse);

        // Apply response format validation if needed
        if (request.ResponseFormat != null)
        {
            parsedResponse.Content = ProcessResponseFormat(parsedResponse.Content, request.ResponseFormat);
        }

        // Send final result
        yield return new StreamingInferenceResult
        {
            ParsedResponse = parsedResponse,
            IsComplete = true,
            RawResponse = completeResponse
        };
    }

    private string ProcessResponseFormat(string content, ResponseFormat responseFormat)
    {
        if (responseFormat.Type == ResponseFormatType.JsonObject)
        {
            try
            {
                // Validate that the response is valid JSON
                var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                return content;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Response format validation failed for JSON object");
                // Return a valid JSON object with the content
                return System.Text.Json.JsonSerializer.Serialize(new { content = content });
            }
        }
        else if (responseFormat.Type == ResponseFormatType.JsonSchema && responseFormat.JsonSchema?.Schema != null)
        {
            // For JSON schema validation, we would need a JSON schema validator library
            // For now, just validate it's valid JSON
            try
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                return content;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Response format validation failed for JSON schema");
                return System.Text.Json.JsonSerializer.Serialize(new { content = content });
            }
        }

        return content;
    }
}

public class StreamingInferenceResult
{
    public HarmonyStreamDelta? Delta { get; set; }
    public ParsedResponse? ParsedResponse { get; set; }
    public bool IsComplete { get; set; }
    public string RawResponse { get; set; } = string.Empty;
}