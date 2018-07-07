using System;
using System.Threading.Tasks;
using I8Beef.Neptune.Apex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HomeAutio.Mqtt.Apex
{
    /// <summary>
    /// Main program entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main program entry point.
        /// </summary>
        /// <param name="args">Arguments.</param>
        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Main program entry point.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Awaitable <see cref="Task" />.</returns>
        public static async Task MainAsync(string[] args)
        {
            // Setup config
            var config = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            // Setup logging
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();

            try
            {
                var hostBuilder = CreateHostBuilder(config);
                await hostBuilder.RunConsoleAsync();
            }
            catch (Exception ex)
            {
                Log.Logger.Fatal(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Creates an <see cref="IHostBuilder"/>.
        /// </summary>
        /// <param name="config">External configuration.</param>
        /// <returns>A configured <see cref="IHostBuilder"/>.</returns>
        private static IHostBuilder CreateHostBuilder(IConfiguration config)
        {
            return new HostBuilder()
                .ConfigureAppConfiguration((hostContext, configuration) => configuration.AddConfiguration(config))
                .ConfigureLogging((hostingContext, logging) => logging.AddSerilog())
                .ConfigureServices((hostContext, services) =>
                {
                    // Setup client
                    services.AddScoped<Client>(serviceProvider =>
                    {
                        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                        return new Client(
                            configuration.GetValue<string>("apexHost"),
                            configuration.GetValue<string>("apexUsername"),
                            configuration.GetValue<string>("apexPassword"));
                    });

                    // Setup service instance
                    services.AddScoped<IHostedService, ApexMqttService>(serviceProvider =>
                    {
                        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                        return new ApexMqttService(
                            serviceProvider.GetRequiredService<IApplicationLifetime>(),
                            serviceProvider.GetRequiredService<ILogger<ApexMqttService>>(),
                            serviceProvider.GetRequiredService<Client>(),
                            configuration.GetValue<string>("apexName", "default"),
                            configuration.GetValue<int>("refreshInterval", 30),
                            configuration.GetValue<bool>("publishOnlyChangedValues", false),
                            configuration.GetValue<string>("brokerIp"),
                            configuration.GetValue<int>("brokerPort", 1883),
                            configuration.GetValue<string>("brokerUsername"),
                            configuration.GetValue<string>("brokerPassword"));
                    });
                });
        }
    }
}
