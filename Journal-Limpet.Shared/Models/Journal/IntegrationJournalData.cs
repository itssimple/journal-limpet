using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.Journal
{
    public class IntegrationJournalData
    {
        [JsonPropertyName("fullySent")]
        public bool FullySent { get; set; }
        [JsonPropertyName("lastSentLineNumber")]
        public long LastSentLineNumber { get; set; }
        [JsonPropertyName("currentGameState")]
        public EDGameState CurrentGameState { get; set; }
    }
}
