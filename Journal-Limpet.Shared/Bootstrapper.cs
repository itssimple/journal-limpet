﻿using Journal_Limpet.Shared.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using System.Net.Http;

namespace Journal_Limpet.Shared
{
    public static class Bootstrapper
    {
        public static void AddJournalLimpetDependencies(this IServiceCollection sc, IConfiguration configuration)
        {
            sc.AddScoped(x => new SqlConnection(configuration["Database:ConnectionString"]));

            sc.AddScoped<MSSQLDB>();
            sc.AddScoped<StarSystemChecker>();

            sc.AddSingleton(x =>
                new MinioClient(
                    configuration["Minio:ConnectionString"],
                    configuration["Minio:AccessKey"],
                    configuration["Minio:SecretKey"]
                    )
                );

            sc.AddScoped(x =>
                new TwitterSender(
                    configuration["Twitter:ConsumerKey"],
                    configuration["Twitter:ConsumerSecret"],
                    configuration["Twitter:AccessToken"],
                    configuration["Twitter:AccessSecret"],
                    x.GetRequiredService<IHttpClientFactory>()
                    )
                );

            sc.AddScoped(x =>
                new DiscordWebhook(
                    configuration["Discord:ErrorWebhook"],
                    x.GetRequiredService<IHttpClientFactory>()
                    )
                );
        }
    }
}
