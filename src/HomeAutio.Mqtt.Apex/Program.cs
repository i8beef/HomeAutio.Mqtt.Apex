using System;
using System.Threading.Tasks;
using I8Beef.Neptune.Apex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        /// <returns>Awaitable <see cref="Task" />.</returns>
        public static async Task Main(string[] args)
        {
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    config.SetBasePath(Environment.CurrentDirectory);
                    config.AddJsonFile("appsettings.json", optional: false);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddScoped<Client>(serviceProvider =>
                    {
                        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                        return new Client(
                            configuration.GetValue<string>("apexHost"),
                            configuration.GetValue<string>("apexUsername"),
                            configuration.GetValue<string>("apexPassword"));
                    });

                    services.AddScoped<IHostedService, ApexMqttService>(serviceProvider =>
                    {
                        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                        return new ApexMqttService(
                            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ApexMqttService>(),
                            serviceProvider.GetRequiredService<Client>(),
                            configuration.GetValue<string>("apexName"),
                            configuration.GetValue<int>("refreshInterval"),
                            configuration.GetValue<string>("brokerIp"),
                            configuration.GetValue<int>("brokerPort"),
                            configuration.GetValue<string>("brokerUsername"),
                            configuration.GetValue<string>("brokerPassword"));
                    });
                });

            await hostBuilder.RunConsoleAsync();
        }
    }
}
