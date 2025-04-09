using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LicenseSystem.Models;

namespace LicenseSystem.Services
{
    public class ClientEventArgs(string clientId, string username) : EventArgs
    {
        public string ClientId { get; } = clientId;
        public string Username { get; } = username;
    }

    public sealed class LicenseServer
    {
        private TcpListener _server;
        private readonly int _port;
        private readonly string _usersFilePath;
        private List<User> _users;
        private readonly Dictionary<string, TcpClient> _connectedClients;
        private bool _isRunning;

        public event EventHandler<ClientEventArgs> ClientConnected;
        public event EventHandler<ClientEventArgs> ClientDisconnected;

        public LicenseServer(int port, string usersFilePath)
        {
            _port = port;
            _usersFilePath = usersFilePath;
            _users = [];
            _connectedClients = new Dictionary<string, TcpClient>();
            _isRunning = false;

            // Lade Benutzerdaten aus der JSON-Datei
            LoadUsers();
            Console.WriteLine(
                $"[DEBUG] LicenseServer Konstruktor abgeschlossen. Port: {port}, UsersFile: {usersFilePath}");
        }

        // Starte den Server
        public void Start()
        {
            if (_isRunning)
            {
                Console.WriteLine("[DEBUG] Server läuft bereits, Start wird ignoriert");
                return;
            }

            try
            {
                Console.WriteLine($"[DEBUG] Versuche, Server auf Port {_port} zu starten...");
                _server = new TcpListener(IPAddress.Any, _port);
                _server.Start();
                _isRunning = true;

                Console.WriteLine($"[DEBUG] Server erfolgreich gestartet auf {IPAddress.Any}:{_port}");

                // Starte einen Thread, der auf eingehende Verbindungen wartet
                Task.Run(AcceptClients);
                Console.WriteLine("[DEBUG] AcceptClients Task gestartet");

                // Starte einen Thread für regelmäßige Überprüfungen
                Task.Run(MaintenanceTask);
                Console.WriteLine("[DEBUG] MaintenanceTask Task gestartet");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Fehler beim Starten des Servers: {ex.Message}");
                Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
            }
        }

        // Stoppe den Server
        public void Stop()
        {
            if (!_isRunning)
            {
                Console.WriteLine("[DEBUG] Server läuft nicht, Stop wird ignoriert");
                return;
            }

            Console.WriteLine("[DEBUG] Stoppe Server...");
            _isRunning = false;
            DisconnectAllClients("Server wird heruntergefahren.");
            _server.Stop();
            SaveUsers();

            Console.WriteLine("[DEBUG] Server erfolgreich gestoppt");
        }

        // Akzeptiere eingehende Client-Verbindungen
        private async Task AcceptClients()
        {
            Console.WriteLine("[DEBUG] AcceptClients-Schleife gestartet");
            while (_isRunning)
            {
                try
                {
                    Console.WriteLine("[DEBUG] Warte auf eingehende Verbindung...");
                    var client = await _server.AcceptTcpClientAsync();

                    var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                    Console.WriteLine($"[DEBUG] Neue Verbindung von {endpoint?.Address}:{endpoint?.Port}");

                    await Task.Run(() => HandleClient(client));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Fehler beim Akzeptieren eines Clients: {ex.Message}");
                    Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");

                    // Kurze Pause, um CPU-Auslastung zu reduzieren, falls wiederholt Fehler auftreten
                    await Task.Delay(1000);
                }
            }

            Console.WriteLine("[DEBUG] AcceptClients-Schleife beendet");
        }

