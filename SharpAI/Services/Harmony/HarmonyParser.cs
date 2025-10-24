using System.Text.RegularExpressions;
using SharpAI.Models;

namespace SharpAI.Services.Harmony;

public class HarmonyParser
{
    private readonly ILogger<HarmonyParser> _logger;

    public HarmonyParser(ILogger<HarmonyParser> logger)
    {
        _logger = logger;
    }

    public HarmonyStreamDelta ParseIncrementalResponse(HarmonyStreamState state, string newToken)
    {
        var delta = new HarmonyStreamDelta();
        
        // Append new token to raw response
        state.RawResponse += newToken;
        
        // Check for completion tokens
        if (state.RawResponse.Contains("<|call|>"))
        {
            state.IsComplete = true;
            state.CompletionReason = "tool_calls";
            delta.IsComplete = true;
            delta.CompletionReason = "tool_calls";
        }
        else if (state.RawResponse.Contains("<|return|>"))
        {
            state.IsComplete = true;
            state.CompletionReason = "stop";
            delta.IsComplete = true;
            delta.CompletionReason = "stop";
        }

        // Parse current messages
        var messages = ParseHarmonyMessages(state.RawResponse);
        
        // Extract final channel content
        var finalMessages = messages.Where(m => m.Channel == "final").ToList();
        if (finalMessages.Any())
        {
            var currentFinalContent = string.Join(" ", finalMessages.Select(m => m.Content));
            if (currentFinalContent.Length > state.LastFinalStreamedLength)
            {
                delta.FinalContent = currentFinalContent.Substring(state.LastFinalStreamedLength);
                state.FinalContent = currentFinalContent;
                state.LastFinalStreamedLength = currentFinalContent.Length;
            }
        }

        // Extract analysis content (for reasoning tracking)
        var analysisMessages = messages.Where(m => m.Channel == "analysis").ToList();
        if (analysisMessages.Any())
        {
            var currentAnalysisContent = string.Join("\n", analysisMessages.Select(m => m.Content));
            if (currentAnalysisContent != state.AnalysisContent)
            {
                delta.AnalysisContent = currentAnalysisContent.Substring(state.AnalysisContent.Length);
                state.AnalysisContent = currentAnalysisContent;
            }
        }

        // Extract function calls from commentary channel and browser calls from analysis channel
        var functionCalls = messages.Where(m => 
            m.Channel == "commentary" && 
            !string.IsNullOrEmpty(m.Recipient) &&
            m.Recipient.StartsWith("functions.", StringComparison.OrdinalIgnoreCase)).ToList();
            
        var browserCalls = messages.Where(m => 
            m.Channel == "analysis" && 
            !string.IsNullOrEmpty(m.Recipient) &&
            m.Recipient.StartsWith("browser.", StringComparison.OrdinalIgnoreCase)).ToList();
            
        var newToolCalls = new List<HarmonyToolCall>();
        
        // Handle function calls
        foreach (var toolCall in functionCalls)
        {
            var harmonyCall = new HarmonyToolCall
            {
                Name = toolCall.Recipient.Substring("functions.".Length),
                Arguments = toolCall.Content,
                Recipient = toolCall.Recipient
            };

            // Check if this is a new tool call
            if (!state.ToolCalls.Any(tc => tc.Name == harmonyCall.Name && tc.Arguments == harmonyCall.Arguments))
            {
                state.ToolCalls.Add(harmonyCall);
                newToolCalls.Add(harmonyCall);
                state.HasToolCall = true;
            }
        }
        
        // Handle browser calls
        foreach (var toolCall in browserCalls)
        {
            var harmonyCall = new HarmonyToolCall
            {
                Name = toolCall.Recipient.Substring("browser.".Length),
                Arguments = toolCall.Content,
                Recipient = toolCall.Recipient
            };

            // Check if this is a new tool call
            if (!state.ToolCalls.Any(tc => tc.Name == harmonyCall.Name && tc.Arguments == harmonyCall.Arguments))
            {
                state.ToolCalls.Add(harmonyCall);
                newToolCalls.Add(harmonyCall);
                state.HasToolCall = true;
            }
        }

        if (newToolCalls.Any())
        {
            delta.NewToolCalls = newToolCalls;
        }

        return delta;
    }

