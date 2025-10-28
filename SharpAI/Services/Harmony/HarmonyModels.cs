using System.Text;
using SharpAI.Models;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Contains data models and enums for the Harmony message format system.
/// Harmony is a structured format for AI conversations that supports multi-channel communication,
/// tool calls, and advanced conversation flow control.
/// </summary>

/// <summary>
/// Formatting tokens used by the Harmony renderer.
/// Ported from encoding.rs FormattingToken enum.
/// </summary>
public enum HarmonyFormattingToken
{
    Start,
    Message,
    EndMessage,
    EndMessageDoneSampling,
    EndMessageAssistantToTool,
    Refusal,
    ConstrainedFormat,
    Channel,
    BeginUntrusted,
    EndUntrusted,
    MetaSep,
    MetaEnd
}

public static class HarmonyFormattingTokenExtensions
{
    public static string AsString(this HarmonyFormattingToken token) => token switch
    {
        HarmonyFormattingToken.Start => "<|start|>",
        HarmonyFormattingToken.Message => "<|message|>",
        HarmonyFormattingToken.EndMessage => "<|end|>",
        HarmonyFormattingToken.EndMessageDoneSampling => "<|return|>",
        HarmonyFormattingToken.EndMessageAssistantToTool => "<|call|>",
        HarmonyFormattingToken.Refusal => "<|refusal|>",
        HarmonyFormattingToken.ConstrainedFormat => "<|constrain|>",
        HarmonyFormattingToken.Channel => "<|channel|>",
        HarmonyFormattingToken.BeginUntrusted => "<|untrusted|>",
        HarmonyFormattingToken.EndUntrusted => "<|end_untrusted|>",
        HarmonyFormattingToken.MetaSep => "<|meta_sep|>",
        HarmonyFormattingToken.MetaEnd => "<|meta_end|>",
        _ => throw new ArgumentOutOfRangeException(nameof(token))
    };
}

/// <summary>
/// Represents different communication channels in the Harmony format.
/// Allows the AI to express different types of content (final response, analysis, commentary).
/// </summary>
public enum HarmonyChannel
{
    /// <summary>
    /// The final response channel containing the main response to the user.
    /// </summary>
    Final,
    /// <summary>
    /// The analysis channel containing detailed analysis of the request or situation.
    /// </summary>
    Analysis,
    /// <summary>
    /// The commentary channel containing meta-commentary about the response process.
    /// </summary>
    Commentary,
}

public static class HarmonyChannelExtensions
{
    public static string AsString(this HarmonyChannel channel) => channel switch
    {
        HarmonyChannel.Final => "final",
        HarmonyChannel.Analysis => "analysis",
        HarmonyChannel.Commentary => "commentary",
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    public static HarmonyChannel FromString(string channelStr) => channelStr.ToLowerInvariant() switch
    {
        "final" => HarmonyChannel.Final,
        "analysis" => HarmonyChannel.Analysis,
        "commentary" => HarmonyChannel.Commentary,
        _ => throw new ArgumentException($"Unknown channel: {channelStr}")
    };
}

/// <summary>
/// Represents the role of a participant in a Harmony conversation.
/// Similar to standard chat roles but with additional developer role.
/// </summary>
public enum HarmonyRole
{
    /// <summary>
    /// System role for setting initial instructions and behavior.
    /// </summary>
    System,
    /// <summary>
    /// Developer role for development-specific instructions and context.
    /// </summary>
    Developer,
    /// <summary>
    /// User role for end-user messages and requests.
    /// </summary>
    User,
    /// <summary>
    /// Assistant role for AI responses and interactions.
    /// </summary>
    Assistant,
    /// <summary>
    /// Tool role for tool execution results and responses.
    /// </summary>
    Tool,
}

public static class HarmonyRoleExtensions
{
    public static string AsString(this HarmonyRole role) => role switch
    {
        HarmonyRole.User => "user",
        HarmonyRole.Assistant => "assistant",
        HarmonyRole.System => "system",
        HarmonyRole.Developer => "developer",
        HarmonyRole.Tool => "tool",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static HarmonyRole FromString(string roleStr) => roleStr.ToLowerInvariant() switch
    {
        "user" => HarmonyRole.User,
        "assistant" => HarmonyRole.Assistant,
        "system" => HarmonyRole.System,
        "developer" => HarmonyRole.Developer,
        "tool" => HarmonyRole.Tool,
        _ => throw new ArgumentException($"Unknown role: {roleStr}")
    };
}

/// <summary>
/// Represents assistant content in a Harmony message, including channel specification and optional tool calls.
/// </summary>
public class HarmonyAssistantContent : HarmonyMessageContent
{
    /// <summary>
    /// The communication channel for this content (final, analysis, commentary).
    /// </summary>
    public HarmonyChannel Channel { get; set; }
    /// <summary>
    /// The text content of the message (null if this is a tool call).
    /// </summary>
    public string? Content { get; set; }
    /// <summary>
    /// Tool call information if this content represents a tool invocation.
    /// </summary>
    public HarmonyToolCall? ToolCall { get; set; }

