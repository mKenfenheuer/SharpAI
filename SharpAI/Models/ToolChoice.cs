using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    [JsonConverter(typeof(LowercaseEnumConverter<ToolChoice>))]
    public enum ToolChoice { None, Auto, Required, Function }
}
