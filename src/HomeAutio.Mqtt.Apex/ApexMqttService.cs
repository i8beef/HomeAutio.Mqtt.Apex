using System.Collections.Generic;
using System.Text;
using System.Timers;
using HomeAutio.Mqtt.Core;
using HomeAutio.Mqtt.Core.Utilities;
using I8Beef.Neptune.Apex;
using I8Beef.Neptune.Apex.Schema;
using NLog;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace HomeAutio.Mqtt.Apex
{
    /// <summary>
    /// Apex MQTT service.
    /// </summary>
    public class ApexMqttService : ServiceBase
    {
        private ILogger _log = LogManager.GetCurrentClassLogger();
        private bool _disposed = false;

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
            { "AON", "auto" },
            { "AOF", "auto" }
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="ApexMqttService"/> class.
        /// </summary>
        /// <param name="apexClient">Apex client.</param>
        /// <param name="apexName">Apex name.</param>
        /// <param name="refreshInterval">Refresh interval.</param>
        /// <param name="brokerIp">MQTT broker IP.</param>
        /// <param name="brokerPort">MQTT broker port.</param>
        /// <param name="brokerUsername">MQTT broker username.</param>
        /// <param name="brokerPassword">MQTT broker password.</param>
        public ApexMqttService(Client apexClient, string apexName, int refreshInterval, string brokerIp, int brokerPort = 1883, string brokerUsername = null, string brokerPassword = null)
            : base(brokerIp, brokerPort, brokerUsername, brokerPassword, "apex/" + apexName)
        {
            _refreshInterval = refreshInterval;
            _topicOutletMap = new Dictionary<string, string>();
            SubscribedTopics.Add(TopicRoot + "/outlets/+/set");
            SubscribedTopics.Add(TopicRoot + "/feedCycle/set");

            _client = apexClient;
            _apexName = apexName;
        }

        #region Service implementation

        /// <summary>
        /// Service Start action.
        /// </summary>
        protected override void StartService()
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
        protected override void StopService()
        {
            Dispose();
        }

        #endregion

        #region MQTT Implementation

        /// <summary>
        /// Handles commands for the Harmony published to MQTT.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        protected override void Mqtt_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            var message = Encoding.UTF8.GetString(e.Message);
            _log.Debug("MQTT message received for topic " + e.Topic + ": " + message);

            if (e.Topic == TopicRoot + "/feedCycle/set" && _feedCycleMap.ContainsKey(message.ToUpper()))
            {
                var feed = _feedCycleMap[message.ToUpper()];
                _client.SetFeed(feed);
            }
            else if (_topicOutletMap.ContainsKey(e.Topic))
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
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
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
                    MqttClient.Publish(TopicRoot + update.Key, Encoding.UTF8.GetBytes(update.Value), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
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

            // Map all outlets at {TopicRoot}/outlets/{outletName}/set
            // Listen at topic {TopicRoot}/outlets/+/set
            foreach (var outlet in _config.Outlets)
            {
                var commandTopic = $"{TopicRoot}/outlets/{outlet.Name.Sluggify()}/set";
                _topicOutletMap.Add(commandTopic, outlet.Name);

                var currentValue = _outletStateMap[outlet.State.ToUpper()];

                // Publish initial value
                MqttClient.Publish($"{TopicRoot}/outlets/{outlet.Name.Sluggify()}", Encoding.UTF8.GetBytes(currentValue), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
            }

            // Initial probe states published at {TopicRoot}/probes/{probeName}
            foreach (var probe in _config.Probes)
            {
                MqttClient.Publish($"{TopicRoot}/probes/{probe.Name.Sluggify()}", Encoding.UTF8.GetBytes(probe.Value), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
            }
        }

        #endregion

        #region IDisposable Support

        /// <summary>
        /// Dispose implementation.
        /// </summary>
        /// <param name="disposing">Indicates if disposing.</param>
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (_refresh != null)
                {
                    _refresh.Stop();
                    _refresh.Dispose();
                }
            }

            _disposed = true;
            base.Dispose(disposing);
        }

        #endregion
    }
    }
