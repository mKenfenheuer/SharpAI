using System.Text;
using SharpAI.Models;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Formats chat messages and tool definitions into the Harmony prompt format.
/// Handles message conversion, tool definitions, response format specifications, and reasoning effort levels.
/// </summary>
public class HarmonyFormatter
{
    private readonly ILogger<HarmonyFormatter> _logger;

    /// <summary>
    /// Initializes a new instance of the HarmonyFormatter.
    /// </summary>
    /// <param name="logger">Logger for formatter operations</param>
    public HarmonyFormatter(ILogger<HarmonyFormatter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Formats chat messages and configuration into a Harmony-compatible prompt.
    /// </summary>
    /// <param name="messages">List of chat messages to format</param>
    /// <param name="hasFunctions">Whether function calling is enabled</param>
    /// <param name="reasoningEffort">Level of reasoning effort to apply</param>
    /// <param name="tools">Optional list of tool definitions</param>
    /// <param name="responseFormat">Optional response format specification</param>
    /// <returns>Formatted prompt string</returns>
    public string FormatPrompt(List<ChatMessage> messages, bool hasFunctions, ReasoningEffortLevel reasoningEffort, List<ToolDefinition>? tools = null, ResponseFormat? responseFormat = null)
    {
        var prompt = new StringBuilder();
        _logger.LogDebug("Formatting prompt with {MessageCount} messages, functions: {HasFunctions}, reasoning: {ReasoningEffort}", 
            messages.Count, hasFunctions, reasoningEffort);

        // System message with harmony format
        prompt.AppendLine($"<|start|>system<|message|>You are ChatGPT, a large language model trained by OpenAI.");
        prompt.AppendLine("Knowledge cutoff: 2024-06");
        prompt.AppendLine($"Current date: {DateTime.Now:yyyy-MM-dd}");
        prompt.AppendLine();
        prompt.AppendLine($"Reasoning: {reasoningEffort.ToString().ToLower()}");
        prompt.AppendLine();
        
        // Add browser tool definition
        prompt.AppendLine("# Tools");
        prompt.AppendLine();
        prompt.AppendLine("## browser");
        prompt.AppendLine();
        prompt.AppendLine("// Tool for browsing.");
        prompt.AppendLine("// The `cursor` appears in brackets before each browsing display: `[{cursor}]`.");
        prompt.AppendLine("// Cite information from the tool using the following format:");
        prompt.AppendLine("// `【{cursor}†L{line_start}(-L{line_end})?】`, for example: `【6†L9-L11】` or `【8†L3】`.");
        prompt.AppendLine("// Do not quote more than 10 words directly from the tool output.");
        prompt.AppendLine("// sources=web (default: web)");
        prompt.AppendLine("namespace browser {");
        prompt.AppendLine();
        prompt.AppendLine("// Searches for information related to `query` and displays `topn` results.");
        prompt.AppendLine("type search = (_: {");
        prompt.AppendLine("query: string,");
        prompt.AppendLine("topn?: number, // default: 10");
        prompt.AppendLine("source?: string,");
        prompt.AppendLine("}) => any;");
        prompt.AppendLine();
        prompt.AppendLine("// Searches for information related to `query` and returns raw JSON data with `topn` results.");
        prompt.AppendLine("type searchjson = (_: {");
        prompt.AppendLine("query: string,");
        prompt.AppendLine("topn?: number, // default: 10");
        prompt.AppendLine("source?: string,");
        prompt.AppendLine("}) => any;");
        prompt.AppendLine();
        prompt.AppendLine("// Opens the link `id` from the page indicated by `cursor` starting at line number `loc`, showing `num_lines` lines.");
        prompt.AppendLine("// Valid link ids are displayed with the formatting: `【{id}†.*】`.");
        prompt.AppendLine("// If `cursor` is not provided, the most recent page is implied.");
        prompt.AppendLine("// If `id` is a string, it is treated as a fully qualified URL associated with `source`.");
        prompt.AppendLine("// If `loc` is not provided, the viewport will be positioned at the beginning of the document or centered on the most relevant passage, if available.");
        prompt.AppendLine("// Use this function without `id` to scroll to a new location of an opened page.");
        prompt.AppendLine("type open = (_: {");
        prompt.AppendLine("id?: number | string, // default: -1");
        prompt.AppendLine("cursor?: number, // default: -1");
        prompt.AppendLine("loc?: number, // default: -1");
        prompt.AppendLine("num_lines?: number, // default: -1");
        prompt.AppendLine("view_source?: boolean, // default: false");
        prompt.AppendLine("source?: string,");
        prompt.AppendLine("}) => any;");
        prompt.AppendLine();
        prompt.AppendLine("// Opens the link `id` from the page indicated by `cursor` starting at line number `loc`, showing `num_lines` lines, and returns structured JSON data.");
        prompt.AppendLine("// Valid link ids are displayed with the formatting: `【{id}†.*】`.");
        prompt.AppendLine("// If `cursor` is not provided, the most recent page is implied.");
        prompt.AppendLine("// If `id` is a string, it is treated as a fully qualified URL associated with `source`.");
        prompt.AppendLine("// If `loc` is not provided, the viewport will be positioned at the beginning of the document or centered on the most relevant passage, if available.");
        prompt.AppendLine("// Use this function without `id` to scroll to a new location of an opened page.");
        prompt.AppendLine("type openjson = (_: {");
        prompt.AppendLine("id?: number | string, // default: -1");
        prompt.AppendLine("cursor?: number, // default: -1");
        prompt.AppendLine("loc?: number, // default: -1");
        prompt.AppendLine("num_lines?: number, // default: -1");
        prompt.AppendLine("view_source?: boolean, // default: false");
        prompt.AppendLine("source?: string,");
        prompt.AppendLine("}) => any;");
        prompt.AppendLine();
        prompt.AppendLine("// Finds exact matches of `pattern` in the current page, or the page given by `cursor`.");
        prompt.AppendLine("type find = (_: {");
        prompt.AppendLine("pattern: string,");
        prompt.AppendLine("cursor?: number, // default: -1");
        prompt.AppendLine("}) => any;");
        prompt.AppendLine();
        prompt.AppendLine("} // namespace browser");
        prompt.AppendLine();
        
        prompt.AppendLine("# Valid channels: analysis, commentary, final. Channel must be included for every message.");
        prompt.AppendLine();
        prompt.AppendLine("IMPORTANT FORMAT RULES:");
        prompt.AppendLine("- Browser tool calls MUST use the analysis channel");
        prompt.AppendLine("- Use this EXACT format for browser calls:");
        prompt.AppendLine("  <|channel|>analysis to=browser.search<|constrain|>json<|message|>{\"query\":\"search terms\",\"topn\":10,\"source\":\"web\"}");
        prompt.AppendLine("  <|channel|>analysis to=browser.searchjson<|constrain|>json<|message|>{\"query\":\"search terms\",\"topn\":10,\"source\":\"web\"}");
        prompt.AppendLine("  <|channel|>analysis to=browser.open<|constrain|>json<|message|>{\"id\":1,\"cursor\":1}");
        prompt.AppendLine("  <|channel|>analysis to=browser.openjson<|constrain|>json<|message|>{\"id\":1,\"cursor\":1}");
        prompt.AppendLine("  <|channel|>analysis to=browser.find<|constrain|>json<|message|>{\"pattern\":\"search pattern\"}");
        prompt.AppendLine("- Always end with proper channel for final response:");
        prompt.AppendLine("  <|channel|>final<|message|>Your response to the user");
        
        if (hasFunctions)
        {
            _logger.LogDebug("Adding function call instructions to prompt");
            prompt.AppendLine("- Function calls must go to the commentary channel with format:");
            prompt.AppendLine("  <|channel|>commentary to=functions.function_name<|message|>{\"param\":\"value\"}");
        }
        
        prompt.AppendLine("<|end|>");

        // Create developer message with instructions and tools
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        var instructions = systemMessages.Any() ? 
            string.Join("\n\n", systemMessages.Select(m => m.Content?.ToString())) : 
            "You are a helpful assistant.";

        prompt.AppendLine($"<|start|>developer<|message|># Instructions");
        prompt.AppendLine();
        prompt.AppendLine(instructions);

        // Add tools definition if functions are available
        if (hasFunctions && tools?.Any() == true)
        {
            _logger.LogDebug("Adding {ToolCount} tool definitions to prompt", tools.Count);
            prompt.AppendLine();
            prompt.AppendLine(CreateToolsDefinition(tools));
        }

        // Add response format definition if specified
        if (responseFormat != null)
        {
            _logger.LogDebug("Adding response format definition: {FormatType}", responseFormat.Type);
            prompt.AppendLine();
            prompt.AppendLine(CreateResponseFormatDefinition(responseFormat));
        }

        prompt.AppendLine("<|end|>");

        // Add conversation messages (excluding system messages as they're in developer section)
        var conversationMessages = messages.Where(m => m.Role != ChatRole.System).ToList();
        _logger.LogDebug("Processing {ConversationMessageCount} conversation messages", conversationMessages.Count);
        
        for (int i = 0; i < conversationMessages.Count; i++)
        {
            var message = conversationMessages[i];

            var role = message.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.Tool => GetToolName(message),
                _ => "user"
            };

            var content = message.Content?.ToString() ?? "";
            
            if (message.Role == ChatRole.Tool)
            {
                // Tool messages have special format: <|start|>toolname to=assistant<|channel|>commentary<|message|>content<|end|>
                prompt.AppendLine($"<|start|>{role} to=assistant<|channel|>commentary<|message|>{content}<|end|>");
            }
            else if (!string.IsNullOrEmpty(content))
            {
                prompt.AppendLine($"<|start|>{role}<|message|>{content}<|end|>");
            }
        }

        // Start assistant response
        prompt.Append("<|start|>assistant");

        _logger.LogInformation("Completed prompt formatting with {TotalLength} characters", prompt.Length);
        return prompt.ToString();
    }

