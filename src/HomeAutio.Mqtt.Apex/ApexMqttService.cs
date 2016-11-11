using HomeAutio.Mqtt.Core;
using HomeAutio.Mqtt.Core.Utilities;
using I8Beef.Neptune.Apex;
using I8Beef.Neptune.Apex.Schema;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;
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

        /// <summary>
        /// Holds mapping of possible MQTT topics mapped to outlets they trigger.
        /// </summary>
        private IDictionary<string, string> _topicOutletMap;

        private IDictionary<string, FeedCycle> _feedCycleMap = new Dictionary<string, FeedCycle>
        {
            { "A", FeedCycle.A },
            { "B", FeedCycle.B },
            { "C", FeedCycle.C },
            { "D", FeedCycle.D },
            { "CANCEL", FeedCycle.Cancel },
        };

        private IDictionary<string, string> _outletStateMap = new Dictionary<string, string>
        {
            { "ON", "on" },
            { "OFF", "off" },
            { "AON", "on" },
            { "AOF", "off" }
        };

        public ApexMqttService(Client apexClient, string apexName, int refreshInterval, string brokerIp, int brokerPort = 1883, string brokerUsername = null, string brokerPassword = null)
            : base(brokerIp, brokerPort, brokerUsername, brokerPassword, "apex/" + apexName)
        {
            _refreshInterval = refreshInterval;
            _topicOutletMap = new Dictionary<string, string>();
            _subscribedTopics = new List<string>();
            _subscribedTopics.Add(_topicRoot + "/outlets/+/set");
            _subscribedTopics.Add(_topicRoot + "/feedCycle/set");

            _client = apexClient;
            _apexName = apexName;
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

            if (e.Topic == _topicRoot + "/feedCycle/set" && _feedCycleMap.ContainsKey(message.ToUpper()))
            {
                var feed = _feedCycleMap[message.ToUpper()];
                _client.SetFeed(feed);
            }
            else if(_topicOutletMap.ContainsKey(e.Topic))
            {
                var outlet = _topicOutletMap[e.Topic];
                OutletState outletState;
                switch (message.ToLower())
                {
                    case "on":
                        outletState = OutletState.On;
                        break;
                    case "off":
                        outletState = OutletState.Off;
                        break;
                    default:
                        outletState = OutletState.Auto;
                        break;
                }

                _client.SetOutlet(outlet, outletState);
            }
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
            // Make all of the calls to get current status
            var status = await _client.GetStatus();

            // Compare to current cached status
            var updates = CompareStatusObjects(_config, status);

            // If updated, publish changes
            if (updates.Count > 0)
            {
                foreach (var update in updates)
                {
                    _mqttClient.Publish(_topicRoot + update.Key, Encoding.UTF8.GetBytes(update.Value), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
                }

                _config = status;
            }
        }

        /// <summary>
        /// Compares twomaster state objects.
        /// </summary>
        /// <param name="status1">First status.</param>
        /// <param name="status2">Second status.</param>
        /// <returns>List of changes.</returns>
        private IDictionary<string, string> CompareStatusObjects(Status status1, Status status2)
        {
            var updates = new Dictionary<string, string>();

            for (var i = 0; i < status1.Outlets.Length; i++)
            {
                if (status1.Outlets[i].State != status2.Outlets[i].State)
                {
                    updates.Add("/outlets/" + status2.Outlets[i].Name.Sluggify(), _outletStateMap[status2.Outlets[i].State.ToUpper()]);
                }
            }

            for (var i = 0; i < status1.Probes.Length; i++)
            {
                if (status1.Probes[i].Value != status2.Probes[i].Value)
                {
                    updates.Add("/probes/" + status2.Probes[i].Name.Sluggify(), status2.Probes[i].Value);
                }
            }

            return updates;
        }

        /// <summary>
        /// Maps Apex device actions to subscription topics.
        /// </summary>
        private void GetConfig()
        {
            _config = _client.GetStatus().GetAwaiter().GetResult();

            // Wipe topic to outlet map for reload
            if (_topicOutletMap.Count > 0)
                _topicOutletMap.Clear();

            // Map all outlets at {_topicRoot}/outlets/{outletName}/set
            // Listen at topic {_topicRoot}/outlets/+/set
            foreach (var outlet in _config.Outlets)
            {
                var commandTopic = $"{_topicRoot}/outlets/{outlet.Name.Sluggify()}/set";
                _topicOutletMap.Add(commandTopic, outlet.Name);

                var currentValue = _outletStateMap[outlet.State.ToUpper()];

                // Publish initial value
                _mqttClient.Publish($"{_topicRoot}/outlets/{outlet.Name.Sluggify()}", Encoding.UTF8.GetBytes(currentValue), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
            }

            // Initial probe states published at {_topicRoot}/probes/{probeName}
            foreach (var probe in _config.Probes)
            {
                _mqttClient.Publish($"{_topicRoot}/probes/{probe.Name.Sluggify()}", Encoding.UTF8.GetBytes(probe.Value), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
            }
        }

        #endregion
    }
}
