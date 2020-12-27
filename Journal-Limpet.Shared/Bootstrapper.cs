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
        }
    }
}
