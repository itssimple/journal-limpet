using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.User
{
    public interface IIntegrationBaseSettings
    {
    }

    public class BaseIntegrationSettings : IIntegrationBaseSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    public class EDSMIntegrationSettings : BaseIntegrationSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; }
        [JsonPropertyName("cmdrName")]
        public string CommanderName { get; set; }
    }
}