    internal override string? StringContent => Content;

    internal override string? ChannelString => Channel.AsString();

    internal override void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options)
    {
        into.Append(HarmonyFormattingToken.Channel.AsString());
        into.Append(Channel.AsString());
        into.Append(" ");
        if (ToolCall != null)
        {
            into.Append($"to={ToolCall.Namespace}.{ToolCall.ToolName}");
            into.Append(HarmonyFormattingToken.ConstrainedFormat.AsString());
            into.Append(ToolCall.ConstraintType);
            into.Append(HarmonyFormattingToken.Message.AsString());
            into.Append(ToolCall.Arguments);
        }
        else
        {
            into.Append(HarmonyFormattingToken.Message.AsString());
            into.Append(Content);
        }
    }
}

/// <summary>
/// Represents a complete message in the Harmony format with role, content, and completion status.
/// </summary>
public class HarmonyMessage
{
    /// <summary>
    /// The role of the message author.
    /// </summary>
    public HarmonyRole Role { get; set; }
    /// <summary>
    /// List of content parts that make up this message.
    /// </summary>
    public List<HarmonyMessageContent> Content { get; set; } = new List<HarmonyMessageContent>();
    /// <summary>
    /// Whether this message is complete or still being generated.
    /// </summary>
    public bool IsComplete { get; set; }

    internal void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options)
    {
        into.Append(HarmonyFormattingToken.Start.AsString());

        // If there are tool result contents skip the role and message tokens
        if (!Content.Any(m => m is HarmonyToolResultContent))
        {
            into.Append(Role.AsString());
            into.Append(HarmonyFormattingToken.Message.AsString());
        }

        foreach (var content in Content)
        {
            content.RenderInto(into, options);
        }

        into.Append(HarmonyFormattingToken.EndMessage.AsString());
    }
}

public class HarmonyToolCall
{
    public string Recipient { get; set; }
    public string Namespace { get; set; }
    public string ToolName { get; set; }
    public string Arguments { get; set; }
    public string? ConstraintType { get; set; }
}

public class HarmonyResponse
{
    public List<HarmonyMessage> Messages { get; set; } = new List<HarmonyMessage>();
    public HarmonyToolCall ToolCall => Messages?.Where(m => m.Role == HarmonyRole.Assistant)
                                                        .SelectMany(m => m.Content.OfType<HarmonyAssistantContent>())
                                                        .Where(ch => ch.ToolCall != null)
                                                        .Select(ch => ch.ToolCall)
                                                        .LastOrDefault();

    /// <summary>
    /// Extracts user-readable content from harmony response messages
    /// </summary>
    public string ExtractFinalContent()
    {
        var content = new StringBuilder();

        // Extract content from final channel if available
        var channels = Messages.Where(m => m.Role == HarmonyRole.Assistant)
            .SelectMany(m => m.Content.OfType<HarmonyAssistantContent>());

        var finalChannel = channels.FirstOrDefault(c => c.Channel == HarmonyChannel.Final);

        if (finalChannel != null)
        {
            content.Append(finalChannel.Content);
        }
        else if (channels.Any())
        {
            // If no final channel, use the last channel's content
            content.Append(channels.Last().Content);
        }

        return content.ToString().Trim();
    }


    /// <summary>
    /// Extracts user-readable content from harmony response messages
    /// </summary>
    public string ExtractReasoningContent()
    {
        var content = new StringBuilder();

        // Extract content from final channel if available
        var channels = Messages.Where(m => m.Role == HarmonyRole.Assistant)
            .SelectMany(m => m.Content.OfType<HarmonyAssistantContent>());

        var finalChannel = channels.FirstOrDefault(c => c.Channel == HarmonyChannel.Analysis);

        if (finalChannel != null)
        {
            content.Append(finalChannel.Content);
        }
        else if (channels.Any())
        {
            // If no final channel, use the last channel's content
            content.Append(channels.Last().Content);
        }

        return content.ToString().Trim();
    }
}



/// <summary>
/// Reasoning effort levels.
/// </summary>
public enum HarmonyReasoningEffort
{
    Low,

    Medium,

