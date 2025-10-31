using System.Text;
using System.Text.Json;
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

    internal override List<HarmonyToolDescription>? Tools => null;

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
    public string Id { get; internal set; }
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

    /// <summary>
    /// Extracts commentary content from harmony response messages
    /// </summary>
    public string ExtractCommentaryContent()
    {
        var content = new StringBuilder();

        // Extract content from commentary channel if available
        var channels = Messages.Where(m => m.Role == HarmonyRole.Assistant)
            .SelectMany(m => m.Content.OfType<HarmonyAssistantContent>());

        var commentaryChannel = channels.FirstOrDefault(c => c.Channel == HarmonyChannel.Commentary);

        if (commentaryChannel != null)
        {
            content.Append(commentaryChannel.Content);
        }

        return content.ToString().Trim();
    }
}

/// <summary>
/// Represents a delta (partial update) in harmony format responses for streaming.
/// Contains incremental updates for different channels during parsing.
/// </summary>
public class HarmonyDelta
{
    public HarmonyRole? Role { get; set; }
    public HarmonyChannel? Channel { get; set; }
    public string? Content { get; set; }
    public string? FinalContent { get; set; }
    public string? ReasoningContent { get; set; }
    public string? CommentaryContent { get; set; }
    public HarmonyToolCall? ToolCall { get; set; }
    public bool IsComplete { get; set; }
    public bool HasNewContent => !string.IsNullOrEmpty(Content) || 
                                !string.IsNullOrEmpty(FinalContent) || 
                                !string.IsNullOrEmpty(ReasoningContent) || 
                                !string.IsNullOrEmpty(CommentaryContent) ||
                                ToolCall != null;
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

    public static HarmonyChannelConfig RequireChannels(IEnumerable<HarmonyChannel> channels)
    {
        return new HarmonyChannelConfig
        {
            ValidChannels = channels.Select(c => c.ToString()).ToList(),
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



    internal JsonSchemaFormat GetJsonSchema()
    {
        var format = new JsonSchemaFormat
        {
            Name = this.Name,
            Description = this.Description,
            Schema = JsonSerializer.Deserialize<dynamic>(JsonSerializer.Serialize(this.Parameters)),
            Strict = true
        };
        return format;
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
}

/// <summary>
/// Base class for different content types.
/// </summary>
public abstract class HarmonyMessageContent
{
    internal abstract void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options);
    internal abstract string? StringContent { get; }
    internal abstract string? ChannelString { get; }
    internal abstract List<HarmonyToolDescription>? Tools { get; }
}

/// <summary>
/// Plain text content.
/// </summary>
public class HarmonyTextContent : HarmonyMessageContent
{
    public string Text { get; set; } = string.Empty;
    internal override string StringContent => Text;

    internal override string? ChannelString => null;

    internal override List<HarmonyToolDescription>? Tools => null;

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

    internal override List<HarmonyToolDescription>? Tools => null;

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
/// System-specific content with Harmony-compatible model configuration.
/// </summary>
public class HarmonySystemContent : HarmonyMessageContent
{
    public string? ModelIdentity { get; set; } = "You are ChatGPT, a large language model trained by OpenAI.";
    public HarmonyReasoningEffort? ReasoningEffort { get; set; } = HarmonyReasoningEffort.Medium;
    public string? ConversationStartDate { get; set; }
    public string? KnowledgeCutoff { get; set; } = "2024-06";
    public HarmonyChannelConfig? ChannelConfig { get; set; }

    internal override string? StringContent {
        get
        {
            var sb = new StringBuilder();
            RenderInto(sb, null);
            return sb.ToString();
        }
    }

    internal override string? ChannelString => null;

    internal override List<HarmonyToolDescription>? Tools => null;

    public HarmonySystemContent()
    {
        ChannelConfig = HarmonyChannelConfig.RequireChannels(new[] { HarmonyChannel.Analysis, HarmonyChannel.Commentary, HarmonyChannel.Final });
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

    public HarmonySystemContent WithRequiredChannels(IEnumerable<HarmonyChannel> channels)
    {
        ChannelConfig = HarmonyChannelConfig.RequireChannels(channels);
        return this;
    }

    internal override void RenderInto(StringBuilder into, HarmonyMessageRenderConfig? options)
    {
        var sections = new List<string>();

        // Top section: model identity, knowledge cutoff, current date
        var topSection = new List<string>();
        if (!string.IsNullOrEmpty(ModelIdentity))
            topSection.Add(ModelIdentity);
        if (!string.IsNullOrEmpty(KnowledgeCutoff))
            topSection.Add($"Knowledge cutoff: {KnowledgeCutoff}");
        if (!string.IsNullOrEmpty(ConversationStartDate))
            topSection.Add($"Current date: {ConversationStartDate}");
        if (topSection.Count > 0)
            sections.Add(string.Join("\n", topSection));

        // Reasoning
        if (ReasoningEffort.HasValue)
        {
            var effortStr = ReasoningEffort.Value switch
            {
                HarmonyReasoningEffort.Low => "low",
                HarmonyReasoningEffort.Medium => "medium",
                HarmonyReasoningEffort.High => "high",
                _ => throw new ArgumentOutOfRangeException()
            };
            sections.Add($"Reasoning: {effortStr}");
        }

        // Channels
        if (ChannelConfig?.ValidChannels?.Count > 0)
        {
            var channels = string.Join(", ", ChannelConfig.ValidChannels);
            var channelsHeader = $"# Valid channels: {channels}.";
            if (ChannelConfig.ChannelRequired)
                channelsHeader += " Channel must be included for every message.";
            sections.Add(channelsHeader);
        }

        into.Append(string.Join("\n\n", sections));
    }
}

/// <summary>
/// Developer-specific content with Harmony-compatible instructions and tools.
/// </summary>
public class HarmonyDeveloperContent : HarmonyMessageContent
{
    public string? Instructions { get; set; }
    public Dictionary<string, HarmonyToolNamespaceConfig>? DefinedTools { get; set; }
    public List<HarmonyResponseFormat> ResponseFormats { get; set; } = new List<HarmonyResponseFormat>();

    internal override string? StringContent => Instructions;
    internal override string? ChannelString => null;

    internal override List<HarmonyToolDescription>? Tools => DefinedTools?.SelectMany(kv => kv.Value.Tools.Select(t => new HarmonyToolDescription()
    {
        Name = $"{kv.Key}.{t.Name}",
        Description = t.Description,
        Parameters = t.Parameters
    })).ToList();

    public static HarmonyDeveloperContent New() => new();

    public HarmonyDeveloperContent WithInstructions(string instructions)
    {
        Instructions = instructions;
        return this;
    }

    public HarmonyDeveloperContent WithTools(HarmonyToolNamespaceConfig nsConfig)
    {
        DefinedTools ??= new Dictionary<string, HarmonyToolNamespaceConfig>();
        DefinedTools[nsConfig.Name] = nsConfig;
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

        if (DefinedTools?.Count > 0)
            sections.Add(DefinedTools.Render(options));

        if (ResponseFormats.Count > 0)
        {
            sections.Add("# Response Formats");
            foreach (var responseFormat in ResponseFormats)
            {
                sections.Add($"## {responseFormat.Name}");
                if (!string.IsNullOrEmpty(responseFormat.Description))
                    sections.Add($"// {responseFormat.Description}");
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