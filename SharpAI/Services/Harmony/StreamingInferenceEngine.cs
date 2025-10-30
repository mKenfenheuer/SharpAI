using LLama;
using LLama.Common;
using SharpAI.Models;
using SharpAI.Services.Harmony;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Handles streaming inference with browser tool integration using the Harmony format.
/// This version uses only the Harmony library methods without any fallback parsing.
/// </summary>
public class StreamingInferenceEngine
{
    private readonly ILogger<StreamingInferenceEngine> _logger;
    private readonly HarmonyFormatter _formatter;
    private readonly HarmonyParser _parser;
    private readonly BrowserToolHandler _browserHandler;

    public StreamingInferenceEngine(
        ILogger<StreamingInferenceEngine> logger,
        HarmonyFormatter formatter,
        HarmonyParser parser,
        BrowserToolHandler browserHandler)
    {
        _logger = logger;
        _formatter = formatter;
        _parser = parser;
        _browserHandler = browserHandler;
    }

    /// <summary>
    /// Generates a streaming response with browser tool integration.
    /// Uses only Harmony library parsing methods.
    /// </summary>
    public async IAsyncEnumerable<ChatCompletionResponse> GenerateStreamAsync(
        List<ChatMessage> messages,
        ChatCompletionRequest request,
        ModelInstance modelInstance,
        InferenceParams inferenceParams,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting streaming inference for model: {Model} with {MessageCount} messages",
            request.Model, messages.Count);



        // Check if tools are available and should be used
        if (request.ToolChoice == ToolChoice.None)
        {
            request.Tools = null;
        }

        var conversation = _formatter.ConvertToHarmonyConversation(
            messages,
            request.ReasoningEffort ?? ReasoningEffortLevel.Low,
            request.Tools,
            request.ResponseFormat);

        // Apply harmony format to the prompt
        var formattedPrompt = _formatter.FormatHarmonyConversation(conversation);

        var rawResponseBuilder = new StringBuilder();
        // Generate response using the stateless executor

        // Pass the predefined next turn role to the parser
        var nextTurnToken = new[] { "<|start|>", "assistant" };
        var tokenCount = 0;

        foreach (var t in nextTurnToken)
        {
            _parser.ParseToken(t);
            rawResponseBuilder.Append(t);
            tokenCount++;
        }

        // Create a fresh stateless executor to avoid state contamination from previous inferences
        var executor = new StatelessExecutor(modelInstance.Weights!, modelInstance.Context!.Params);

        var continueWithGrammar = false;
        var continueWithFunctionGrammar = false;
        var hasContinued = false;


    startInference:

        await foreach (var inferResponse in InferAsync(formattedPrompt, request, messages, executor, inferenceParams, continueWithFunctionGrammar || continueWithGrammar, cancellationToken))
        {
            if (!hasContinued && (continueWithGrammar || continueWithFunctionGrammar))
            {
                hasContinued = true;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Streaming inference cancelled by user");
                break;
            }

            if (inferResponse.ExitReason == InferExitReason.ParsingFailed)
            {
                _logger.LogError("Parsing failed during streaming inference. Aborting.");

                // We need to reset the parser state for continued inference
                _parser.ResetParserState();

                // Continue inference with tool results using recursive call
                await foreach (var continuedResult in GenerateStreamAsync(
                    messages, request, modelInstance, inferenceParams, cancellationToken))
                {
                    yield return continuedResult;
                }

                // Return early as the continued inference handles the final result
                yield break;
            }
            else if (inferResponse.ExitReason == InferExitReason.SwitchToGrammar)
            {
                if (_parser.IsInToolCallArguments)
                {
                    continueWithFunctionGrammar = true;
                }
                else
                {
                    continueWithGrammar = true;
                }
                break; // Exit the loop to switch to grammar-based inference
            }
            else
            {
                // Yield the chunked response
                yield return inferResponse.Chunk;
            }
        }


