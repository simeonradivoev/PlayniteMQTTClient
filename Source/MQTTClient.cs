
using ColorThiefDotNet;
using Microsoft.Win32;
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

        private readonly MqttClient client;

        private readonly MQTTClientSettingsViewModel settings;

        private readonly TopicHelper topicHelper;

        private readonly ColorThief colorThief;

        private readonly DiscoveryModule discoveryModule;

        private readonly ObjectSerializer serializer;

        private readonly CancellationTokenSource applicationClosingCompletionSource;

        private readonly IProgress<float> sidebarProgress;

        private readonly IProgress<ConnectionState> connectedState;

        private PowerModes lastPowerMode;

        public MQTTClient(IPlayniteAPI api) : base(api)
        {
            serializer = new ObjectSerializer();
            settings = new MQTTClientSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            applicationClosingCompletionSource = new CancellationTokenSource();
            client = new MqttFactory().CreateMqttClient();
            topicHelper = new TopicHelper(client, settings);
            discoveryModule = new DiscoveryModule(settings, PlayniteApi, topicHelper, client, serializer);
            colorThief = new ColorThief();
            
            var progressSidebar = new SidebarItem
            {
                Visible = true,
                Icon = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources", "icon.png"),
                Activated = SideButtonActivated,
                ProgressMaximum = 1
            };
            sidebarItems = new List<SidebarItem>
            {
                progressSidebar
            };
            sidebarProgress = new Progress<float>(progress => progressSidebar.ProgressValue = progress);
            connectedState = new Progress<ConnectionState>(v =>
            {
                switch (v)
                {

                    case ConnectionState.Disconnected:
                        progressSidebar.Title = "MQTT (disconnected)";
                        break;
                    case ConnectionState.Connected:
                        progressSidebar.Title = "MQTT (connected)";
                        break;
                    case ConnectionState.Connecting:
                        progressSidebar.Title = "MQTT (connecting)";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(v), v, null);
                }
            });
            connectedState.Report(ConnectionState.Disconnected);
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

        public async Task<MqttClientConnectResult> StartConnectionTask(bool notifyCompletion, IProgress<float> progress = null,CancellationToken cancellationToken = default)
        {
            var optionsUnBuilt = new MqttClientOptionsBuilder().WithClientId(settings.Settings.ClientId)
                .WithTcpServer(settings.Settings.ServerAddress, settings.Settings.Port)
                .WithCredentials(settings.Settings.Username, LoadPassword())
                .WithCleanSession();

            if (settings.Settings.UseSecureConnection)
            {
                optionsUnBuilt = optionsUnBuilt.WithTlsOptions(o =>
                {
                    o.UseTls(true);
                });
            }

            var options = optionsUnBuilt.Build();
            try
            {
                progress?.Report(0.1f);
                var connectionResult = await client.ConnectAsync(options, cancellationToken);
                if (notifyCompletion && client.IsConnected)
                {
                    PlayniteApi.Dialogs.ShowMessage("MQTT Connected");
                }
                if (client.IsConnected)
                {
                    logger.Debug("MQTT Connected");
                }

                return connectionResult;
            }
            catch (Exception e)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    $"MQTT: {e.Message}",
                    "MQTT Error");
            }

            return null;
        }

        public GlobalProgressResult StartConnection(bool notifyCompletion = false)
        {
            connectedState.Report(ConnectionState.Connecting);
            if (client.IsConnected)
            {
                PlayniteApi.Notifications.Add(
                    new NotificationMessage(Guid.NewGuid().ToString(), "Connection to MQTT underway", NotificationType.Error));
                throw new Exception("Connection to MQTT underway");
            }

            return PlayniteApi.Dialogs.ActivateGlobalProgress(
                args =>
                {
                    args.ProgressMaxValue = 1;
                    args.CurrentProgressValue = 0;
                    StartConnectionTask(notifyCompletion, sidebarProgress, args.CancelToken).ContinueWith(t => args.CurrentProgressValue = 1,args.CancelToken);
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
            sidebarProgress.Report(0.6f);

            if (topicHelper.TryGetTopic(Topics.ConnectionSubTopic, out var connectionTopic))
            {
                await client.PublishStringAsync(connectionTopic, "online", cancellationToken: applicationClosingCompletionSource.Token, retain: true);
            }

            sidebarProgress.Report(0.7f);

            await UpdateSelectedGames(PlayniteApi.MainView.SelectedGames,applicationClosingCompletionSource.Token);

            sidebarProgress.Report(0.8f);

            if (topicHelper.TryGetTopic(Topics.ActiveViewSubTopic, out var activeViewTopic))
            {
                await client.PublishStringAsync(activeViewTopic, PlayniteApi.MainView.ActiveDesktopView.ToString(),cancellationToken:applicationClosingCompletionSource.Token);
            }

            sidebarProgress.Report(0.9f);

            await discoveryModule.Initialize();

            sidebarProgress.Report(1f);
            connectedState.Report(ConnectionState.Connected);
        }

        private void DisconnectMenuAction(MainMenuItemActionArgs obj)
        {
            StartDisconnect().ContinueWith(t => PlayniteApi.Dialogs.ShowMessage("MQTT Disconnected Successfully")).Wait(TimeSpan.FromSeconds(3));
        }

        private void ReconnectMenuAction(MainMenuItemActionArgs obj)
        {
            StartDisconnect().ContinueWith(r => StartConnection(true)).Wait(applicationClosingCompletionSource.Token);
        }

        private Task ClientOnDisconnectedAsync(EventArgs eventArgs)
        {
            connectedState.Report(ConnectionState.Disconnected);
            sidebarProgress.Report(-1);

            logger.Debug("MQTT client disconnected.");
            if (lastPowerMode == PowerModes.Resume)
            {
                logger.Debug("Last power modes is Resume. Connecting...");
                Task.Run(async () =>
                {
                    var sidebarItem = sidebarItems.First();
                    sidebarItem.ProgressMaximum = 1;
                    sidebarItem.ProgressValue = 0;
                    try
                    {
                        await StartConnectionTask(false, cancellationToken: applicationClosingCompletionSource.Token);
                        sidebarItem.ProgressValue = 1;
                        logger.Debug("MQTT client reconnected after disconnect on power resume.");
                    }
                    catch (Exception ex)
                    {
                        logger.Debug($"MQTT reconnection failed on power resume. Exception: {ex}");
                    }
                });
            }

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

        private async Task PublishGame(string topic, Game game, ArraySegment<byte>? coverData, bool retain, CancellationToken cancellationToken = default)
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

            await client.PublishStringAsync(topic, serializer.Serialize(new GameData(game, color)), retain: retain,cancellationToken:cancellationToken);
        }

        private async Task UpdateSelectedGames(IEnumerable<Game> selectedGames,CancellationToken cancellationToken = default)
        {
            var first = selectedGames.FirstOrDefault();
            if (first != null)
            {
                if (topicHelper.TryGetTopic(Topics.SelectedGameStatusSubTopic, out var statusTopic) &&
                    topicHelper.TryGetTopic(Topics.SelectedGameAttributesSubTopic, out var attributesTopic) &&
                    topicHelper.TryGetTopic(Topics.SelectedGameCoverSubTopic, out var selectedGameCoverSubTopic))
                {
                    await client.PublishStringAsync(statusTopic, "online", retain: true, cancellationToken: cancellationToken);
                    var cover = settings.Settings.PublishCover || settings.Settings.PublishCoverColors ? await GetCoverData(first.CoverImage) : null;
                    await PublishGame(attributesTopic, first, cover, true,cancellationToken);

                    if (settings.Settings.PublishCover)
                    {
                        if (!cover.HasValue)
                        {
                            cover = await GetCoverData(first.Platforms.FirstOrDefault(p => !string.IsNullOrEmpty(p.Cover))?.Cover);
                        }
                        if (cover.HasValue)
                        {
                            await client.PublishBinaryAsync(selectedGameCoverSubTopic, cover.Value, retain: true, cancellationToken:cancellationToken);
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
                    await client.PublishStringAsync(statusTopic, "offline", retain: true, cancellationToken: cancellationToken);
                    await client.PublishStringAsync(attributesTopic, retain: true,cancellationToken: cancellationToken);
                    await PublishFileAsync(selectedGameCoverSubTopic, retain: true, cancellationToken:cancellationToken);
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
                        false),applicationClosingCompletionSource.Token);
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            if (topicHelper.TryGetTopic(Topics.CurrentStateSubTopic, out var topic))
            {
                Task.Run(() => client.PublishStringAsync(topic, "ON", retain: true,cancellationToken:applicationClosingCompletionSource.Token),applicationClosingCompletionSource.Token);
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
                tasks = tasks.ContinueWith(async t => await PublishGame(dataTopic, args.Game, await GetCoverData(args.Game.CoverImage), true,applicationClosingCompletionSource.Token));
                tasks = tasks.ContinueWith(
                    async t => await PublishFileAsync(
                        currentCoverTopic,
                        args.Game.CoverImage ?? args.Game.Platforms.FirstOrDefault(p => !string.IsNullOrEmpty(p.Cover))?.Cover,
                        retain: true,cancellationToken:applicationClosingCompletionSource.Token));
                tasks = tasks.ContinueWith(
                    async t => await PublishFileAsync(
                        currentBackgroundTopic,
                        args.Game.BackgroundImage ?? args.Game.Platforms.FirstOrDefault(p => !string.IsNullOrEmpty(p.Background))?.Background,
                        retain: true,cancellationToken:applicationClosingCompletionSource.Token));
                tasks = tasks.ContinueWith(
                    async t => await PublishFileAsync(
                        iconTopic,
                        args.Game.Icon ?? args.Game.Platforms.FirstOrDefault(p => !string.IsNullOrEmpty(p.Icon))?.Icon,
                        retain: true,cancellationToken: applicationClosingCompletionSource.Token));
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var tasks = Task.CompletedTask;
            if (topicHelper.TryGetTopic(Topics.CurrentStateSubTopic, out var stageTopic))
            {
                tasks = tasks.ContinueWith(async t => await client.PublishStringAsync(stageTopic, "OFF", retain: true,cancellationToken:applicationClosingCompletionSource.Token));
            }

            if (topicHelper.TryGetTopic(Topics.CurrentAttributesSubTopic, out var dataTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentCoverSubTopic, out var currentCoverTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentBackgroundSubTopic, out var currentBackgroundTopic) &&
                topicHelper.TryGetTopic(Topics.CurrentIconTopic, out var iconTopic))
            {
                tasks.ContinueWith(async t => await client.PublishStringAsync(dataTopic, retain: true,cancellationToken:applicationClosingCompletionSource.Token));
                tasks.ContinueWith(async t => await client.PublishStringAsync(currentCoverTopic, retain: true,cancellationToken:applicationClosingCompletionSource.Token));
                tasks.ContinueWith(async t => await client.PublishStringAsync(currentBackgroundTopic, retain: true, cancellationToken: applicationClosingCompletionSource.Token));
                tasks.ContinueWith(async t => await client.PublishStringAsync(iconTopic, retain: true, cancellationToken: applicationClosingCompletionSource.Token));
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
            client.ConnectingAsync += ClientOnConnectingAsync;
            client.DisconnectedAsync += ClientOnDisconnectedAsync;
            SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;
            if (settings.Settings.ShowProgress)
            {
                StartConnection();
            }
            else
            {
                Task.Run(async () =>
                {
                    var sidebarItem = sidebarItems.First();
                    sidebarItem.ProgressMaximum = 1;
                    sidebarItem.ProgressValue = 0;
                    try
                    {
                        await StartConnectionTask(false, cancellationToken:applicationClosingCompletionSource.Token);
                    }
                    finally
                    {
                        sidebarItem.ProgressValue = 1;
                    }
                });
            }
        }

        private Task ClientOnConnectingAsync(MqttClientConnectingEventArgs arg)
        {
            sidebarProgress.Report(0.5f);
            return Task.CompletedTask;
        }

        private void SystemEventsOnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            logger.Debug($"System power mode changed to: {e.Mode}");
            lastPowerMode = e.Mode;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            client.ConnectedAsync -= ClientOnConnectedAsync;
            client.ConnectingAsync -= ClientOnConnectingAsync;
            client.DisconnectedAsync -= ClientOnDisconnectedAsync;
            SystemEvents.PowerModeChanged -= SystemEventsOnPowerModeChanged;
            StartDisconnect().Wait();
            applicationClosingCompletionSource.Cancel();
            applicationClosingCompletionSource.Dispose();
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            Task.Run(() => UpdateSelectedGames(args.NewValue,applicationClosingCompletionSource.Token));
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

        private enum ConnectionState
        {
            Disconnected,
            Connected,
            Connecting
        }
    }
}