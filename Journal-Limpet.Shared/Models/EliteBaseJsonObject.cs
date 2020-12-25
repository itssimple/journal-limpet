using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models
{
    public class EliteBaseJsonObject
    {
        [JsonExtensionData]
        public Dictionary<string, object> ExtensionData { get; set; }
    }
}
