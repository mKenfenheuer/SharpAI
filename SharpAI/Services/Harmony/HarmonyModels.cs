using SharpAI.Models;

namespace SharpAI.Services.Harmony;

public class ParsedResponse
{
    public string Content { get; set; } = string.Empty;
    public bool FunctionCall { get; set; }
    public string? Reasoning { get; set; }
    public List<HarmonyToolCall> ToolCalls { get; set; } = new List<HarmonyToolCall>();
}

public class HarmonyMessage
{
    public string Role { get; set; } = string.Empty;
    public string? Channel { get; set; }
    public string? Recipient { get; set; }
    public string? Constraint { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class HarmonyToolCall
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
}

public class HarmonyStreamState
{
    public string RawResponse { get; set; } = string.Empty;
    public string CurrentChannel { get; set; } = string.Empty;
    public string FinalContent { get; set; } = string.Empty;
    public string AnalysisContent { get; set; } = string.Empty;
    public List<HarmonyToolCall> ToolCalls { get; set; } = new List<HarmonyToolCall>();
    public int LastFinalStreamedLength { get; set; } = 0;
    public bool HasToolCall { get; set; } = false;
    public bool IsComplete { get; set; } = false;
    public string? CompletionReason { get; set; }
}

public class HarmonyStreamDelta
{
    public string? FinalContent { get; set; }
    public string? AnalysisContent { get; set; }
    public List<HarmonyToolCall>? NewToolCalls { get; set; }
    public bool IsComplete { get; set; }
    public string? CompletionReason { get; set; }
}