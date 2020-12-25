using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.API.Profile
{
    public class EliteProfile : EliteBaseJsonObject
    {
        [JsonPropertyName("commander")]
        public EliteCommander Commander { get; set; }
        [JsonPropertyName("lastSystem")]
        public EliteLastSystem LastSystem { get; set; }
        [JsonPropertyName("lastStarport")]
        public EliteLastStarport LastStarport { get; set; }
    }
}
