using System.Configuration;
using I8Beef.Neptune.Apex;
using NLog;
using Topshelf;

namespace HomeAutio.Mqtt.Apex
{
    /// <summary>
    /// Main program entrypoint.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main method.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        public static void Main(string[] args)
        {
            var log = LogManager.GetCurrentClassLogger();

            var brokerIp = ConfigurationManager.AppSettings["brokerIp"];
            var brokerPort = int.Parse(ConfigurationManager.AppSettings["brokerPort"]);
            var brokerUsername = ConfigurationManager.AppSettings["brokerUsername"];
            var brokerPassword = ConfigurationManager.AppSettings["brokerPassword"];

            var apexIp = ConfigurationManager.AppSettings["apexIp"];
            var apexUsername = ConfigurationManager.AppSettings["apexUsername"];
            var apexPassword = ConfigurationManager.AppSettings["apexPassword"];
            var apexClient = new Client(apexIp, apexUsername, apexPassword);

            var apexName = ConfigurationManager.AppSettings["apexName"];
            if (int.TryParse(ConfigurationManager.AppSettings["apexRefreshInterval"], out int apexRereshInterval))
                apexRereshInterval = apexRereshInterval * 1000;

            HostFactory.Run(x =>
            {
                x.UseNLog();
                x.OnException(ex => log.Error(ex));

                x.Service<ApexMqttService>(s =>
                {
                    s.ConstructUsing(name => new ApexMqttService(apexClient, apexName, apexRereshInterval, brokerIp, brokerPort, brokerUsername, brokerPassword));
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });

                x.EnableServiceRecovery(r =>
                {
                    r.RestartService(0);
                    r.RestartService(0);
                    r.RestartService(0);
                });

                x.RunAsLocalSystem();
                x.UseAssemblyInfoForServiceInfo();
            });
        }
    }
}
