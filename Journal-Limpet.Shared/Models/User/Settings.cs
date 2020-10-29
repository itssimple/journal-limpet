using System;
using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models.User
{
    public class Settings
    {
        public FrontierProfile FrontierProfile { get; set; }
        public string AuthToken { get; set; }
        public Guid RefreshToken { get; set; }
        public DateTimeOffset TokenExpiration { get; set; }
    }

    public class FrontierProfile
    {
        [JsonPropertyName("customer_id")]
        public string CustomerId { get; set; }
        [JsonPropertyName("email")]
        public string Email { get; set; }
    }
}
