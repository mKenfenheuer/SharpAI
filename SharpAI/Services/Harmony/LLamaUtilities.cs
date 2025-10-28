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
    public static InferenceParams CreateInferenceParams(ChatCompletionRequest request, ModelInstance modelInstance)
    {
        // Build stop sequences list
        var antiPrompts = new List<string> { "<|return|>", "<|call|>" };

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
            SamplingPipeline = BuildSamplingPipeline(request),
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

        var samplingPipeline = new DefaultSamplingPipeline()
        {
            Temperature = (float)(request.Temperature ?? 0.7),
            TopP = (float)(request.TopP ?? 1.0),
            RepeatPenalty = repeatPenalty,
            PreventEOS = false,
            Grammar = grammar,
        };
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
            Object = CompletionObjectType.ChatCompletion,
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
        foreach (var harmonyMessage in harmonyMessages)
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
                }

                var chatMessage = new ChatMessage
                {
                    Role = harmonyMessage.Role.ToChatRole(),
                    Content = content.StringContent,
                    Name = content.ChannelString,
                };

                if (harmonyMessage.Role == HarmonyRole.Assistant && harmonyMessage.Content.Cast<HarmonyAssistantContent>()
                    .Any(c => c.ToolCall != null))
                {
                    foreach (var toolCall in harmonyMessage.Content.Cast<HarmonyAssistantContent>()
                        .Where(c => c.ToolCall != null)
                        .Select(c => c.ToolCall!))
                    {
                        chatMessage.ToolCalls ??= new List<ToolCall>();
                        chatMessage.ToolCalls.Add(new ToolCall
                        {
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
                chatMessages.Add(chatMessage);
            }
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
    /// Gets the current Unix timestamp for API responses.
    /// </summary>
    /// <returns>Unix timestamp in seconds</returns>
    public static long GetCurrentUnixTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}