using LLama;
using LLama.Common;
using LLama.Sampling;
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
    private readonly RequestLogger _requestLogger;

    public StreamingInferenceEngine(
        ILogger<StreamingInferenceEngine> logger,
        HarmonyFormatter formatter,
        HarmonyParser parser,
        RequestLogger requestLogger)
    {
        _logger = logger;
        _formatter = formatter;
        _parser = parser;
        _requestLogger = requestLogger;
    }

    /// <summary>
    /// Generates a streaming response with browser tool integration.
    /// Uses only Harmony library parsing methods.
    /// </summary>
    public async IAsyncEnumerable<ChatCompletionResponse> GenerateStreamAsync(
        List<ChatMessage> messages,
        ChatCompletionRequest request,
        ModelInstance modelInstance,
        string requestId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[{RequestId}] Starting streaming inference for model: {Model} with {MessageCount} messages",
            requestId, request.Model, messages.Count);

        _parser.ResetParserState();

        var conversation = _formatter.ConvertToHarmonyConversation(
            messages,
            request.ReasoningEffort ?? ReasoningEffortLevel.Low,
            request.Tools,
            request.ResponseFormat);

        // Apply harmony format to the prompt
        var formattedPrompt = _formatter.FormatHarmonyConversation(conversation);
        
        // Log formatted prompt
        _logger.LogInformation("[{RequestId}] Formatted prompt for model", requestId);
        _logger.LogDebug("[{RequestId}] Formatted prompt: {Prompt}", requestId, formattedPrompt);
        await _requestLogger.LogFormattedPromptAsync(requestId, formattedPrompt, new
        {
            Model = request.Model,
            MessageCount = messages.Count,
            HasTools = request.Tools?.Any() ?? false,
            ToolCount = request.Tools?.Count ?? 0
        });

        // Pass the predefined next turn role to the parser
        var nextTurnToken = new[] { "<|start|>", "assistant" };
        var tokenCount = 0;

        foreach (var t in nextTurnToken)
        {
            _parser.ParseToken(t, false);
            tokenCount++;
        }

        // Create a fresh stateless executor to avoid state contamination from previous inferences
        var executor = new StatelessExecutor(modelInstance.Weights!, modelInstance.Context!.Params);

        var continueWithGrammar = false;
        var continueWithFunctionGrammar = false;

        Grammar? grammar = null;

        startInference:

        await foreach (var inferResponse in InferAsync(modelInstance, formattedPrompt, request, messages, grammar, requestId, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("[{RequestId}] Streaming inference cancelled by user", requestId);
                yield break;
            }

            if (inferResponse.ExitReason == InferExitReason.ParsingFailed)
            {
                _logger.LogError("[{RequestId}] Parsing failed during streaming inference. Aborting.", requestId);

                // We need to reset the parser state for continued inference
                _parser.ResetParserState();

                // Continue inference with tool results using recursive call
                await foreach (var continuedResult in GenerateStreamAsync(
                    messages, request, modelInstance, requestId, cancellationToken))
                {
                    yield return continuedResult;
                }

                // Return early as the continued inference handles the final result
                break;
            }
            else if (inferResponse.ExitReason == InferExitReason.SwitchToGrammar && (grammar == null || grammar.Root == "harmony-root"))
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


        if (continueWithGrammar && grammar == null)
        {
            if (request.ResponseFormat == null)
            {
                _logger.LogWarning("[{RequestId}] Grammar continuation requested but ResponseFormat is null. Skipping grammar validation.", requestId);
            }
            else
            {
                _logger.LogInformation("[{RequestId}] Continuing inference with grammar-based response format validation", requestId);

                grammar = BuildResponseFormatGrammar(request.ResponseFormat);

                formattedPrompt += _parser.RawResponse;

                goto startInference;
            }
        }

        if (continueWithFunctionGrammar && grammar == null)
        {
            _logger.LogInformation("[{RequestId}] Continuing inference with grammar-based tool argument format validation", requestId);
            var tools = conversation.SelectMany(m => m.Content)
                        .Where(c => c.Tools != null)
                        .SelectMany(c => c.Tools);
            var tool = tools.FirstOrDefault(t => t.Name == _parser.ToolCallRecipient);

            if (tool == null)
            {
                _logger.LogError("[{RequestId}] Tool '{ToolName}' not found in conversation. Cannot apply grammar validation.", requestId, _parser.ToolCallRecipient);
                // Continue without grammar validation
            }
            else
            {
                grammar = BuildResponseFormatGrammar(new ResponseFormat
                {
                    Type = ResponseFormatType.JsonSchema,
                    JsonSchema = tool.GetJsonSchema()
                });

                formattedPrompt += _parser.RawResponse;

                goto startInference;
            }
        }

        var completeMessages = _parser.GetResponse();
        
        // Log raw model response
        _logger.LogInformation("[{RequestId}] Model inference completed", requestId);
        _logger.LogDebug("[{RequestId}] Raw model response: {RawResponse}", requestId, _parser.RawResponse);
        await _requestLogger.LogModelResponseAsync(requestId, _parser.RawResponse, new
        {
            ReasoningTokens = _parser.ReasoningTokens,
            CompletionTokens = _parser.CompletionTokens,
            PromptTokens = LLamaUtilities.EstimateTokens(formattedPrompt),
            FinishReason = completeMessages.ToolCall != null ? "tool_calls" : "stop"
        });

        // Send final result
        yield return completeMessages.CreateChatCompletionResponse(
                        messages,
                        request,
                        CompletionObjectType.ChatCompletion,
                        completeMessages.ToolCall != null ? "tool_calls" : "stop",
                        _parser.ReasoningTokens,
                        LLamaUtilities.EstimateTokens(formattedPrompt),
                        _parser.CompletionTokens);
    }

    private async IAsyncEnumerable<InferResponse<ChatCompletionResponse>> InferAsync(ModelInstance modelInstance, string formattedPrompt, ChatCompletionRequest request, List<ChatMessage> messages, Grammar? grammar, string requestId, CancellationToken cancellationToken)
    {
        await foreach (var token in modelInstance.InferAsync(request, formattedPrompt, grammar))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("[{RequestId}] Streaming inference cancelled by user", requestId);
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

            if (_parser.IsInToolCallArguments && grammar == null)
            {
                yield return new InferResponse<ChatCompletionResponse>()
                {
                    ExitReason = InferExitReason.SwitchToGrammar
                };
                break;
            }

            if ((_parser.HasConstraint || request.ResponseFormat?.JsonSchema?.Strict == true) && _parser.IsInFinalContent && grammar == null && request.ResponseFormat != null)
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
                                        Name = tc.Function.Name?.StartsWith("functions.") == true ? tc.Function.Name.Substring("functions.".Length) : tc.Function.Name,
                                        Namespace = "functions",
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