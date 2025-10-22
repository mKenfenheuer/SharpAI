using LLama;
using LLama.Common;
using LLama.Sampling;
using SharpAI.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpAI.Services;

public class LLamaSharpService
{
    private readonly LLamaModelManager _modelManager;
    private readonly ILogger<LLamaSharpService> _logger;

    public LLamaSharpService(LLamaModelManager modelManager, ILogger<LLamaSharpService> logger)
    {
        _modelManager = modelManager;
        _logger = logger;
    }

    public async Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request)
    {
        var modelName = request.Model ?? "llama-3.2-3b-instruct";
        _logger.LogInformation("Using model: {ModelName}", modelName);
        
        ModelInstance modelInstance;
        try
        {
            modelInstance = await _modelManager.GetModelAsync(modelName);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid model requested: {ModelName}", modelName);
            throw new ArgumentException($"Model '{modelName}' is not available. Use /v1/models to see available models.", ex);
        }
        
        var chatHistory = ConvertToChatHistory(request.Messages);
        var session = new ChatSession(modelInstance.Executor!, chatHistory);

        var inferenceParams = CreateInferenceParams(request);
        var responseBuilder = new StringBuilder();
        var reasoningTokens = 0;

        // Check if tools are available and should be used
        var shouldUseFunctions = request.Tools?.Any() == true && 
                                 (request.ToolChoice != ToolChoice.None);

        // Add function calling instructions if tools are available
        if (shouldUseFunctions)
        {
            var functionsPrompt = CreateFunctionCallingPrompt(request.Tools!);
            chatHistory.AddMessage(AuthorRole.System, functionsPrompt);
        }

        // Add reasoning effort if specified
        if (request.ReasoningEffort.HasValue)
        {
            var reasoningPrompt = GetReasoningPrompt(request.ReasoningEffort.Value);
            chatHistory.AddMessage(AuthorRole.System, reasoningPrompt);
        }

        // Add response format instructions if specified
        if (request.ResponseFormat != null)
        {
            var formatPrompt = CreateResponseFormatPrompt(request.ResponseFormat);
            chatHistory.AddMessage(AuthorRole.System, formatPrompt);
        }

        // Add default system prompt if none provided
        if (!request.Messages.Any(m => m.Role == ChatRole.System))
        {
            var defaultSystemPrompt = "You are a helpful and knowledgeable assistant.";
            chatHistory.AddMessage(AuthorRole.System, defaultSystemPrompt);
        }

        var lastMessage = new ChatHistory.Message(AuthorRole.User, GetLastUserMessage(request.Messages));

        // remove the last user message from history to avoid duplication
        chatHistory.Messages.Remove(chatHistory.Messages.Where(m => m.AuthorRole == AuthorRole.User && m.Content == lastMessage.Content).Last());

        await foreach (var text in session.ChatAsync(lastMessage, inferenceParams))
        {
            responseBuilder.Append(text);
        }

        var responseText = responseBuilder.ToString().Trim();
        
        // Apply response format validation and processing
        if (request.ResponseFormat != null)
        {
            responseText = ProcessResponseFormat(responseText, request.ResponseFormat);
        }
        
        var assistantMessage = new ChatMessage { Role = ChatRole.Assistant };
        var finishReason = "stop";

        // Check if the response contains function calls
        var toolCalls = ExtractToolCalls(responseText);
        if (toolCalls.Any())
        {
            assistantMessage.ToolCalls = toolCalls;
            assistantMessage.Content = null;
            finishReason = "tool_calls";

            // If parallel tool calls are disabled, only keep the first one
            if (request.ParallelToolCalls == false)
            {
                assistantMessage.ToolCalls = assistantMessage.ToolCalls.Take(1).ToList();
            }
        }
        else
        {
            assistantMessage.Content = responseText;
        }

        // Calculate reasoning tokens if reasoning was used
        if (request.ReasoningEffort.HasValue)
        {
            reasoningTokens = EstimateReasoningTokens(responseText, request.ReasoningEffort.Value);
        }

        var response = new ChatCompletionResponse
        {
            Id = $"chatcmpl-{Guid.NewGuid():N}",
            Object = CompletionObjectType.ChatCompletion,
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = request.Model,
            Choices = new List<ChatCompletionChoice>
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    Message = assistantMessage,
                    FinishReason = finishReason
                }
            },
            Usage = new TokenUsage
            {
                PromptTokens = EstimateTokens(string.Join("", request.Messages.Select(m => m.Content?.ToString() ?? ""))),
                CompletionTokens = EstimateTokens(responseText),
                ReasoningTokens = reasoningTokens > 0 ? reasoningTokens : null,
                TotalTokens = 0 // Will be calculated
            }
        };

        response.Usage.TotalTokens = response.Usage.PromptTokens + response.Usage.CompletionTokens + (reasoningTokens);
        return response;
    }

    public async IAsyncEnumerable<ChatCompletionChunk> GenerateChatCompletionStreamAsync(ChatCompletionRequest request)
    {
        var modelName = request.Model ?? "llama-3.2-3b-instruct";
        _logger.LogInformation("Using model for streaming: {ModelName}", modelName);
        
        ModelInstance modelInstance;
        try
        {
            modelInstance = await _modelManager.GetModelAsync(modelName);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid model requested for streaming: {ModelName}", modelName);
            throw new ArgumentException($"Model '{modelName}' is not available. Use /v1/models to see available models.", ex);
        }
        
        var chatHistory = ConvertToChatHistory(request.Messages);
        var session = new ChatSession(modelInstance.Executor!, chatHistory);

        var inferenceParams = CreateInferenceParams(request);
        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Check if tools are available and should be used
        var shouldUseFunctions = request.Tools?.Any() == true && 
                                 (request.ToolChoice != ToolChoice.None);

        // Add function calling instructions if tools are available
        if (shouldUseFunctions)
        {
            var functionsPrompt = CreateFunctionCallingPrompt(request.Tools!);
            chatHistory.AddMessage(AuthorRole.System, functionsPrompt);
        }

        // Add reasoning effort if specified
        if (request.ReasoningEffort.HasValue)
        {
            var reasoningPrompt = GetReasoningPrompt(request.ReasoningEffort.Value);
            chatHistory.AddMessage(AuthorRole.System, reasoningPrompt);
        }

        // Add response format instructions if specified
        if (request.ResponseFormat != null)
        {
            var formatPrompt = CreateResponseFormatPrompt(request.ResponseFormat);
            chatHistory.AddMessage(AuthorRole.System, formatPrompt);
        }

        // Send initial chunk
        yield return new ChatCompletionChunk
        {
            Id = completionId,
            Object = CompletionObjectType.ChatCompletionChunk,
            Created = created,
            Model = request.Model,
            Choices = new List<ChatCompletionChunkChoice>
            {
                new ChatCompletionChunkChoice
                {
                    Index = 0,
                    Delta = new ChatMessage { Role = ChatRole.Assistant, Content = "" },
                    FinishReason = null
                }
            }
        };

        var responseBuilder = new StringBuilder();
        var lastMessage = GetLastUserMessage(request.Messages);
        
        await foreach (var text in session.ChatAsync(
            new ChatHistory.Message(AuthorRole.User, lastMessage),
            inferenceParams))
        {
            responseBuilder.Append(text);
            
            if (!string.IsNullOrEmpty(text))
            {
                yield return new ChatCompletionChunk
                {
                    Id = completionId,
                    Object = CompletionObjectType.ChatCompletionChunk,
                    Created = created,
                    Model = request.Model,
                    Choices = new List<ChatCompletionChunkChoice>
                    {
                        new ChatCompletionChunkChoice
                        {
                            Index = 0,
                            Delta = new ChatMessage { Role = ChatRole.Assistant, Content = text },
                            FinishReason = null
                        }
                    }
                };
            }
        }

        // Check for tool calls in the complete response
        var completeResponse = responseBuilder.ToString();
        
        // Apply response format validation and processing for streaming
        if (request.ResponseFormat != null)
        {
            completeResponse = ProcessResponseFormat(completeResponse, request.ResponseFormat);
        }
        
        var toolCalls = ExtractToolCalls(completeResponse);
        var finishReason = toolCalls.Any() ? "tool_calls" : "stop";

        // Send tool calls if present
        if (toolCalls.Any())
        {
            foreach (var toolCall in toolCalls)
            {
                yield return new ChatCompletionChunk
                {
                    Id = completionId,
                    Object = CompletionObjectType.ChatCompletionChunk,
                    Created = created,
                    Model = request.Model,
                    Choices = new List<ChatCompletionChunkChoice>
                    {
                        new ChatCompletionChunkChoice
                        {
                            Index = 0,
                            Delta = new ChatMessage 
                            { 
                                Role = ChatRole.Assistant,
                                ToolCalls = new List<ToolCall> { toolCall }
                            },
                            FinishReason = null
                        }
                    }
                };
            }
        }

        // Send final chunk with usage if requested
        var finalChunk = new ChatCompletionChunk
        {
            Id = completionId,
            Object = CompletionObjectType.ChatCompletionChunk,
            Created = created,
            Model = request.Model,
            Choices = new List<ChatCompletionChunkChoice>
            {
                new ChatCompletionChunkChoice
                {
                    Index = 0,
                    Delta = new ChatMessage { Role = ChatRole.Assistant },
                    FinishReason = finishReason
                }
            }
        };

        // Include usage if stream_options.include_usage is true
        if (request.StreamOptions?.IncludeUsage == true)
        {
            var reasoningTokens = request.ReasoningEffort.HasValue ? 
                EstimateReasoningTokens(completeResponse, request.ReasoningEffort.Value) : 0;
            
            finalChunk.Usage = new TokenUsage
            {
                PromptTokens = EstimateTokens(string.Join("", request.Messages.Select(m => m.Content?.ToString() ?? ""))),
                CompletionTokens = EstimateTokens(completeResponse),
                ReasoningTokens = reasoningTokens > 0 ? reasoningTokens : null,
                TotalTokens = 0
            };
            finalChunk.Usage.TotalTokens = finalChunk.Usage.PromptTokens + finalChunk.Usage.CompletionTokens + reasoningTokens;
        }

        yield return finalChunk;
    }

    private ChatHistory ConvertToChatHistory(List<ChatMessage> messages)
    {
        var chatHistory = new ChatHistory();
        
        foreach (var message in messages)
        {
            var role = message.Role switch
            {
                ChatRole.System => AuthorRole.System,
                ChatRole.User => AuthorRole.User,
                ChatRole.Assistant => AuthorRole.Assistant,
                ChatRole.Tool => AuthorRole.User, // Tool messages are treated as user messages in LLama
                _ => AuthorRole.User
            };

            var content = message.Content?.ToString() ?? "";
            chatHistory.AddMessage(role, content);
        }

        return chatHistory;
    }

    private string GetLastUserMessage(List<ChatMessage> messages)
    {
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        return lastUserMessage?.Content?.ToString() ?? "";
    }

    private InferenceParams CreateInferenceParams(ChatCompletionRequest request)
    {
        var repeatPenalty = 1.0f;
        
        // Apply penalties if specified
        if (request.FrequencyPenalty.HasValue)
        {
            repeatPenalty = (float)(1.0 + Math.Abs(request.FrequencyPenalty.Value));
        }
        else if (request.PresencePenalty.HasValue)
        {
            // Presence penalty affects the likelihood of new tokens
            repeatPenalty = (float)(1.0 + Math.Abs(request.PresencePenalty.Value));
        }

        var samplingPipeline = new DefaultSamplingPipeline()
        {
            Temperature = (float)(request.Temperature ?? 0.7),
            TopP = (float)(request.TopP ?? 1.0),
            RepeatPenalty = repeatPenalty
        };

        var stopSequences = ParseStopSequences(request.Stop);

        return new InferenceParams
        {
            MaxTokens = request.MaxCompletionTokens ?? request.MaxTokens ?? 256,
            AntiPrompts = stopSequences,
            SamplingPipeline = samplingPipeline
        };
    }

    private int EstimateTokens(string text)
    {
        // Simple token estimation - approximately 4 characters per token
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private int EstimateReasoningTokens(string text, ReasoningEffortLevel effort)
    {
        var baseTokens = EstimateTokens(text);
        
        return effort switch
        {
            ReasoningEffortLevel.Minimal => (int)(baseTokens * 0.1),
            ReasoningEffortLevel.Low => (int)(baseTokens * 0.3),
            ReasoningEffortLevel.Medium => (int)(baseTokens * 0.5),
            ReasoningEffortLevel.High => (int)(baseTokens * 0.8),
            _ => 0
        };
    }

    private string CreateFunctionCallingPrompt(List<ToolDefinition> tools)
    {
        var toolsJson = JsonSerializer.Serialize(tools, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower 
        });

        return $@"You have access to the following tools. When you need to use a tool, respond with a function call in this exact format:

FUNCTION_CALL:
{{
  ""name"": ""function_name"",
  ""arguments"": {{""param"": ""value""}}
}}

Available tools:
{toolsJson}

Only respond with function calls when you need to use a tool to answer the user's question. The user will execute the function and provide you with the result.";
    }

    private string GetReasoningPrompt(ReasoningEffortLevel effort)
    {
        return effort switch
        {
            ReasoningEffortLevel.Minimal => "Provide a brief, direct response.",
            ReasoningEffortLevel.Low => "Think through this step by step, but keep your reasoning concise.",
            ReasoningEffortLevel.Medium => "Let me think through this carefully. I'll work through the problem step by step and explain my reasoning.",
            ReasoningEffortLevel.High => "I'll approach this problem with thorough analysis. Let me break this down completely, consider multiple approaches, and provide detailed reasoning for my conclusions.",
            _ => ""
        };
    }

    private List<ToolCall> ExtractToolCalls(string responseText)
    {
        var toolCalls = new List<ToolCall>();
        
        // Look for function call pattern
        var functionCallPattern = @"FUNCTION_CALL:\s*(\{[^}]+\})";
        var matches = Regex.Matches(responseText, functionCallPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            try
            {
                var jsonText = match.Groups[1].Value;
                var functionCall = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonText);
                
                if (functionCall != null && 
                    functionCall.TryGetValue("name", out var nameObj) && 
                    functionCall.TryGetValue("arguments", out var argsObj))
                {
                    var toolCall = new ToolCall
                    {
                        Id = $"call_{Guid.NewGuid():N}",
                        Type = ToolType.Function,
                        Function = new FunctionCall
                        {
                            Name = nameObj.ToString() ?? "",
                            Arguments = JsonSerializer.Serialize(argsObj)
                        }
                    };
                    
                    toolCalls.Add(toolCall);
                }
            }
            catch (JsonException)
            {
                // Ignore malformed JSON
                continue;
            }
        }

        return toolCalls;
    }

    private string CreateResponseFormatPrompt(ResponseFormat responseFormat)
    {
        return responseFormat.Type switch
        {
            ResponseFormatType.JsonObject => 
                "You must respond with valid JSON only. Do not include any text outside of the JSON object. " +
                "Ensure your response is properly formatted JSON that can be parsed.",

            ResponseFormatType.JsonSchema when responseFormat.JsonSchema != null => 
                CreateJsonSchemaPrompt(responseFormat.JsonSchema),

            ResponseFormatType.Text or null => 
                "", // No special formatting needed for text

            _ => ""
        };
    }

    private string CreateJsonSchemaPrompt(JsonSchemaFormat jsonSchema)
    {
        var prompt = $"You must respond with valid JSON that strictly follows the schema for '{jsonSchema.Name}'.";
        
        if (!string.IsNullOrEmpty(jsonSchema.Description))
        {
            prompt += $" {jsonSchema.Description}";
        }

        if (jsonSchema.Schema != null)
        {
            var schemaJson = JsonSerializer.Serialize(jsonSchema.Schema, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true 
            });
            prompt += $"\n\nRequired JSON Schema:\n{schemaJson}";
        }

        if (jsonSchema.Strict == true)
        {
            prompt += "\n\nIMPORTANT: Strict schema adherence is required. You must follow the schema exactly with no deviations.";
        }

        prompt += "\n\nRespond ONLY with the JSON object. Do not include any explanation or text outside the JSON.";
        
        return prompt;
    }

    private string ProcessResponseFormat(string responseText, ResponseFormat responseFormat)
    {
        if (responseFormat.Type == ResponseFormatType.JsonObject || 
            responseFormat.Type == ResponseFormatType.JsonSchema)
        {
            return ExtractAndValidateJson(responseText);
        }
        
        return responseText;
    }

    private string ExtractAndValidateJson(string responseText)
    {
        try
        {
            // Try to find JSON content in the response
            var jsonMatch = Regex.Match(responseText, @"\{.*\}", RegexOptions.Singleline);
            if (jsonMatch.Success)
            {
                var jsonText = jsonMatch.Value;
                
                // Validate that it's proper JSON
                JsonDocument.Parse(jsonText);
                return jsonText;
            }
            
            // If no JSON found, try to parse the entire response as JSON
            JsonDocument.Parse(responseText);
            return responseText;
        }
        catch (JsonException)
        {
            // If JSON is invalid, wrap the response in a simple JSON object
            var escapedText = JsonSerializer.Serialize(responseText);
            return $"{{\"response\": {escapedText}}}";
        }
    }

    private List<string> ParseStopSequences(object? stop)
    {
        var stopSequences = new List<string> { "User:", "\nUser:" };

        if (stop != null)
        {
            if (stop is string singleStop)
            {
                stopSequences.Add(singleStop);
            }
            else if (stop is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in jsonElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        stopSequences.Add(element.GetString() ?? "");
                    }
                }
            }
        }

        return stopSequences;
    }

    public void Dispose()
    {
        _modelManager?.Dispose();
    }
}