        // Behandle einen verbundenen Client
        private async Task HandleClient(TcpClient client)
        {
            var clientId = Guid.NewGuid().ToString();
            Console.WriteLine($"[DEBUG] HandleClient gestartet für Client-ID: {clientId}");

            var stream = client.GetStream();
            var reader = new StreamReader(stream, new UTF8Encoding(false));
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            try
            {
                Console.WriteLine($"[DEBUG] Client {clientId}: Warte auf Authentifizierungsnachricht...");

                // Setze Timeout für Lesevorgänge
                var readTask = reader.ReadLineAsync();
                var timeoutTask = Task.Delay(10000); // 10 Sekunden Timeout

                await Task.WhenAny(readTask, timeoutTask);

                if (readTask.IsCompleted)
                {
                    var authMessageJson = await readTask;
                    Console.WriteLine($"[DEBUG] Client {clientId}: Nachricht empfangen: {authMessageJson}");

                    if (authMessageJson == null)
                    {
                        Console.WriteLine($"[DEBUG] Client {clientId}: Keine Nachricht empfangen (null)");
                        client.Close();
                        return;
                    }

                    try
                    {
                        Console.WriteLine($"[DEBUG] Client {clientId}: Versuche, Nachricht zu deserialisieren");
                        var authMessage = JsonSerializer.Deserialize<Message>(authMessageJson);
                        Console.WriteLine(
                            $"[DEBUG] Client {clientId}: Nachricht deserialisiert. Typ: {authMessage.Type}");

                        if (authMessage.Type != "AUTH")
                        {
                            Console.WriteLine($"[DEBUG] Client {clientId}: Erste Nachricht ist keine AUTH-Nachricht");
                            // Wenn die erste Nachricht keine Authentifizierung ist, trenne die Verbindung
                            SendMessage(writer, new Message
                            {
                                Type = "DISCONNECT",
                                Content = "Authentifizierung erforderlich",
                                Sender = "Server",
                                Timestamp = DateTime.Now
                            });
                            client.Close();
                            return;
                        }

                        // Authentifizierungsdaten parsen
                        Console.WriteLine(
                            $"[DEBUG] Client {clientId}: Parsen der Authentifizierungsdaten. Content: {authMessage.Content}");
                        var authData = JsonSerializer.Deserialize<Dictionary<string, string>>(authMessage.Content);
                        var username = authData["username"];
                        var licenseKey = authData["licenseKey"];
                        Console.WriteLine(
                            $"[DEBUG] Client {clientId}: Authentifizierungsdaten - Username: {username}, LicenseKey: {licenseKey}");

                        // Überprüfe Lizenz
                        Console.WriteLine($"[DEBUG] Client {clientId}: Überprüfe Lizenz...");
                        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unbekannt";
                        var authResult = AuthenticateUser(clientId, username, licenseKey, endpoint);
                        Console.WriteLine($"[DEBUG] Client {clientId}: Authentifizierungsergebnis: {authResult}");

                        if (!authResult)
                        {
                            Console.WriteLine($"[DEBUG] Client {clientId}: Authentifizierung fehlgeschlagen");
                            // Bei fehlgeschlagener Authentifizierung, trenne die Verbindung
                            SendMessage(writer, new Message
                            {
                                Type = "DISCONNECT",
                                Content = "Ungültige Lizenz oder Benutzername",
                                Sender = "Server",
                                Timestamp = DateTime.Now
                            });
                            client.Close();
                            return;
                        }

                        // Füge Client zur Liste hinzu
                        Console.WriteLine($"[DEBUG] Client {clientId}: Füge Client zur Liste hinzu");
                        lock (_connectedClients)
                        {
                            _connectedClients.Add(clientId, client);
                        }

                        // Löse Event aus
                        Console.WriteLine($"[DEBUG] Client {clientId}: Löse ClientConnected-Event aus");
                        OnClientConnected(clientId, username);

                        // Sende Erfolgsbenachrichtigung
                        Console.WriteLine($"[DEBUG] Client {clientId}: Sende Erfolgsbenachrichtigung");
                        SendMessage(writer, new Message
                        {
                            Type = "AUTH",
                            Content = "Authentifizierung erfolgreich",
                            Sender = "Server",
                            Timestamp = DateTime.Now
                        });

                        // Hauptschleife für den Client
                        Console.WriteLine($"[DEBUG] Client {clientId}: Starte Hauptschleife für Nachrichten");
                        while (_isRunning && client.Connected)
                        {
                            Console.WriteLine($"[DEBUG] Client {clientId}: Warte auf nächste Nachricht...");
                            string messageJson = await reader.ReadLineAsync();

                            if (messageJson == null)
                            {
                                Console.WriteLine(
                                    $"[DEBUG] Client {clientId}: Client hat die Verbindung getrennt (null message)");
                                break; // Client hat die Verbindung getrennt
                            }

                            Console.WriteLine($"[DEBUG] Client {clientId}: Nachricht empfangen: {messageJson}");

                            // Verarbeite Nachricht
                            var message = JsonSerializer.Deserialize<Message>(messageJson);
                            Console.WriteLine(
                                $"[DEBUG] Client {clientId}: Verarbeite Nachricht vom Typ {message.Type}");
                            await ProcessMessage(message, clientId);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine(
                            $"[ERROR] Client {clientId}: Fehler bei der JSON-Deserialisierung: {jsonEx.Message}");
                        Console.WriteLine($"[ERROR] Details: {jsonEx.StackTrace}");
                    }
                }
                else
                {
                    Console.WriteLine(
                        $"[DEBUG] Client {clientId}: Timeout beim Warten auf Authentifizierungsnachricht");
                    client.Close();
                    return;
                }
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"[ERROR] Client {clientId}: IO-Fehler: {ioEx.Message}");
                Console.WriteLine($"[ERROR] InnerException: {ioEx.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Client {clientId}: Allgemeiner Fehler: {ex.Message}");
                Console.WriteLine($"[ERROR] Typ: {ex.GetType().Name}");
                Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
            }
            finally
            {
                // Bereinige, wenn der Client die Verbindung trennt
                Console.WriteLine($"[DEBUG] Client {clientId}: Bereinige Client-Ressourcen");
                lock (_connectedClients)
                {
                    if (_connectedClients.ContainsKey(clientId))
                    {
                        // Setze den Benutzer auf offline
                        var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                        if (user != null)
                        {
                            Console.WriteLine($"[DEBUG] Client {clientId}: Setze Benutzer {user.Username} auf offline");
                            user.IsOnline = false;
                            user.ClientId = null;
                            SaveUsers();

                            // Löse Event aus
                            OnClientDisconnected(clientId, user.Username);
                        }

                        _connectedClients.Remove(clientId);
                    }
                }

                try
                {
                    client.Close();
                    Console.WriteLine($"[DEBUG] Client {clientId}: Client-Verbindung geschlossen");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Client {clientId}: Fehler beim Schließen des Clients: {ex.Message}");
                }
            }
        }

