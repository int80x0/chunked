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
            
            // Subscribe to message handling in LicenseServer
            _licenseServer.ClientMessageReceived += OnClientMessageReceived;
            
            _logger.Info("MessageHandler initialized and subscribed to client messages");
        }

        private async void OnClientMessageReceived(object sender, ClientMessageEventArgs args)
        {
            try
            {
                // Only handle COMMAND type messages
                if (args.Message.Type != "COMMAND")
                    return;

                string command = args.Message.Content.Trim();
                _logger.Info($"Received command from client {args.ClientId}: {command}");

                // Parse the command
                string[] parts = command.Split(' ', 2);
                string commandName = parts[0].ToLower();
                string commandArgs = parts.Length > 1 ? parts[1] : string.Empty;

                // Handle different command types
                switch (commandName)
                {
                    case "download":
                        await HandleDownloadCommand(args.ClientId, commandArgs);
                        break;
                    // Add other command handlers as needed
                    default:
                        _logger.Warning($"Unknown command from client {args.ClientId}: {commandName}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling client message: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task HandleDownloadCommand(string clientId, string commandArgs)
        {
            try
            {
                _logger.Info($"Processing download command for client {clientId}: {commandArgs}");
                
                // Parse game ID
                string gameIdStr = commandArgs.Trim();
                if (string.IsNullOrEmpty(gameIdStr))
                {
                    _logger.Warning($"Client {clientId} requested download without providing a game ID");
                    _licenseServer.SendMessageToClient(clientId, "Error: No game ID provided", "ERROR");
                    return;
                }

                // Get game info
                var gameInfo = await _gameInfoService.GetGameInfoAsync(gameIdStr);
                if (gameInfo == null)
                {
                    _logger.Warning($"Game not found for ID: {gameIdStr}");
                    _licenseServer.SendMessageToClient(clientId, $"Error: Game with ID '{gameIdStr}' not found", "ERROR");
                    return;
                }

                _logger.Info($"Found game: {gameInfo.Title} (ID: {gameInfo.Id})");

                // This would use the DownloadCommand functionality, but adapted for direct client communication
                // instead of console output
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

                // Convert to JSON and send to client
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
    }
}