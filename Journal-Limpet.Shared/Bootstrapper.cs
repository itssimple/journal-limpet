using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Journal_Limpet.Shared.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Journal_Limpet.Shared
{
    public static class Bootstrapper
    {
        public static void AddJournalLimpetDependencies(this IServiceCollection sc, IConfiguration configuration)
        {
            sc.AddScoped(x => new SqlConnection(configuration["Database:ConnectionString"]));
            sc.AddScoped(x => new MSSQLDB(x.GetRequiredService<SqlConnection>()));
            sc.AddScoped(x => new AmazonS3Client(new BasicAWSCredentials(configuration["Amazon:AccessKey"], configuration["Amazon:SecretKey"]), RegionEndpoint.EUNorth1));
        }
    }
}