        // Verarbeite eine Nachricht vom Client
        private async Task ProcessMessage(Message message, string clientId)
        {
            Console.WriteLine($"[DEBUG] ProcessMessage: Nachricht erhalten: {message.Type} von {clientId}");

            switch (message.Type)
            {
                case "COMMAND":
                    Console.WriteLine($"[DEBUG] ProcessMessage: Verarbeite COMMAND von {clientId}: {message.Content}");
                    // Verarbeite Client-Befehle
                    // Hier könntest du erweiterte Funktionen implementieren
                    break;

                case "DISCONNECT":
                    Console.WriteLine($"[DEBUG] ProcessMessage: Client {clientId} möchte sich selbst trennen");
                    // Client möchte sich selbst trennen
                    lock (_connectedClients)
                    {
                        if (_connectedClients.TryGetValue(clientId, out TcpClient client))
                        {
                            Console.WriteLine($"[DEBUG] ProcessMessage: Trenne Client {clientId}");
                            client.Close();
                            _connectedClients.Remove(clientId);

                            // Setze den Benutzer auf offline
                            var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                            if (user != null)
                            {
                                Console.WriteLine(
                                    $"[DEBUG] ProcessMessage: Setze Benutzer {user.Username} auf offline");
                                user.IsOnline = false;
                                user.ClientId = null;
                                SaveUsers();

                                // Löse Event aus
                                OnClientDisconnected(clientId, user.Username);
                            }
                        }
                    }

                    break;

                default:
                    Console.WriteLine($"[DEBUG] ProcessMessage: Unbekannter Nachrichtentyp: {message.Type}");
                    break;
            }
        }

