
using ColorThiefDotNet;
using MQTTClient.Discovery;
using MQTTClient.Helpers;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Color = System.Drawing.Color;
using Image = System.Drawing.Image;

namespace MQTTClient
{
    public class MQTTClient : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly List<MainMenuItem> mainMenuItems;

        private readonly List<SidebarItem> sidebarItems;

        private readonly SidebarItem progressSidebar;

        private readonly MqttClient client;

        private readonly MQTTClientSettingsViewModel settings;

        private readonly TopicHelper topicHelper;

        private readonly ColorThief colorThief;

        private readonly DiscoveryModule discoveryModule;

        private readonly ObjectSerializer serializer;

        public MQTTClient(IPlayniteAPI api) : base(api)
        {
            serializer = new ObjectSerializer();
            settings = new MQTTClientSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            client = new MqttFactory().CreateMqttClient();
            topicHelper = new TopicHelper(client, settings);
            discoveryModule = new DiscoveryModule(settings, PlayniteApi, topicHelper, client, serializer);
            colorThief = new ColorThief();
            progressSidebar = new SidebarItem
            {
                Visible = true,
                Title = "MQTT",
                Icon = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources", "icon.png"),
                Activated = SideButtonActivated
            };
            sidebarItems = new List<SidebarItem>
            {
                progressSidebar
            };
            mainMenuItems = new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    Description = "Reconnect", MenuSection = "@MQTT Client", Action = ReconnectMenuAction
                },
                new MainMenuItem
                {
                    Description = "Disconnect", MenuSection = "@MQTT Client", Action = DisconnectMenuAction
                }
            };
        }

        public Task StartDisconnect(bool notify = false)
        {
            var task = Task.CompletedTask;
            
            if (client.IsConnected)
            {
                if (topicHelper.TryGetTopic(Topics.ConnectionSubTopic, out var connectionTopic) &&
                    topicHelper.TryGetTopic(Topics.SelectedGameStatusSubTopic, out var selectedGameStatusTopic))
                {
                    task = client.PublishStringAsync(connectionTopic, "offline", retain: true)
                        .ContinueWith(async t => await client.PublishStringAsync(selectedGameStatusTopic, "offline", retain: true));
                }
                
                task = task.ContinueWith(async r => await client.DisconnectAsync())
                    .ContinueWith(
                        t =>
                        {
                            if (notify && !client.IsConnected)
                            {
                                PlayniteApi.Dialogs.ShowMessage("MQTT Disconnected");
                            }
                        });
            }

            return task;
        }

        public GlobalProgressResult StartConnection(bool notifyCompletion = false)
        {
            if (client.IsConnected)
            {
                PlayniteApi.Notifications.Add(
                    new NotificationMessage(Guid.NewGuid().ToString(), "Connection to MQTT underway", NotificationType.Error));
                throw new Exception("Connection to MQTT underway");
            }

            return PlayniteApi.Dialogs.ActivateGlobalProgress(
                args =>
                {
                    args.CurrentProgressValue = -1;
                    var optionsUnBuilt = new MqttClientOptionsBuilder().WithClientId(settings.Settings.ClientId)
                        .WithTcpServer(settings.Settings.ServerAddress, settings.Settings.Port)
                        .WithCredentials(settings.Settings.Username, LoadPassword())
                        .WithCleanSession();

                    if (settings.Settings.UseSecureConnection)
                    {
                        optionsUnBuilt = optionsUnBuilt.WithTls();
                    }

                    var options = optionsUnBuilt.Build();

                    client.ConnectAsync(options, args.CancelToken)
                        .ContinueWith(
                            t =>
                            {
                                if (t.Exception != null)
                                {
                                    PlayniteApi.Dialogs.ShowErrorMessage(
                                        $"MQTT: {string.Join(",", t.Exception.InnerExceptions.Select(i => i.Message))}",
                                        "MQTT Error");
                                }
                                else
                                {
                                    if (notifyCompletion && client.IsConnected)
                                    {
                                        PlayniteApi.Dialogs.ShowMessage("MQTT Connected");
                                    }
                                }
                            },
                            args.CancelToken)
                        .Wait(args.CancelToken);
                },
                new GlobalProgressOptions($"Connection to MQTT ({settings.Settings.ServerAddress}:{settings.Settings.Port})", true));
        }

        private string LoadPassword()
        {
            if (settings.Settings.Password == null)
            {
                return "";
            }

            return Encoding.UTF8.GetString(ProtectedData.Unprotect(settings.Settings.Password, Id.ToByteArray(), DataProtectionScope.CurrentUser));
        }

        private void SideButtonActivated()
        {
            if (client.IsConnected)
            {
                StartDisconnect(true);
            }
            else
            {
                StartConnection(true);
            }
        }

        private async Task ClientOnConnectedAsync(EventArgs eventArgs)
        {
            progressSidebar.ProgressValue = 100;

            if (topicHelper.TryGetTopic(Topics.ConnectionSubTopic, out var connectionTopic))
            {
                await client.PublishStringAsync(connectionTopic, "online", retain: true);
            }

            await UpdateSelectedGames(PlayniteApi.MainView.SelectedGames);

            if (topicHelper.TryGetTopic(Topics.ActiveViewSubTopic, out var activeViewTopic))
            {
                await client.PublishStringAsync(activeViewTopic, PlayniteApi.MainView.ActiveDesktopView.ToString());
            }

            await discoveryModule.Initialize();
        }

        private void DisconnectMenuAction(MainMenuItemActionArgs obj)
        {
            StartDisconnect().ContinueWith(t => PlayniteApi.Dialogs.ShowMessage("MQTT Disconnected Successfully")).Wait(TimeSpan.FromSeconds(3));
        }

        private void ReconnectMenuAction(MainMenuItemActionArgs obj)
        {
            StartDisconnect().ContinueWith(r => StartConnection(true)).Wait();
        }

        private Task ClientOnDisconnectedAsync(EventArgs eventArgs)
        {
            progressSidebar.ProgressValue = -1;
            return Task.CompletedTask;
        }

        private async Task<MqttClientPublishResult> PublishFileAsync(
            string topic,
            string filePath = null,
            MqttQualityOfServiceLevel qualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
            bool retain = false,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                var coverPath = PlayniteApi.Database.GetFullFilePath(filePath);
                if (File.Exists(coverPath))
                {
                    using (var fileStream = File.OpenRead(coverPath))
                    {
                        var result = new byte[fileStream.Length];
                        await fileStream.ReadAsync(result, 0, result.Length, cancellationToken);
                        return await client.PublishBinaryAsync(
                            topic,
                            result,
                            retain: retain,
                            qualityOfServiceLevel: qualityOfServiceLevel,
                            cancellationToken: cancellationToken);
                    }
                }
            }

            return await client.PublishBinaryAsync(
                topic,
                retain: retain,
                qualityOfServiceLevel: qualityOfServiceLevel,
                cancellationToken: cancellationToken);
        }

        private async Task<ArraySegment<byte>?> GetCoverData(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(PlayniteApi.Database.GetFullFilePath(path)))
            {
                using (FileStream fileStream = new FileStream(PlayniteApi.Database.GetFullFilePath(path), FileMode.Open))
                {
                    byte[] buffer = new byte[fileStream.Length];
                    int length = await fileStream.ReadAsync(buffer, 0, buffer.Length);
                    return new ArraySegment<byte>(buffer, 0, length);
                }
            }

            return null;
        }

        private async Task PublishGame(string topic, Game game, ArraySegment<byte>? coverData, bool retain)
        {
            var color = new ColorThiefDotNet.Color();
            if (settings.Settings.PublishCoverColors)
            {
                if (coverData?.Array != null)
                {
                    using (var memoryStream = new MemoryStream(coverData.Value.Array, coverData.Value.Offset, coverData.Value.Count, false))
                    {
                        color = colorThief.GetColor(new Bitmap(memoryStream)).Color;
                    }
                }
            }

            await client.PublishStringAsync(topic, serializer.Serialize(new GameData(game, color)), retain: retain);
        }

        private async Task UpdateSelectedGames(IEnumerable<Game> selectedGames)
        {
            var first = selectedGames.FirstOrDefault();
            if (first != null)
            {
                if (topicHelper.TryGetTopic(Topics.SelectedGameStatusSubTopic, out var statusTopic) &&
                    topicHelper.TryGetTopic(Topics.SelectedGameAttributesSubTopic, out var attributesTopic) &&
                    topicHelper.TryGetTopic(Topics.SelectedGameCoverSubTopic, out var selectedGameCoverSubTopic))
                {
                    await client.PublishStringAsync(statusTopic, "online", retain: true);
                    var cover = settings.Settings.PublishCover || settings.Settings.PublishCoverColors ? await GetCoverData(first.CoverImage) : null;
                    await PublishGame(attributesTopic, first, cover, true);

                    if (settings.Settings.PublishCover)
                    {
                        if (!cover.HasValue)
                        {
                            cover = await GetCoverData(first.Platforms.FirstOrDefault(p => !string.IsNullOrEmpty(p.Cover))?.Cover);
                        }
                        if (cover.HasValue)
                        {
                            await client.PublishBinaryAsync(selectedGameCoverSubTopic, cover.Value, retain: true);
                        }
                    }
                }
            }
            else
            {
                if (topicHelper.TryGetTopic(Topics.SelectedGameStatusSubTopic, out var statusTopic) &&
                    topicHelper.TryGetTopic(Topics.SelectedGameAttributesSubTopic, out var attributesTopic) &&
                    topicHelper.TryGetTopic(Topics.SelectedGameCoverSubTopic, out var selectedGameCoverSubTopic))
                {
                    await client.PublishStringAsync(statusTopic, "offline", retain: true);
                    await client.PublishStringAsync(attributesTopic, retain: true);
                    await PublishFileAsync(selectedGameCoverSubTopic, retain: true);
                }
            }
        }

        #region Overrides of Plugin

        public override Guid Id { get; } = Guid.Parse("90c44048-4f8f-43f7-a0c1-f8164bf1d7ef");

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            PlayniteApi.Dialogs.ActivateGlobalProgress(
                arg =>
                {
                    arg.CurrentProgressValue = -1;
                    Task.Run(async () => await discoveryModule.UpdateSelectedGamesDiscovery(), arg.CancelToken).Wait(arg.CancelToken);
                },
                new GlobalProgressOptions("Updating Selectable Games MQQT Discovery Topic"));

        }

        public override void Dispose()
        {
            client.Dispose();
            base.Dispose();
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            if (topicHelper.TryGetTopic(Topics.InstalledDataSubTopic, out var topic))
            {
                Task.Run(
                    async () => PublishGame(
                        topic,
                        args.Game,
                        settings.Settings.PublishCover ? await GetCoverData(args.Game.CoverImage) : null,
                        false));
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            if (topicHelper.TryGetTopic(Topics.CurrentStateSubTopic, out var topic))
            {
                client.PublishStringAsync(topic, "ON", retain: true);
            }
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            var tasks = Task.CompletedTask;
            if (topicHelper.TryGetTopic(Topics.CurrentAttributesSubTopic, out var dataTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentCoverSubTopic, out var currentCoverTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentBackgroundSubTopic, out var currentBackgroundTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentIconTopic, out var iconTopic))
            {
                tasks = tasks.ContinueWith(async t => await PublishGame(dataTopic, args.Game, await GetCoverData(args.Game.CoverImage), true));
                tasks = tasks.ContinueWith(
                    async t => await PublishFileAsync(
                        currentCoverTopic,
                        args.Game.CoverImage ?? args.Game.Platforms.FirstOrDefault(p => !string.IsNullOrEmpty(p.Cover))?.Cover,
                        retain: true));
                tasks = tasks.ContinueWith(
                    async t => await PublishFileAsync(
                        currentBackgroundTopic,
                        args.Game.BackgroundImage ?? args.Game.Platforms.FirstOrDefault(p => !string.IsNullOrEmpty(p.Background))?.Background,
                        retain: true));
                tasks = tasks.ContinueWith(
                    async t => await PublishFileAsync(
                        iconTopic,
                        args.Game.Icon ?? args.Game.Platforms.FirstOrDefault(p => !string.IsNullOrEmpty(p.Icon))?.Icon,
                        retain: true));
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var tasks = Task.CompletedTask;
            if (topicHelper.TryGetTopic(Topics.CurrentStateSubTopic, out var stageTopic))
            {
                tasks = tasks.ContinueWith(async t => await client.PublishStringAsync(stageTopic, "OFF", retain: true));
            }

            if (topicHelper.TryGetTopic(Topics.CurrentAttributesSubTopic, out var dataTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentCoverSubTopic, out var currentCoverTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentBackgroundSubTopic, out var currentBackgroundTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentIconTopic, out var iconTopic))
            {
                tasks.ContinueWith(async t => await client.PublishStringAsync(dataTopic, retain: true));
                tasks.ContinueWith(async t => await client.PublishStringAsync(currentCoverTopic, retain: true));
                tasks.ContinueWith(async t => await client.PublishStringAsync(currentBackgroundTopic, retain: true));
                tasks.ContinueWith(async t => await client.PublishStringAsync(iconTopic, retain: true));
            }
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            if (topicHelper.TryGetTopic(Topics.UninstalledDataSubTopic, out var topic))
            {
                Task.Run(
                    async () => await PublishGame(
                        topic,
                        args.Game,
                        settings.Settings.PublishCover ? await GetCoverData(args.Game.CoverImage) : null,
                        false));
            }
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            client.ConnectedAsync += ClientOnConnectedAsync;
            client.DisconnectedAsync += ClientOnDisconnectedAsync;
            StartConnection();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            client.ConnectedAsync -= ClientOnConnectedAsync;
            client.DisconnectedAsync -= ClientOnDisconnectedAsync;
            StartDisconnect();
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            Task.Run(() => UpdateSelectedGames(args.NewValue));
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            return sidebarItems;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            return mainMenuItems;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new MQTTClientSettingsView();
        }

        #endregion
    }
}