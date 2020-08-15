using Journal_Limpet.Shared.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Journal_Limpet.Shared
{
    public static class Bootstrapper
    {
        public static void AddJournalLimpetDependencies(this IServiceCollection sc, IConfiguration configuration)
        {
            sc.AddScoped(x => new Npgsql.NpgsqlConnection(configuration["Database:ConnectionString"]));
            sc.AddScoped(x => new NPGDB(x.GetRequiredService<NpgsqlConnection>()));
        }
    }
}