    High
}

public static class HarmonyReasoningEffortExtensions
{
    public static string AsString(this HarmonyReasoningEffort effort) => effort switch
    {
        HarmonyReasoningEffort.Low => "low",
        HarmonyReasoningEffort.Medium => "medium",
        HarmonyReasoningEffort.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(effort))
    };
    public static HarmonyReasoningEffort FromString(string effortStr) => effortStr.ToLowerInvariant() switch
    {
        "low" => HarmonyReasoningEffort.Low,
        "medium" => HarmonyReasoningEffort.Medium,
        "high" => HarmonyReasoningEffort.High,
        _ => throw new ArgumentException($"Unknown reasoning effort: {effortStr}")
    };
}

/// <summary>
/// Channel configuration for controlling message visibility.
/// </summary>
public class HarmonyChannelConfig
{
    public List<string> ValidChannels { get; set; } = new();
    public bool ChannelRequired { get; set; }

    public static HarmonyChannelConfig RequireChannels(IEnumerable<string> channels)
    {
        return new HarmonyChannelConfig
        {
            ValidChannels = channels.ToList(),
            ChannelRequired = true
        };
    }
}

/// <summary>
/// Tool description for function calling.
/// </summary>
public class HarmonyToolDescription
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }

    public static HarmonyToolDescription New(string name, string description, Dictionary<string, object>? parameters = null)
    {
        return new HarmonyToolDescription
        {
            Name = name,
            Description = description,
            Parameters = parameters
        };
    }
}

/// <summary>
/// Tool namespace configuration.
/// </summary>
public class HarmonyToolNamespaceConfig
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<HarmonyToolDescription> Tools { get; set; } = new();
    public HarmonyToolNamespaceConfig() { }

    public HarmonyToolNamespaceConfig(string name, string? description, IEnumerable<HarmonyToolDescription> tools)
    {
        Name = name;
        Description = description;
        Tools = tools.ToList();
    }

    public static HarmonyToolNamespaceConfig Browser()
    {
        // Browser tool configuration matching the detailed specification
        return new HarmonyToolNamespaceConfig("browser", @"Tool for browsing.
The `cursor` appears in brackets before each browsing display: `[{cursor}]`.
Cite information from the tool using the following format:
`【{cursor}†L{line_start}(-L{line_end})?】`, for example: `【6†L9-L11】` or `【8†L3】`.
Do not quote more than 10 words directly from the tool output.
sources=web (default: web)", new[]
        {
            HarmonyToolDescription.New("search", "Searches for information related to `query` and displays `topn` results.", new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["query"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Search query string"
                    },
                    ["topn"] = new Dictionary<string, object>
                    {
                        ["type"] = "number",
                        ["description"] = "Number of results to display",
                        ["default"] = 10
                    },
                    ["source"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Source to search",
                        ["default"] = "web"
                    }
                },
                ["required"] = new[] { "query" }
            }),
            HarmonyToolDescription.New("open", @"Opens the link `id` from the page indicated by `cursor` starting at line number `loc`, showing `num_lines` lines.
Valid link ids are displayed with the formatting: `【{id}†.*】`.
If `cursor` is not provided, the most recent page is implied.
If `id` is a string, it is treated as a fully qualified URL associated with `source`.
If `loc` is not provided, the viewport will be positioned at the beginning of the document or centered on the most relevant passage, if available.
Use this function without `id` to scroll to a new location of an opened page.", new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["id"] = new Dictionary<string, object>
                    {
                        ["oneOf"] = new object[]
                        {
                            new Dictionary<string, object> { ["type"] = "number" },
                            new Dictionary<string, object> { ["type"] = "string" }
                        },
                        ["description"] = "Link ID or URL to open",
                        ["default"] = -1
                    },
                    ["cursor"] = new Dictionary<string, object>
                    {
                        ["type"] = "number",
                        ["description"] = "Page cursor identifier",
                        ["default"] = -1
                    },
                    ["loc"] = new Dictionary<string, object>
                    {
                        ["type"] = "number",
                        ["description"] = "Line number to start viewing from",
                        ["default"] = -1
                    },
                    ["num_lines"] = new Dictionary<string, object>
                    {
                        ["type"] = "number",
                        ["description"] = "Number of lines to display",
                        ["default"] = -1
                    },
                    ["view_source"] = new Dictionary<string, object>
                    {
                        ["type"] = "boolean",
                        ["description"] = "Whether to view source code",
                        ["default"] = false
                    },
                    ["source"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Source identifier"
                    }
                },
                ["required"] = new string[] { }
            }),
            HarmonyToolDescription.New("find", "Finds exact matches of `pattern` in the current page, or the page given by `cursor`.", new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["pattern"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Pattern to search for"
                    },
                    ["cursor"] = new Dictionary<string, object>
                    {
                        ["type"] = "number",
                        ["description"] = "Page cursor identifier",
                        ["default"] = -1
                    }
                },
                ["required"] = new[] { "pattern" }
            })
        });
    }

    public static HarmonyToolNamespaceConfig Python()
    {
        // Simplified Python tool config
        return new HarmonyToolNamespaceConfig("python", "Python code execution", new[]
        {
            HarmonyToolDescription.New("execute", "Execute Python code", null)
        });
    }
}

