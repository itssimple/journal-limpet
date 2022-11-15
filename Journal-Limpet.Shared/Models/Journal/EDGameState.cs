using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.Journal
{
    public class EDGameState
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }
        [JsonPropertyName("systemAddress")]
        public long? SystemAddress { get; set; }
        [JsonPropertyName("systemName")]
        public string SystemName { get; set; }
        [JsonPropertyName("systemCoordinates")]
        public JsonElement? SystemCoordinates { get; set; }
        [JsonPropertyName("marketId")]
        public long? MarketId { get; set; }
        [JsonPropertyName("stationName")]
        public string StationName { get; set; }
        [JsonPropertyName("shipId")]
        public long? ShipId { get; set; }
        [JsonPropertyName("sendEvents")]
        public bool SendEvents { get; set; } = true;
        [JsonPropertyName("bodyName")]
        public string BodyName { get; set; }
        [JsonPropertyName("bodyId")]
        public long? BodyId { get; set; }
        [JsonPropertyName("odyssey")]
        public bool Odyssey { get; set; }
        [JsonPropertyName("gameversion")]
        public string GameVersion { get; set; }
        [JsonPropertyName("gamebuild")]
        public string GameBuild { get; set; }
    }
}
