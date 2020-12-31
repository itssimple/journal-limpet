using Journal_Limpet.Shared.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;

namespace Journal_Limpet.Shared
{
    public static class Bootstrapper
    {
        public static void AddJournalLimpetDependencies(this IServiceCollection sc, IConfiguration configuration)
        {
            sc.AddScoped(x => new SqlConnection(configuration["Database:ConnectionString"]));
            sc.AddScoped(x => new MSSQLDB(x.GetRequiredService<SqlConnection>()));
            sc.AddSingleton(x => new MinioClient(configuration["Minio:ConnectionString"], configuration["Minio:AccessKey"], configuration["Minio:SecretKey"]));

            sc.AddScoped(x => new TwitterSender(configuration["Twitter:ConsumerKey"], configuration["Twitter:ConsumerSecret"], configuration["Twitter:AccessToken"], configuration["Twitter:AccessSecret"]));

            sc.AddScoped(x => new DiscordWebhook(configuration["Discord:ErrorWebhook"]));
        }
    }
}
