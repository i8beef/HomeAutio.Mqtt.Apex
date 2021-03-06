﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using HomeAutio.Mqtt.Core;
using HomeAutio.Mqtt.Core.Utilities;
using I8Beef.Neptune.Apex;
using I8Beef.Neptune.Apex.Schema;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;

namespace HomeAutio.Mqtt.Apex
{
    /// <summary>
    /// Apex MQTT service.
    /// </summary>
    public class ApexMqttService : ServiceBase
    {
        private readonly ILogger<ApexMqttService> _log;

        private readonly Client _client;
        private readonly bool _publishOnlyChangedValues;
        private readonly int _refreshInterval;

        /// <summary>
        /// Holds mapping of possible MQTT topics mapped to outlets they trigger.
        /// </summary>
        private readonly IDictionary<string, string> _topicOutletMap = new Dictionary<string, string>();

        private readonly IReadOnlyDictionary<string, FeedCycle> _feedCycleMap = new Dictionary<string, FeedCycle>
        {
            { "A", FeedCycle.A },
            { "B", FeedCycle.B },
            { "C", FeedCycle.C },
            { "D", FeedCycle.D },
            { "CANCEL", FeedCycle.Cancel },
        };

        private readonly IReadOnlyDictionary<string, string> _outletStateMap = new Dictionary<string, string>
        {
            { "ON", "on" },
            { "OFF", "off" },
            { "AON", "auto" },
            { "AOF", "auto" }
        };

        private Status _config;
        private bool _disposed = false;
        private System.Timers.Timer _refresh;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApexMqttService"/> class.
        /// </summary>
        /// <param name="logger">Logging instance.</param>
        /// <param name="apexClient">Apex client.</param>
        /// <param name="apexName">Apex name.</param>
        /// <param name="refreshInterval">Refresh interval.</param>
        /// <param name="publishOnlyChangedValues">Only publish values when they change from previous value.</param>
        /// <param name="brokerSettings">MQTT broker settings.</param>
        public ApexMqttService(
            ILogger<ApexMqttService> logger,
            Client apexClient,
            string apexName,
            int refreshInterval,
            bool publishOnlyChangedValues,
            BrokerSettings brokerSettings)
            : base(logger, brokerSettings, "apex/" + apexName)
        {
            _log = logger;
            _refreshInterval = refreshInterval * 1000;
            _publishOnlyChangedValues = publishOnlyChangedValues;
            SubscribedTopics.Add(TopicRoot + "/outlets/+/set");
            SubscribedTopics.Add(TopicRoot + "/feedCycle/set");

            _client = apexClient;
        }

        #region Service implementation

        /// <inheritdoc />
        protected override async Task StartServiceAsync(CancellationToken cancellationToken = default)
        {
            await GetConfigAsync(cancellationToken)
                .ConfigureAwait(false);

            // Enable refresh
            if (_refresh != null)
                _refresh.Dispose();

            _refresh = new System.Timers.Timer(_refreshInterval);
            _refresh.Elapsed += RefreshAsync;
            _refresh.Start();
        }

        /// <inheritdoc />
        protected override Task StopServiceAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        #endregion

        #region MQTT Implementation

        /// <summary>
        /// Handles commands for the Harmony published to MQTT.
        /// </summary>
        /// <param name="e">Event args.</param>
        protected override async void Mqtt_MqttMsgPublishReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            var message = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            _log.LogInformation("MQTT message received for topic " + e.ApplicationMessage.Topic + ": " + message);

            if (e.ApplicationMessage.Topic == TopicRoot + "/feedCycle/set" && _feedCycleMap.ContainsKey(message.ToUpper()))
            {
                var feed = _feedCycleMap[message.ToUpper()];
                await _client.SetFeed(feed)
                    .ConfigureAwait(false);
            }
            else if (_topicOutletMap.ContainsKey(e.ApplicationMessage.Topic))
            {
                var outlet = _topicOutletMap[e.ApplicationMessage.Topic];
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

                await _client.SetOutlet(outlet, outletState)
                    .ConfigureAwait(false);
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
            var status = await _client.GetStatus()
                .ConfigureAwait(false);

            // Compare to current cached status
            var updates = CompareStatusObjects(_config, status);

            // If updated, publish changes
            if (updates.Count > 0)
            {
                foreach (var update in updates)
                {
                    await MqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(TopicRoot + update.Key)
                        .WithPayload(update.Value.Trim())
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build()).ConfigureAwait(false);
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
                if (!_publishOnlyChangedValues || (_publishOnlyChangedValues && status1.Outlets[i].State != status2.Outlets[i].State))
                {
                    updates.Add("/outlets/" + status2.Outlets[i].Name.Sluggify(), _outletStateMap[status2.Outlets[i].State.ToUpper()]);
                }
            }

            for (var i = 0; i < status1.Probes.Length; i++)
            {
                // Always publish probe values
                updates.Add("/probes/" + status2.Probes[i].Name.Sluggify(), status2.Probes[i].Value);
            }

            return updates;
        }

        /// <summary>
        /// Maps Apex device actions to subscription topics.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Awaitable <see cref="Task" />.</returns>
        private async Task GetConfigAsync(CancellationToken cancellationToken = default)
        {
            _config = await _client.GetStatus(cancellationToken)
                .ConfigureAwait(false);

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
                await MqttClient.PublishAsync(
                    new MqttApplicationMessageBuilder()
                        .WithTopic($"{TopicRoot}/outlets/{outlet.Name.Sluggify()}")
                        .WithPayload(currentValue.Trim())
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build(),
                    cancellationToken).ConfigureAwait(false);
            }

            // Initial probe states published at {TopicRoot}/probes/{probeName}
            foreach (var probe in _config.Probes)
            {
                await MqttClient.PublishAsync(
                    new MqttApplicationMessageBuilder()
                        .WithTopic($"{TopicRoot}/probes/{probe.Name.Sluggify()}")
                        .WithPayload(probe.Value.Trim())
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build(),
                    cancellationToken).ConfigureAwait(false);
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
