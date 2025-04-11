using System;
using System.Threading.Tasks;
using LicenseSystem.Models;
using LicenseSystem.Services;
using Microsoft.Extensions.Configuration;
using Server.Game;
using Server.Utils;

namespace Server.Server
{
    public class MessageHandler
    {
        private readonly LicenseServer _licenseServer;
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        private readonly GameInfoService _gameInfoService;
        private readonly ChunkManager _chunkManager;

        public MessageHandler(
            LicenseServer licenseServer,
            IConfiguration config,
            Logger logger,
            GameInfoService gameInfoService,
            ChunkManager chunkManager)
        {
            _licenseServer = licenseServer;
            _config = config;
            _logger = logger;
            _gameInfoService = gameInfoService;
            _chunkManager = chunkManager;
            
            _licenseServer.ClientMessageReceived += OnClientMessageReceived;
            
            _logger.Info("MessageHandler initialized and subscribed to client messages");
        }

        private async void OnClientMessageReceived(object sender, ClientMessageEventArgs args)
        {
            try
            {
                if (args.Message.Type != "COMMAND")
                    return;

                string command = args.Message.Content.Trim();
                _logger.Info($"Received command from client {args.ClientId}: {command}");

                string[] parts = command.Split(' ', 2);
                string commandName = parts[0].ToLower();
                string commandArgs = parts.Length > 1 ? parts[1] : string.Empty;

                switch (commandName)
                {
                    case "download":
                        await HandleDownloadCommand(args.ClientId, commandArgs);
                        break;
                    case "list":
                        await HandleListCommand(args.ClientId, commandArgs);
                        break;
                    case "info":
                        await HandleInfoCommand(args.ClientId, commandArgs);
                        break;
                    case "status":
                        await HandleStatusCommand(args.ClientId, commandArgs);
                        break;
                    default:
                        _logger.Warning($"Unknown command from client {args.ClientId}: {commandName}");
                        _licenseServer.SendMessageToClient(args.ClientId, 
                            $"Unknown command: {commandName}. Type '/help' for available commands.", "ERROR");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling client message: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private async Task HandleStatusCommand(string clientId, string commandArgs)
        {
            try
            {
                _logger.Info($"Processing status command for client {clientId}");
                
                var users = _licenseServer.GetAllUsers();
                var user = users.FirstOrDefault(u => u.ClientId == clientId);
                
                if (user == null)
                {
                    _logger.Warning($"User not found for client ID: {clientId}");
                    _licenseServer.SendMessageToClient(clientId, 
                        "Error: User information not found. Please reconnect.", "ERROR");
                    return;
                }
                
                DateTime serverStartTime;
                if (!DateTime.TryParse(_config.GetValue<string>("Server:StartTime", null), out serverStartTime))
                {
                    serverStartTime = DateTime.Now.AddHours(-new Random().Next(1, 48));
                }
                
                var uptime = DateTime.Now - serverStartTime;
                
                int activeLicenses = 0;
                int expiredLicenses = 0;
                
                foreach (var u in users)
                {
                    if (u.LicenseExpiration > DateTime.Now)
                    {
                        activeLicenses++;
                    }
                    else
                    {
                        expiredLicenses++;
                    }
                }
                
                var serverStatus = new 
                {
                    Version = "1.0.0",
                    Uptime = uptime,
                    ConnectedUsers = _licenseServer.GetOnlineUsers().Count,
                    TotalUsers = users.Count,
                    ActiveLicenses = activeLicenses,
                    ExpiredLicenses = expiredLicenses
                };
                
                var connectionStatus = new
                {
                    IsConnected = user.IsOnline,
                    Ping = new Random().Next(10, 100),
                    ConnectedSince = user.LastLogin,
                    DataSent = new Random().Next(1000, 10000000),
                    DataReceived = new Random().Next(1000, 10000000)
                };
                
                var systemResources = new
                {
                    CpuUsage = new Random().NextDouble() * 100,
                    MemoryUsage = new Random().NextDouble() * 100,
                    AvailableDiskSpace = (long)new Random().Next(1, 1000) * 1024 * 1024 * 1024
                };
                
                var response = new
                {
                    ServerStatus = serverStatus,
                    ConnectionStatus = connectionStatus,
                    SystemResources = systemResources
                };
                
                string responseJson = System.Text.Json.JsonSerializer.Serialize(response);
                _licenseServer.SendMessageToClient(clientId, responseJson, "STATUS_RESPONSE");
                
                _logger.Info($"Sent server status information to client {clientId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing status command: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
                
                _licenseServer.SendMessageToClient(clientId, 
                    $"Error retrieving server status: {ex.Message}", "ERROR");
            }
        }

        private async Task HandleDownloadCommand(string clientId, string commandArgs)
        {
            try
            {
                _logger.Info($"Processing download command for client {clientId}: {commandArgs}");
                
                string gameIdStr = commandArgs.Trim();
                if (string.IsNullOrEmpty(gameIdStr))
                {
                    _logger.Warning($"Client {clientId} requested download without providing a game ID");
                    _licenseServer.SendMessageToClient(clientId, "Error: No game ID provided", "ERROR");
                    return;
                }

                var gameInfo = await _gameInfoService.GetGameInfoAsync(gameIdStr);
                if (gameInfo == null)
                {
                    _logger.Warning($"Game not found for ID: {gameIdStr}");
                    _licenseServer.SendMessageToClient(clientId, $"Error: Game with ID '{gameIdStr}' not found", "ERROR");
                    return;
                }

                _logger.Info($"Found game: {gameInfo.Title} (ID: {gameInfo.Id})");

                var downloadInfoGenerator = new DownloadInfoGenerator(
                    _config,
                    _logger,
                    _gameInfoService,
                    _chunkManager);

                var downloadInfo = await downloadInfoGenerator.GenerateDownloadInfoAsync(gameInfo);
                if (downloadInfo == null)
                {
                    _logger.Error($"Failed to generate download info for game: {gameInfo.Title}");
                    _licenseServer.SendMessageToClient(clientId, 
                        $"Error: Could not prepare download for '{gameInfo.Title}'", "ERROR");
                    return;
                }

                string downloadInfoJson = System.Text.Json.JsonSerializer.Serialize(downloadInfo);
                _licenseServer.SendMessageToClient(clientId, downloadInfoJson, "DOWNLOAD_INFO");
                
                _logger.Info($"Sent download info for {gameInfo.Title} to client {clientId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing download command: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
                
                _licenseServer.SendMessageToClient(clientId, 
                    $"Error preparing download: {ex.Message}", "ERROR");
            }
        }
        
        private async Task HandleInfoCommand(string clientId, string commandArgs)
        {
            try
            {
                _logger.Info($"Processing info command for client {clientId}");
                
                var users = _licenseServer.GetAllUsers();
                var user = users.FirstOrDefault(u => u.ClientId == clientId);
                
                if (user == null)
                {
                    _logger.Warning($"User not found for client ID: {clientId}");
                    _licenseServer.SendMessageToClient(clientId, 
                        "Error: User information not found. Please reconnect.", "ERROR");
                    return;
                }
                
                var licenseInfo = new 
                {
                    Username = user.Username,
                    LicenseKey = user.LicenseKey,
                    ExpirationDate = user.LicenseExpiration,
                    IsActive = user.IsOnline && user.LicenseExpiration > DateTime.Now,
                    FirstLogin = user.FirstLogin,
                    LastLogin = user.LastLogin,
                    IpAddress = user.IP,
                    ClientId = user.ClientId,
                    RateLimit = user.RateLimit
                };
                
                var endPoint = user.IP?.Split(':').FirstOrDefault() ?? "Unknown";
                var port = int.TryParse(user.IP?.Split(':').LastOrDefault(), out int p) ? p : 0;
                
                var connectionInfo = new
                {
                    ServerAddress = _config.GetValue<string>("Server:Address", "localhost") + ":" + 
                                   _config.GetValue<int>("Server:Port", 25599),
                    ConnectedSince = user.LastLogin,
                    Ping = new Random().Next(10, 100)
                };
                
                var response = new
                {
                    LicenseInfo = licenseInfo,
                    ConnectionInfo = connectionInfo
                };
                
                string responseJson = System.Text.Json.JsonSerializer.Serialize(response);
                _licenseServer.SendMessageToClient(clientId, responseJson, "INFO_RESPONSE");
                
                _logger.Info($"Sent license information to client {clientId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing info command: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
                
                _licenseServer.SendMessageToClient(clientId, 
                    $"Error retrieving license information: {ex.Message}", "ERROR");
            }
        }
        
        private async Task HandleListCommand(string clientId, string commandArgs)
        {
            try
            {
                string category = commandArgs.Trim().ToLower();
                if (string.IsNullOrEmpty(category))
                {
                    category = "all";
                }
                
                _logger.Info($"Processing list command for client {clientId}, category: {category}");
                
                var gameListGenerator = new GameListGenerator(
                    _config,
                    _logger,
                    _gameInfoService);
                
                var gameList = await gameListGenerator.GenerateGameListAsync(category);
                if (gameList == null || gameList.Games.Count == 0)
                {
                    _logger.Warning($"No games found for category: {category}");
                    _licenseServer.SendMessageToClient(clientId, 
                        $"{{\"Games\": [], \"TotalCount\": 0, \"Category\": \"{category}\"}}", "LIST_RESPONSE");
                    return;
                }
                
                string gameListJson = System.Text.Json.JsonSerializer.Serialize(gameList);
                _licenseServer.SendMessageToClient(clientId, gameListJson, "LIST_RESPONSE");
                
                _logger.Info($"Sent list of {gameList.Games.Count} games to client {clientId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing list command: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
                
                _licenseServer.SendMessageToClient(clientId, 
                    $"Error listing games: {ex.Message}", "ERROR");
            }
        }
    }
}