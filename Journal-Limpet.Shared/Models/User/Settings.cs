using System;
using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.User
{
    public class Settings
    {
        public FrontierProfile FrontierProfile { get; set; }
    }

    public class FrontierProfile
    {
        [JsonPropertyName("customer_id")]
        public long CustomerId { get; set; }
        public string AuthToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTimeOffset TokenExpiration { get; set; }
    }
}
