using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.API.Profile
{
    public class EliteStarportServices
    {
        [JsonPropertyName("dock")]
        public string Dock { get; set; }
        [JsonPropertyName("contacts")]
        public string Contacts { get; set; }
        [JsonPropertyName("refuel")]
        public string Refuel { get; set; }
        [JsonPropertyName("repair")]
        public string Repair { get; set; }
        [JsonPropertyName("rearm")]
        public string Rearm { get; set; }
        [JsonPropertyName("outfitting")]
        public string Outfitting { get; set; }
        [JsonPropertyName("stationmenu")]
        public string Stationmenu { get; set; }
        [JsonPropertyName("shop")]
        public string Shop { get; set; }
        [JsonPropertyName("engineer")]
        public string Engineer { get; set; }
    }
}
