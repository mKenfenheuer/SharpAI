using System.Text;
using SharpAI.Models;

namespace SharpAI.Services.Harmony;

public class HarmonyFormatter
{
    private readonly ILogger<HarmonyFormatter> _logger;

    public HarmonyFormatter(ILogger<HarmonyFormatter> logger)
    {
        _logger = logger;
    }

    public string FormatPrompt(List<ChatMessage> messages, bool hasFunctions, ReasoningEffortLevel reasoningEffort, List<ToolDefinition>? tools = null, ResponseFormat? responseFormat = null)
    {
        var prompt = new StringBuilder();

        // System message with harmony format
        prompt.AppendLine($"<|start|>system<|message|>You are ChatGPT, a large language model trained by OpenAI.");
        prompt.AppendLine("Knowledge cutoff: 2024-06");
        prompt.AppendLine($"Current date: {DateTime.Now:yyyy-MM-dd}");
        prompt.AppendLine();
        prompt.AppendLine($"Reasoning: {reasoningEffort.ToString().ToLower()}");
        prompt.AppendLine();
        prompt.AppendLine("# Valid channels: analysis, commentary, final. Channel must be included for every message.");
        
        if (hasFunctions)
        {
            prompt.AppendLine("Calls to these tools must go to the commentary channel: 'functions'.");
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
            prompt.AppendLine();
            prompt.AppendLine(CreateToolsDefinition(tools));
        }

        // Add response format definition if specified
        if (responseFormat != null)
        {
            prompt.AppendLine();
            prompt.AppendLine(CreateResponseFormatDefinition(responseFormat));
        }

        prompt.AppendLine("<|end|>");

        // Add conversation messages (excluding system messages as they're in developer section)
        var conversationMessages = messages.Where(m => m.Role != ChatRole.System).ToList();
        
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

        return prompt.ToString();
    }

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

    private string GetToolName(ChatMessage message)
    {
        // Extract tool name from message context or use default
        // This would need to be implemented based on how tool messages are structured
        return "functions.tool_result";
    }
}