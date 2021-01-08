using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Threading;

namespace Journal_Limpet
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ThreadPool.SetMinThreads(200, 200);
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(cb =>
            {
                cb.AddJsonFile("Journal-Limpet.environment");
            })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