    /// <summary>
    /// Creates TypeScript-style tool definitions for function calling.
    /// </summary>
    /// <param name="tools">List of tool definitions to format</param>
    /// <returns>Formatted tools definition string</returns>
    private string CreateToolsDefinition(List<ToolDefinition> tools)
    {
        var toolsBuilder = new StringBuilder();
        toolsBuilder.AppendLine("# Tools");
        toolsBuilder.AppendLine();
        toolsBuilder.AppendLine("## functions");
        toolsBuilder.AppendLine();
        toolsBuilder.AppendLine("namespace functions {");
        toolsBuilder.AppendLine();

        foreach (var tool in tools)
        {
            if (tool.Function != null)
            {
                // Add description as comment
                if (!string.IsNullOrEmpty(tool.Function.Description))
                {
                    toolsBuilder.AppendLine($"// {tool.Function.Description}");
                }

                // Convert parameters to TypeScript-like syntax
                var parametersDefinition = ConvertParametersToTypeScript(tool.Function.Parameters);
                
                if (string.IsNullOrEmpty(parametersDefinition))
                {
                    toolsBuilder.AppendLine($"type {tool.Function.Name} = () => any;");
                }
                else
                {
                    toolsBuilder.AppendLine($"type {tool.Function.Name} = (_: {parametersDefinition}) => any;");
                }
                
                toolsBuilder.AppendLine();
            }
        }

        toolsBuilder.AppendLine("} // namespace functions");
        return toolsBuilder.ToString();
    }

