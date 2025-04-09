using Discord;
using Discord.WebSocket;
using LicenseSystem.Services;
using Microsoft.Extensions.Configuration;
using Server.Utils;

namespace Server.Discord
{
    public class BotHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly CommandHandler _commandHandler;
        private readonly PresenceManager _presenceManager;
        private readonly LicenseServer _licenseServer;
        private readonly IConfiguration _config;
        private readonly Logger _logger;

        private readonly string _token;
        private readonly ulong _adminChannelId;

        public BotHandler(
            DiscordSocketClient client,
            CommandHandler commandHandler,
            PresenceManager presenceManager,
            LicenseServer licenseServer,
            IConfiguration config,
            Logger logger)
        {
            _client = client;
            _commandHandler = commandHandler;
            _presenceManager = presenceManager;
            _licenseServer = licenseServer;
            _config = config;
            _logger = logger;

            _token = _config["Discord:Token"];
            _adminChannelId = _config.GetValue<ulong>("Discord:AdminChannelId", 0);

            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;

            _licenseServer.ClientConnected += OnClientConnected;
            _licenseServer.ClientDisconnected += OnClientDisconnected;
        }

        public async Task StartAsync()
        {
            _logger.Info("Discord-Bot wird gestartet...");

            if (string.IsNullOrEmpty(_token))
            {
                _logger.Error("Discord-Token fehlt in der Konfiguration!");
                return;
            }

            try
            {
                await _client.LoginAsync(TokenType.Bot, _token);
                await _client.StartAsync();

                await _commandHandler.InitializeAsync();

                _logger.Info("Discord-Bot erfolgreich angemeldet.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Starten des Discord-Bots: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            _logger.Info("Discord-Bot wird gestoppt...");

            try
            {
                _presenceManager.Stop();

                await _client.StopAsync();
                await _client.LogoutAsync();

                _logger.Info("Discord-Bot erfolgreich gestoppt.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Stoppen des Discord-Bots: {ex.Message}");
            }
        }

        private async void OnClientConnected(object sender, ClientEventArgs args)
        {
            try
            {
                if (_adminChannelId != 0)
                {
                    if (_client.GetChannel(_adminChannelId) is ITextChannel channel)
                    {
                        var embed = new EmbedBuilder()
                            .WithTitle("Neuer Client verbunden")
                            .WithColor(Color.Green)
                            .WithDescription($"Ein neuer Client hat sich mit dem Server verbunden.")
                            .AddField("Benutzername", args.Username)
                            .AddField("Client-ID", args.ClientId)
                            .WithCurrentTimestamp()
                            .Build();

                        await channel.SendMessageAsync(embed: embed);
                    }
                }

                await _presenceManager.UpdatePresenceAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler bei der Verarbeitung eines ClientConnected-Events: {ex.Message}");
            }
        }

        private async void OnClientDisconnected(object sender, ClientEventArgs args)
        {
            try
            {
                if (_adminChannelId != 0)
                {
                    if (_client.GetChannel(_adminChannelId) is ITextChannel channel)
                    {
                        var embed = new EmbedBuilder()
                            .WithTitle("Client getrennt")
                            .WithColor(Color.Red)
                            .WithDescription($"Ein Client hat die Verbindung zum Server getrennt.")
                            .AddField("Benutzername", args.Username)
                            .AddField("Client-ID", args.ClientId)
                            .WithCurrentTimestamp()
                            .Build();

                        await channel.SendMessageAsync(embed: embed);
                    }
                }

                await _presenceManager.UpdatePresenceAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler bei der Verarbeitung eines ClientDisconnected-Events: {ex.Message}");
            }
        }

        private async Task ReadyAsync()
        {
            _logger.Info($"Bot ist verbunden als {_client.CurrentUser.Username}#{_client.CurrentUser.Discriminator}");

            _presenceManager.Start();
            
        }

        private Task LogAsync(LogMessage msg)
        {
            switch (msg.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    _logger.Error($"Discord: {msg.Source}: {msg.Message} {msg.Exception}");
                    break;
                case LogSeverity.Warning:
                    _logger.Warning($"Discord: {msg.Source}: {msg.Message}");
                    break;
                case LogSeverity.Info:
                    _logger.Info($"Discord: {msg.Source}: {msg.Message}");
                    break;
                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                    _logger.Debug($"Discord: {msg.Source}: {msg.Message}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return Task.CompletedTask;
        }
    }
}