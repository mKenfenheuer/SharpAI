using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Specifies the service tier to use for the request.
    /// Controls performance characteristics and resource allocation.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<ServiceTier>))]
    public enum ServiceTier 
    { 
        /// <summary>
        /// Automatically select the appropriate service tier.
        /// </summary>
        Auto, 
        /// <summary>
        /// Use the default service tier.
        /// </summary>
        Default, 
        /// <summary>
        /// Flexible service tier with variable performance.
        /// </summary>
        Flex, 
        /// <summary>
        /// Priority service tier for high-priority requests.
        /// </summary>
        Priority 
    }
}