    /// <summary>
    /// Converts function parameters from JSON schema format to TypeScript-like syntax.
    /// </summary>
    /// <param name="parameters">Function parameters to convert</param>
    /// <returns>TypeScript-style parameter definition</returns>
    private string ConvertParametersToTypeScript(FunctionParameters? parameters)
    {
        if (parameters == null) return "";

        try
        {
            var tsBuilder = new StringBuilder();
            tsBuilder.Append("{\n");
            
            var props = new List<string>();
            
            if (parameters.Properties != null)
            {
                foreach (var (propName, propSchema) in parameters.Properties)
                {
                    var tsType = ConvertTypeToTypeScript(propSchema);
                    var isRequired = parameters.Required?.Contains(propName) == true;
                    var isOptional = !isRequired;

                    var propDeclaration = isOptional ? $"{propName}?" : propName;
                    
                    if (!string.IsNullOrEmpty(propSchema.Description))
                    {
                        // Handle multi-line type definitions (for nested objects)
                        if (tsType.Contains('\n'))
                        {
                            // Indent the nested object properly
                            var indentedType = string.Join("\n", tsType.Split('\n').Select(line => 
                                string.IsNullOrWhiteSpace(line) ? line : $"  {line}"));
                            props.Add($"// {propSchema.Description}\n{propDeclaration}: {indentedType}");
                        }
                        else
                        {
                            props.Add($"// {propSchema.Description}\n{propDeclaration}: {tsType}");
                        }
                    }
                    else
                    {
                        // Handle multi-line type definitions (for nested objects)
                        if (tsType.Contains('\n'))
                        {
                            var indentedType = string.Join("\n", tsType.Split('\n').Select(line => 
                                string.IsNullOrWhiteSpace(line) ? line : $"  {line}"));
                            props.Add($"{propDeclaration}: {indentedType}");
                        }
                        else
                        {
                            props.Add($"{propDeclaration}: {tsType}");
                        }
                    }
                }
            }
            
            if (props.Count > 0)
            {
                tsBuilder.Append(string.Join(",\n", props));
            }
            
            tsBuilder.Append("\n}");
            return tsBuilder.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert parameters to TypeScript format");
        }

        return "";
    }

