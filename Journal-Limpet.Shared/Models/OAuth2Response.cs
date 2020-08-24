using System;
using System.Text.Json.Serialization;

namespace Journal_Limpet.Shared.Models
{
    public class OAuth2Response
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; set; }
        [JsonPropertyName("refresh_token")]
        public Guid RefreshToken { get; set; }
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }
}
