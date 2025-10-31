using LLama.Common;
using LLama.Sampling;
using SharpAI.Models;
using System.Text.Json;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Utility functions for LLamaSharp integration and token management.
/// Provides token estimation, tool call conversion, and inference parameter configuration.
/// </summary>
public static class LLamaUtilities
{
    /// <summary>
    /// Estimates the number of tokens in a text string.
    /// </summary>
    /// <param name="text">Text to estimate tokens for</param>
    /// <returns>Estimated token count</returns>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        // Simple token estimation: roughly 4 characters per token
        // This is a rough approximation for GPT-style tokenizers
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Estimates reasoning tokens based on content and effort level.
    /// </summary>
    /// <param name="reasoning">Reasoning content</param>
    /// <param name="level">Reasoning effort level</param>
    /// <returns>Adjusted token count for reasoning</returns>
    public static int EstimateReasoningTokens(string reasoning, ReasoningEffortLevel level)
    {
        if (string.IsNullOrEmpty(reasoning)) return 0;

        var baseTokens = EstimateTokens(reasoning);

        // Adjust based on reasoning effort level
        return level switch
        {
            ReasoningEffortLevel.Low => (int)(baseTokens * 0.7),
            ReasoningEffortLevel.Medium => baseTokens,
            ReasoningEffortLevel.High => (int)(baseTokens * 1.3),
            _ => baseTokens
        };
    }

    /// <summary>
    /// Converts Harmony tool calls to OpenAI-compatible format.
    /// </summary>
    /// <param name="harmonyToolCalls">Harmony tool calls to convert</param>
    /// <returns>List of OpenAI-compatible tool calls</returns>
    public static List<ToolCall> ConvertHarmonyToolCalls(List<HarmonyToolCall> harmonyToolCalls)
    {
        var toolCalls = new List<ToolCall>();

        foreach (var harmonyCall in harmonyToolCalls)
        {
            // Generate proper call ID according to OpenAI format
            var callId = $"call_{Guid.NewGuid():N}";

            var toolCall = new ToolCall
            {
                CallId = callId, // For streaming compatibility
                Type = ToolType.Function,
                Function = new FunctionCall
                {
                    Name = harmonyCall.ToolName,
                    Arguments = !string.IsNullOrEmpty(harmonyCall.Arguments)
                        ? harmonyCall.Arguments
                        : "{}"
                }
            };

            toolCalls.Add(toolCall);
        }

        return toolCalls;
    }

