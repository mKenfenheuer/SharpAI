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

    public string FormatPrompt(List<ChatMessage> messages, bool hasFunctions, ReasoningEffortLevel reasoningEffort, List<ToolDefinition>? tools = null)
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

    private string ConvertParametersToTypeScript(object? parameters)
    {
        if (parameters == null) return "";

        try
        {
            var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(parameters.ToString() ?? "");
            
            if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object && jsonElement.TryGetProperty("properties", out var properties))
            {
                var tsBuilder = new StringBuilder();
                tsBuilder.Append("{");
                
                var props = new List<string>();
                foreach (var prop in properties.EnumerateObject())
                {
                    var propName = prop.Name;
                    var propDef = prop.Value;
                    
                    var tsType = "any";
                    var description = "";

                    if (propDef.TryGetProperty("type", out var typeElement))
                    {
                        tsType = typeElement.GetString() switch
                        {
                            "string" => "string",
                            "number" => "number",
                            "integer" => "number",
                            "boolean" => "boolean",
                            "array" => "any[]",
                            "object" => "any",
                            _ => "any"
                        };
                    }

                    if (propDef.TryGetProperty("description", out var descElement))
                    {
                        description = descElement.GetString() ?? "";
                    }

                    if (propDef.TryGetProperty("enum", out var enumElement))
                    {
                        var enumValues = enumElement.EnumerateArray()
                            .Select(e => $"\"{e.GetString()}\"")
                            .ToArray();
                        tsType = string.Join(" | ", enumValues);
                    }

                    var propDeclaration = $"{propName}?"; // Always optional for simplicity
                    
                    if (!string.IsNullOrEmpty(description))
                    {
                        props.Add($"\n// {description}\n{propDeclaration}: {tsType}");
                    }
                    else
                    {
                        props.Add($"{propDeclaration}: {tsType}");
                    }
                }
                
                tsBuilder.Append(string.Join(",\n", props));
                tsBuilder.Append("\n}");
                return tsBuilder.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert parameters to TypeScript format");
        }

        return "";
    }

    private string GetToolName(ChatMessage message)
    {
        // Extract tool name from message context or use default
        // This would need to be implemented based on how tool messages are structured
        return "functions.tool_result";
    }
}