/// <summary>
/// Base class for different content types.
/// </summary>
public abstract class HarmonyMessageContent
{
    internal abstract void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options);
    internal abstract string? StringContent { get; }
    internal abstract string? ChannelString { get; }
}

/// <summary>
/// Plain text content.
/// </summary>
public class HarmonyTextContent : HarmonyMessageContent
{
    public string Text { get; set; } = string.Empty;
    internal override string StringContent => Text;

    internal override string? ChannelString => null;

    public HarmonyTextContent() { }
    public HarmonyTextContent(string text) { Text = text; }

    internal override void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options)
    {
        into.Append(Text);
    }
}



/// <summary>
/// Plain text content.
/// </summary>
public class HarmonyToolResultContent : HarmonyMessageContent
{
    public HarmonyToolCall ToolCall { get; set; }
    public string Content { get; set; } = string.Empty;
    internal override string? StringContent => Content;
    internal override string? ChannelString => null;

    public HarmonyToolResultContent(HarmonyToolCall toolCall, string content)
    {
        ToolCall = toolCall;
        Content = content;
    }

    public HarmonyToolResultContent()
    {
    }

    internal override void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options)
    {
        into.Append($"{ToolCall.Namespace}.{ToolCall.ToolName}");
        into.Append(" to=assistant");
        into.Append(HarmonyFormattingToken.Channel.AsString());
        into.Append(HarmonyChannel.Commentary.AsString());
        into.Append(HarmonyFormattingToken.Message.AsString());
        into.Append(Content);
    }
}


/// <summary>
/// System-specific content with model configuration.
/// </summary>
public class HarmonySystemContent : HarmonyMessageContent
{
    public string? ModelIdentity { get; set; } = "You are ChatGPT, a large language model trained by OpenAI.";
    public HarmonyReasoningEffort? ReasoningEffort { get; set; } = HarmonyReasoningEffort.Medium;
    public string? ConversationStartDate { get; set; }
    public string? KnowledgeCutoff { get; set; } = "2024-06";
    public HarmonyChannelConfig? ChannelConfig { get; set; }
    public Dictionary<string, HarmonyToolNamespaceConfig>? Tools { get; set; }

    internal override string? StringContent {
        get
        {
            var sb = new StringBuilder();
            RenderInto(sb, null);
            return sb.ToString();
        }
    }

    internal override string? ChannelString => null;

    public HarmonySystemContent()
    {
        ChannelConfig = HarmonyChannelConfig.RequireChannels(new[] { "analysis", "commentary", "final" });
    }

    public static HarmonySystemContent New() => new();

    // Fluent interface
    public HarmonySystemContent WithModelIdentity(string modelIdentity)
    {
        ModelIdentity = modelIdentity;
        return this;
    }

    public HarmonySystemContent WithReasoningEffort(HarmonyReasoningEffort reasoningEffort)
    {
        ReasoningEffort = reasoningEffort;
        return this;
    }

    public HarmonySystemContent WithConversationStartDate(string conversationStartDate)
    {
        ConversationStartDate = conversationStartDate;
        return this;
    }

    public HarmonySystemContent WithKnowledgeCutoff(string knowledgeCutoff)
    {
        KnowledgeCutoff = knowledgeCutoff;
        return this;
    }

    public HarmonySystemContent WithChannelConfig(HarmonyChannelConfig channelConfig)
    {
        ChannelConfig = channelConfig;
        return this;
    }

    public HarmonySystemContent WithRequiredChannels(IEnumerable<string> channels)
    {
        ChannelConfig = HarmonyChannelConfig.RequireChannels(channels);
        return this;
    }

    public HarmonySystemContent WithTools(HarmonyToolNamespaceConfig nsConfig)
    {
        Tools ??= new Dictionary<string, HarmonyToolNamespaceConfig>();
        Tools[nsConfig.Name] = nsConfig;
        return this;
    }

