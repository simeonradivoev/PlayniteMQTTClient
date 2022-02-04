using MQTTClient.Helpers;
using MQTTnet.Client;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MQTTClient.Discovery
{
    public class DiscoveryModule
    {
        private readonly MQTTClientSettingsViewModel settings;

        private readonly IPlayniteAPI playniteApi;

        private readonly TopicHelper topicHelper;

        private readonly MqttClient client;

        private readonly ObjectSerializer serializer;

        public DiscoveryModule(MQTTClientSettingsViewModel settings, IPlayniteAPI playniteApi, TopicHelper topicHelper, MqttClient client, ObjectSerializer serializer)
        {
            this.settings = settings;
            this.playniteApi = playniteApi;
            this.topicHelper = topicHelper;
            this.client = client;
            this.serializer = serializer;
        }

        private DeviceDiscoveryInfo BuildDeviceDiscoveryInfo()
        {
            return new DeviceDiscoveryInfo()
            {
                name = settings.Settings.DeviceName,
                identifiers = new List<string>()
                {
                    $"playnite_{settings.Settings.DeviceId}"
                },
                manufacturer = "Playnite",
                sw_version = playniteApi.ApplicationInfo.ApplicationVersion.ToString()
            };
        }
        
        private bool TryGetHomeAssistantTopic(string topic,out string topicOut)
        {
            if (!string.IsNullOrEmpty(settings.Settings.HomeAssistantTopic))
            {
                topicOut = $"{settings.Settings.HomeAssistantTopic}/{topic}";
                return true;
            }

            topicOut = null;
            return false;
        }
        
        public async Task UpdateSelectedGamesDiscovery(DeviceDiscoveryInfo device = null)
        {
            if (device == null)
            {
                device = BuildDeviceDiscoveryInfo();
            }

            if (topicHelper.TryGetTopic(Topics.SelectedGameStatusSubTopic, out var selectedGameStatusTopic) &&
                topicHelper.TryGetTopic(Topics.SelectedGameAttributesSubTopic, out var selectedGameAttributesTopic))
            {
                if (topicHelper.TryGetTopic(Topics.SelectedGameCommandsSubTopic, out var selectedGameCommandTopic) &&
                    TryGetHomeAssistantTopic($"select/{settings.Settings.DeviceId}/{Topics.SelectedGameTopic}/config", out var selectedGameTopic))
                {
                    await client.PublishStringAsync(
                        selectedGameTopic,
                        serializer.Serialize(
                            new SelectedGameDiscoveryInfo()
                            {
                                name = $"{settings.Settings.ClientId} Selected Game",
                                state_topic = selectedGameAttributesTopic,
                                unique_id = $"playnite_{settings.Settings.DeviceId}_selected_game",
                                device = device,
                                availability_topic = selectedGameStatusTopic,
                                json_attributes_topic = selectedGameAttributesTopic,
                                options = playniteApi.Database.Games.Select(g => g.Name).ToList(),
                                value_template = "{{ value_json.Name }}",
                                command_topic = selectedGameCommandTopic,
                                icon = "mdi:selection"
                            }),
                        retain: true);
                }

                if (topicHelper.TryGetTopic(Topics.SelectedGameCoverSubTopic, out var selectedGameCoverSubTopic) &&
                    TryGetHomeAssistantTopic($"camera/{settings.Settings.DeviceId}/{Topics.SelectedGameCoverTopic}/config", out var selectedGameCoverTopic))
                {
                    await client.PublishStringAsync(
                        selectedGameCoverTopic,
                        serializer.Serialize(
                            new SelectedGameCoverDiscoveryInfo()
                            {
                                name = $"{settings.Settings.ClientId} Selected Game Cover",
                                topic = selectedGameCoverSubTopic,
                                unique_id = $"playnite_{settings.Settings.DeviceId}_selected_game_cover",
                                device = device,
                                availability_topic = selectedGameStatusTopic,
                                json_attributes_topic = selectedGameAttributesTopic,
                                icon = "mdi:image"
                            }),
                        retain: true);
                }
            }
        }
        
        public async Task Initialize()
        {
            var device = BuildDeviceDiscoveryInfo();

            if (topicHelper.TryGetTopic(Topics.ConnectionSubTopic, out var connectionTopic))
            {
                if (topicHelper.TryGetTopic(Topics.CurrentAttributesSubTopic, out var currentAttributesTopic))
                {
                    if (topicHelper.TryGetTopic(Topics.CurrentStateSubTopic, out var currentStateTopic) &&
                        TryGetHomeAssistantTopic($"binary_sensor/{settings.Settings.DeviceId}/{Topics.CurrentTopic}/config", out var currentDiscoveryTopic))
                    {
                        await client.PublishStringAsync(
                            currentDiscoveryTopic,
                            serializer.Serialize(
                                new PlayingGameDiscoveryInfo()
                                {
                                    name = $"{settings.Settings.ClientId} Playing Game",
                                    state_topic = currentStateTopic,
                                    unique_id = $"playnite_{settings.Settings.DeviceId}_playing_game",
                                    device = device,
                                    availability_topic = connectionTopic,
                                    device_class = "running",
                                    json_attributes_topic = currentAttributesTopic,
                                    icon = "mdi:gamepad"
                                }),
                            retain: true);
                    }

                    if (topicHelper.TryGetTopic(Topics.CurrentCoverSubTopic, out var currentCoverSubTopic) &&
                        TryGetHomeAssistantTopic($"camera/{settings.Settings.DeviceId}/{Topics.CurrentCoverTopic}/config", out var currentCoverTopic))
                    {
                        await client.PublishStringAsync(
                            currentCoverTopic,
                            serializer.Serialize(
                                new PlayingGameCoverDiscoveryInfo()
                                {
                                    name = $"{settings.Settings.ClientId} Playing Game Cover",
                                    topic = currentCoverSubTopic,
                                    unique_id = $"playnite_{settings.Settings.DeviceId}_playing_game_cover",
                                    device = device,
                                    availability_topic = connectionTopic,
                                    json_attributes_topic = currentAttributesTopic,
                                    icon = "mdi:image"
                                }),
                            retain: true);
                    }
                    
                    if (topicHelper.TryGetTopic(Topics.CurrentBackgroundSubTopic, out var currentBackgroundSubTopic) &&
                        TryGetHomeAssistantTopic($"camera/{settings.Settings.DeviceId}/{Topics.CurrentBackgroundTopic}/config", out var currentBackgroundTopic))
                    {
                        await client.PublishStringAsync(
                            currentBackgroundTopic,
                            serializer.Serialize(
                                new PlayingGameBackgroundDiscoveryInfo()
                                {
                                    name = $"{settings.Settings.ClientId} Playing Game Background",
                                    topic = currentBackgroundSubTopic,
                                    unique_id = $"playnite_{settings.Settings.DeviceId}_playing_game_background",
                                    device = device,
                                    availability_topic = connectionTopic,
                                    json_attributes_topic = currentAttributesTopic,
                                    icon = "mdi:image"
                                }),
                            retain: true);
                    }
                    
                    if (topicHelper.TryGetTopic(Topics.CurrentIconSubTopic, out var currentIconSubTopic) &&
                        TryGetHomeAssistantTopic($"camera/{settings.Settings.DeviceId}/{Topics.CurrentIconTopic}/config", out var currentIconTopic))
                    {
                        await client.PublishStringAsync(
                            currentIconTopic,
                            serializer.Serialize(
                                new PlayingGameBackgroundDiscoveryInfo()
                                {
                                    name = $"{settings.Settings.ClientId} Playing Game Icon",
                                    topic = currentIconSubTopic,
                                    unique_id = $"playnite_{settings.Settings.DeviceId}_playing_game_icon",
                                    device = device,
                                    availability_topic = connectionTopic,
                                    json_attributes_topic = currentAttributesTopic,
                                    icon = "mdi:image"
                                }),
                            retain: true);
                    }
                }

                await UpdateSelectedGamesDiscovery(device);
                
                if (topicHelper.TryGetTopic(Topics.ActiveViewSubTopic, out var activeViewTopic) &&
                    topicHelper.TryGetTopic(Topics.ActiveViewCommandSubTopic, out var activeViewCommandTopic) &&
                    TryGetHomeAssistantTopic($"select/{settings.Settings.DeviceId}/{Topics.ActiveViewSubTopic}/config", out var statusDiscoveryTopic))
                {
                    await client.PublishStringAsync(
                        statusDiscoveryTopic,
                        serializer.Serialize(
                            new ActiveViewGameDiscoveryInfo()
                            {
                                name = $"{settings.Settings.ClientId} Active View",
                                state_topic = activeViewTopic,
                                unique_id = $"playnite_{settings.Settings.DeviceId}_active_view",
                                device = device,
                                availability_topic = connectionTopic,
                                options = Enum.GetNames(typeof(DesktopView)).ToList(),
                                command_topic = activeViewCommandTopic,
                                icon = "mdi:view-carousel"
                            }),
                        retain: true);
                }
            }
        }
    }
}