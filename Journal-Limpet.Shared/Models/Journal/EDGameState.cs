using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.Journal
{
    public class EDGameState
    {
        [JsonPropertyName("systemAddress")]
        public long SystemAddress { get; set; }
        [JsonPropertyName("systemName")]
        public string SystemName { get; set; }
        [JsonPropertyName("systemCoordinates")]
        public List<float> SystemCoordinates { get; set; }
        [JsonPropertyName("marketId")]
        public long MarketId { get; set; }
        [JsonPropertyName("stationName")]
        public string StationName { get; set; }
        [JsonPropertyName("shipId")]
        public long ShipId { get; set; }
    }
}
