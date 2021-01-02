using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Threading;
using System.Threading.Tasks;

namespace Journal_Limpet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SSEController : ControllerBase
    {
        private readonly IDatabase _rdb;
        private readonly ISubscriber _pubsub;

        public SSEController()
        {
            _rdb = SharedSettings.RedisClient.GetDatabase(2);
            _pubsub = SharedSettings.RedisClient.GetSubscriber();
        }

        [HttpGet("activity")]
        public async Task SSEActivityAsync(CancellationToken ct)
        {
            var response = Response;

            response.Headers.Add("Content-Type", "text/event-stream");
            response.Headers.Add("X-Accel-Buffering", "no");
            response.Headers.Add("Cache-Control", "no-cache");

            await response.WriteAsync("data: Connected to Activity SSE endpoint\r\r");
            await response.Body.FlushAsync();

            await _pubsub.SubscribeAsync("global-activity", (channel, data) =>
            {
                response.WriteAsync("event: globalactivity\r");
                response.WriteAsync($"data: {data}\r\r");
                response.Body.Flush();
            });

            if (User.Identity.IsAuthenticated)
            {
                await response.WriteAsync($"data: Signed in as {User.Identity.Name}\r\r");
                await response.Body.FlushAsync();

                await _pubsub.SubscribeAsync($"user-activity-{User.Identity.Name}", (channel, data) =>
                {
                    response.WriteAsync("event: useractivity\r");
                    response.WriteAsync($"data: {data}\r\r");
                    response.Body.Flush();
                });
            }

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(10000);
                await response.WriteAsync(": PING\r\r");
                await response.Body.FlushAsync();
            }
        }
    }
}