    /// <summary>
    /// Creates inference parameters for LLamaSharp from a chat completion request.
    /// </summary>
    /// <param name="request">Chat completion request containing generation parameters</param>
    /// <param name="modelInstance">Model instance for context</param>
    /// <returns>Configured inference parameters for LLamaSharp</returns>
    public static InferenceParams CreateInferenceParams(ChatCompletionRequest request, ModelInstance modelInstance, Grammar? grammar = null)
    {
        // Build stop sequences list
        var antiPrompts = new List<string> { "<|return|>", "<|call|>" };
        //var antiPrompts = new List<string> { };

        // Handle custom stop sequences
        if (request.Stop != null)
        {
            if (request.Stop is string stopString)
            {
                antiPrompts.Add(stopString);
            }
            else if (request.Stop is string[] stopArray)
            {
                antiPrompts.AddRange(stopArray);
            }
            else if (request.Stop is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in jsonElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var stopSeq = element.GetString();
                        if (!string.IsNullOrEmpty(stopSeq))
                        {
                            antiPrompts.Add(stopSeq);
                        }
                    }
                }
            }
        }

        var inferenceParams = new InferenceParams
        {
            MaxTokens = request.MaxTokens ?? int.MaxValue,
            AntiPrompts = new List<string>(antiPrompts),
            SamplingPipeline = BuildSamplingPipeline(request, grammar),
            DecodeSpecialTokens = true
        };

        return inferenceParams;
    }

    public static DefaultSamplingPipeline BuildSamplingPipeline(ChatCompletionRequest request, Grammar? grammar = null)
    {
        var repeatPenalty = 1.1f;
        if (request.FrequencyPenalty.HasValue)
        {
            repeatPenalty = (float)(1.0 + Math.Abs(request.FrequencyPenalty.Value));
        }
        else if (request.PresencePenalty.HasValue)
        {
            repeatPenalty = (float)(1.0 + Math.Abs(request.PresencePenalty.Value));
        }

        DefaultSamplingPipeline samplingPipeline;

        if (grammar != null)
        {
            samplingPipeline = new DefaultSamplingPipeline()
            {
                Temperature = (float)(request.Temperature ?? 0.7),
                TopP = (float)(request.TopP ?? 1.0),
                RepeatPenalty = repeatPenalty,
                PreventEOS = false,
                Grammar = grammar
            };
        }
        else
        {
            samplingPipeline = new DefaultSamplingPipeline()
            {
                Temperature = (float)(request.Temperature ?? 0.7),
                TopP = (float)(request.TopP ?? 1.0),
                RepeatPenalty = repeatPenalty,
                PreventEOS = false
            };
        }   
        return samplingPipeline;
    }

    public static List<ChatCompletionChoice> ToChatCompletionChoices(
        this IEnumerable<ChatMessage> messages, string finishReason = "stop")
    {
        var assistantMessage = messages.LastOrDefault(m => m.Role == ChatRole.Assistant);

        var choices = new List<ChatCompletionChoice>
        {
            new ChatCompletionChoice
            {
                Index = 0,
                Message = assistantMessage,
                FinishReason = finishReason
            }
        };

        return choices;
    }

    public static ChatCompletionResponse CreateChatCompletionResponse(
        this HarmonyResponse harmonyResponse,
        IEnumerable<ChatMessage> promptMessages,
        ChatCompletionRequest request,
        CompletionObjectType completionObjectType = CompletionObjectType.ChatCompletion,
        string finishReason = "stop",
        int reasoningTokens = 0,
        int promptTokens = 0,
        int completionTokens = 0)
    {
        var completeMessages = promptMessages.ToList();
        completeMessages.AddRange(harmonyResponse.Messages.ToChatMessages());

        var response = new ChatCompletionResponse
        {
            Model = request.Model,
            Id = CreateCompletionId(),
            Object = completionObjectType,
            Created = GetCurrentUnixTimestamp(),
            Choices = completeMessages.ToChatCompletionChoices(finishReason),
            Usage = new TokenUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = reasoningTokens + promptTokens + completionTokens,
                ReasoningTokens = reasoningTokens
            }
        };

        return response;
    }

    /// <summary>
    /// Creates a streaming chat completion response with delta structure.
    /// </summary>
    /// <param name="harmonyResponse">Harmony response containing the partial content</param>
    /// <param name="promptMessages">Original prompt messages</param>
    /// <param name="request">Chat completion request</param>
    /// <param name="finishReason">Finish reason for the chunk (null for intermediate chunks)</param>
    /// <param name="isFirstChunk">Whether this is the first chunk in the stream</param>
    /// <returns>Streaming chat completion response with delta structure</returns>
    public static ChatCompletionStreamingResponse CreateStreamingChatCompletionResponse(
        this HarmonyResponse harmonyResponse,
        IEnumerable<ChatMessage> promptMessages,
        ChatCompletionRequest request,
        string? finishReason = null,
        bool isFirstChunk = false)
    {
        var delta = new ChatCompletionDelta();
        var choices = new List<ChatCompletionStreamingChoice>();

        // For first chunk, include the role
        if (isFirstChunk)
        {
            delta.Role = ChatRole.Assistant;
        }

        // Process the messages from harmony response
        var harmonyMessages = harmonyResponse.Messages.ToChatMessages();
        var assistantMessage = harmonyMessages.LastOrDefault(m => m.Role == ChatRole.Assistant);

        if (assistantMessage != null)
        {
            // For content streaming
            if (!string.IsNullOrEmpty(assistantMessage.Content))
            {
                delta.Content = assistantMessage.Content;
            }

            // For tool calls streaming
            if (assistantMessage.ToolCalls?.Any() == true)
            {
                delta.ToolCalls = new List<ToolCallDelta>();
                for (int i = 0; i < assistantMessage.ToolCalls.Count; i++)
                {
                    var toolCall = assistantMessage.ToolCalls[i];
                    var toolCallDelta = new ToolCallDelta
                    {
                        Index = i
                    };

                    if (isFirstChunk)
                    {
                        toolCallDelta.Id = toolCall.CallId;
                        toolCallDelta.Type = toolCall.Type.ToString().ToLower();
                        toolCallDelta.Function = new FunctionCallDelta
                        {
                            Name = toolCall.Function?.Name
                        };
                    }

                    if (toolCall.Function?.Arguments != null)
                    {
                        toolCallDelta.Function ??= new FunctionCallDelta();
                        toolCallDelta.Function.Arguments = toolCall.Function.Arguments;
                    }

                    delta.ToolCalls.Add(toolCallDelta);
                }
            }
        }

        var choice = new ChatCompletionStreamingChoice
        {
            Index = 0,
            Delta = delta,
            FinishReason = finishReason
        };

        choices.Add(choice);

        var response = new ChatCompletionStreamingResponse
        {
            Model = request.Model,
            Id = CreateCompletionId(),
            Object = CompletionObjectType.ChatCompletionChunk,
            Created = GetCurrentUnixTimestamp(),
            Choices = choices,
            Usage = finishReason != null ? new TokenUsage
            {
                PromptTokens = 0, // Can be calculated if needed
                CompletionTokens = 0, // Can be calculated if needed
                TotalTokens = 0
            } : null
        };

        return response;
    }

    public static ChatRole ToChatRole(this HarmonyRole role)
    {
        return role switch
        {
            HarmonyRole.User => ChatRole.User,
            HarmonyRole.Assistant => ChatRole.Assistant,
            HarmonyRole.System => ChatRole.System,
            HarmonyRole.Tool => ChatRole.Tool,
            _ => ChatRole.User,
        };
    }

    public static List<ChatMessage> ToChatMessages(this List<HarmonyMessage> harmonyMessages)
    {
        var chatMessages = new List<ChatMessage>();
        
        // Group assistant content by channel
        var channelContent = new Dictionary<string, ChatMessage>();
        
        foreach (var harmonyMessage in harmonyMessages)
        {
            foreach (var content in harmonyMessage.Content)
            {
                if (content is HarmonySystemContent)
                    continue;
                
                if (content is HarmonyDeveloperContent)
                {
                    chatMessages.Add(new ChatMessage
                    {
                        Role = ChatRole.System,
                        Content = content.StringContent,
                    });
                    continue;
                }

                var channelString = content.ChannelString ?? "final";
                
                // For assistant messages, we need to handle channels specially
                if (harmonyMessage.Role == HarmonyRole.Assistant)
                {
                    // Get or create chat message for this channel
                    if (!channelContent.TryGetValue(channelString, out var chatMessage))
                    {
                        chatMessage = new ChatMessage
                        {
                            Role = ChatRole.Assistant,
                            Name = channelString,
                        };
                        channelContent[channelString] = chatMessage;
                    }

                    // Map content based on channel
                    switch (channelString.ToLowerInvariant())
                    {
                        case "final":
                            chatMessage.Content = content.StringContent;
                            break;
                        case "analysis":
                            chatMessage.Reasoning = content.StringContent;
                            break;
                        case "commentary":
                            chatMessage.Commentary = content.StringContent;
                            break;
                        default:
                            // Unknown channel, put in content
                            chatMessage.Content = content.StringContent;
                            break;
                    }

                    // Handle tool calls
                    if (content is HarmonyAssistantContent assistantContent && assistantContent.ToolCall != null)
                    {
                        var toolCall = assistantContent.ToolCall;
                        chatMessage.ToolCalls ??= new List<ToolCall>();
                        chatMessage.ToolCalls.Add(new ToolCall
                        {
                            CallId = toolCall.Id,
                            Type = ToolType.Function,
                            Function = new FunctionCall
                            {
                                Name = toolCall.ToolName,
                                Namespace = toolCall.Namespace,
                                Arguments = toolCall.Arguments ?? "{}"
                            }
                        });
                    }
                }
                else
                {
                    // For non-assistant messages, create standard chat message
                    var chatMessage = new ChatMessage
                    {
                        Role = harmonyMessage.Role.ToChatRole(),
                        Content = content.StringContent,
                        Name = channelString != "final" ? channelString : null,
                    };
                    chatMessages.Add(chatMessage);
                }
            }
        }
        
        // Add all channel-based assistant messages
        chatMessages.AddRange(channelContent.Values);
        
        return chatMessages;
    }

    /// <summary>
    /// Creates a unique completion ID in OpenAI format.
    /// </summary>
    /// <returns>Completion ID string</returns>
    public static string CreateCompletionId()
    {
        return $"chatcmpl-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Creates a streaming chat completion response from HarmonyDelta for real-time streaming.
    /// </summary>
    /// <param name="delta">Harmony delta containing incremental updates</param>
    /// <param name="promptMessages">Original prompt messages</param>
    /// <param name="request">Chat completion request</param>
    /// <param name="finishReason">Finish reason for the chunk (null for intermediate chunks)</param>
    /// <param name="isFirstChunk">Whether this is the first chunk in the stream</param>
    /// <param name="activeToolCalls">Dictionary mapping tool call recipients to their IDs for consistency</param>
    /// <returns>Streaming chat completion response with delta structure</returns>
    public static ChatCompletionStreamingResponse CreateStreamingResponseFromDelta(
        this HarmonyDelta delta,
        IEnumerable<ChatMessage> promptMessages,
        ChatCompletionRequest request,
        string? finishReason = null,
        bool isFirstChunk = false)
    {
        var responseDelta = new ChatCompletionDelta();
        var choices = new List<ChatCompletionStreamingChoice>();

        // For first chunk, include the role
        if (isFirstChunk && delta.Role != null)
        {
            responseDelta.Role = delta.Role.Value.ToChatRole();
        }

        // Map delta content to appropriate fields
        if (!string.IsNullOrEmpty(delta.FinalContent))
        {
            responseDelta.Content = delta.FinalContent;
        }
        
        if (!string.IsNullOrEmpty(delta.ReasoningContent))
        {
            responseDelta.Reasoning = delta.ReasoningContent;
        }
        
        if (!string.IsNullOrEmpty(delta.CommentaryContent))
        {
            responseDelta.Commentary = delta.CommentaryContent;
        }

        // Handle tool calls
        if (delta.ToolCall != null)
        {
            var isFirstToolCallChunk = !string.IsNullOrEmpty(delta.ToolCall.ToolName); // Has metadata = first chunk
            
            responseDelta.ToolCalls = new List<ToolCallDelta>
            {
                new ToolCallDelta
                {
                    Index = 0,
                    // Only provide ID and type for the first token of a tool call
                    Id = delta.ToolCall.Id,
                    Type = isFirstToolCallChunk ? "function" : null,
                    Function = new FunctionCallDelta
                    {
                        // Only provide name for the first token of a tool call
                        // Include namespace in name if it's not "functions"
                        Name = isFirstToolCallChunk ? 
                            (delta.ToolCall.Namespace != "functions" ? 
                                $"{delta.ToolCall.Namespace}.{delta.ToolCall.ToolName}" : 
                                delta.ToolCall.ToolName) : null,
                        // Always provide the argument delta
                        Arguments = delta.ToolCall.Arguments ?? ""
                    }
                }
            };
        }

        var choice = new ChatCompletionStreamingChoice
        {
            Index = 0,
            Delta = responseDelta,
            FinishReason = finishReason
        };

        choices.Add(choice);

        var response = new ChatCompletionStreamingResponse
        {
            Model = request.Model,
            Id = CreateCompletionId(),
            Object = CompletionObjectType.ChatCompletionChunk,
            Created = GetCurrentUnixTimestamp(),
            Choices = choices,
            Usage = finishReason != null ? new TokenUsage
            {
                PromptTokens = 0, // Can be calculated if needed
                CompletionTokens = 0, // Can be calculated if needed
                TotalTokens = 0
            } : null
        };

        return response;
    }

    /// <summary>
    /// Gets the current Unix timestamp for API responses.
    /// </summary>
    /// <returns>Unix timestamp in seconds</returns>
    public static long GetCurrentUnixTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}