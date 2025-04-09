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
        private readonly Logger _logger;

        private Timer? _presenceTimer;
        private int _currentPresenceIndex = 0;
        private int _currentStatusIndex = 0;
        private readonly DateTime _startTime;
        
        private readonly TimeSpan _updateInterval;

        public PresenceManager(
            DiscordSocketClient client,
            LicenseServer licenseServer,
            IConfiguration config,
            Logger logger)
        {
            _client = client;
            _licenseServer = licenseServer;
            _logger = logger;
            
            _updateInterval = TimeSpan.FromSeconds(
                config.GetValue("Discord:PresenceUpdateIntervalSeconds", 60));

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
                var activity = GetNextActivity();
                
                _currentStatusIndex = (_currentStatusIndex + 1) % 3;
                var status = GetNextStatus();
                
                await _client.SetStatusAsync(status);
                await _client.SetActivityAsync(activity);
                
                _logger.Debug($"Presence aktualisiert: Status={status}, Activity={activity.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Aktualisieren der Presence: {ex.Message}");
            }
        }

        private global::Discord.Game GetNextActivity()
        {
            switch (_currentPresenceIndex)
            {
                case 0:
                    var onlineUsers = _licenseServer.GetOnlineUsers().Count;
                    if (onlineUsers == 0)
                        return new global::Discord.Game("user activity", ActivityType.Watching);
            
                    string userText = onlineUsers == 1 ? "user" : "users";
                    return new global::Discord.Game($"over {onlineUsers} {userText}", ActivityType.Watching);


                case 1:
                    var totalUsers = _licenseServer.GetAllUsers().Count;
                    return new global::Discord.Game($"{totalUsers} registered licenses", ActivityType.Watching);

                case 2:
                    var uptime = DateTime.Now - _startTime;
                    return new global::Discord.Game($"since: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");
                
                case 3:
                default:
                    return new global::Discord.Game("license server", ActivityType.Watching);
            }
        }

        private UserStatus GetNextStatus()
        {
            return _currentStatusIndex switch
            {
                0 => UserStatus.Online,
                1 => UserStatus.DoNotDisturb,
                2 => UserStatus.Idle,
                _ => UserStatus.Online
            };
        }
    }
}