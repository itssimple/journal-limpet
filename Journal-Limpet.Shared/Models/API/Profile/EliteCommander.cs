using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.API.Profile
{
    public class EliteCommander : EliteBaseJsonObject
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("credits")]
        public long Credits { get; set; }
        [JsonPropertyName("debt")]
        public long Debt { get; set; }
        [JsonPropertyName("currentShipId")]
        public int CurrentShipId { get; set; }
        [JsonPropertyName("alive")]
        public bool Alive { get; set; }
        [JsonPropertyName("docked")]
        public bool Docked { get; set; }

        [JsonPropertyName("rank")]
        public EliteCommanderRank Rank { get; set; }
    }
}
