using LLama.Common;
using LLama.Sampling;
using SharpAI.Models;
using System.Text.Json;

namespace SharpAI.Services.Harmony;

public static class LLamaUtilities
{
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        // Simple token estimation: roughly 4 characters per token
        // This is a rough approximation for GPT-style tokenizers
        return (int)Math.Ceiling(text.Length / 4.0);
    }

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

    public static List<ToolCall> ConvertHarmonyToolCalls(List<HarmonyToolCall> harmonyToolCalls)
    {
        var toolCalls = new List<ToolCall>();

        foreach (var harmonyCall in harmonyToolCalls)
        {
            var toolCall = new ToolCall
            {
                Id = harmonyCall.Id ?? $"call_{Guid.NewGuid():N}",
                Type = ToolType.Function,
                Function = new FunctionCall
                {
                    Name = harmonyCall.Name,
                    Arguments = harmonyCall.Arguments ?? "{}"
                }
            };

            toolCalls.Add(toolCall);
        }

        return toolCalls;
    }

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
            PreventEOS = false
        };

        var inferenceParams = new InferenceParams
        {
            MaxTokens = request.MaxTokens ?? 2048,
            AntiPrompts = antiPrompts,
            SamplingPipeline = samplingPipeline,
            DecodeSpecialTokens = true
        };

        return inferenceParams;
    }

    public static string CreateCompletionId()
    {
        return $"chatcmpl-{Guid.NewGuid():N}";
    }

    public static long GetCurrentUnixTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}