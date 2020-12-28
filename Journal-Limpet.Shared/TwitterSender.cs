using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Journal_Limpet.Shared
{
    public class TwitterSender
    {
        readonly string _consumerKey;
        readonly string _consumerSecret;

        readonly string _accessToken;
        readonly string _secretToken;

        readonly HMACSHA1 _hash;
        readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static Random _rng = new Random();

        public TwitterSender(string consumerKey, string consumerSecret, string accessToken, string secretToken)
        {
            _consumerKey = consumerKey;
            _consumerSecret = consumerSecret;
            _accessToken = accessToken;
            _secretToken = secretToken;

            _hash = new HMACSHA1(Encoding.ASCII.GetBytes($"{_consumerSecret}&{_secretToken}"));
        }

        public async Task<(bool status, string response)> SendAsync(string tweet)
        {
            if (tweet.Length > 160)
            {
                throw new ArgumentOutOfRangeException("tweet", tweet, "Message must be shorter than 160 characters to fit in a tweet");
            }

            var data = new Dictionary<string, string>() {
            { "status", tweet }
        };

            return await sendTwitterRequestAsync("https://api.twitter.com/1.1/statuses/update.json", data);
        }

        internal TwitterOAuthData generateOAuthSignatureData(string url, Dictionary<string, string> data)
        {
            var ts = (int)((DateTime.UtcNow - _epoch).TotalSeconds);

            byte[] rngBytes = new byte[32];

            _rng.NextBytes(rngBytes);

            var odata = new Dictionary<string, string>() {
            { "oauth_consumer_key", _consumerKey },
            { "oauth_signature_method", "HMAC-SHA1" },
            { "oauth_timestamp", ts.ToString() },
            { "oauth_nonce", Convert.ToBase64String(_hash.ComputeHash(rngBytes)) },
            { "oauth_token", _accessToken },
            { "oauth_version", "1.0" }
        };

            var finalData = data.Concat(odata).GroupBy(i => i.Key).ToDictionary(i => i.Key, i => i.First().Value);
            finalData.Add("oauth_signature", getSignature(url, finalData));

            var oauthHeader = getOAuthHeaderFromOAuthData(finalData.Where(k => k.Key.StartsWith("oauth_")).ToDictionary(k => k.Key, k => k.Value));

            var formData = new FormUrlEncodedContent(data);

            return new TwitterOAuthData(formData, oauthHeader);
        }

        internal string getSignature(string url, Dictionary<string, string> signatureData)
        {
            var dataString = string.Join("&", signatureData.Select(i => $"{Uri.EscapeDataString(i.Key)}={Uri.EscapeDataString(i.Value)}").OrderBy(s => s));
            return Convert.ToBase64String(_hash.ComputeHash(Encoding.ASCII.GetBytes($"POST&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(dataString)}")));
        }

        internal string getOAuthHeaderFromOAuthData(Dictionary<string, string> data)
        {
            return $"OAuth {string.Join(", ", data.Select(k => $"{Uri.EscapeDataString(k.Key)}=\"{Uri.EscapeDataString(k.Value)}\"").OrderBy(s => s))}";
        }

        internal async Task<(bool status, string response)> sendTwitterRequestAsync(string url, Dictionary<string, string> data)
        {
            var oauthData = generateOAuthSignatureData(url, data);

            using (var hc = new HttpClient())
            {
                hc.DefaultRequestHeaders.Add("Authorization", oauthData.OAuthHeader);

                var resp = await hc.PostAsync(url, oauthData.FormData);
                var content = await resp.Content.ReadAsStringAsync();

                return (resp.IsSuccessStatusCode, content);
            }
        }

        internal class TwitterOAuthData
        {
            public FormUrlEncodedContent FormData { get; set; }
            public string OAuthHeader { get; set; }

            public TwitterOAuthData(FormUrlEncodedContent formData, string oauthHeader)
            {
                FormData = formData;
                OAuthHeader = oauthHeader;
            }
        }
    }
}