        if (continueWithGrammar && !hasContinued)
        {
            _logger.LogInformation("Continuing inference with grammar-based response format validation");

            inferenceParams.SamplingPipeline = LLamaUtilities.BuildSamplingPipeline(
                request,
                BuildResponseFormatGrammar(request.ResponseFormat!));

            // Create a fresh stateless executor to avoid state contamination from previous inferences
            executor = new StatelessExecutor(modelInstance.Weights!, modelInstance.Context!.Params);

            formattedPrompt += rawResponseBuilder.ToString();

            goto startInference;
        }

        if (continueWithFunctionGrammar && !hasContinued)
        {
            _logger.LogInformation("Continuing inference with grammar-based tool argument format validation");
            var tools = conversation.SelectMany(m => m.Content)
                        .Where(c => c.Tools != null)
                        .SelectMany(c => c.Tools);
            var tool = tools.FirstOrDefault(t => t.Name == _parser.ToolCallRecipient);

            inferenceParams.SamplingPipeline = LLamaUtilities.BuildSamplingPipeline(
                request,
                BuildResponseFormatGrammar(new ResponseFormat
                {
                    Type = ResponseFormatType.JsonSchema,
                    JsonSchema = tool.GetJsonSchema()
                }));


            // Create a fresh stateless executor to avoid state contamination from previous inferences
            executor = new StatelessExecutor(modelInstance.Weights!, modelInstance.Context!.Params);

            formattedPrompt += rawResponseBuilder.ToString();

            goto startInference;
        }

        Console.WriteLine($"Prompt: {formattedPrompt}");
        Console.WriteLine($"Raw Response: {_parser.RawResponse}");

        var completeMessages = _parser.GetResponse();
        var rawResponse = rawResponseBuilder.ToString();

        // Handle tool calls internally
        if (completeMessages.ToolCall != null)
        {
            ChatMessage? toolResultMessage = null;

            if (BrowserToolHandler.IsBrowserToolCall(completeMessages.ToolCall.Recipient))
            {
                _logger.LogInformation("Executing browser tool call");
                toolResultMessage = await HandleBrowserToolCallAsync(completeMessages, messages.Where(m => m.Role == ChatRole.Tool && m.Name.StartsWith("browser.")));
            }

            if (toolResultMessage != null)
            {
                var continuedMessages = new List<ChatMessage>(messages);
                continuedMessages.AddRange(completeMessages.Messages.ToChatMessages());
                continuedMessages.Add(toolResultMessage);

                _logger.LogInformation("Continuing inference with tool results");

                // We need to reset the parser state for continued inference
                _parser.ResetParserState();

                // We need to reset executor state as well


                inferenceParams.SamplingPipeline = LLamaUtilities.BuildSamplingPipeline(
                    request);

                // Create a fresh stateless executor to avoid state contamination from previous inferences
                executor = new StatelessExecutor(modelInstance.Weights!, modelInstance.Context!.Params);

                // Continue inference with tool results using recursive call
                await foreach (var continuedResult in GenerateStreamAsync(
                    continuedMessages, request, modelInstance, inferenceParams, cancellationToken))
                {
                    yield return continuedResult;
                }

                // Return early as the continued inference handles the final result
                yield break;
            }
        }

