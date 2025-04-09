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

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public sealed class LicenseServer
    {
        // Logging-related properties
        private readonly LogLevel _minLogLevel;
        
        // Original properties
        private TcpListener _server;
        private readonly int _port;
        private readonly string _usersFilePath;
        private List<User> _users;
        private readonly Dictionary<string, TcpClient> _connectedClients;
        private bool _isRunning;

        public event EventHandler<ClientEventArgs> ClientConnected;
        public event EventHandler<ClientEventArgs> ClientDisconnected;

        public LicenseServer(int port, string usersFilePath, LogLevel minLogLevel = LogLevel.Info)
        {
            _port = port;
            _usersFilePath = usersFilePath;
            _users = [];
            _connectedClients = new Dictionary<string, TcpClient>();
            _isRunning = false;
            _minLogLevel = minLogLevel;
            
            // When creating an instance, output diagnostic information directly
            Console.WriteLine($"DIAGNOSTIC: LicenseServer created with LogLevel: {_minLogLevel}");

            // Load user data from JSON file
            LoadUsers();
            LogInfo($"LicenseServer constructor completed. Port: {port}, UsersFile: {usersFilePath}, LogLevel: {_minLogLevel}");
        }

        // Start the server
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

                // Start a thread to wait for incoming connections
                Task.Run(AcceptClients);
                LogDebug("AcceptClients task started");

                // Start a thread for regular checks
                Task.Run(MaintenanceTask);
                LogDebug("MaintenanceTask started");
            }
            catch (Exception ex)
            {
                LogError($"Error starting server: {ex.Message}");
                LogDebug($"StackTrace: {ex.StackTrace}");
            }
        }

        // Stop the server
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

        // Accept incoming client connections
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

                    // Short pause to reduce CPU usage if errors occur repeatedly
                    await Task.Delay(1000);
                }
            }

            LogDebug("AcceptClients loop ended");
        }

        // Handle a connected client
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

                // Set timeout for read operations
                var readTask = reader.ReadLineAsync();
                var timeoutTask = Task.Delay(10000); // 10 seconds timeout

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
                            // If the first message is not an authentication, disconnect
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

                        // Parse authentication data
                        LogDebug($"Client {clientId}: Parsing authentication data. Content: {authMessage.Content}");
                        var authData = JsonSerializer.Deserialize<Dictionary<string, string>>(authMessage.Content);
                        var username = authData["username"];
                        var licenseKey = authData["licenseKey"];
                        LogDebug($"Client {clientId}: Authentication data - Username: {username}, LicenseKey: {licenseKey}");

                        // Check license
                        LogDebug($"Client {clientId}: Checking license...");
                        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                        var authResult = AuthenticateUser(clientId, username, licenseKey, endpoint);
                        LogDebug($"Client {clientId}: Authentication result: {authResult}");

                        if (!authResult)
                        {
                            LogDebug($"Client {clientId}: Authentication failed");
                            // If authentication fails, disconnect
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

                        // Add client to the list
                        LogDebug($"Client {clientId}: Adding client to list");
                        lock (_connectedClients)
                        {
                            _connectedClients.Add(clientId, client);
                        }

                        // Trigger event
                        LogDebug($"Client {clientId}: Triggering ClientConnected event");
                        OnClientConnected(clientId, username);

                        // Send success notification
                        LogDebug($"Client {clientId}: Sending success notification");
                        SendMessage(writer, new Message
                        {
                            Type = "AUTH",
                            Content = "Authentication successful",
                            Sender = "Server",
                            Timestamp = DateTime.Now
                        });

                        // Main loop for the client
                        LogDebug($"Client {clientId}: Starting main loop for messages");
                        while (_isRunning && client.Connected)
                        {
                            LogDebug($"Client {clientId}: Waiting for next message...");
                            string messageJson = await reader.ReadLineAsync();

                            if (messageJson == null)
                            {
                                LogDebug($"Client {clientId}: Client has disconnected (null message)");
                                break; // Client has disconnected
                            }

                            LogDebug($"Client {clientId}: Message received: {messageJson}");

                            // Process message
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
                // Clean up when the client disconnects
                LogDebug($"Client {clientId}: Cleaning up client resources");
                lock (_connectedClients)
                {
                    if (_connectedClients.ContainsKey(clientId))
                    {
                        // Set the user to offline
                        var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                        if (user != null)
                        {
                            LogDebug($"Client {clientId}: Setting user {user.Username} to offline");
                            user.IsOnline = false;
                            user.ClientId = null;
                            SaveUsers();

                            // Trigger event
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

        // Process a message from the client
        private async Task ProcessMessage(Message message, string clientId)
        {
            LogDebug($"ProcessMessage: Message received: {message.Type} from {clientId}");

            switch (message.Type)
            {
                case "COMMAND":
                    LogDebug($"ProcessMessage: Processing COMMAND from {clientId}: {message.Content}");
                    // Process client commands
                    // Here you could implement advanced functions
                    break;

                case "DISCONNECT":
                    LogDebug($"ProcessMessage: Client {clientId} wants to disconnect");
                    // Client wants to disconnect
                    lock (_connectedClients)
                    {
                        if (_connectedClients.TryGetValue(clientId, out TcpClient client))
                        {
                            LogDebug($"ProcessMessage: Disconnecting client {clientId}");
                            client.Close();
                            _connectedClients.Remove(clientId);

                            // Set the user to offline
                            var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                            if (user != null)
                            {
                                LogDebug($"ProcessMessage: Setting user {user.Username} to offline");
                                user.IsOnline = false;
                                user.ClientId = null;
                                SaveUsers();

                                // Trigger event
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

        // Authenticate a user
        private bool AuthenticateUser(string clientId, string username, string licenseKey, string ipAddress)
        {
            LogDebug($"AuthenticateUser: Authenticating user {username} with license {licenseKey}");
            lock (_users)
            {
                // Look for user with the specified license key
                var user = _users.FirstOrDefault(u => u.LicenseKey == licenseKey);
                LogDebug($"AuthenticateUser: User found: {user != null}");

                // If user doesn't exist, create a new one
                if (user == null)
                {
                    LogDebug($"AuthenticateUser: New user, checking license key");
                    // Check if the license key is valid
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
                        LicenseExpiration = DateTime.Now.AddDays(30), // Default: 30 days license
                        RateLimit = 100, // Default rate limit
                        IsOnline = true,
                        ClientId = clientId
                    };

                    _users.Add(user);
                    LogInfo($"AuthenticateUser: New user created and added: {username}");
                }
                else
                {
                    LogDebug($"AuthenticateUser: Existing user found, checking license");
                    // Check if the license has expired
                    if (user.LicenseExpiration < DateTime.Now)
                    {
                        LogDebug($"AuthenticateUser: License expired on {user.LicenseExpiration}");
                        return false;
                    }

                    // Check if the user is already online
                    if (user.IsOnline)
                    {
                        LogDebug($"AuthenticateUser: User is already online with ClientId {user.ClientId}");
                        // Disconnect the old client, if present
                        if (!string.IsNullOrEmpty(user.ClientId) && user.ClientId != clientId)
                        {
                            LogDebug($"AuthenticateUser: Disconnecting old client {user.ClientId}");
                            DisconnectClient(user.ClientId,
                                "Another client has connected with your license.");
                        }
                    }

                    // Update user data
                    LogDebug($"AuthenticateUser: Updating user data for {username}");
                    user.Username = username; // Allow updating the username
                    user.IP = ipAddress;
                    user.LastLogin = DateTime.Now;
                    user.IsOnline = true;
                    user.ClientId = clientId;
                }

                // Save updated user data
                LogDebug($"AuthenticateUser: Saving updated user data");
                SaveUsers();

                return true;
            }
        }

        // Check if a license key is valid
        private bool IsValidLicenseKey(string licenseKey)
        {
            LogDebug($"IsValidLicenseKey: Checking license key: {licenseKey}");
            // Here you can implement your own logic to verify license keys
            // Example: Check for a specific length and format
            if (string.IsNullOrEmpty(licenseKey) || licenseKey.Length != 20)
            {
                LogDebug($"IsValidLicenseKey: Invalid length: {licenseKey?.Length ?? 0}");
                return false;
            }

            // Example: License key must start with "LICS-"
            if (!licenseKey.StartsWith("LICS-"))
            {
                LogDebug($"IsValidLicenseKey: Doesn't start with LICS-");
                return false;
            }

            LogDebug($"IsValidLicenseKey: License key is valid");
            return true;
        }

        // Send a message to a client
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

        // Send a message to all connected clients
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
                        StreamWriter writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
                        writer.WriteLine(messageJson);
                    }
                    catch (Exception ex)
                    {
                        LogError($"BroadcastMessage: Error sending: {ex.Message}");
                    }
                }
            }
        }

        // Send a message to a specific client
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

        // Disconnect a client
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
                    // Send disconnect message
                    LogDebug($"DisconnectClient: Sending disconnect message");
                    var writer = new StreamWriter(client.GetStream(), Encoding.UTF8) { AutoFlush = true };
                    SendMessage(writer, new Message
                    {
                        Type = "DISCONNECT",
                        Content = reason,
                        Sender = "Server",
                        Timestamp = DateTime.Now
                    });

                    // Close the connection
                    LogDebug($"DisconnectClient: Closing connection");
                    client.Close();
                    _connectedClients.Remove(clientId);

                    // Update user status
                    var user = _users.FirstOrDefault(u => u.ClientId == clientId);
                    if (user != null)
                    {
                        LogDebug($"DisconnectClient: Setting user {user.Username} to offline");
                        user.IsOnline = false;
                        string username = user.Username;
                        user.ClientId = null;
                        SaveUsers();

                        // Trigger event
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

        // Disconnect all connected clients
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

        // Load user data from JSON file
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

                // Set all users to offline when loading
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

        // Save user data to JSON file
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

        // Regular maintenance tasks
        private async Task MaintenanceTask()
        {
            LogDebug("MaintenanceTask: Task started");
            while (_isRunning)
            {
                LogDebug("MaintenanceTask: Performing maintenance");
                // Check for expired licenses
                CheckExpiredLicenses();

                // Wait 1 hour until the next check
                LogDebug("MaintenanceTask: Waiting 1 hour until next check");
                await Task.Delay(TimeSpan.FromHours(1));
            }

            LogDebug("MaintenanceTask: Task ended");
        }

        // Check for expired licenses
        private void CheckExpiredLicenses()
        {
            LogDebug("CheckExpiredLicenses: Checking for expired licenses");
            lock (_users)
            {
                var expiredUsers = _users.Where(u => u.IsOnline && u.LicenseExpiration < DateTime.Now).ToList();
                LogDebug($"CheckExpiredLicenses: {expiredUsers.Count} expired licenses found");

                foreach (var user in expiredUsers)
                {
                    LogDebug($"CheckExpiredLicenses: License for {user.Username} has expired. Expiration date: {user.LicenseExpiration}");
                    DisconnectClient(user.ClientId, "Your license has expired.");
                }
            }
        }

        // Method to extend a license
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

                // Notify the user if they are online
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
        
        // Logging methods - strictly respect log level settings
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