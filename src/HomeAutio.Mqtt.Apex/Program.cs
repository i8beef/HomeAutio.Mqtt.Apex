using I8Beef.Neptune.Apex;
using System.Configuration;
using Topshelf;

namespace HomeAutio.Mqtt.Apex
{
    class Program
    {
        static void Main(string[] args)
        {
            var brokerIp = ConfigurationManager.AppSettings["brokerIp"];
            var brokerPort = int.Parse(ConfigurationManager.AppSettings["brokerPort"]);
            var brokerUsername = ConfigurationManager.AppSettings["brokerUsername"];
            var brokerPassword = ConfigurationManager.AppSettings["brokerPassword"];

            var apexIp = ConfigurationManager.AppSettings["apexIp"];
            var apexUsername = ConfigurationManager.AppSettings["apexUsername"];
            var apexPassword = ConfigurationManager.AppSettings["apexPassword"];
            var apexClient = new Client(apexIp, apexUsername, apexPassword);

            var apexName = ConfigurationManager.AppSettings["apexName"];

            HostFactory.Run(x =>
            {
                x.UseNLog();

                x.Service<ApexMqttService>(s =>
                {
                    s.ConstructUsing(name => new ApexMqttService(apexClient, apexName, brokerIp, brokerPort, brokerUsername, brokerPassword));
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
