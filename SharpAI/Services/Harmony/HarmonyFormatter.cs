using System.Text.Json;
using System.Text;
using SharpAI.Models;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Formats chat messages and tool definitions into the Harmony prompt format using the Openai.Harmony library.
/// This version uses only the library without any fallback or custom formatting.
/// </summary>
public class HarmonyFormatter : IDisposable
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
    /// Formats chat messages and configuration into a Harmony-compatible prompt using the Openai.Harmony library.
    /// </summary>
    /// <param name="messages">List of chat messages to format</param>
    /// <param name="hasFunctions">Whether function calling is enabled</param>
    /// <param name="reasoningEffort">Level of reasoning effort to apply</param>
    /// <param name="tools">Optional list of tool definitions</param>
    /// <param name="responseFormat">Optional response format specification</param>
    /// <returns>Formatted prompt string</returns>
    public string FormatPrompt(List<ChatMessage> messages, ReasoningEffortLevel reasoningEffort, List<ToolDefinition>? tools = null, ResponseFormat? responseFormat = null)
    {
        _logger.LogDebug("Formatting prompt with {MessageCount} messages, functions: {HasFunctions}, reasoning: {ReasoningEffort}", 
            messages.Count, tools?.Count ?? 0, reasoningEffort);

        // Convert to Harmony conversation format
        var conversation = ConvertToHarmonyConversation(messages, reasoningEffort, tools, responseFormat);

        // Format the conversation into Harmony format string
        var formattedPrompt = FormatHarmonyConversation(conversation);

        return formattedPrompt;
    }

    /// <summary>
    /// Formats a list of HarmonyMessage objects into the proper Harmony format string
    /// </summary>
    public string FormatHarmonyConversation(List<HarmonyMessage> conversation)
    {
        var result = new StringBuilder();
        var config = new HarmonyMessageRenderConfig();
        config.AutoDropAnalysis = false;

        conversation.RenderForCompletionInto(HarmonyRole.Assistant, result, config);

        return result.ToString();
    }

    /// <summary>
    /// Converts SharpAI messages to Harmony conversation format
    /// </summary>
    public List<HarmonyMessage> ConvertToHarmonyConversation(List<ChatMessage> messages, ReasoningEffortLevel reasoningEffort, List<ToolDefinition>? tools, ResponseFormat? responseFormat)
    {
        var harmonyMessages = new List<HarmonyMessage>();

        // Add system message with proper configuration
        var systemContent = CreateSystemContent(reasoningEffort, tools);

        var systemMessage = new HarmonyMessage
        {
            Role = HarmonyRole.System,
            Content = new List<HarmonyMessageContent> { systemContent }
        };

        if(messages.Any(m => m.Role == ChatRole.System && !string.IsNullOrEmpty(m.Content?.ToString())) || (tools != null && tools.Any()) || responseFormat != null)
        {
            // If the first message is a system message, include its content as well
            systemMessage.Content.Add(CreateDeveloperContent(tools, messages.First(m => m.Role == ChatRole.System), responseFormat));
        }

        harmonyMessages.Add(systemMessage);

        foreach (var message in messages)
        {
            // Skip system messages since we handle them specially
            if (message.Role == ChatRole.System)
            {
                continue;
            }

            if (message.Role == ChatRole.Assistant && message.ToolCalls != null && message.ToolCalls.Any())
            {
                // Tool call messages are handled within the content types
                foreach (var toolCall in message.ToolCalls ?? [])
                {
                    harmonyMessages.Add(new HarmonyMessage
                    {
                        Role = HarmonyRole.Tool,
                        Content = new List<HarmonyMessageContent>
                        {
                            new HarmonyAssistantContent
                            {
                                Channel = HarmonyChannel.Commentary,
                                ToolCall = new HarmonyToolCall
                                {
                                    ToolName = toolCall.Function.Name,
                                    Namespace = toolCall.Function.Namespace,
                                    Arguments = toolCall.Function.Arguments
                                }
                            }
                        },
                    });
                }
                continue; 
            }
            
            if (message.Role == ChatRole.Tool)
            {
                // Tool result messages are handled within the content types
                if (message.Name == null)
                {
                    _logger.LogDebug("Tool message missing Name property, taking from matching ToolCall with id {ToolCallId}.", message.ToolCallId);
                    var matchingToolCall = messages.Where(m => m.ToolCalls != null).SelectMany(m => m.ToolCalls).FirstOrDefault(tc => tc.CallId == message.ToolCallId);
                    if (matchingToolCall != null)
                    {
                        message.Name = matchingToolCall.Function.Name;
                    }
                    else
                    {
                        _logger.LogWarning("No matching ToolCall found for tool message with id {ToolCallId}. tool name will be \"unknown_tool\"", message.ToolCallId);
                    }
                }
                harmonyMessages.Add(new HarmonyMessage
                {
                    Role = HarmonyRole.Tool,
                    Content = new List<HarmonyMessageContent>
                    {
                        new HarmonyToolResultContent
                        {
                            ToolCall = new HarmonyToolCall
                            {
                                ToolName = message.Name ?? "unknown_tool",
                                Arguments = message.Content?.ToString() ?? "{}"
                            },
                            Content = message.Content?.ToString() ?? string.Empty
                        }
                    },
                });
                continue; 
            }

            var role = ConvertRole(message.Role);
            var content = new List<HarmonyMessageContent>();

            // Add text content
            var contentText = message.Content?.ToString();
            if (!string.IsNullOrEmpty(contentText))
            {
                content.Add(new HarmonyTextContent(contentText));
            }

            // Add tool calls if present (these will be handled by the Harmony library)
            if (message.ToolCalls?.Any() == true)
            {
                foreach (var toolCall in message.ToolCalls)
                {
                    // The Harmony library should handle tool call formatting
                    var toolContent = new HarmonyTextContent($"Function call: {toolCall.Function.Name}({toolCall.Function.Arguments})");
                    content.Add(toolContent);
                }
            }

            var harmonyMessage = new HarmonyMessage
            {
                Role = role,
                Content = content
            };

            // Note: Channel and Recipient are now handled within content types, not at message level
            // Tool results would be handled through specific content types if needed

            harmonyMessages.Add(harmonyMessage);
        }

        return new List<HarmonyMessage>(harmonyMessages);
    }

    /// <summary>
    /// Creates a system message with appropriate configuration for the Harmony library
    /// </summary>
    private HarmonySystemContent CreateSystemContent(ReasoningEffortLevel reasoningEffort, List<ToolDefinition>? tools)
    {
        var systemContent = HarmonySystemContent.New()
            .WithConversationStartDate(DateTime.Now.ToString("yyyy-MM-dd"))
            .WithReasoningEffort(ConvertReasoningEffort(reasoningEffort))
            .WithRequiredChannels(new[] { HarmonyChannel.Analysis, HarmonyChannel.Commentary, HarmonyChannel.Final });

        return systemContent;
    }

    /// <summary>
    /// Creates developer content with tool definitions and configuration
    /// </summary>
    private HarmonyDeveloperContent CreateDeveloperContent(List<ToolDefinition>? tools, ChatMessage systemMessage, ResponseFormat? responseFormat)
    {
        var developerContent = HarmonyDeveloperContent.New();

        if (!string.IsNullOrEmpty(systemMessage.Content?.ToString()))
        {
            developerContent = developerContent.WithInstructions(systemMessage.Content?.ToString() ?? "");
        }

        // Add tools if provided
        if (tools?.Any() == true)
        {
            var harmonyTools = tools.Select(tool => HarmonyToolDescription.New(
                tool.Function.Name,
                tool.Function.Description ?? "",
                ConvertParametersToDict(tool.Function.Parameters)
            )).ToArray();

            var toolNamespace = new HarmonyToolNamespaceConfig("functions", "Available functions", harmonyTools);
            developerContent = developerContent.WithTools(toolNamespace);
        }

        // Add response format constraint if specified
        if (responseFormat != null)
        {
            developerContent = developerContent.WithResponseFormat(new HarmonyResponseFormat()
            {
                Name = responseFormat.JsonSchema?.Name ?? "Response Format",
                Schema = JsonSerializer.Serialize(responseFormat.JsonSchema?.Schema ?? new
                {
                }),
                Description = responseFormat.JsonSchema?.Description
            });
        }

        return developerContent;
    }

    


    /// <summary>
    /// Converts SharpAI reasoning effort to Harmony reasoning effort
    /// </summary>
    private HarmonyReasoningEffort ConvertReasoningEffort(ReasoningEffortLevel level)
    {
        return level switch
        {
            ReasoningEffortLevel.Low => HarmonyReasoningEffort.Low,
            ReasoningEffortLevel.Medium => HarmonyReasoningEffort.Medium,
            ReasoningEffortLevel.High => HarmonyReasoningEffort.High,
            _ => HarmonyReasoningEffort.Medium
        };
    }

    /// <summary>
    /// Converts function parameters to dictionary format for Harmony library
    /// </summary>
    private Dictionary<string, object>? ConvertParametersToDict(FunctionParameters? parameters)
    {
        if (parameters?.Properties == null)
            return null;

        var result = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>()
        };

        var properties = new Dictionary<string, object>();
        foreach (var prop in parameters.Properties)
        {
            properties[prop.Key] = ConvertPropertyToDict(prop.Value);
        }
        result["properties"] = properties;

        if (parameters.Required?.Any() == true)
        {
            result["required"] = parameters.Required;
        }

        return result;
    }

    /// <summary>
    /// Converts property schema to dictionary format
    /// </summary>
    private Dictionary<string, object> ConvertPropertyToDict(PropertySchema schema)
    {
        var result = new Dictionary<string, object>();

        if (schema.Type != null)
            result["type"] = schema.Type;

        if (!string.IsNullOrEmpty(schema.Description))
            result["description"] = schema.Description;

        if (schema.Enum?.Any() == true)
            result["enum"] = schema.Enum;

        return result;
    }

    /// <summary>
    /// Converts SharpAI role to Harmony role
    /// </summary>
    private HarmonyRole ConvertRole(ChatRole role)
    {
        return role switch
        {
            ChatRole.System => HarmonyRole.System,
            ChatRole.User => HarmonyRole.User,
            ChatRole.Assistant => HarmonyRole.Assistant,
            ChatRole.Tool => HarmonyRole.Tool,
            _ => HarmonyRole.User
        };
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        // No unmanaged resources to dispose
    }
}