        // Send final result
        yield return completeMessages.CreateChatCompletionResponse(
                        messages,
                        request,
                        CompletionObjectType.ChatCompletion,
                        "stop",
                        _parser.ReasoningTokens,
                        LLamaUtilities.EstimateTokens(formattedPrompt),
                        _parser.CompletionTokens);
    }

    private async IAsyncEnumerable<InferResponse<ChatCompletionResponse>> InferAsync(string formattedPrompt, ChatCompletionRequest request, List<ChatMessage> messages, StatelessExecutor executor, InferenceParams inferenceParams, bool ignoreGrammarChange, CancellationToken cancellationToken)
    {
        await foreach (var token in executor.InferAsync(formattedPrompt, inferenceParams))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Streaming inference cancelled by user");
                break;
            }
            var delta = _parser.ParseToken(token);

            if (_parser.IsComplete && _parser.IsFailed)
            {
                yield return new InferResponse<ChatCompletionResponse>()
                {
                    ExitReason = InferExitReason.ParsingFailed
                };
                break;
            }

            if (_parser.IsInToolCallArguments && !ignoreGrammarChange)
            {
                yield return new InferResponse<ChatCompletionResponse>()
                {
                    ExitReason = InferExitReason.SwitchToGrammar
                };
                break;
            }

            if (_parser.HasConstraint && _parser.IsInFinalContent && !ignoreGrammarChange)
            {
                yield return new InferResponse<ChatCompletionResponse>()
                {
                    ExitReason = InferExitReason.SwitchToGrammar
                };
                break;
            }

            if (delta != null && delta.HasNewContent)
            {
                // Create streaming response from delta
                var streamingResponse = delta.CreateStreamingResponseFromDelta(
                    messages,
                    request,
                    null, // No finish reason for intermediate chunks
                    false);

                // Convert streaming response to ChatCompletionResponse for compatibility
                yield return new InferResponse<ChatCompletionResponse>()
                {
                    Chunk = new ChatCompletionResponse
                    {
                        Id = streamingResponse.Id,
                        Object = streamingResponse.Object,
                        Created = streamingResponse.Created,
                        Model = streamingResponse.Model,
                        Choices = streamingResponse.Choices?.Select(c => new ChatCompletionChoice
                        {
                            Index = c.Index,
                            Message = new ChatMessage
                            {
                                Role = c.Delta?.Role ?? ChatRole.Assistant,
                                Content = c.Delta?.Content,
                                Reasoning = c.Delta?.Reasoning,
                                Commentary = c.Delta?.Commentary,
                                ToolCalls = c.Delta?.ToolCalls?.Select(tc => new ToolCall
                                {
                                    CallId = tc.Id,
                                    Type = Enum.TryParse<ToolType>(tc.Type, true, out var type) ? type : ToolType.Function,
                                    Function = tc.Function != null ? new FunctionCall
                                    {
                                        Name = ParseFunctionName(tc.Function.Name),
                                        Namespace = ParseFunctionNamespace(tc.Function.Name),
                                        Arguments = tc.Function.Arguments ?? ""
                                    } : null
                                }).ToList()
                            },
                            FinishReason = c.FinishReason
                        }).ToList() ?? new List<ChatCompletionChoice>(),
                        Usage = streamingResponse.Usage
                    }
                };
            }

            if (_parser.IsComplete)
            {
                break;
            }
        }
    }

    private LLama.Sampling.Grammar? BuildResponseFormatGrammar(ResponseFormat responseFormat)
    {
        // Check if we need JSON grammar for structured output
        // Use schema-based grammar generation when available
        LLama.Sampling.Grammar? jsonGrammar = null;
        if (responseFormat.Type == ResponseFormatType.JsonObject ||
            responseFormat.Type == ResponseFormatType.JsonSchema)
        {
            try
            {
                var grammarText = ConditionalGrammarBuilder.BuildConditionalGrammar(responseFormat);
                if (!string.IsNullOrEmpty(grammarText))
                {
                    jsonGrammar = new LLama.Sampling.Grammar(grammarText, "root");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building response format grammar");
                // Proceed without grammar if there's an error
            }
        }
        return jsonGrammar;
    }

    /// <summary>
    /// Processes response format validation for JSON responses.
    /// </summary>
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

    /// <summary>
    /// Executes browser tool calls and returns the results as ChatMessage objects.
    /// Browser tool calls are handled internally and their results are prepared for continued inference.
    /// </summary>
    private async Task<ChatMessage> HandleBrowserToolCallAsync(HarmonyResponse harmonyResponse, IEnumerable<ChatMessage> lastToolMessages)
    {
        try
        {
            // Generate an ID for this tool call
            var toolCallId = harmonyResponse.ToolCall.Id;

            var functionName = BrowserToolHandler.ExtractBrowserFunction(harmonyResponse.ToolCall.Recipient);
            var result = await _browserHandler.HandleBrowserToolCall(functionName, harmonyResponse.ToolCall.Arguments, lastToolMessages);

            // Create a tool result message
            var toolResultMessage = new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = result ?? "Tool execution completed successfully",
                ToolCallId = toolCallId,
                Name = harmonyResponse.ToolCall.Recipient
            };

            _logger.LogDebug("Browser tool executed successfully: {Function}", functionName);

            return toolResultMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing browser tool call: {Recipient}", harmonyResponse.ToolCall.Recipient);

            // Generate an ID for error case too
            var toolCallId = Guid.NewGuid().ToString();

            // Create error result message
            var errorMessage = new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = $"Error executing browser tool: {ex.Message}",
                ToolCallId = toolCallId,
                Name = harmonyResponse.ToolCall.Recipient
            };

            return errorMessage;
        }
    }

    /// <summary>
    /// Extracts user-readable content from harmony response messages
    /// </summary>
    private string ExtractUserReadableContent(HarmonyResponse harmonyResponse)
    {
        var content = new StringBuilder();

        // Extract content from final channel if available
        var channels = harmonyResponse.Messages.Where(m => m.Role == HarmonyRole.Assistant)
            .SelectMany(m => m.Content.OfType<HarmonyAssistantContent>());

        var finalChannel = channels.FirstOrDefault(c => c.Channel == HarmonyChannel.Final);

        if (finalChannel != null)
        {
            content.Append(finalChannel.Content);
        }
        else if (channels.Any())
        {
            // If no final channel, use the last channel's content
            content.Append(channels.Last().Content);
        }

        return content.ToString().Trim();
    }

    /// <summary>
    /// Parses the function name from a potentially namespaced function name.
    /// </summary>
    /// <param name="fullName">The full function name, potentially in format "namespace.function"</param>
    /// <returns>The function name without namespace</returns>
    private static string ParseFunctionName(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return "";

        // Only extract namespace if it starts with browser. or python.
        if (fullName.StartsWith("browser."))
        {
            return fullName.Substring("browser.".Length);
        }
        else if (fullName.StartsWith("python."))
        {
            return fullName.Substring("python.".Length);
        }

        // Otherwise, the entire name is the function name
        return fullName;
    }

    /// <summary>
    /// Parses the namespace from a potentially namespaced function name.
    /// </summary>
    /// <param name="fullName">The full function name, potentially in format "namespace.function"</param>
    /// <returns>The namespace, defaulting to "functions" if not specified or invalid</returns>
    private static string ParseFunctionNamespace(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return "functions";

        // Only extract namespace if it starts with browser. or python.
        if (fullName.StartsWith("browser."))
        {
            return "browser";
        }
        else if (fullName.StartsWith("python."))
        {
            return "python";
        }

        // Otherwise, assume functions namespace
        return "functions";
    }
}

internal enum InferExitReason
{
    None,
    ParsingFailed,
    SwitchToGrammar,
    Completed
}

internal class InferResponse<T>
{
    public InferExitReason ExitReason { get; internal set; } = InferExitReason.None;
    public ChatCompletionResponse Chunk { get; internal set; }
}

/// <summary>
/// Represents a single result from the streaming inference process.
/// Contains either incremental delta updates or the final parsed response.
/// </summary>
public class StreamingInferenceResult
{
    public ChatCompletionResponse Response { get; set; }
    public bool IsComplete { get; set; }
    public string RawResponse { get; set; } = string.Empty;
    public int TokenCount { get; set; }
}