    public ParsedResponse ParseCompleteResponse(string rawResponse)
    {
        var response = new ParsedResponse
        {
            Content = rawResponse,
            FunctionCall = false,
            Reasoning = null,
            ToolCalls = new List<HarmonyToolCall>()
        };

        try
        {
            var messages = ParseHarmonyMessages(rawResponse);
            _logger.LogDebug("Parsed {MessageCount} harmony messages from response", messages.Count);
            
            // Extract content from final channel
            var finalMessages = messages.Where(m => m.Channel == "final").ToList();
            if (finalMessages.Any())
            {
                response.Content = string.Join(" ", finalMessages.Select(m => m.Content));
                _logger.LogDebug("Extracted final channel content: {Content}", response.Content);
            }

            // Extract reasoning from analysis channel
            var analysisMessages = messages.Where(m => m.Channel == "analysis").ToList();
            if (analysisMessages.Any())
            {
                response.Reasoning = string.Join("\n", analysisMessages.Select(m => m.Content));
            }

            // Extract function calls from commentary channel
            var functionCalls = messages.Where(m => 
                m.Channel == "commentary" && 
                !string.IsNullOrEmpty(m.Recipient) &&
                m.Recipient.StartsWith("functions.", StringComparison.OrdinalIgnoreCase)).ToList();
                
            foreach (var toolCall in functionCalls)
            {
                response.ToolCalls.Add(new HarmonyToolCall
                {
                    Name = toolCall.Recipient.Substring("functions.".Length),
                    Arguments = toolCall.Content,
                    Recipient = toolCall.Recipient
                });
                response.FunctionCall = true;
            }
            
            // Extract browser calls from analysis channel
            var browserCalls = messages.Where(m => 
                m.Channel == "analysis" && 
                !string.IsNullOrEmpty(m.Recipient) &&
                m.Recipient.StartsWith("browser.", StringComparison.OrdinalIgnoreCase)).ToList();
                
            foreach (var toolCall in browserCalls)
            {
                response.ToolCalls.Add(new HarmonyToolCall
                {
                    Name = toolCall.Recipient.Substring("browser.".Length),
                    Arguments = toolCall.Content,
                    Recipient = toolCall.Recipient
                });
                // Note: browser calls don't set FunctionCall = true as they're handled internally
            }

            // If no final channel content found, try to extract from the raw response
            if (string.IsNullOrEmpty(response.Content))
            {
                _logger.LogDebug("No final channel content found, extracting from raw response");
                response.Content = ExtractUserReadableContent(rawResponse);
                _logger.LogDebug("Extracted user-readable content: {Content}", response.Content);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse harmony response format, using raw response");
            response.Content = rawResponse;
            return response;
        }
    }

    private List<HarmonyMessage> ParseHarmonyMessages(string response)
    {
        var messages = new List<HarmonyMessage>();
        
        // Pattern to match harmony messages with flexible ordering
        var messagePattern = @"<\|start\|>([^<|]+?)(?:<\|channel\|>([^<|]+?))?(?:\s+to=([^\s<|]+?))?(?:\s*<\|constrain\|>\s*([^<|]+?))?<\|message\|>(.*?)(?:<\|end\|>|<\|call\|>|<\|return\|>|$)";
        var matches = Regex.Matches(response, messagePattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var role = match.Groups[1].Value.Trim();
            var channel = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;
            var recipient = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;
            var constraint = match.Groups[4].Success ? match.Groups[4].Value.Trim() : null;
            var content = match.Groups[5].Value.Trim();

            messages.Add(new HarmonyMessage
            {
                Role = role,
                Channel = channel,
                Recipient = recipient,
                Constraint = constraint,
                Content = content
            });
        }

        // Fallback: Look for browser tool calls in a more flexible format
        // This handles cases where the model generates: to=browser.search <|constrain|>json<|message|>{...}
        var browserCallPattern = @"to=(browser\.\w+)\s*<\|constrain\|>\s*json<\|message\|>(\{[^}]*\})";
        var browserMatches = Regex.Matches(response, browserCallPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        foreach (Match match in browserMatches)
        {
            var recipient = match.Groups[1].Value.Trim();
            var content = match.Groups[2].Value.Trim();
            
            // Only add if we don't already have this browser call
            if (!messages.Any(m => m.Recipient == recipient && m.Content == content))
            {
                messages.Add(new HarmonyMessage
                {
                    Role = "assistant",
                    Channel = "analysis", // Browser calls should be in analysis channel
                    Recipient = recipient,
                    Constraint = "json",
                    Content = content
                });
                
                _logger.LogDebug("Fallback browser call parsed: {Recipient} with args {Content}", recipient, content);
            }
        }

        return messages;
    }

    private string ExtractUserReadableContent(string rawResponse)
    {
        try
        {
            // First try to find final channel content
            var finalMatch = Regex.Match(rawResponse, @"<\|channel\|>final<\|message\|>(.*?)(?:<\|return\|>|<\|end\|>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (finalMatch.Success && !string.IsNullOrEmpty(finalMatch.Groups[1].Value.Trim()))
            {
                return finalMatch.Groups[1].Value.Trim();
            }

            // If no final channel, look for any assistant message that's not analysis
            var assistantMatches = Regex.Matches(rawResponse, @"<\|start\|>assistant<\|channel\|>([^<]+)<\|message\|>(.*?)(?:<\|end\|>|<\|return\|>|<\|call\|>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            foreach (Match match in assistantMatches)
            {
                var channel = match.Groups[1].Value.Trim();
                var content = match.Groups[2].Value.Trim();
                
                // Skip analysis channel, return final or commentary content
                if (channel != "analysis" && !string.IsNullOrEmpty(content))
                {
                    return content;
                }
            }

            // Last resort: look for any content after assistant without channel
            var simpleMatch = Regex.Match(rawResponse, @"<\|start\|>assistant[^<]*<\|message\|>(.*?)(?:<\|end\|>|<\|return\|>|<\|call\|>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (simpleMatch.Success && !string.IsNullOrEmpty(simpleMatch.Groups[1].Value.Trim()))
            {
                return simpleMatch.Groups[1].Value.Trim();
            }

            // If all else fails, return a helpful message
            return "I'm processing your request...";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract user-readable content from response");
            return "I'm processing your request...";
        }
    }
}