using HomeAutio.Mqtt.Core;
using I8Beef.Neptune.Apex;
using I8Beef.Neptune.Apex.Schema;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace HomeAutio.Mqtt.Apex
{
    public class ApexMqttService : ServiceBase
    {
        private ILogger _log = LogManager.GetCurrentClassLogger();

        private Client _client;
        private string _apexName;
        private Status _config;

        private Timer _refresh;
        private int _refreshInterval;

        public ApexMqttService(Client apexClient, string apexName, string brokerIp, int brokerPort = 1883, string brokerUsername = null, string brokerPassword = null)
            : base(brokerIp, brokerPort, brokerUsername, brokerPassword, "apex/" + apexName)
        {
            _subscribedTopics = new List<string>();
            _subscribedTopics.Add(_topicRoot + "/controls/+/set");

            _client = apexClient;
            _apexName = apexName;

            //_client.EventReceived += Apex_EventReceived;

            //// Apex client logging
            //_client.MessageSent += (object sender, I8Beef.Apex.Events.MessageSentEventArgs e) => { _log.Debug("Apex Message sent: " + e.Message); };
            //_client.MessageReceived += (object sender, I8Beef.Apex.Events.MessageReceivedEventArgs e) => { _log.Debug("Apex Message received: " + e.Message); };
            //_client.Error += (object sender, System.IO.ErrorEventArgs e) => {
            //    _log.Error(e.GetException());

            //    // Stream parsing is lost at this point, restart the client (need to test this)
            //    _client.Close();
            //    _client.Connect();
            //};
        }

        #region Service implementation

        /// <summary>
        /// Service Start action.
        /// </summary>
        public override void StartService()
        {
            GetConfig();

            // Enable refresh
            if (_refresh != null)
                _refresh.Dispose();

            _refresh = new Timer();
            _refresh.Elapsed += RefreshAsync;
            _refresh.Interval = _refreshInterval;
            _refresh.Start();
        }

        /// <summary>
        /// Service Stop action.
        /// </summary>
        public override void StopService()
        {
            if (_refresh != null)
            {
                _refresh.Stop();
                _refresh.Dispose();
            }
        }

        #endregion

        #region MQTT Implementation

        /// <summary>
        /// Handles commands for the Harmony published to MQTT.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void Mqtt_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            var message = Encoding.UTF8.GetString(e.Message);
            _log.Debug("MQTT message received for topic " + e.Topic + ": " + message);

            if (command != null)
                _client.SendCommandAsync(command);
        }

        #endregion

        #region Apex implementation

        /// <summary>
        /// Heartbeat ping. Failure will result in the heartbeat being stopped, which will 
        /// make any future calls throw an exception as the heartbeat indicator will be disabled.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RefreshAsync(object sender, ElapsedEventArgs e)
        {
            try
            {
                // Make all of the calls to get current status
                var status = await _client.GetStatus();

                // Compare to current cached status
                var updates = CompareStatusObjects(_config, status);

                // If updated, publish changes
                if (updates.Count > 0)
                {
                    foreach (var update in updates)
                    {
                        EventReceived?.Invoke(this, update);
                    }

                    _config = status;
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new ErrorEventArgs(ex));
                Timer timer = (Timer)sender;
                timer.Stop();
            }
        }

        /// <summary>
        /// Compares twomaster state objects.
        /// </summary>
        /// <param name="status1">First status.</param>
        /// <param name="status2">Second status.</param>
        /// <returns>List of changes.</returns>
        private IList<Command> CompareStatusObjects(Status status1, Status status2)
        {
            var updates = new List<Command>();


            for (var i = 0; i < )
            foreach (var outlet in status1.Outlets)
            {
                if (outlet.State != status2.Outlets.FirstOrDefault(x => x.).State)
                {
                    updates.Add(new PowerCommand { Value = status2.Power ? "ON" : "OFF" });
                }
            }

            // Power: PW
            if (status1.Power != status2.Power)
                updates.Add(new PowerCommand { Value = status2.Power ? "ON" : "OFF" });

            // Volume: MV
            if (status1.Volume != status2.Volume)
                updates.Add(new VolumeCommand { Value = status2.Volume.ToString() });

            // Mute: MU
            if (status1.Mute != status2.Mute)
                updates.Add(new MuteCommand { Value = status2.Mute ? "ON" : "OFF" });

            // Input: SI
            if (status1.Input != status2.Input)
                updates.Add(new InputCommand { Value = status2.Input });

            // Surround mode: MS
            if (status1.SurroundMode != status2.SurroundMode)
                updates.Add(new SurroundModeCommand { Value = status2.SurroundMode });

            foreach (var zone in status1.SecondaryZoneStatus)
            {
                var zoneNumber = int.Parse(zone.Key.Substring(4));

                // Zone power: Z#
                if (status1.SecondaryZoneStatus[zone.Key].Power != status2.SecondaryZoneStatus[zone.Key].Power)
                    updates.Add(new ZonePowerCommand { ZoneId = zoneNumber, Value = status2.SecondaryZoneStatus[zone.Key].Power ? "ON" : "OFF" });

                // Zone input: Z#
                if (status1.SecondaryZoneStatus[zone.Key].Input != status2.SecondaryZoneStatus[zone.Key].Input)
                    updates.Add(new ZoneInputCommand { ZoneId = zoneNumber, Value = status2.SecondaryZoneStatus[zone.Key].Input });

                // Zone volume: Z#
                if (status1.SecondaryZoneStatus[zone.Key].Volume != status2.SecondaryZoneStatus[zone.Key].Volume)
                    updates.Add(new ZoneVolumeCommand { ZoneId = zoneNumber, Value = status2.SecondaryZoneStatus[zone.Key].Volume.ToString() });

                // Zone input: Z#MU
                if (status1.SecondaryZoneStatus[zone.Key].Mute != status2.SecondaryZoneStatus[zone.Key].Mute)
                    updates.Add(new ZoneMuteCommand { ZoneId = zoneNumber, Value = status2.SecondaryZoneStatus[zone.Key].Mute ? "ON" : "OFF" });
            }

            return updates;
        }

        /// <summary>
        /// Handles publishing updates to the harmony current activity to MQTT.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Apex_EventReceived(object sender, Command command)
        {
            _log.Debug($"Apex event received: {command.GetType()} {command.Code} {command.Value}");

            string commandType = null;
            switch (command.GetType().Name)
            {
                case "PowerCommand":
                    commandType = "power";
                    break;
                case "VolumeCommand":
                    commandType = "volume";
                    break;
                case "MuteCommand":
                    commandType = "mute";
                    break;
                case "InputCommand":
                    commandType = "input";
                    break;
                case "SurroundModeCommand":
                    commandType = "surroundMode";
                    break;
                case "TunerFrequencyCommand":
                    commandType = "tunerFrequency";
                    break;
                case "TunerModeCommand":
                    commandType = "tunerMode";
                    break;
                case "ZonePowerCommand":
                    commandType = $"zone{((ZonePowerCommand)command).ZoneId}Power";
                    break;
                case "ZoneVolumeCommand":
                    commandType = $"zone{((ZoneVolumeCommand)command).ZoneId}Volume";
                    break;
                case "ZoneMuteCommand":
                    commandType = $"zone{((ZoneMuteCommand)command).ZoneId}Mute";
                    break;
                case "ZoneInputCommand":
                    commandType = $"zone{((ZoneInputCommand)command).ZoneId}Input";
                    break;
            }

            if (commandType != null)
                _mqttClient.Publish(_topicRoot + "/controls/" + commandType, Encoding.UTF8.GetBytes(command.Value), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
        }

        /// <summary>
        /// Maps Apex device actions to subscription topics.
        /// </summary>
        private void GetConfig()
        {
            _config = _client.GetStatus().GetAwaiter().GetResult();

        }

        #endregion
    }
}
