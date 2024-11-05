using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Journal_Limpet.Pages
{
    public class AboutModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public PatreonGoal Goal { get; set; } = null;

        public AboutModel(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task OnGetAsync()
        {
            try
            {
                var hc = _httpClientFactory.CreateClient();
                hc.DefaultRequestHeaders.UserAgent.ParseAdd("Journal-Limpet");
                var res = await hc.GetStringAsync(_configuration["Patreon:GoalApiUrl"]);
                Goal = JsonSerializer.Deserialize<PatreonGoal>(
                        JsonDocument.Parse(res).RootElement
                            .GetProperty("data")
                            .GetProperty("attributes")
                            .GetRawText()
                        );
            }
            catch
            {
                Goal = null;
            }
        }

        public class PatreonGoal
        {
            [JsonPropertyName("amount_cents")]
            public long AmountCents { get; set; }
            [JsonPropertyName("completed_percentage")]
            public int CompletedPercentage { get; set; }
            [JsonPropertyName("currency")]
            public string Currency { get; set; }
        }
    }
}
