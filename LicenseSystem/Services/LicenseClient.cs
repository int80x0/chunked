using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using LicenseSystem.Models;

namespace LicenseSystem.Services
{
    public class MessageReceivedEventArgs(Message message) : EventArgs
    {
        public Message Message { get; } = message;
    }

    public class ConnectionStatusEventArgs(bool isConnected, string reason = null!) : EventArgs
    {
        public bool IsConnected { get; } = isConnected;
        public string Reason { get; } = reason;
    }

    public sealed class LicenseClient
    {
        private TcpClient _client = null!;
        private NetworkStream _stream = null!;
        private StreamReader _reader = null!;
        private StreamWriter _writer = null!;
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private CancellationTokenSource _cts;
        private string _username = null!;
        private string _licenseKey = null!;
        private readonly bool _isDebug;

        public event EventHandler<MessageReceivedEventArgs> MessageReceived = null!;
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged = null!;

        public bool IsConnected { get; private set; }

        public LicenseClient(string serverAddress, int serverPort)
        {
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            IsConnected = false;
            _cts = new CancellationTokenSource();
            
            #if DEBUG
            _isDebug = true;
            #else
            _isDebug = false;
            #endif

            LogDebug($"LicenseClient created. Server: {serverAddress}:{serverPort}");
        }

        public async Task<bool> ConnectAsync(string username, string licenseKey)
        {
            LogDebug($"ConnectAsync: Attempting to connect to server. Username: {username}");

            if (IsConnected)
            {
                LogDebug("ConnectAsync: Already connected");
                return true;
            }

            try
            {
                _username = username;
                _licenseKey = licenseKey;
                _client = new TcpClient();

                LogInfo($"ConnectAsync: Connecting to {_serverAddress}:{_serverPort}...");
                await _client.ConnectAsync(_serverAddress, _serverPort);
                LogDebug("ConnectAsync: TCP connection established");

                _stream = _client.GetStream();
                _reader = new StreamReader(_stream, new UTF8Encoding(false));
                _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = true };
                LogDebug("ConnectAsync: Stream and reader/writer created");

                var authData = new
                {
                    username = _username,
                    licenseKey = _licenseKey
                };

                LogDebug("ConnectAsync: Creating AUTH message");
                var authMessage = new Message
                {
                    Type = "AUTH",
                    Content = JsonSerializer.Serialize(authData),
                    Sender = _username,
                    Timestamp = DateTime.Now
                };

                LogDebug("ConnectAsync: Sending AUTH message");
                var serializedMessage = JsonSerializer.Serialize(authMessage);
                LogDebug($"ConnectAsync: Serialized message: {serializedMessage}");
                await _writer.WriteLineAsync(serializedMessage);
                LogDebug("ConnectAsync: AUTH message sent");

                LogDebug("ConnectAsync: Waiting for response from server");
                var responseJson = await _reader.ReadLineAsync();
                LogDebug($"ConnectAsync: Response received: {responseJson}");

                if (string.IsNullOrEmpty(responseJson))
                {
                    LogDebug("ConnectAsync: Empty response from server");
                    CloseConnection();
                    OnConnectionStatusChanged(false, "No response from server");
                    return false;
                }

                var response = JsonSerializer.Deserialize<Message>(responseJson);
                LogDebug($"ConnectAsync: Response type: {response.Type}, Content: {response.Content}");

                switch (response.Type)
                {
                    case "AUTH":
                        LogDebug("ConnectAsync: Authentication successful");
                        IsConnected = true;
                        _cts = new CancellationTokenSource();

                        LogDebug("ConnectAsync: Starting message receiving task");
                        _ = Task.Run(() => ReceiveMessagesAsync(_cts.Token));

                        OnConnectionStatusChanged(true, "Connected to server.");
                        return true;
                    case "DISCONNECT":
                        LogDebug($"ConnectAsync: Authentication failed: {response.Content}");
                        CloseConnection();
                        OnConnectionStatusChanged(false, response.Content);
                        return false;
                    default:
                        LogDebug($"ConnectAsync: Unexpected response type: {response.Type}");
                        CloseConnection();
                        OnConnectionStatusChanged(false, "Unexpected response from server");
                        return false;
                }
            }
            catch (SocketException sockEx)
            {
                LogError($"ConnectAsync: Socket error: {sockEx.Message}, ErrorCode: {sockEx.ErrorCode}");
                LogError($"ConnectAsync: StackTrace: {sockEx.StackTrace}");
                CloseConnection();
                OnConnectionStatusChanged(false, $"Connection error: {sockEx.Message}");
                return false;
            }
            catch (IOException ioEx)
            {
                LogError($"ConnectAsync: IO error: {ioEx.Message}");
                LogError($"ConnectAsync: InnerException: {ioEx.InnerException?.Message}");
                LogError($"ConnectAsync: StackTrace: {ioEx.StackTrace}");
                CloseConnection();
                OnConnectionStatusChanged(false, $"Transfer error: {ioEx.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"ConnectAsync: General error: {ex.Message}");
                LogError($"ConnectAsync: Type: {ex.GetType().Name}");
                LogError($"ConnectAsync: StackTrace: {ex.StackTrace}");
                CloseConnection();
                OnConnectionStatusChanged(false, $"Connection error: {ex.Message}");
                return false;
            }

            return false;
        }

        public async Task DisconnectAsync(string reason = "Client disconnected")
        {
            LogDebug($"DisconnectAsync: Disconnecting. Reason: {reason}");

            if (!IsConnected)
            {
                LogDebug("DisconnectAsync: Already disconnected");
                return;
            }

            try
            {
                LogDebug("DisconnectAsync: Sending DISCONNECT message");
                await SendMessageAsync(new Message
                {
                    Type = "DISCONNECT",
                    Content = reason,
                    Sender = _username,
                    Timestamp = DateTime.Now
                });
                LogDebug("DisconnectAsync: DISCONNECT message sent");
            }
            catch (Exception ex)
            {
                LogError($"DisconnectAsync: Error sending disconnect message: {ex.Message}");
            }
            finally
            {
                CloseConnection();
            }
        }