    public HarmonySystemContent WithBrowserTool() => WithTools(HarmonyToolNamespaceConfig.Browser());
    public HarmonySystemContent WithPythonTool() => WithTools(HarmonyToolNamespaceConfig.Python());

    internal override void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options)
    {
        var sections = new List<string>();

        // Top section: model identity, knowledge cutoff, current date
        var topSection = new List<string>();
        if (!string.IsNullOrEmpty(ModelIdentity))
        {
            topSection.Add(ModelIdentity);
        }
        if (!string.IsNullOrEmpty(KnowledgeCutoff))
        {
            topSection.Add($"Knowledge cutoff: {KnowledgeCutoff}");
        }
        if (!string.IsNullOrEmpty(ConversationStartDate))
        {
            topSection.Add($"Current date: {ConversationStartDate}");
        }
        if (topSection.Count > 0)
        {
            sections.Add(string.Join("\n", topSection));
        }

        // Instructions and reasoning section
        var instructionsAndReasoning = new List<string>();
        if (ReasoningEffort.HasValue)
        {
            var effortStr = ReasoningEffort.Value switch
            {
                HarmonyReasoningEffort.Low => "low",
                HarmonyReasoningEffort.Medium => "medium",
                HarmonyReasoningEffort.High => "high",
                _ => throw new ArgumentOutOfRangeException()
            };
            instructionsAndReasoning.Add($"Reasoning: {effortStr}");
        }
        if (instructionsAndReasoning.Count > 0)
        {
            sections.Add(string.Join("\n", instructionsAndReasoning));
        }

        // Tools section
        if (Tools?.Count > 0)
        {
            sections.Add(Tools.Render(options));
        }

        // Channel configuration section
        if (ChannelConfig?.ValidChannels?.Count > 0)
        {
            var channels = string.Join(", ", ChannelConfig.ValidChannels);
            var channelsHeader = $"# Valid channels: {channels}.";
            
            if (ChannelConfig.ChannelRequired)
            {
                channelsHeader += " Channel must be included for every HarmonyMessage.";
            }
            
            if (Tools.Any())
            {
                channelsHeader += "\nCalls to these tools must go to the commentary channel: 'functions'.";
            }
            
            sections.Add(channelsHeader);
        }

        into.Append(string.Join("\n\n", sections));
    }
}

/// <summary>
/// Developer-specific content with instructions and tools.
/// </summary>
public class HarmonyDeveloperContent : HarmonyMessageContent
{
    public string? Instructions { get; set; }
    public Dictionary<string, HarmonyToolNamespaceConfig>? Tools { get; set; }
    public List<HarmonyResponseFormat> ResponseFormats { get; set; } = new List<HarmonyResponseFormat>();
    internal override string? StringContent => Instructions;
    internal override string? ChannelString => null;

    public static HarmonyDeveloperContent New() => new();

    public HarmonyDeveloperContent WithInstructions(string instructions)
    {
        Instructions = instructions;
        return this;
    }

    public HarmonyDeveloperContent WithTools(HarmonyToolNamespaceConfig nsConfig)
    {
        Tools ??= new Dictionary<string, HarmonyToolNamespaceConfig>();
        Tools[nsConfig.Name] = nsConfig;
        return this;
    }

    public HarmonyDeveloperContent WithFunctionTools(IEnumerable<HarmonyToolDescription> tools)
    {
        return WithTools(new HarmonyToolNamespaceConfig("functions", null, tools));
    }

    public HarmonyDeveloperContent WithResponseFormat(HarmonyResponseFormat responseFormat)
    {
        ResponseFormats.Add(responseFormat);
        return this;
    }

    internal override void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options)
    {
        var sections = new List<string>();

        if (!string.IsNullOrEmpty(Instructions))
        {
            sections.Add("# Instructions");
            sections.Add(Instructions);
        }

        if (Tools?.Count > 0)
        {
            sections.Add(Tools.Render(options));
        }

        if (ResponseFormats.Count > 0)
        {
            sections.Add("# Response Formats");
            foreach (var responseFormat in ResponseFormats)
            {
                sections.Add($"## {responseFormat.Name}");
                if (!string.IsNullOrEmpty(responseFormat.Description))
                {
                    sections.Add($"// {responseFormat.Description}");
                }
                sections.Add($"{responseFormat.Schema}");
            }
        }

        into.Append(string.Join("\n\n", sections));
    }
}

public class HarmonyResponseFormat
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Schema { get; set; }
}


/// <summary>
/// Configuration for rendering conversations.
/// </summary>
public class HarmonyMessageRenderConfig
{
    public bool AutoDropAnalysis { get; set; } = true;
}