        // Authentifiziere einen Benutzer
        private bool AuthenticateUser(string clientId, string username, string licenseKey, string ipAddress)
        {
            Console.WriteLine($"[DEBUG] AuthenticateUser: Authenticating user {username} with license {licenseKey}");
            lock (_users)
            {
                // Suche nach Benutzer mit dem angegebenen Lizenzschlüssel
                var user = _users.FirstOrDefault(u => u.LicenseKey == licenseKey);
                Console.WriteLine($"[DEBUG] AuthenticateUser: User found: {user != null}");

                // Wenn Benutzer nicht existiert, erstelle einen neuen
                if (user == null)
                {
                    Console.WriteLine($"[DEBUG] AuthenticateUser: Neuer Benutzer, prüfe Lizenzschlüssel");
                    // Überprüfe, ob der Lizenzschlüssel gültig ist
                    if (!IsValidLicenseKey(licenseKey))
                    {
                        Console.WriteLine($"[DEBUG] AuthenticateUser: Ungültiger Lizenzschlüssel: {licenseKey}");
                        return false;
                    }

                    Console.WriteLine($"[DEBUG] AuthenticateUser: Erstelle neuen Benutzer für {username}");
                    user = new User
                    {
                        Username = username,
                        IP = ipAddress,
                        FirstLogin = DateTime.Now,
                        LastLogin = DateTime.Now,
                        LicenseKey = licenseKey,
                        LicenseExpiration = DateTime.Now.AddDays(30), // Standard: 30 Tage Lizenz
                        RateLimit = 100, // Standard Rate-Limit
                        IsOnline = true,
                        ClientId = clientId
                    };

                    _users.Add(user);
                    Console.WriteLine($"[DEBUG] AuthenticateUser: Neuer Benutzer erstellt und hinzugefügt");
                }
                else
                {
                    Console.WriteLine($"[DEBUG] AuthenticateUser: Bestehender Benutzer gefunden, prüfe Lizenz");
                    // Überprüfe, ob die Lizenz abgelaufen ist
                    if (user.LicenseExpiration < DateTime.Now)
                    {
                        Console.WriteLine($"[DEBUG] AuthenticateUser: Lizenz abgelaufen am {user.LicenseExpiration}");
                        return false;
                    }

                    // Überprüfe, ob der Benutzer bereits online ist
                    if (user.IsOnline)
                    {
                        Console.WriteLine(
                            $"[DEBUG] AuthenticateUser: Benutzer ist bereits online mit ClientId {user.ClientId}");
                        // Trenne den alten Client, wenn vorhanden
                        if (!string.IsNullOrEmpty(user.ClientId) && user.ClientId != clientId)
                        {
                            Console.WriteLine($"[DEBUG] AuthenticateUser: Trenne alten Client {user.ClientId}");
                            DisconnectClient(user.ClientId,
                                "Ein anderer Client hat sich mit deiner Lizenz angemeldet.");
                        }
                    }

                    // Aktualisiere Benutzerdaten
                    Console.WriteLine($"[DEBUG] AuthenticateUser: Aktualisiere Benutzerdaten für {username}");
                    user.Username = username; // Erlaube Aktualisierung des Benutzernamens
                    user.IP = ipAddress;
                    user.LastLogin = DateTime.Now;
                    user.IsOnline = true;
                    user.ClientId = clientId;
                }

                // Speichere aktualisierte Benutzerdaten
                Console.WriteLine($"[DEBUG] AuthenticateUser: Speichere aktualisierte Benutzerdaten");
                SaveUsers();

                return true;
            }
        }