        private void CloseConnection()
        {
            LogDebug("CloseConnection: Closing connection");
            IsConnected = false;

            try
            {
                _cts?.Cancel();
                LogDebug("CloseConnection: CancellationToken set");
            }
            catch (Exception ex)
            {
                LogError($"CloseConnection: Error cancelling CancellationToken: {ex.Message}");
            }

            try
            {
                _reader?.Dispose();
                _writer?.Dispose();
                _stream?.Dispose();
                _client?.Close();
                LogDebug("CloseConnection: Resources released");
            }
            catch (Exception ex)
            {
                LogError($"CloseConnection: Error releasing resources: {ex.Message}");
            }

            OnConnectionStatusChanged(false, "Disconnected from server.");
        }

        private async Task SendMessageAsync(Message message)
        {
            LogDebug($"SendMessageAsync: Sending message of type {message.Type}");

            if (!IsConnected)
            {
                LogError("SendMessageAsync: Not connected to server.");
                throw new InvalidOperationException("Not connected to server.");
            }

            try
            {
                var messageJson = JsonSerializer.Serialize(message);
                LogDebug($"SendMessageAsync: Serialized message: {messageJson}");
                await _writer.WriteLineAsync(messageJson);
                LogDebug("SendMessageAsync: Message sent");
            }
            catch (Exception ex)
            {
                LogError($"SendMessageAsync: Error sending message: {ex.Message}");
                throw;
            }
        }

        public async Task SendCommandAsync(string command)
        {
            LogInfo($"SendCommandAsync: Sending command: {command}");
            await SendMessageAsync(new Message
            {
                Type = "COMMAND",
                Content = command,
                Sender = _username,
                Timestamp = DateTime.Now
            });
        }

        private async Task<Message> ReadMessageAsync()
        {
            LogDebug("ReadMessageAsync: Reading message from server");
            var messageJson = await _reader.ReadLineAsync();
            
            if (!string.IsNullOrEmpty(messageJson) && messageJson.StartsWith("?"))
            {
                messageJson = messageJson.TrimStart('?');
            }

            if (string.IsNullOrEmpty(messageJson))
            {
                LogDebug("ReadMessageAsync: Empty message (null or empty) received from server");
                throw new IOException("Server has disconnected.");
            }

            LogDebug($"ReadMessageAsync: Message received: {messageJson}");
            var message = JsonSerializer.Deserialize<Message>(messageJson);
            LogDebug($"ReadMessageAsync: Message deserialized. Type: {message!.Type}");
            return message;
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            LogDebug("ReceiveMessagesAsync: Starting message receive loop");
            try
            {
                while (!cancellationToken.IsCancellationRequested && _client.Connected)
                {
                    LogDebug("ReceiveMessagesAsync: Waiting for message from server");
                    try
                    {
                        var message = await ReadMessageAsync();

                        LogDebug($"ReceiveMessagesAsync: Message of type {message.Type} received");
                        if (message.Type == "DISCONNECT")
                        {
                            LogDebug($"ReceiveMessagesAsync: Server has disconnected. Reason: {message.Content}");
                            CloseConnection();
                            OnConnectionStatusChanged(false, message.Content);
                            break;
                        }
                        LogDebug($"ReceiveMessagesAsync: Triggering MessageReceived event");
                        OnMessageReceived(message);
                    }
                    catch (IOException ioEx)
                    {
                        LogError($"ReceiveMessagesAsync: IO error: {ioEx.Message}");
                        LogError($"ReceiveMessagesAsync: InnerException: {ioEx.InnerException?.Message}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        LogError($"ReceiveMessagesAsync: Error receiving a message: {ex.Message}");
                        LogError($"ReceiveMessagesAsync: Type: {ex.GetType().Name}");
                        throw;
                    }
                }
            }
            catch (IOException)
            {
                LogError("ReceiveMessagesAsync: Connection to server lost (IOException)");
                CloseConnection();
                OnConnectionStatusChanged(false, "Connection to server lost.");
            }
            catch (ObjectDisposedException)
            {
                LogError("ReceiveMessagesAsync: Stream was closed (ObjectDisposedException)");
            }
            catch (Exception ex)
            {
                LogError($"ReceiveMessagesAsync: General error: {ex.Message}");
                LogError($"ReceiveMessagesAsync: Type: {ex.GetType().Name}");
                LogError($"ReceiveMessagesAsync: StackTrace: {ex.StackTrace}");
                CloseConnection();
                OnConnectionStatusChanged(false, $"Error: {ex.Message}");
            }
            finally
            {
                LogDebug("ReceiveMessagesAsync: Message receive loop ended");
            }
        }

        private void OnMessageReceived(Message message)
        {
            LogDebug($"OnMessageReceived: Event for message of type {message.Type}");
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
        }

        private void OnConnectionStatusChanged(bool isConnected, string reason)
        {
            LogDebug($"OnConnectionStatusChanged: Event for status {isConnected}, Reason: {reason}");
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(isConnected, reason));
        }
        
        
        private void LogDebug(string message)
        {
            if (_isDebug)
            {
                Debug.WriteLine($"[DEBUG] {message}");
            }
        }

        private void LogInfo(string message)
        {
            Console.WriteLine($"[INFO] {message}");
        }

        private void LogError(string message)
        {
            Console.WriteLine($"[ERROR] {message}");
        }
    }
}