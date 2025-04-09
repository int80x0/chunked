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
        }
        
        private void OnClientConnected(object sender, ClientEventArgs args)
        {
            _logger.Info($"Client verbunden: {args.Username} (ID: {args.ClientId})");
            
            try
            {
                
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Senden der Begrüßungsnachricht: {ex.Message}");
            }
        }
        
        private void OnClientDisconnected(object sender, ClientEventArgs args)
        {
            _logger.Info($"Client getrennt: {args.Username} (ID: {args.ClientId})");
        }
        
        public void BroadcastMessage(string message, string source = "Server")
        {
            try
            {
                _licenseServer.BroadcastMessage(message);
                _logger.Info($"Broadcast-Nachricht gesendet: {message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Senden einer Broadcast-Nachricht: {ex.Message}");
            }
        }
        
        public void SendMessageToClient(string clientId, string message)
        {
            try
            {
                _licenseServer.SendMessageToClient(clientId, message);
                _logger.Info($"Nachricht an Client {clientId} gesendet: {message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Senden einer Nachricht an Client {clientId}: {ex.Message}");
            }
        }
        
        public void DisconnectClient(string clientId, string reason = "Vom Server getrennt")
        {
            try
            {
                _licenseServer.DisconnectClient(clientId, reason);
                _logger.Info($"Client {clientId} getrennt. Grund: {reason}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Trennen des Clients {clientId}: {ex.Message}");
            }
        }
        
        public void DisconnectAllClients(string reason = "Vom Server getrennt")
        {
            try
            {
                _licenseServer.DisconnectAllClients(reason);
                _logger.Info($"Alle Clients getrennt. Grund: {reason}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Trennen aller Clients: {ex.Message}");
            }
        }
        
        public bool ExtendLicense(string licenseKey, int days)
        {
            try
            {
                var result = _licenseServer.ExtendLicense(licenseKey, days);
                if (result)
                {
                    _logger.Info($"Lizenz {licenseKey} um {days} Tage verlängert.");
                }
                else
                {
                    _logger.Warning($"Lizenz {licenseKey} konnte nicht verlängert werden.");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Verlängern der Lizenz {licenseKey}: {ex.Message}");
                return false;
            }
        }
    }
}