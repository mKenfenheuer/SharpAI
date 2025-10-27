using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpAI.Services.Harmony;


/// <summary>
/// Extension methods for the HarmonyEncoding to support rendering.
/// These methods are ported from the Rust implementation in rs.
/// </summary>
public static class HarmonyMessageRenderer
{

    /// <summary>
    /// Render a conversation into the provided token list.
    /// </summary>
    public static void RenderInto(
        
        this List<HarmonyMessage> messages,
        StringBuilder into,
        HarmonyMessageRenderConfig? config = null)
    {
        config ??= new HarmonyMessageRenderConfig();

        foreach (var HarmonyMessage in messages)
        {
            HarmonyMessage.RenderInto(into, config);
        }
    }

    /// <summary>
    /// Render a conversation for completion into the provided token list.
    /// </summary>
    public static void RenderForCompletionInto(
        
        this List<HarmonyMessage> messages,
        HarmonyRole nextTurnRole,
        StringBuilder into,
        HarmonyMessageRenderConfig? config = null)
    {
        messages.RenderInto(into, config);

        // Add the next turn role
        into.Append(HarmonyFormattingToken.Message.AsString());
        into.Append(nextTurnRole.AsString());
    }

    /// <summary>
    /// Template the tools section for system/developer content rendering.
    /// </summary>
    public static string Render(this Dictionary<string, HarmonyToolNamespaceConfig>? tools, HarmonyMessageRenderConfig? options)
    {
        if (tools == null || tools.Count == 0)
        {
            return string.Empty;
        }

        var toolSections = new List<string>();
        toolSections.Add("# Tools");
        
        foreach (var nsConfig in tools.Values)
        {
            var toolSectionContent = new List<string>();
            toolSectionContent.Add($"## {nsConfig.Name}\n");
            
            if (!string.IsNullOrEmpty(nsConfig.Description))
            {
                var descriptionLines = nsConfig.Description.Split('\n');
                foreach (var line in descriptionLines)
                {
                    if (nsConfig.Tools.Count > 0)
                    {
                        toolSectionContent.Add($"// {line}");
                    }
                    else
                    {
                        toolSectionContent.Add(line);
                    }
                }
            }
            
            if (nsConfig.Tools.Count > 0)
            {
                toolSectionContent.Add($"namespace {nsConfig.Name} {{");
                
                foreach (var tool in nsConfig.Tools)
                {
                    var descriptionLines = tool.Description.Split('\n');
                    foreach (var line in descriptionLines)
                    {
                        toolSectionContent.Add($"// {line}");
                    }
                    
                    if (tool.Parameters != null)
                    {
                        var paramType = tool.Parameters.JsonSchemaToTypescript("");
                        toolSectionContent.Add($"type {tool.Name} = (_: {paramType}) => any;\n");
                    }
                    else
                    {
                        toolSectionContent.Add($"type {tool.Name} = () => any;\n");
                    }
                }
                
                toolSectionContent.Add($"}} // namespace {nsConfig.Name}");
            }
            
            toolSections.Add(string.Join("\n", toolSectionContent));
        }
        
        return string.Join("\n\n", toolSections);
    }

    /// <summary>
    /// Convert JSON schema to TypeScript type definition.
    /// Simplified version of the Rust implementation.
    /// </summary>
    private static string JsonSchemaToTypescript(this Dictionary<string, object> schema, string indent)
    {
        // This is a simplified implementation. The full Rust version is much more complex.
        // For now, we'll return a basic object type representation.
        if (schema.TryGetValue("type", out var typeValue) && typeValue is string typeStr)
        {
            return typeStr switch
            {
                "object" => "object",
                "string" => "string",
                "number" or "integer" => "number",
                "boolean" => "boolean",
                "array" => "Array<any>",
                _ => "any"
            };
        }
        
        return "any";
    }
}