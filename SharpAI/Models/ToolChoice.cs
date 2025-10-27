using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Controls which (if any) tool is called by the model.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<ToolChoice>))]
    public enum ToolChoice 
    { 
        /// <summary>
        /// The model will not call any tools and instead generates a message.
        /// </summary>
        None, 
        /// <summary>
        /// The model can pick between generating a message or calling one or more tools.
        /// </summary>
        Auto, 
        /// <summary>
        /// The model must call one or more tools before responding to the user.
        /// </summary>
        Required, 
        /// <summary>
        /// Forces the model to call a specific function.
        /// </summary>
        Function 
    }
}
