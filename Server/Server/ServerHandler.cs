using LicenseSystem.Services;
using Microsoft.Extensions.Configuration;
using Server.Utils;

namespace Server.Server
{
    public class ServerHandler
    {
        private readonly LicenseServer _licenseServer;
        private readonly IConfiguration _config;
        private readonly Logger _logger;

        public ServerHandler(
            LicenseServer licenseServer,
            IConfiguration config,
            Logger logger)
        {
            _licenseServer = licenseServer;
            _config = config;
            _logger = logger;

            _licenseServer.ClientConnected += OnClientConnected;
            _licenseServer.ClientDisconnected += OnClientDisconnected;
            
            _logger.Debug("ServerHandler initialized");
        }
        
        private void OnClientConnected(object sender, ClientEventArgs args)
        {
            _logger.Info($"Client connected: {args.Username} (ID: {args.ClientId})");
            
            try
            {
                // Welcome message or other initialization could be added here
                _logger.Debug($"Client {args.Username} connected successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error sending welcome message: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private void OnClientDisconnected(object sender, ClientEventArgs args)
        {
            _logger.Info($"Client disconnected: {args.Username} (ID: {args.ClientId})");
        }
        
        public void BroadcastMessage(string message, string source = "Server")
        {
            try
            {
                _logger.Debug($"Broadcasting message from {source}: {message}");
                _licenseServer.BroadcastMessage(message);
                _logger.Info($"Broadcast message sent: {message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error sending broadcast message: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
            }
        }
        
        public void SendMessageToClient(string clientId, string message)
        {
            try
            {
                _logger.Debug($"Sending message to client {clientId}: {message}");
                _licenseServer.SendMessageToClient(clientId, message);
                _logger.Info($"Message sent to client {clientId}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error sending message to client {clientId}: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
            }
        }
        
        public void DisconnectClient(string clientId, string reason = "Disconnected by server")
        {
            try
            {
                _logger.Debug($"Disconnecting client {clientId}. Reason: {reason}");
                _licenseServer.DisconnectClient(clientId, reason);
                _logger.Info($"Client {clientId} disconnected. Reason: {reason}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error disconnecting client {clientId}: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
            }
        }
        
        public void DisconnectAllClients(string reason = "Disconnected by server")
        {
            try
            {
                _logger.Debug($"Disconnecting all clients. Reason: {reason}");
                _licenseServer.DisconnectAllClients(reason);
                _logger.Info($"All clients disconnected. Reason: {reason}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error disconnecting all clients: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
            }
        }
        
        public bool ExtendLicense(string licenseKey, int days)
        {
            try
            {
                _logger.Debug($"Extending license {licenseKey} by {days} days");
                var result = _licenseServer.ExtendLicense(licenseKey, days);
                if (result)
                {
                    _logger.Info($"License {licenseKey} extended by {days} days.");
                }
                else
                {
                    _logger.Warning($"License {licenseKey} could not be extended.");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error extending license {licenseKey}: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }
    }
}