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

    public class ClientMessageEventArgs(string clientId, string username, Message message) : EventArgs
    {
        public string ClientId { get; } = clientId;
        public string Username { get; } = username;
        public Message Message { get; } = message;
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public sealed class LicenseServer
    {
        private readonly LogLevel _minLogLevel;


        private TcpListener _server;
        private readonly int _port;
        private readonly string _usersFilePath;
        private List<User> _users;
        private readonly Dictionary<string, TcpClient> _connectedClients;
        private bool _isRunning;

        public event EventHandler<ClientEventArgs> ClientConnected;
        public event EventHandler<ClientEventArgs> ClientDisconnected;
        public event EventHandler<ClientMessageEventArgs> ClientMessageReceived;

        public LicenseServer(int port, string usersFilePath, LogLevel minLogLevel = LogLevel.Info)
        {
            _port = port;
            _usersFilePath = usersFilePath;
            _users = [];
            _connectedClients = new Dictionary<string, TcpClient>();
            _isRunning = false;
            _minLogLevel = minLogLevel;


            Console.WriteLine($"DIAGNOSTIC: LicenseServer created with LogLevel: {_minLogLevel}");


            LoadUsers();
            LogInfo(
                $"LicenseServer constructor completed. Port: {port}, UsersFile: {usersFilePath}, LogLevel: {_minLogLevel}");
        }


        public void Start()
        {
            if (_isRunning)
            {
                LogDebug("Server is already running, ignoring Start");
                return;
            }

            try
            {
                LogInfo($"Attempting to start server on port {_port}...");
                _server = new TcpListener(IPAddress.Any, _port);
                _server.Start();
                _isRunning = true;

                LogInfo($"Server successfully started on {IPAddress.Any}:{_port}");


                Task.Run(AcceptClients);
                LogDebug("AcceptClients task started");


                Task.Run(MaintenanceTask);
                LogDebug("MaintenanceTask started");
            }
            catch (Exception ex)
            {
                LogError($"Error starting server: {ex.Message}");
                LogDebug($"StackTrace: {ex.StackTrace}");
            }
        }


        public void Stop()
        {
            if (!_isRunning)
            {
                LogDebug("Server is not running, ignoring Stop");
                return;
            }

            LogInfo("Stopping server...");
            _isRunning = false;
            DisconnectAllClients("Server is shutting down.");
            _server.Stop();
            SaveUsers();

            LogInfo("Server successfully stopped");
        }


        private async Task AcceptClients()
        {
            LogDebug("AcceptClients loop started");
            while (_isRunning)
            {
                try
                {
                    LogDebug("Waiting for incoming connection...");
                    var client = await _server.AcceptTcpClientAsync();

                    var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                    LogInfo($"New connection from {endpoint?.Address}:{endpoint?.Port}");

                    await Task.Run(() => HandleClient(client));
                }
                catch (Exception ex)
                {
                    LogError($"Error accepting client: {ex.Message}");
                    LogDebug($"StackTrace: {ex.StackTrace}");


                    await Task.Delay(1000);
                }
            }

            LogDebug("AcceptClients loop ended");
        }


        private async Task HandleClient(TcpClient client)
        {
            var clientId = Guid.NewGuid().ToString();
            LogDebug($"HandleClient started for client ID: {clientId}");

            var stream = client.GetStream();
            var reader = new StreamReader(stream, new UTF8Encoding(false));
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            try
            {
                LogDebug($"Client {clientId}: Waiting for authentication message...");


                var readTask = reader.ReadLineAsync();
                var timeoutTask = Task.Delay(10000);

                await Task.WhenAny(readTask, timeoutTask);

                if (readTask.IsCompleted)
                {
                    var authMessageJson = await readTask;
                    LogDebug($"Client {clientId}: Message received: {authMessageJson}");

                    if (authMessageJson == null)
                    {
                        LogDebug($"Client {clientId}: No message received (null)");
                        client.Close();
                        return;
                    }

                    try
                    {
                        LogDebug($"Client {clientId}: Attempting to deserialize message");
                        var authMessage = JsonSerializer.Deserialize<Message>(authMessageJson);
                        LogDebug($"Client {clientId}: Message deserialized. Type: {authMessage.Type}");

                        if (authMessage.Type != "AUTH")
                        {
                            LogDebug($"Client {clientId}: First message is not an AUTH message");

                            SendMessage(writer, new Message
                            {
                                Type = "DISCONNECT",
                                Content = "Authentication required",
                                Sender = "Server",
                                Timestamp = DateTime.Now
                            });
                            client.Close();
                            return;
                        }


                        LogDebug($"Client {clientId}: Parsing authentication data. Content: {authMessage.Content}");
                        var authData = JsonSerializer.Deserialize<Dictionary<string, string>>(authMessage.Content);
                        var username = authData["username"];
                        var licenseKey = authData["licenseKey"];
                        LogDebug(
                            $"Client {clientId}: Authentication data - Username: {username}, LicenseKey: {licenseKey}");


                        LogDebug($"Client {clientId}: Checking license...");
                        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                        var authResult = AuthenticateUser(clientId, username, licenseKey, endpoint);
                        LogDebug($"Client {clientId}: Authentication result: {authResult}");

                        if (!authResult)
                        {
                            LogDebug($"Client {clientId}: Authentication failed");

                            SendMessage(writer, new Message
                            {
                                Type = "DISCONNECT",
                                Content = "Invalid license or username",
                                Sender = "Server",
                                Timestamp = DateTime.Now
                            });
                            client.Close();
                            return;
                        }


                        LogDebug($"Client {clientId}: Adding client to list");
                        lock (_connectedClients)
                        {
                            _connectedClients.Add(clientId, client);
                        }


                        LogDebug($"Client {clientId}: Triggering ClientConnected event");
                        OnClientConnected(clientId, username);


                        LogDebug($"Client {clientId}: Sending success notification");
                        SendMessage(writer, new Message
                        {
                            Type = "AUTH",
                            Content = "Authentication successful",
                            Sender = "Server",
                            Timestamp = DateTime.Now
                        });

                        await Task.Delay(500);


                        LogDebug($"Client {clientId}: Starting main loop for messages");
                        while (_isRunning && client.Connected)
                        {
                            LogDebug($"Client {clientId}: Waiting for next message...");
                            string messageJson = await reader.ReadLineAsync();

                            if (messageJson == null)
                            {
                                LogDebug($"Client {clientId}: Client has disconnected (null message)");
                                break;
                            }

                            LogDebug($"Client {clientId}: Message received: {messageJson}");


                            var message = JsonSerializer.Deserialize<Message>(messageJson);
                            LogDebug($"Client {clientId}: Processing message of type {message.Type}");
                            await ProcessMessage(message, clientId);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        LogError($"Client {clientId}: Error in JSON deserialization: {jsonEx.Message}");
                        LogDebug($"Details: {jsonEx.StackTrace}");
                    }
                }
                else
                {
                    LogDebug($"Client {clientId}: Timeout waiting for authentication message");
                    client.Close();
                    return;
                }
            }
            catch (IOException ioEx)
            {
                LogError($"Client {clientId}: IO error: {ioEx.Message}");
                LogDebug($"InnerException: {ioEx.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                LogError($"Client {clientId}: General error: {ex.Message}");
                LogDebug($"Type: {ex.GetType().Name}");
                LogDebug($"StackTrace: {ex.StackTrace}");
            }
            finally
            {
                LogDebug($"Client {clientId}: Cleaning up client resources");
                lock (_connectedClients)
                {
                    if (_connectedClients.ContainsKey(clientId))
                    {
                        var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                        if (user != null)
                        {
                            LogDebug($"Client {clientId}: Setting user {user.Username} to offline");
                            user.IsOnline = false;
                            user.ClientId = null;
                            SaveUsers();


                            OnClientDisconnected(clientId, user.Username);
                        }

                        _connectedClients.Remove(clientId);
                    }
                }

                try
                {
                    client.Close();
                    LogDebug($"Client {clientId}: Client connection closed");
                }
                catch (Exception ex)
                {
                    LogError($"Client {clientId}: Error closing client: {ex.Message}");
                }
            }
        }


        private async Task ProcessMessage(Message message, string clientId)
        {
            LogDebug($"ProcessMessage: Message received: {message.Type} from {clientId}");

            switch (message.Type)
            {
                case "COMMAND":
                    LogDebug($"ProcessMessage: Processing COMMAND from {clientId}: {message.Content}");

                    var user2 = _users.FirstOrDefault(u => u.ClientId == clientId);
                    if (user2 != null)
                    {
                        ClientMessageReceived?.Invoke(this,
                            new ClientMessageEventArgs(clientId, user2.Username, message));
                    }

                    break;

                case "DISCONNECT":
                    LogDebug($"ProcessMessage: Client {clientId} wants to disconnect");

                    lock (_connectedClients)
                    {
                        if (_connectedClients.TryGetValue(clientId, out TcpClient client))
                        {
                            LogDebug($"ProcessMessage: Disconnecting client {clientId}");
                            client.Close();
                            _connectedClients.Remove(clientId);


                            var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                            if (user != null)
                            {
                                LogDebug($"ProcessMessage: Setting user {user.Username} to offline");
                                user.IsOnline = false;
                                user.ClientId = null;
                                SaveUsers();


                                OnClientDisconnected(clientId, user.Username);
                            }
                        }
                    }

                    break;

                default:
                    LogDebug($"ProcessMessage: Unknown message type: {message.Type}");
                    break;
            }
        }


        private bool AuthenticateUser(string clientId, string username, string licenseKey, string ipAddress)
        {
            LogDebug($"AuthenticateUser: Authenticating user {username} with license {licenseKey}");
            lock (_users)
            {
                var user = _users.FirstOrDefault(u => u.LicenseKey == licenseKey);
                LogDebug($"AuthenticateUser: User found: {user != null}");


                if (user == null)
                {
                    LogDebug($"AuthenticateUser: New user, checking license key");

                    if (!IsValidLicenseKey(licenseKey))
                    {
                        LogDebug($"AuthenticateUser: Invalid license key: {licenseKey}");
                        return false;
                    }

                    LogDebug($"AuthenticateUser: Creating new user for {username}");
                    user = new User
                    {
                        Username = username,
                        IP = ipAddress,
                        FirstLogin = DateTime.Now,
                        LastLogin = DateTime.Now,
                        LicenseKey = licenseKey,
                        LicenseExpiration = DateTime.Now.AddDays(30),
                        RateLimit = 100,
                        IsOnline = true,
                        ClientId = clientId
                    };

                    _users.Add(user);
                    LogInfo($"AuthenticateUser: New user created and added: {username}");
                }
                else
                {
                    LogDebug($"AuthenticateUser: Existing user found, checking license");

                    if (user.LicenseExpiration < DateTime.Now)
                    {
                        LogDebug($"AuthenticateUser: License expired on {user.LicenseExpiration}");
                        return false;
                    }


                    if (user.IsOnline)
                    {
                        LogDebug($"AuthenticateUser: User is already online with ClientId {user.ClientId}");

                        if (!string.IsNullOrEmpty(user.ClientId) && user.ClientId != clientId)
                        {
                            LogDebug($"AuthenticateUser: Disconnecting old client {user.ClientId}");
                            DisconnectClient(user.ClientId,
                                "Another client has connected with your license.");
                        }
                    }


                    LogDebug($"AuthenticateUser: Updating user data for {username}");
                    user.Username = username;
                    user.IP = ipAddress;
                    user.LastLogin = DateTime.Now;
                    user.IsOnline = true;
                    user.ClientId = clientId;
                }


                LogDebug($"AuthenticateUser: Saving updated user data");
                SaveUsers();

                return true;
            }
        }


        private bool IsValidLicenseKey(string licenseKey)
        {
            LogDebug($"IsValidLicenseKey: Checking license key: {licenseKey}");


            if (string.IsNullOrEmpty(licenseKey) || licenseKey.Length != 20)
            {
                LogDebug($"IsValidLicenseKey: Invalid length: {licenseKey?.Length ?? 0}");
                return false;
            }


            if (!licenseKey.StartsWith("LICS-"))
            {
                LogDebug($"IsValidLicenseKey: Doesn't start with LICS-");
                return false;
            }

            LogDebug($"IsValidLicenseKey: License key is valid");
            return true;
        }


        private void SendMessage(StreamWriter writer, Message message)
        {
            LogDebug($"SendMessage: Sending message of type {message.Type}");
            try
            {
                var messageJson = JsonSerializer.Serialize(message);
                LogDebug($"SendMessage: Serialized message: {messageJson}");
                writer.WriteLine(messageJson);
                LogDebug($"SendMessage: Message sent");
            }
            catch (Exception ex)
            {
                LogError($"SendMessage: Error sending message: {ex.Message}");
            }
        }

        private void OnClientConnected(string clientId, string username)
        {
            LogDebug($"OnClientConnected: Event for {username} (ID: {clientId})");
            ClientConnected?.Invoke(this, new ClientEventArgs(clientId, username));
        }

        private void OnClientDisconnected(string clientId, string username)
        {
            LogDebug($"OnClientDisconnected: Event for {username} (ID: {clientId})");
            ClientDisconnected?.Invoke(this, new ClientEventArgs(clientId, username));
        }


        public void BroadcastMessage(string content, string messageType = "NOTIFICATION")
        {
            LogInfo($"BroadcastMessage: Sending broadcast message: {content}");
            var message = new Message
            {
                Type = messageType,
                Content = content,
                Sender = "Server",
                Timestamp = DateTime.Now
            };

            var messageJson = JsonSerializer.Serialize(message);
            LogDebug($"BroadcastMessage: Serialized message: {messageJson}");

            lock (_connectedClients)
            {
                LogDebug($"BroadcastMessage: Sending to {_connectedClients.Count} clients");
                foreach (var client in _connectedClients.Values)
                {
                    try
                    {
                        StreamWriter writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false))
                            { AutoFlush = true };
                        writer.WriteLine(messageJson);
                    }
                    catch (Exception ex)
                    {
                        LogError($"BroadcastMessage: Error sending: {ex.Message}");
                    }
                }
            }
        }


        public void SendMessageToClient(string clientId, string content, string messageType = "NOTIFICATION")
        {
            LogInfo($"SendMessageToClient: Sending message to client {clientId}: {content}");
            Message message = new Message
            {
                Type = messageType,
                Content = content,
                Sender = "Server",
                Timestamp = DateTime.Now
            };

            var messageJson = JsonSerializer.Serialize(message);
            LogDebug($"SendMessageToClient: Serialized message: {messageJson}");

            lock (_connectedClients)
            {
                if (!_connectedClients.TryGetValue(clientId, out TcpClient client))
                {
                    LogDebug($"SendMessageToClient: Client {clientId} not found");
                    return;
                }

                try
                {
                    var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
                    writer.WriteLine(messageJson);
                    LogDebug($"SendMessageToClient: Message sent");
                }
                catch (Exception ex)
                {
                    LogError($"SendMessageToClient: Error sending: {ex.Message}");
                }
            }
        }


        public void DisconnectClient(string clientId, string reason)
        {
            LogInfo($"DisconnectClient: Disconnecting client {clientId}. Reason: {reason}");
            lock (_connectedClients)
            {
                if (!_connectedClients.TryGetValue(clientId, out TcpClient client))
                {
                    LogDebug($"DisconnectClient: Client {clientId} not found");
                    return;
                }

                try
                {
                    LogDebug($"DisconnectClient: Sending disconnect message");
                    var writer = new StreamWriter(client.GetStream(), Encoding.UTF8) { AutoFlush = true };
                    SendMessage(writer, new Message
                    {
                        Type = "DISCONNECT",
                        Content = reason,
                        Sender = "Server",
                        Timestamp = DateTime.Now
                    });


                    LogDebug($"DisconnectClient: Closing connection");
                    client.Close();
                    _connectedClients.Remove(clientId);


                    var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                    if (user != null)
                    {
                        LogDebug($"DisconnectClient: Setting user {user.Username} to offline");
                        user.IsOnline = false;
                        string username = user.Username;
                        user.ClientId = null;
                        SaveUsers();


                        OnClientDisconnected(clientId, username);
                    }

                    LogDebug($"DisconnectClient: Client {clientId} successfully disconnected");
                }
                catch (Exception ex)
                {
                    LogError($"DisconnectClient: Error disconnecting client {clientId}: {ex.Message}");
                }
            }
        }


        public void DisconnectAllClients(string reason)
        {
            LogInfo($"DisconnectAllClients: Disconnecting all clients. Reason: {reason}");
            lock (_connectedClients)
            {
                LogDebug($"DisconnectAllClients: {_connectedClients.Count} clients to disconnect");
                foreach (var clientId in _connectedClients.Keys.ToList())
                {
                    DisconnectClient(clientId, reason);
                }
            }
        }


        private void LoadUsers()
        {
            LogDebug($"LoadUsers: Loading user data from {_usersFilePath}");
            try
            {
                if (!File.Exists(_usersFilePath))
                {
                    LogDebug($"LoadUsers: File doesn't exist, creating empty user list");
                    return;
                }

                var json = File.ReadAllText(_usersFilePath);
                LogDebug($"LoadUsers: JSON loaded, length: {json.Length} characters");
                _users = JsonSerializer.Deserialize<List<User>>(json) ??
                         throw new InvalidOperationException("Deserialization resulted in null");
                LogInfo($"LoadUsers: {_users.Count} users loaded");


                foreach (var user in _users)
                {
                    user.IsOnline = false;
                    user.ClientId = null;
                }

                LogDebug("LoadUsers: All users set to offline");
            }
            catch (Exception ex)
            {
                LogError($"LoadUsers: Error loading user data: {ex.Message}");
                LogDebug($"LoadUsers: {ex.StackTrace}");
                _users = [];
            }
        }


        private void SaveUsers()
        {
            LogDebug($"SaveUsers: Saving {_users.Count} users to {_usersFilePath}");
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(_users, options);
                LogDebug($"SaveUsers: JSON serialized, length: {json.Length} characters");
                File.WriteAllText(_usersFilePath, json);
                LogDebug("SaveUsers: User data successfully saved");
            }
            catch (Exception ex)
            {
                LogError($"SaveUsers: Error saving user data: {ex.Message}");
                LogDebug($"SaveUsers: {ex.StackTrace}");
            }
        }


        private async Task MaintenanceTask()
        {
            LogDebug("MaintenanceTask: Task started");
            while (_isRunning)
            {
                LogDebug("MaintenanceTask: Performing maintenance");

                CheckExpiredLicenses();


                LogDebug("MaintenanceTask: Waiting 1 hour until next check");
                await Task.Delay(TimeSpan.FromHours(1));
            }

            LogDebug("MaintenanceTask: Task ended");
        }


        private void CheckExpiredLicenses()
        {
            LogDebug("CheckExpiredLicenses: Checking for expired licenses");
            lock (_users)
            {
                var expiredUsers = _users.Where(u => u.IsOnline && u.LicenseExpiration < DateTime.Now).ToList();
                LogDebug($"CheckExpiredLicenses: {expiredUsers.Count} expired licenses found");

                foreach (var user in expiredUsers)
                {
                    LogDebug(
                        $"CheckExpiredLicenses: License for {user.Username} has expired. Expiration date: {user.LicenseExpiration}");
                    DisconnectClient(user.ClientId, "Your license has expired.");
                }
            }
        }


        public bool ExtendLicense(string licenseKey, int days)
        {
            LogInfo($"ExtendLicense: Extending license {licenseKey} by {days} days");
            lock (_users)
            {
                var user = _users.FirstOrDefault(u => u.LicenseKey == licenseKey);
                if (user == null)
                {
                    LogDebug($"ExtendLicense: User with license {licenseKey} not found");
                    return false;
                }

                var oldExpiration = user.LicenseExpiration;
                user.LicenseExpiration = user.LicenseExpiration.AddDays(days);
                LogDebug($"ExtendLicense: License extended from {oldExpiration} to {user.LicenseExpiration}");
                SaveUsers();


                if (user.IsOnline && !string.IsNullOrEmpty(user.ClientId))
                {
                    LogDebug($"ExtendLicense: Notifying user {user.Username} about license extension");
                    SendMessageToClient(user.ClientId,
                        $"Your license has been extended by {days} days. New expiration date: {user.LicenseExpiration}");
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


        private void LogDebug(string message)
        {
            if (_minLogLevel <= LogLevel.Debug)
            {
                Console.WriteLine($"[DEBUG] {message}");
            }
        }

        private void LogInfo(string message)
        {
            if (_minLogLevel <= LogLevel.Info)
            {
                Console.WriteLine($"[INFO] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (_minLogLevel <= LogLevel.Warning)
            {
                Console.WriteLine($"[WARNING] {message}");
            }
        }

        private void LogError(string message)
        {
            if (_minLogLevel <= LogLevel.Error)
            {
                Console.WriteLine($"[ERROR] {message}");
            }
        }
    }
}