        // Überprüfe, ob ein Lizenzschlüssel gültig ist
        private bool IsValidLicenseKey(string licenseKey)
        {
            Console.WriteLine($"[DEBUG] IsValidLicenseKey: Prüfe Lizenzschlüssel: {licenseKey}");
            // Hier kannst du deine eigene Logik zur Überprüfung von Lizenzschlüsseln implementieren
            // Beispiel: Prüfe auf eine bestimmte Länge und ein Format
            if (string.IsNullOrEmpty(licenseKey) || licenseKey.Length != 20)
            {
                Console.WriteLine($"[DEBUG] IsValidLicenseKey: Ungültige Länge: {licenseKey?.Length ?? 0}");
                return false;
            }

            // Beispiel: Lizenzschlüssel muss mit "LICS-" beginnen
            if (!licenseKey.StartsWith("LICS-"))
            {
                Console.WriteLine($"[DEBUG] IsValidLicenseKey: Beginnt nicht mit LICS-");
                return false;
            }

            Console.WriteLine($"[DEBUG] IsValidLicenseKey: Lizenzschlüssel ist gültig");
            return true;
        }

        // Sende eine Nachricht an einen Client
        private void SendMessage(StreamWriter writer, Message message)
        {
            Console.WriteLine($"[DEBUG] SendMessage: Sende Nachricht vom Typ {message.Type}");
            try
            {
                var messageJson = JsonSerializer.Serialize(message);
                Console.WriteLine($"[DEBUG] SendMessage: Serialisierte Nachricht: {messageJson}");
                writer.WriteLine(messageJson);
                Console.WriteLine($"[DEBUG] SendMessage: Nachricht gesendet");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] SendMessage: Fehler beim Senden der Nachricht: {ex.Message}");
            }
        }

        private void OnClientConnected(string clientId, string username)
        {
            Console.WriteLine($"[DEBUG] OnClientConnected: Event für {username} (ID: {clientId})");
            ClientConnected?.Invoke(this, new ClientEventArgs(clientId, username));
        }

        private void OnClientDisconnected(string clientId, string username)
        {
            Console.WriteLine($"[DEBUG] OnClientDisconnected: Event für {username} (ID: {clientId})");
            ClientDisconnected?.Invoke(this, new ClientEventArgs(clientId, username));
        }

