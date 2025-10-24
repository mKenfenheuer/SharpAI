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
        
        var inferenceParams = CreateInferenceParams(request, modelInstance);
        var responseBuilder = new StringBuilder();
        var reasoningTokens = 0;

        // Prepare messages with additional context
        var allMessages = new List<ChatMessage>(request.Messages);

        // Add default system prompt if none provided
        if (!allMessages.Any(m => m.Role == ChatRole.System))
        {
            var defaultSystemPrompt = "You are a helpful and knowledgeable assistant.";
            allMessages.Insert(0, new ChatMessage { Role = ChatRole.System, Content = defaultSystemPrompt });
        }

        // Check if tools are available and should be used
        var shouldUseFunctions = request.Tools?.Any() == true && 
                                 (request.ToolChoice != ToolChoice.None);

        // Add function calling instructions if tools are available
        if (shouldUseFunctions)
        {
            var functionsPrompt = CreateFunctionCallingPrompt(request.Tools!);
            allMessages.Add(new ChatMessage { Role = ChatRole.System, Content = functionsPrompt });
        }

        // Add reasoning effort if specified
        if (request.ReasoningEffort.HasValue)
        {
            var reasoningPrompt = GetReasoningPrompt(request.ReasoningEffort.Value);
            allMessages.Add(new ChatMessage { Role = ChatRole.System, Content = reasoningPrompt });
        }

        // Add response format instructions if specified
        if (request.ResponseFormat != null)
        {
            var formatPrompt = CreateResponseFormatPrompt(request.ResponseFormat);
            allMessages.Add(new ChatMessage { Role = ChatRole.System, Content = formatPrompt });
        }

        // Apply Jinja template to format the prompt
        var formattedPrompt = ApplyJinjaTemplate(allMessages, modelInstance.Template);

        // Generate response using the stateless executor
        await foreach (var text in modelInstance.Executor!.InferAsync(formattedPrompt, inferenceParams))
        {
            responseBuilder.Append(text);
        }

        var responseText = responseBuilder.ToString().Trim();
        
        // Parse structured response format if present
        responseText = ParseStructuredResponse(responseText, modelInstance.Configuration);
        
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
                PromptTokens = EstimateTokens(formattedPrompt),
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
        
        var inferenceParams = CreateInferenceParams(request, modelInstance);
        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Prepare messages with additional context
        var allMessages = new List<ChatMessage>(request.Messages);

        // Add default system prompt if none provided
        if (!allMessages.Any(m => m.Role == ChatRole.System))
        {
            var defaultSystemPrompt = "You are a helpful and knowledgeable assistant.";
            allMessages.Insert(0, new ChatMessage { Role = ChatRole.System, Content = defaultSystemPrompt });
        }

        // Check if tools are available and should be used
        var shouldUseFunctions = request.Tools?.Any() == true && 
                                 (request.ToolChoice != ToolChoice.None);

        // Add function calling instructions if tools are available
        if (shouldUseFunctions)
        {
            var functionsPrompt = CreateFunctionCallingPrompt(request.Tools!);
            allMessages.Add(new ChatMessage { Role = ChatRole.System, Content = functionsPrompt });
        }

        // Add reasoning effort if specified
        if (request.ReasoningEffort.HasValue)
        {
            var reasoningPrompt = GetReasoningPrompt(request.ReasoningEffort.Value);
            allMessages.Add(new ChatMessage { Role = ChatRole.System, Content = reasoningPrompt });
        }

        // Add response format instructions if specified
        if (request.ResponseFormat != null)
        {
            var formatPrompt = CreateResponseFormatPrompt(request.ResponseFormat);
            allMessages.Add(new ChatMessage { Role = ChatRole.System, Content = formatPrompt });
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

        // Apply Jinja template to format the prompt
        var formattedPrompt = ApplyJinjaTemplate(allMessages, modelInstance.Template);

        var responseBuilder = new StringBuilder();
        var hasStreamedContent = false;
        
        await foreach (var text in modelInstance.Executor!.InferAsync(formattedPrompt, inferenceParams))
        {
            responseBuilder.Append(text);
            
            // Try to detect if we have a complete structured response
            var currentResponse = responseBuilder.ToString();
            var parsedResponse = ParseStructuredResponse(currentResponse, modelInstance.Configuration);
            
            // If the parsed response is different from the raw response, we have structured content
            if (parsedResponse != currentResponse && !string.IsNullOrEmpty(parsedResponse))
            {
                // Stream the parsed content instead of raw content
                if (!hasStreamedContent)
                {
                    // Stream the entire parsed content as chunks
                    var words = parsedResponse.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var word in words)
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
                                    Delta = new ChatMessage { Role = ChatRole.Assistant, Content = word + " " },
                                    FinishReason = null
                                }
                            }
                        };
                    }
                    hasStreamedContent = true;
                }
            }
            else if (!hasStreamedContent && !string.IsNullOrEmpty(text))
            {
                // Stream raw content if no structured format detected yet
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
        
        // Parse structured response format if present
        completeResponse = ParseStructuredResponse(completeResponse, modelInstance.Configuration);
        
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
                PromptTokens = EstimateTokens(formattedPrompt),
                CompletionTokens = EstimateTokens(completeResponse),
                ReasoningTokens = reasoningTokens > 0 ? reasoningTokens : null,
                TotalTokens = 0
            };
            finalChunk.Usage.TotalTokens = finalChunk.Usage.PromptTokens + finalChunk.Usage.CompletionTokens + reasoningTokens;
        }

        yield return finalChunk;
    }



    private InferenceParams CreateInferenceParams(ChatCompletionRequest request, ModelInstance modelInstance)
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
            RepeatPenalty = repeatPenalty,
            
        };

        // Get stop sequences from model EOS token and request
        var stopSequences = GetStopSequencesFromModel(request, modelInstance);

        return new InferenceParams
        {
            MaxTokens = request.MaxCompletionTokens ?? request.MaxTokens ?? int.MaxValue,
            AntiPrompts = stopSequences,
            SamplingPipeline = samplingPipeline,
            DecodeSpecialTokens = true
        };
    }

    private List<string> GetStopSequencesFromModel(ChatCompletionRequest request, ModelInstance modelInstance)
    {
        var stopSequences = new List<string>();

        try
        {
            // Try to get the EOS token from the model
            if (modelInstance.Weights != null)
            {
                // Get the EOS token and convert it to string
                var eosToken = modelInstance.Weights.NativeHandle.Vocab.EOS;
                if (eosToken.HasValue)
                {
                    try
                    {
                        var eosTokenString = modelInstance.Weights.NativeHandle.Vocab.LLamaTokenToString(eosToken, true);
                        if (!string.IsNullOrEmpty(eosTokenString))
                        {
                            stopSequences.Add(eosTokenString);
                            _logger.LogDebug("Added EOS token as stop sequence: {EosToken}", eosTokenString);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to convert EOS token to string");
                    }
                }

                // Also try to get BOS token (sometimes relevant for stopping)
                try
                {
                    var bosToken = modelInstance.Weights.NativeHandle.Vocab.BOS;
                    if (bosToken.HasValue)
                    {
                        var bosTokenString = modelInstance.Weights.NativeHandle.Vocab.LLamaTokenToString(bosToken, true);
                        if (!string.IsNullOrEmpty(bosTokenString) && !stopSequences.Contains(bosTokenString))
                        {
                            // Only add BOS as stop sequence if it's different from EOS and seems relevant
                            if (bosToken != eosToken)
                            {
                                _logger.LogDebug("Found BOS token: {BosToken}", bosTokenString);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to get BOS token from model");
                }

                // Also try to get EOT token (sometimes relevant for stopping)
                try
                {
                    var eotToken = modelInstance.Weights.NativeHandle.Vocab.EOT;
                    if (eotToken.HasValue)
                    {
                        var eotTokenString = modelInstance.Weights.NativeHandle.Vocab.LLamaTokenToString(eotToken, true);
                        if (!string.IsNullOrEmpty(eotTokenString) && !stopSequences.Contains(eotTokenString))
                        {
                            // Only add EOT as stop sequence if it's different from EOS and seems relevant
                            if (eotToken != eosToken)
                            {
                                _logger.LogDebug("Found EOT token: {EotToken}", eotTokenString);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to get EOT token from model");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get stop sequences from model, using fallback");
        }

        // Add any custom stop sequences from the request
        if (request.Stop != null)
        {
            if (request.Stop is string singleStop)
            {
                stopSequences.Add(singleStop);
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
                            stopSequences.Add(stopSeq);
                        }
                    }
                }
            }
        }

        // Add common fallback stop sequences if we didn't find any
        if (stopSequences.Count == 0)
        {
            stopSequences.AddRange(new[] { "<|end|>", "<|eot_id|>", "<|im_end|>", "<|endoftext|>" });
            _logger.LogDebug("Using fallback stop sequences as no model-specific tokens were found");
        }

        return stopSequences.Distinct().ToList();
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
    
    private string ApplyJinjaTemplate(List<ChatMessage> messages, LLamaTemplate? template)
    {
        if (template == null)
        {
            // Fallback to the existing ConvertToChatHistory approach
            return FormatMessagesAsPlainText(messages);
        }

        try
        {
            // Clear any existing messages from the template
            template.Clear();

            // Add messages to the template
            foreach (var message in messages)
            {
                var role = message.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.User => "user", 
                    ChatRole.Assistant => "assistant",
                    ChatRole.Tool => "tool",
                    _ => "user"
                };

                var content = message.Content?.ToString() ?? "";
                template.Add(role, content);
            }

            // Apply the template to get the formatted prompt
            var appliedTemplate = template.Apply();
            return System.Text.Encoding.UTF8.GetString(appliedTemplate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply Jinja template, falling back to plain text formatting");
            return FormatMessagesAsPlainText(messages);
        }
    }

    private string FormatMessagesAsPlainText(List<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        
        foreach (var message in messages)
        {
            var role = message.Role switch
            {
                ChatRole.System => "System",
                ChatRole.User => "User",
                ChatRole.Assistant => "Assistant", 
                ChatRole.Tool => "Tool",
                _ => "User"
            };

            var content = message.Content?.ToString() ?? "";
            if (!string.IsNullOrEmpty(content))
            {
                builder.AppendLine($"{role}: {content}");
            }
        }

        return builder.ToString();
    }

    private string ParseStructuredResponse(string rawResponse, ModelConfiguration? modelConfig = null)
    {
        try
        {
            // First try to use the model-specific regex if provided
            if (modelConfig?.ResponseParsingRegex != null)
            {
                var customMatch = Regex.Match(rawResponse, modelConfig.ResponseParsingRegex, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (customMatch.Success)
                {
                    // If the regex has named groups, try to extract from "content" group first, then use the first capturing group
                    if (customMatch.Groups["content"].Success)
                    {
                        var content = customMatch.Groups["content"].Value.Trim();
                        _logger.LogDebug("Extracted content using custom regex with named group: {Content}", content);
                        return content;
                    }
                    else if (customMatch.Groups.Count > 1)
                    {
                        var content = customMatch.Groups[1].Value.Trim();
                        _logger.LogDebug("Extracted content using custom regex: {Content}", content);
                        return content;
                    }
                }
            }

            // Fallback to default structured response parsing
            return ParseDefaultStructuredResponse(rawResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse structured response with custom regex, falling back to default parsing");
            return ParseDefaultStructuredResponse(rawResponse);
        }
    }

    private string ParseDefaultStructuredResponse(string rawResponse)
    {
        try
        {
            // Look for the final assistant message in the structured response
            var pattern = @"<\|start\|>assistant<\|channel\|>final<\|message\|>(.*?)(?:<\|end\|>|$)";
            var match = Regex.Match(rawResponse, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            if (match.Success && match.Groups.Count > 1)
            {
                var content = match.Groups[1].Value.Trim();
                _logger.LogDebug("Extracted structured response content: {Content}", content);
                return content;
            }

            // Fallback: look for any assistant message with any channel
            pattern = @"<\|start\|>assistant<\|channel\|>[^<]*<\|message\|>(.*?)(?:<\|end\|>|$)";
            match = Regex.Match(rawResponse, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            if (match.Success && match.Groups.Count > 1)
            {
                var content = match.Groups[1].Value.Trim();
                _logger.LogDebug("Extracted fallback assistant response: {Content}", content);
                return content;
            }

            // Alternative format: Look for assistant messages without channel
            pattern = @"<\|start\|>assistant<\|message\|>(.*?)(?:<\|end\|>|$)";
            match = Regex.Match(rawResponse, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            if (match.Success && match.Groups.Count > 1)
            {
                var content = match.Groups[1].Value.Trim();
                _logger.LogDebug("Extracted simple assistant response: {Content}", content);
                return content;
            }

            // Check if the response contains structured tokens but extract everything after the last assistant start
            if (rawResponse.Contains("<|start|>assistant"))
            {
                var lastAssistantIndex = rawResponse.LastIndexOf("<|start|>assistant", StringComparison.OrdinalIgnoreCase);
                if (lastAssistantIndex >= 0)
                {
                    var afterAssistant = rawResponse.Substring(lastAssistantIndex);
                    var messageMatch = Regex.Match(afterAssistant, @"<\|message\|>(.*?)(?:<\|end\|>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (messageMatch.Success && messageMatch.Groups.Count > 1)
                    {
                        var content = messageMatch.Groups[1].Value.Trim();
                        _logger.LogDebug("Extracted last assistant message: {Content}", content);
                        return content;
                    }
                }
            }

            // If no structured format found, return the original response
            _logger.LogDebug("No structured response format detected, returning original response");
            return rawResponse;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse structured response, returning original");
            return rawResponse;
        }
    }

    public void Dispose()
    {
        _modelManager?.Dispose();
    }
}