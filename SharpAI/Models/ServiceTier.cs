using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    [JsonConverter(typeof(LowercaseEnumConverter<ServiceTier>))]
    public enum ServiceTier { Auto, Default, Flex, Priority }
}
