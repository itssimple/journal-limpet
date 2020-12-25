using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.API.Profile
{
    public class EliteCommanderRank
    {
        [JsonPropertyName("combat")]
        public int Combat { get; set; }
        [JsonPropertyName("trade")]
        public int Trade { get; set; }
        [JsonPropertyName("explore")]
        public int Explore { get; set; }
        [JsonPropertyName("crime")]
        public int Crime { get; set; }
        [JsonPropertyName("service")]
        public int Service { get; set; }
        [JsonPropertyName("empire")]
        public int Empire { get; set; }
        [JsonPropertyName("federation")]
        public int Federation { get; set; }
        [JsonPropertyName("power")]
        public int Power { get; set; }
        [JsonPropertyName("cqc")]
        public int CQC { get; set; }
    }
}
