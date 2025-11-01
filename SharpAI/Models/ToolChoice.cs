using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Controls which (if any) tool is called by the model.
    /// Can be either a string mode ("none", "auto", "required") or an object specifying a specific tool.
    /// </summary>
    [JsonConverter(typeof(ToolChoiceConverter))]
    public class ToolChoice 
    {
        /// <summary>
        /// The mode when tool_choice is a string value.
        /// </summary>
        public ToolChoiceMode? Mode { get; set; }

        /// <summary>
        /// The specific tool to call when tool_choice is an object.
        /// </summary>
        public ToolChoiceObject? SpecificTool { get; set; }

        /// <summary>
        /// Creates a ToolChoice from a mode string.
        /// </summary>
        public static ToolChoice FromMode(ToolChoiceMode mode) => new ToolChoice { Mode = mode };

        /// <summary>
        /// Creates a ToolChoice from a specific tool object.
        /// </summary>
        public static ToolChoice FromTool(ToolChoiceObject tool) => new ToolChoice { SpecificTool = tool };

        /// <summary>
        /// Checks if this represents a mode-based choice.
        /// </summary>
        public bool IsMode => Mode.HasValue;

        /// <summary>
        /// Checks if this represents a specific tool choice.
        /// </summary>
        public bool IsSpecificTool => SpecificTool != null;
    }

    /// <summary>
    /// The mode for tool choice when specified as a string.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<ToolChoiceMode>))]
    public enum ToolChoiceMode
    {
        /// <summary>
        /// The model will not call any tool and instead generates a message.
        /// </summary>
        None,

        /// <summary>
        /// The model can pick between generating a message or calling one or more tools.
        /// </summary>
        Auto,

        /// <summary>
        /// The model must call one or more tools before responding to the user.
        /// </summary>
        Required
    }

    /// <summary>
    /// Specifies a specific tool to call when tool_choice is an object.
    /// </summary>
    public class ToolChoiceObject
    {
        /// <summary>
        /// The type of the tool. Currently only "function" is supported.
        /// </summary>
        [JsonPropertyName("type")]
        public ToolType Type { get; set; }

        /// <summary>
        /// The function to call.
        /// </summary>
        [JsonPropertyName("function")]
        public ToolChoiceFunction? Function { get; set; }
    }

    /// <summary>
    /// Function specification for tool choice.
    /// </summary>
    public class ToolChoiceFunction
    {
        /// <summary>
        /// The name of the function to call.
        /// </summary>
        [JsonPropertyName("name")]
        public required string Name { get; set; }
    }

    /// <summary>
    /// Custom JSON converter for ToolChoice that handles both string and object forms.
    /// </summary>
    public class ToolChoiceConverter : JsonConverter<ToolChoice>
    {
        public override ToolChoice? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                // Handle string form: "none", "auto", "required"
                var value = reader.GetString()?.ToLowerInvariant();
                return value switch
                {
                    "none" => ToolChoice.FromMode(ToolChoiceMode.None),
                    "auto" => ToolChoice.FromMode(ToolChoiceMode.Auto),
                    "required" => ToolChoice.FromMode(ToolChoiceMode.Required),
                    _ => throw new JsonException($"Invalid tool_choice value: {value}")
                };
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Handle object form: {"type": "function", "function": {"name": "my_function"}}
                var tool = JsonSerializer.Deserialize<ToolChoiceObject>(ref reader, options);
                return tool != null ? ToolChoice.FromTool(tool) : null;
            }
            else if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            throw new JsonException($"Unexpected token type for tool_choice: {reader.TokenType}");
        }

        public override void Write(Utf8JsonWriter writer, ToolChoice value, JsonSerializerOptions options)
        {
            if (value.IsMode)
            {
                // Write as string
                var modeString = value.Mode switch
                {
                    ToolChoiceMode.None => "none",
                    ToolChoiceMode.Auto => "auto",
                    ToolChoiceMode.Required => "required",
                    _ => throw new JsonException($"Invalid ToolChoiceMode: {value.Mode}")
                };
                writer.WriteStringValue(modeString);
            }
            else if (value.IsSpecificTool && value.SpecificTool != null)
            {
                // Write as object
                JsonSerializer.Serialize(writer, value.SpecificTool, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
