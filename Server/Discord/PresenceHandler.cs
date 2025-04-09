using Discord;
using Discord.WebSocket;
using LicenseSystem.Services;
using Microsoft.Extensions.Configuration;
using Server.Utils;

namespace Server.Discord
{
    public class PresenceManager
    {
        private readonly DiscordSocketClient _client;
        private readonly LicenseServer _licenseServer;
        private readonly IConfiguration _config;
        private readonly Logger _logger;

        private Timer _presenceTimer;
        private int _currentPresenceIndex = 0;
        private DateTime _startTime;
        
        private readonly TimeSpan _updateInterval;

        public PresenceManager(
            DiscordSocketClient client,
            LicenseServer licenseServer,
            IConfiguration config,
            Logger logger)
        {
            _client = client;
            _licenseServer = licenseServer;
            _config = config;
            _logger = logger;
            
            _updateInterval = TimeSpan.FromSeconds(
                _config.GetValue("Discord:PresenceUpdateIntervalSeconds", 60));

            _startTime = DateTime.Now;
        }
        
        public void Start()
        {
            _logger.Info("PresenceManager wird gestartet...");
            
            _presenceTimer = new Timer(async void (_) =>
            {
                await UpdatePresenceAsync();
            }, null, TimeSpan.Zero, _updateInterval);
        }
        
        public void Stop()
        {
            _logger.Info("PresenceManager wird gestoppt...");
            _presenceTimer?.Dispose();
            _presenceTimer = null;
        }
        
        public async Task UpdatePresenceAsync()
        {
            try
            {
                _currentPresenceIndex = (_currentPresenceIndex + 1) % 4;

                Game activity;
                switch (_currentPresenceIndex)
                {
                    case 0:
                        var onlineUsers = _licenseServer.GetOnlineUsers().Count;
                        activity = new Game($"{onlineUsers} Benutzer online", ActivityType.Watching);
                        break;

                    case 1:
                        var totalUsers = _licenseServer.GetAllUsers().Count;
                        activity = new Game($"{totalUsers} registrierte Benutzer", ActivityType.Watching);
                        break;

                    case 3:
                    default:
                        var uptime = DateTime.Now - _startTime;
                        activity = new Game($"Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");
                        break;
                }
                
                await _client.SetActivityAsync(activity);
                _logger.Debug($"Presence aktualisiert: {activity.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Aktualisieren der Presence: {ex.Message}");
            }
        }
    }
}