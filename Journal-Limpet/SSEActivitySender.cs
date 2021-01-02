using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Journal_Limpet
{
    public static class SSEActivitySender
    {
        public static async Task SendGlobalActivityAsync(string title, string message)
        {
            var msg = new
            {
                title,
                message
            };

            await SharedSettings.RedisPubsubSubscriber.PublishAsync("global-activity", JsonSerializer.Serialize(msg));
        }

        public static async Task SendUserActivityAsync(Guid userIdentifier, string title, string message)
        {
            var msg = new
            {
                title,
                message
            };

            await SharedSettings.RedisPubsubSubscriber.PublishAsync($"user-activity-{userIdentifier}", JsonSerializer.Serialize(msg));
        }
    }
}
