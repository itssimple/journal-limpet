using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.API.Profile
{
    public class EliteLastStarport
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("faction")]
        public string Faction { get; set; }
        [JsonPropertyName("minorfaction")]
        public string MinorFaction { get; set; }
        [JsonPropertyName("services")]
        public EliteStarportServices Services { get; set; }
    }
}
