using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Journal_Limpet.Shared
{
    public class DiscordWebhook
    {
        readonly string _webhookUrl;

        public DiscordWebhook(string webhookUrl)
        {
            _webhookUrl = webhookUrl;
        }

        public async Task SendMessageAsync(string messageContent, List<DiscordWebhookEmbed> embeds = null)
        {
            var jobj = new DiscordWebhookRequest
            {
                Content = messageContent,
                Embeds = embeds ?? new List<DiscordWebhookEmbed>()
            };

            using (var _hc = new HttpClient())
            {
                var res = await _hc.PostAsync(_webhookUrl, new StringContent(JsonSerializer.Serialize(jobj), Encoding.UTF8, "application/json"));
            }
        }
    }

    public class DiscordWebhookRequest
    {
        [JsonPropertyName("content")]
        public string Content { get; set; }
        [JsonPropertyName("embeds")]
        public List<DiscordWebhookEmbed> Embeds { get; set; }
    }

    public class DiscordWebhookEmbed
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("fields")]
        public List<DiscordWebhookEmbedField> Fields { get; set; }
    }

    public class DiscordWebhookEmbedField
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("value")]
        public string Value { get; set; }
        [JsonPropertyName("inline")]
        public bool Inline { get; set; }
    }
}