        // Sende eine Nachricht an alle verbundenen Clients
        public void BroadcastMessage(string content, string messageType = "NOTIFICATION")
        {
            Console.WriteLine($"[DEBUG] BroadcastMessage: Sende Broadcast-Nachricht: {content}");
            var message = new Message
            {
                Type = messageType,
                Content = content,
                Sender = "Server",
                Timestamp = DateTime.Now
            };

            var messageJson = JsonSerializer.Serialize(message);
            Console.WriteLine($"[DEBUG] BroadcastMessage: Serialisierte Nachricht: {messageJson}");

            lock (_connectedClients)
            {
                Console.WriteLine($"[DEBUG] BroadcastMessage: Sende an {_connectedClients.Count} Clients");
                foreach (var client in _connectedClients.Values)
                {
                    try
                    {
                        StreamWriter writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
                        writer.WriteLine(messageJson);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] BroadcastMessage: Fehler beim Senden: {ex.Message}");
                    }
                }
            }
        }

        // Sende eine Nachricht an einen bestimmten Client
        public void SendMessageToClient(string clientId, string content, string messageType = "NOTIFICATION")
        {
            Console.WriteLine($"[DEBUG] SendMessageToClient: Sende Nachricht an Client {clientId}: {content}");
            Message message = new Message
            {
                Type = messageType,
                Content = content,
                Sender = "Server",
                Timestamp = DateTime.Now
            };

            var messageJson = JsonSerializer.Serialize(message);
            Console.WriteLine($"[DEBUG] SendMessageToClient: Serialisierte Nachricht: {messageJson}");

            lock (_connectedClients)
            {
                if (!_connectedClients.TryGetValue(clientId, out TcpClient client))
                {
                    Console.WriteLine($"[DEBUG] SendMessageToClient: Client {clientId} nicht gefunden");
                    return;
                }

                try
                {
                    var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
                    writer.WriteLine(messageJson);
                    Console.WriteLine($"[DEBUG] SendMessageToClient: Nachricht gesendet");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] SendMessageToClient: Fehler beim Senden: {ex.Message}");
                }
            }
        }

        // Trenne die Verbindung zu einem Client
        public void DisconnectClient(string clientId, string reason)
        {
            Console.WriteLine($"[DEBUG] DisconnectClient: Trenne Client {clientId}. Grund: {reason}");
            lock (_connectedClients)
            {
                if (!_connectedClients.TryGetValue(clientId, out TcpClient client))
                {
                    Console.WriteLine($"[DEBUG] DisconnectClient: Client {clientId} nicht gefunden");
                    return;
                }

                try
                {
                    // Sende Trennungsnachricht
                    Console.WriteLine($"[DEBUG] DisconnectClient: Sende Trennungsnachricht");
                    var writer = new StreamWriter(client.GetStream(), Encoding.UTF8) { AutoFlush = true };
                    SendMessage(writer, new Message
                    {
                        Type = "DISCONNECT",
                        Content = reason,
                        Sender = "Server",
                        Timestamp = DateTime.Now
                    });

                    // Schließe die Verbindung
                    Console.WriteLine($"[DEBUG] DisconnectClient: Schließe Verbindung");
                    client.Close();
                    _connectedClients.Remove(clientId);

                    // Aktualisiere Benutzerstatus
                    var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                    if (user != null)
                    {
                        Console.WriteLine($"[DEBUG] DisconnectClient: Setze Benutzer {user.Username} auf offline");
                        user.IsOnline = false;
                        string username = user.Username;
                        user.ClientId = null;
                        SaveUsers();

                        // Löse Event aus
                        OnClientDisconnected(clientId, username);
                    }

                    Console.WriteLine($"[DEBUG] DisconnectClient: Client {clientId} erfolgreich getrennt");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[ERROR] DisconnectClient: Fehler beim Trennen des Clients {clientId}: {ex.Message}");
                }
            }
        }

        // Trenne alle verbundenen Clients
        public void DisconnectAllClients(string reason)
        {
            Console.WriteLine($"[DEBUG] DisconnectAllClients: Trenne alle Clients. Grund: {reason}");
            lock (_connectedClients)
            {
                Console.WriteLine($"[DEBUG] DisconnectAllClients: {_connectedClients.Count} Clients zu trennen");
                foreach (var clientId in _connectedClients.Keys.ToList())
                {
                    DisconnectClient(clientId, reason);
                }
            }
        }

        // Lade Benutzerdaten aus der JSON-Datei
        private void LoadUsers()
        {
            Console.WriteLine($"[DEBUG] LoadUsers: Lade Benutzerdaten aus {_usersFilePath}");
            try
            {
                if (!File.Exists(_usersFilePath))
                {
                    Console.WriteLine($"[DEBUG] LoadUsers: Datei existiert nicht, erstelle leere Benutzerliste");
                    return;
                }

                var json = File.ReadAllText(_usersFilePath);
                Console.WriteLine($"[DEBUG] LoadUsers: JSON geladen, Länge: {json.Length} Zeichen");
                _users = JsonSerializer.Deserialize<List<User>>(json) ??
                         throw new InvalidOperationException("Deserialisierung ergab null");
                Console.WriteLine($"[DEBUG] LoadUsers: {_users.Count} Benutzer geladen");

                // Setze alle Benutzer auf offline beim Laden
                foreach (var user in _users)
                {
                    user.IsOnline = false;
                    user.ClientId = null;
                }

                Console.WriteLine("[DEBUG] LoadUsers: Alle Benutzer auf offline gesetzt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] LoadUsers: Fehler beim Laden der Benutzerdaten: {ex.Message}");
                Console.WriteLine($"[ERROR] LoadUsers: {ex.StackTrace}");
                _users = [];
            }
        }

        // Speichere Benutzerdaten in die JSON-Datei
        private void SaveUsers()
        {
            Console.WriteLine($"[DEBUG] SaveUsers: Speichere {_users.Count} Benutzer in {_usersFilePath}");
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(_users, options);
                Console.WriteLine($"[DEBUG] SaveUsers: JSON serialisiert, Länge: {json.Length} Zeichen");
                File.WriteAllText(_usersFilePath, json);
                Console.WriteLine("[DEBUG] SaveUsers: Benutzerdaten erfolgreich gespeichert");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] SaveUsers: Fehler beim Speichern der Benutzerdaten: {ex.Message}");
                Console.WriteLine($"[ERROR] SaveUsers: {ex.StackTrace}");
            }
        }

        // Regelmäßige Wartungsaufgaben
        private async Task MaintenanceTask()
        {
            Console.WriteLine("[DEBUG] MaintenanceTask: Task gestartet");
            while (_isRunning)
            {
                Console.WriteLine("[DEBUG] MaintenanceTask: Führe Wartungsarbeiten durch");
                // Überprüfe abgelaufene Lizenzen
                CheckExpiredLicenses();

                // Warte 1 Stunde bis zur nächsten Überprüfung
                Console.WriteLine("[DEBUG] MaintenanceTask: Warte 1 Stunde bis zur nächsten Überprüfung");
                await Task.Delay(TimeSpan.FromHours(1));
            }

            Console.WriteLine("[DEBUG] MaintenanceTask: Task beendet");
        }

        // Überprüfe auf abgelaufene Lizenzen
        private void CheckExpiredLicenses()
        {
            Console.WriteLine("[DEBUG] CheckExpiredLicenses: Prüfe auf abgelaufene Lizenzen");
            lock (_users)
            {
                var expiredUsers = _users.Where(u => u.IsOnline && u.LicenseExpiration < DateTime.Now).ToList();
                Console.WriteLine($"[DEBUG] CheckExpiredLicenses: {expiredUsers.Count} abgelaufene Lizenzen gefunden");

                foreach (var user in expiredUsers)
                {
                    Console.WriteLine(
                        $"[DEBUG] CheckExpiredLicenses: Lizenz für {user.Username} ist abgelaufen. Ablaufdatum: {user.LicenseExpiration}");
                    DisconnectClient(user.ClientId, "Deine Lizenz ist abgelaufen.");
                }
            }
        }

        // Methode zum Verlängern einer Lizenz
        public bool ExtendLicense(string licenseKey, int days)
        {
            Console.WriteLine($"[DEBUG] ExtendLicense: Verlängere Lizenz {licenseKey} um {days} Tage");
            lock (_users)
            {
                var user = _users.FirstOrDefault(u => u.LicenseKey == licenseKey);
                if (user == null)
                {
                    Console.WriteLine($"[DEBUG] ExtendLicense: Benutzer mit Lizenz {licenseKey} nicht gefunden");
                    return false;
                }

                var oldExpiration = user.LicenseExpiration;
                user.LicenseExpiration = user.LicenseExpiration.AddDays(days);
                Console.WriteLine(
                    $"[DEBUG] ExtendLicense: Lizenz verlängert von {oldExpiration} auf {user.LicenseExpiration}");
                SaveUsers();

                // Benachrichtige den Benutzer, wenn er online ist
                if (user.IsOnline && !string.IsNullOrEmpty(user.ClientId))
                {
                    Console.WriteLine(
                        $"[DEBUG] ExtendLicense: Benachrichtige Benutzer {user.Username} über Lizenzverlängerung");
                    SendMessageToClient(user.ClientId,
                        $"Deine Lizenz wurde um {days} Tage verlängert. Neues Ablaufdatum: {user.LicenseExpiration}");
                }

                return true;
            }
        }
        
        public List<User> GetOnlineUsers()
        {
            lock (_users)
            {
                return _users.Where(u => u.IsOnline).ToList();
            }
        }
        
        public List<User> GetAllUsers()
        {
            lock (_users)
            {
                return _users.ToList();
            }
        }
    }
}