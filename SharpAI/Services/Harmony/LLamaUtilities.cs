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
            var callId = !string.IsNullOrEmpty(harmonyCall.Id) 
                ? harmonyCall.Id 
                : $"call_{Guid.NewGuid():N}";

            var toolCall = new ToolCall
            {
                Id = callId,
                CallId = callId, // For streaming compatibility
                Type = ToolType.Function,
                Function = new FunctionCall
                {
                    Name = harmonyCall.Name,
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

        var repeatPenalty = 1.1f;
        if (request.FrequencyPenalty.HasValue)
        {
            repeatPenalty = (float)(1.0 + Math.Abs(request.FrequencyPenalty.Value));
        }
        else if (request.PresencePenalty.HasValue)
        {
            repeatPenalty = (float)(1.0 + Math.Abs(request.PresencePenalty.Value));
        }

        // Check if we need JSON grammar for structured output
        LLama.Sampling.Grammar? jsonGrammar = null;
        if (request.ResponseFormat?.Type == ResponseFormatType.JsonObject || 
            request.ResponseFormat?.Type == ResponseFormatType.JsonSchema)
        {
            try
            {
                var grammarText = GetJsonGrammar();
                if (!string.IsNullOrEmpty(grammarText))
                {
                    jsonGrammar = new LLama.Sampling.Grammar(grammarText, "root");
                }
            }
            catch (Exception)
            {
                // Failed to load JSON grammar; continue without grammar
            }
        }

        var samplingPipeline = new DefaultSamplingPipeline()
        {
            Temperature = (float)(request.Temperature ?? 0.7),
            TopP = (float)(request.TopP ?? 1.0),
            RepeatPenalty = repeatPenalty,
            PreventEOS = false,
            Grammar = jsonGrammar
        };

        var inferenceParams = new InferenceParams
        {
            MaxTokens = request.MaxTokens ?? int.MaxValue,
            AntiPrompts = antiPrompts,
            SamplingPipeline = samplingPipeline,
            DecodeSpecialTokens = true
        };

        return inferenceParams;
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

    /// <summary>
    /// Provides JSON grammar definition for structured output parsing.
    /// </summary>
    /// <returns>JSON grammar string for LLamaSharp</returns>
    private static string GetJsonGrammar()
    {
        // Inline JSON grammar for structured output
        // This is based on the standard JSON grammar from LLamaSharp examples
        return @"
root   ::= object
value  ::= object | array | string | number | (""true"" | ""false"" | ""null"") ws

object ::=
  ""{"" ws (
            string "":"" ws value
    ("","" ws string "":"" ws value)*
  )? ""}"" ws

array  ::=
  ""["" ws (
            value
    ("","" ws value)*
  )? ""]"" ws

string ::=
  '""' (
    [^""\\\x7F\x00-\x1F] |
    ""\\"" ([""\\bfnrt] | ""u"" [0-9a-fA-F]{4}) # escapes
  )* '""' ws

number ::= (""-""? ([0-9] | [1-9] [0-9]{0,15})) (""."" [0-9]+)? ([eE] [-+]? [0-9] [1-9]{0,15})? ws

# Optional space: by convention, applied in this grammar after literal chars when allowed
ws ::= | "" "" | ""\n"" [ \t]{0,20}
".Trim();
    }
}