    /// <summary>
    /// Converts a property schema to TypeScript type syntax.
    /// </summary>
    /// <param name="schema">Property schema to convert</param>
    /// <returns>TypeScript type definition</returns>
    private string ConvertTypeToTypeScript(PropertySchema schema)
    {
        if (schema.Enum != null && schema.Enum.Count > 0)
        {
            var enumValues = schema.Enum.Select(e => $"\"{e}\"").ToArray();
            return string.Join(" | ", enumValues);
        }

        if (schema.Type is string typeString)
        {
            return typeString switch
            {
                "string" => "string",
                "number" => "number", 
                "integer" => "number",
                "boolean" => "boolean",
                "array" => schema.Items != null ? $"{ConvertTypeToTypeScript(schema.Items)}[]" : "any[]",
                "object" => ConvertNestedObjectToTypeScript(schema),
                _ => "any"
            };
        }
        
        // Handle union types like ["string", "null"]
        if (schema.Type is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var types = new List<string>();
            foreach (var typeElement in jsonElement.EnumerateArray())
            {
                var typeStr = typeElement.GetString();
                if (typeStr == "null")
                {
                    continue; // Handle null in optional parameters
                }
                types.Add(typeStr switch
                {
                    "string" => "string",
                    "number" => "number",
                    "integer" => "number", 
                    "boolean" => "boolean",
                    "object" => ConvertNestedObjectToTypeScript(schema),
                    _ => "any"
                });
            }
            return types.Count > 0 ? string.Join(" | ", types) : "any";
        }

        return "any";
    }

    /// <summary>
    /// Converts nested object schemas to TypeScript interface syntax.
    /// </summary>
    /// <param name="schema">Property schema containing nested object definition</param>
    /// <returns>TypeScript interface definition</returns>
    private string ConvertNestedObjectToTypeScript(PropertySchema schema)
    {
        if (schema.Properties == null || !schema.Properties.Any())
        {
            return "any";
        }

        var nestedBuilder = new StringBuilder();
        nestedBuilder.Append("{\n");
        
        var nestedProps = new List<string>();
        
        foreach (var (propName, propSchema) in schema.Properties)
        {
            var tsType = ConvertTypeToTypeScript(propSchema);
            var isRequired = schema.Required?.Contains(propName) == true;
            var isOptional = !isRequired;

            var propDeclaration = isOptional ? $"  {propName}?" : $"  {propName}";
            
            if (!string.IsNullOrEmpty(propSchema.Description))
            {
                nestedProps.Add($"  // {propSchema.Description}\n  {propDeclaration}: {tsType}");
            }
            else
            {
                nestedProps.Add($"  {propDeclaration}: {tsType}");
            }
        }
        
        if (nestedProps.Count > 0)
        {
            nestedBuilder.Append(string.Join(",\n", nestedProps));
        }
        
        nestedBuilder.Append("\n}");
        return nestedBuilder.ToString();
    }

    /// <summary>
    /// Creates response format definition for structured output requirements.
    /// </summary>
    /// <param name="responseFormat">Response format specification</param>
    /// <returns>Formatted response format definition</returns>
    private string CreateResponseFormatDefinition(ResponseFormat responseFormat)
    {
        var formatBuilder = new StringBuilder();
        formatBuilder.AppendLine("# Response Formats");
        formatBuilder.AppendLine();

        if (responseFormat.Type == ResponseFormatType.JsonSchema && responseFormat.JsonSchema != null)
        {
            var schema = responseFormat.JsonSchema;
            formatBuilder.AppendLine($"## {schema.Name}");
            formatBuilder.AppendLine();

            // Add description if available
            if (!string.IsNullOrEmpty(schema.Description))
            {
                formatBuilder.AppendLine($"// {schema.Description}");
            }

            // Convert schema to JSON string
            var schemaJson = ConvertSchemaToJson(schema.Schema);
            formatBuilder.AppendLine(schemaJson);
        }
        else if (responseFormat.Type == ResponseFormatType.JsonObject)
        {
            formatBuilder.AppendLine("## json_object");
            formatBuilder.AppendLine();
            formatBuilder.AppendLine("// Response must be a valid JSON object");
            formatBuilder.AppendLine("{\"type\":\"object\"}");
        }

        return formatBuilder.ToString();
    }

