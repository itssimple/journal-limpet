using Journal_Limpet.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Journal_Limpet.Hangfire
{
    class Program
    {
        static IServiceCollection _serviceCollection;
        static IConfiguration _configuration;

        static void Main(string[] args)
        {
            _configuration = new ConfigurationBuilder()
               .AddJsonFile("Journal-Limpet.environment", optional: true, reloadOnChange: true)
               .AddEnvironmentVariables()
               .AddCommandLine(args)
               .Build();

            _serviceCollection = new ServiceCollection();

            _serviceCollection.AddJournalLimpetDependencies(_configuration);
        }
    }
}