    /// <summary>
    /// Converts schema objects to JSON string representation.
    /// </summary>
    /// <param name="schema">Schema object to convert</param>
    /// <returns>JSON string representation of the schema</returns>
    private string ConvertSchemaToJson(object? schema)
    {
        if (schema == null)
        {
            return "{\"type\":\"object\"}";
        }

        try
        {
            // If it's already a JSON string, return as-is
            if (schema is string jsonString)
            {
                return jsonString;
            }

            // If it's a FunctionParameters object, convert it properly
            if (schema is FunctionParameters functionParams)
            {
                return ConvertFunctionParametersToJsonSchema(functionParams);
            }

            // Otherwise, serialize it to JSON
            return System.Text.Json.JsonSerializer.Serialize(schema, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert schema to JSON");
            return "{\"type\":\"object\"}";
        }
    }

    /// <summary>
    /// Converts function parameters to JSON schema format.
    /// </summary>
    /// <param name="parameters">Function parameters to convert</param>
    /// <returns>JSON schema string</returns>
    private string ConvertFunctionParametersToJsonSchema(FunctionParameters parameters)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = parameters.Type
        };

        if (parameters.Properties != null && parameters.Properties.Any())
        {
            var properties = new Dictionary<string, object>();
            foreach (var (key, value) in parameters.Properties)
            {
                properties[key] = ConvertPropertySchemaToJson(value);
            }
            schema["properties"] = properties;
        }

        if (parameters.Required != null && parameters.Required.Any())
        {
            schema["required"] = parameters.Required;
        }

        schema["additionalProperties"] = parameters.AdditionalProperties;

        if (!string.IsNullOrEmpty(parameters.Description))
        {
            schema["description"] = parameters.Description;
        }

        return System.Text.Json.JsonSerializer.Serialize(schema, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    /// <summary>
    /// Converts a property schema to JSON representation.
    /// </summary>
    /// <param name="propertySchema">Property schema to convert</param>
    /// <returns>JSON object representation of the property schema</returns>
    private object ConvertPropertySchemaToJson(PropertySchema propertySchema)
    {
        var property = new Dictionary<string, object>();

        if (propertySchema.Type != null)
        {
            property["type"] = propertySchema.Type;
        }

        if (!string.IsNullOrEmpty(propertySchema.Description))
        {
            property["description"] = propertySchema.Description;
        }

        if (propertySchema.Enum != null && propertySchema.Enum.Any())
        {
            property["enum"] = propertySchema.Enum;
        }

        if (propertySchema.Items != null)
        {
            property["items"] = ConvertPropertySchemaToJson(propertySchema.Items);
        }

        if (propertySchema.Properties != null && propertySchema.Properties.Any())
        {
            var nestedProperties = new Dictionary<string, object>();
            foreach (var (key, value) in propertySchema.Properties)
            {
                nestedProperties[key] = ConvertPropertySchemaToJson(value);
            }
            property["properties"] = nestedProperties;

            if (propertySchema.Required != null && propertySchema.Required.Any())
            {
                property["required"] = propertySchema.Required;
            }

            if (propertySchema.AdditionalProperties.HasValue)
            {
                property["additionalProperties"] = propertySchema.AdditionalProperties.Value;
            }
        }

        if (propertySchema.Default != null)
        {
            property["default"] = propertySchema.Default;
        }

        if (propertySchema.Minimum.HasValue)
        {
            property["minimum"] = propertySchema.Minimum.Value;
        }

        if (propertySchema.Maximum.HasValue)
        {
            property["maximum"] = propertySchema.Maximum.Value;
        }

        if (propertySchema.MinLength.HasValue)
        {
            property["minLength"] = propertySchema.MinLength.Value;
        }

        if (propertySchema.MaxLength.HasValue)
        {
            property["maxLength"] = propertySchema.MaxLength.Value;
        }

        if (!string.IsNullOrEmpty(propertySchema.Pattern))
        {
            property["pattern"] = propertySchema.Pattern;
        }

        if (!string.IsNullOrEmpty(propertySchema.Format))
        {
            property["format"] = propertySchema.Format;
        }

        return property;
    }

    /// <summary>
    /// Extracts the tool name from a chat message for proper formatting.
    /// </summary>
    /// <param name="message">Chat message containing tool information</param>
    /// <returns>Tool name for formatting</returns>
    private string GetToolName(ChatMessage message)
    {
        // Extract tool name from message context
        if (!string.IsNullOrEmpty(message.Name))
        {
            // For browser tools, use the recipient name directly (e.g., "browser.search")
            if (message.Name.StartsWith("browser.", StringComparison.OrdinalIgnoreCase))
            {
                return message.Name;
            }
            // For other function tools, use the name as is
            return message.Name;
        }
        
        // Fallback for when no name is provided
        return "functions.tool_result";
    }
}