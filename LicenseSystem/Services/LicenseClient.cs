using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
        private TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private CancellationTokenSource _cts;
        private string _username;
        private string _licenseKey;

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        public bool IsConnected { get; private set; }

        public LicenseClient(string serverAddress, int serverPort)
        {
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            IsConnected = false;
            _cts = new CancellationTokenSource();

            //Console.WriteLine($"[DEBUG] LicenseClient erstellt. Server: {serverAddress}:{serverPort}");
        }

        public async Task<bool> ConnectAsync(string username, string licenseKey)
        {
            //Console.WriteLine($"[DEBUG] ConnectAsync: Versuche, eine Verbindung zum Server herzustellen. Username: {username}");

            if (IsConnected)
            {
                //Console.WriteLine("[DEBUG] ConnectAsync: Bereits verbunden");
                return true;
            }

            try
            {
                _username = username;
                _licenseKey = licenseKey;
                _client = new TcpClient();

                Console.WriteLine($"[DEBUG] ConnectAsync: Verbinde zu {_serverAddress}:{_serverPort}...");
                await _client.ConnectAsync(_serverAddress, _serverPort);
                Console.WriteLine("[DEBUG] ConnectAsync: TCP-Verbindung hergestellt");

                _stream = _client.GetStream();
                _reader = new StreamReader(_stream, new UTF8Encoding(false));
                _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = true };
                Console.WriteLine("[DEBUG] ConnectAsync: Stream und Reader/Writer erstellt");

                var authData = new
                {
                    username = _username,
                    licenseKey = _licenseKey
                };

                //Console.WriteLine("[DEBUG] ConnectAsync: Erstelle AUTH-Nachricht");
                var authMessage = new Message
                {
                    Type = "AUTH",
                    Content = JsonSerializer.Serialize(authData),
                    Sender = _username,
                    Timestamp = DateTime.Now
                };

                //Console.WriteLine("[DEBUG] ConnectAsync: Sende AUTH-Nachricht");
                var serializedMessage = JsonSerializer.Serialize(authMessage);
                //Console.WriteLine($"[DEBUG] ConnectAsync: Serialisierte Nachricht: {serializedMessage}");
                await _writer.WriteLineAsync(serializedMessage);
                //Console.WriteLine("[DEBUG] ConnectAsync: AUTH-Nachricht gesendet");

                //Console.WriteLine("[DEBUG] ConnectAsync: Warte auf Antwort vom Server");
                var responseJson = await _reader.ReadLineAsync();
                //Console.WriteLine($"[DEBUG] ConnectAsync: Antwort erhalten: {responseJson}");

                if (string.IsNullOrEmpty(responseJson))
                {
                    //Console.WriteLine("[DEBUG] ConnectAsync: Leere Antwort vom Server");
                    CloseConnection();
                    OnConnectionStatusChanged(false, "Keine Antwort vom Server");
                    return false;
                }

                var response = JsonSerializer.Deserialize<Message>(responseJson);
                //Console.WriteLine($"[DEBUG] ConnectAsync: Antworttyp: {response.Type}, Inhalt: {response.Content}");

                switch (response.Type)
                {
                    case "AUTH":
                        //Console.WriteLine("[DEBUG] ConnectAsync: Authentifizierung erfolgreich");
                        IsConnected = true;
                        _cts = new CancellationTokenSource();

                        //Console.WriteLine("[DEBUG] ConnectAsync: Starte Nachrichtenempfangs-Task");
                        _ = Task.Run(() => ReceiveMessagesAsync(_cts.Token));

                        OnConnectionStatusChanged(true, "Verbindung zum Server hergestellt.");
                        return true;
                    case "DISCONNECT":
                        //Console.WriteLine($"[DEBUG] ConnectAsync: Authentifizierung fehlgeschlagen: {response.Content}");
                        CloseConnection();
                        OnConnectionStatusChanged(false, response.Content);
                        return false;
                    default:
                        //Console.WriteLine($"[DEBUG] ConnectAsync: Unerwarteter Antworttyp: {response.Type}");
                        CloseConnection();
                        OnConnectionStatusChanged(false, "Unerwartete Antwort vom Server");
                        return false;
                }
            }
            catch (SocketException sockEx)
            {
                Console.WriteLine(
                    $"[ERROR] ConnectAsync: Socket-Fehler: {sockEx.Message}, ErrorCode: {sockEx.ErrorCode}");
                Console.WriteLine($"[ERROR] ConnectAsync: StackTrace: {sockEx.StackTrace}");
                CloseConnection();
                OnConnectionStatusChanged(false, $"Verbindungsfehler: {sockEx.Message}");
                return false;
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"[ERROR] ConnectAsync: IO-Fehler: {ioEx.Message}");
                Console.WriteLine($"[ERROR] ConnectAsync: InnerException: {ioEx.InnerException?.Message}");
                Console.WriteLine($"[ERROR] ConnectAsync: StackTrace: {ioEx.StackTrace}");
                CloseConnection();
                OnConnectionStatusChanged(false, $"Fehler bei der Übertragung: {ioEx.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] ConnectAsync: Allgemeiner Fehler: {ex.Message}");
                Console.WriteLine($"[ERROR] ConnectAsync: Typ: {ex.GetType().Name}");
                Console.WriteLine($"[ERROR] ConnectAsync: StackTrace: {ex.StackTrace}");
                CloseConnection();
                OnConnectionStatusChanged(false, $"Verbindungsfehler: {ex.Message}");
                return false;
            }

            return false;
        }

        public async Task DisconnectAsync(string reason = "Client hat die Verbindung getrennt")
        {
            //Console.WriteLine($"[DEBUG] DisconnectAsync: Trenne Verbindung. Grund: {reason}");

            if (!IsConnected)
            {
                //Console.WriteLine("[DEBUG] DisconnectAsync: Bereits getrennt");
                return;
            }

            try
            {
                //Console.WriteLine("[DEBUG] DisconnectAsync: Sende DISCONNECT-Nachricht");
                await SendMessageAsync(new Message
                {
                    Type = "DISCONNECT",
                    Content = reason,
                    Sender = _username,
                    Timestamp = DateTime.Now
                });
                //Console.WriteLine("[DEBUG] DisconnectAsync: DISCONNECT-Nachricht gesendet");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] DisconnectAsync: Fehler beim Senden der Trennungsnachricht: {ex.Message}");
            }
            finally
            {
                CloseConnection();
            }
        }

        private void CloseConnection()
        {
            //Console.WriteLine("[DEBUG] CloseConnection: Schließe Verbindung");
            IsConnected = false;

            try
            {
                _cts?.Cancel();
                //Console.WriteLine("[DEBUG] CloseConnection: CancellationToken gesetzt");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[ERROR] CloseConnection: Fehler beim Abbrechen des CancellationToken: {ex.Message}");
            }

            try
            {
                _reader?.Dispose();
                _writer?.Dispose();
                _stream?.Dispose();
                _client?.Close();
                //Console.WriteLine("[DEBUG] CloseConnection: Ressourcen freigegeben");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] CloseConnection: Fehler beim Freigeben der Ressourcen: {ex.Message}");
            }

            OnConnectionStatusChanged(false, "Verbindung zum Server getrennt.");
        }

        public async Task SendMessageAsync(Message message)
        {
            //Console.WriteLine($"[DEBUG] SendMessageAsync: Sende Nachricht vom Typ {message.Type}");

            if (!IsConnected)
            {
                //Console.WriteLine("[ERROR] SendMessageAsync: Nicht mit dem Server verbunden.");
                throw new InvalidOperationException("Nicht mit dem Server verbunden.");
            }

            try
            {
                var messageJson = JsonSerializer.Serialize(message);
                //Console.WriteLine($"[DEBUG] SendMessageAsync: Serialisierte Nachricht: {messageJson}");
                await _writer.WriteLineAsync(messageJson);
                //Console.WriteLine("[DEBUG] SendMessageAsync: Nachricht gesendet");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] SendMessageAsync: Fehler beim Senden der Nachricht: {ex.Message}");
                throw;
            }
        }

        public async Task SendCommandAsync(string command)
        {
            Console.WriteLine($"[DEBUG] SendCommandAsync: Sende Befehl: {command}");
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
            Console.WriteLine("[DEBUG] ReadMessageAsync: Lese Nachricht vom Server");
            var messageJson = await _reader.ReadLineAsync();
            
            if (!string.IsNullOrEmpty(messageJson) && messageJson.StartsWith("?"))
            {
                messageJson = messageJson.TrimStart('?');
            }

            if (string.IsNullOrEmpty(messageJson))
            {
                //Console.WriteLine("[DEBUG] ReadMessageAsync: Leere Nachricht (null oder leer) vom Server erhalten");
                throw new IOException("Server hat die Verbindung getrennt.");
            }

            Console.WriteLine($"[DEBUG] ReadMessageAsync: Nachricht empfangen: {messageJson}");
            var message = JsonSerializer.Deserialize<Message>(messageJson);
            Console.WriteLine($"[DEBUG] ReadMessageAsync: Nachricht deserialisiert. Typ: {message!.Type}");
            return message;
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            //Console.WriteLine("[DEBUG] ReceiveMessagesAsync: Starte Nachrichtenempfangsschleife");
            try
            {
                while (!cancellationToken.IsCancellationRequested && _client.Connected)
                {
                    Console.WriteLine("[DEBUG] ReceiveMessagesAsync: Warte auf Nachricht vom Server");
                    try
                    {
                        var message = await ReadMessageAsync();

                        //Console.WriteLine($"[DEBUG] ReceiveMessagesAsync: Nachricht vom Typ {message.Type} empfangen");
                        if (message.Type == "DISCONNECT")
                        {
                            //Console.WriteLine($"[DEBUG] ReceiveMessagesAsync: Server hat die Verbindung getrennt. Grund: {message.Content}");
                            CloseConnection();
                            OnConnectionStatusChanged(false, message.Content);
                            break;
                        }
                        //Console.WriteLine($"[DEBUG] ReceiveMessagesAsync: Löse MessageReceived-Event aus");
                        OnMessageReceived(message);
                    }
                    catch (IOException ioEx)
                    {
                        Console.WriteLine($"[ERROR] ReceiveMessagesAsync: IO-Fehler: {ioEx.Message}");
                        Console.WriteLine(
                            $"[ERROR] ReceiveMessagesAsync: InnerException: {ioEx.InnerException?.Message}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[ERROR] ReceiveMessagesAsync: Fehler beim Empfangen einer Nachricht: {ex.Message}");
                        Console.WriteLine($"[ERROR] ReceiveMessagesAsync: Typ: {ex.GetType().Name}");
                        throw;
                    }
                }
            }
            catch (IOException)
            {
                Console.WriteLine("[ERROR] ReceiveMessagesAsync: Verbindung zum Server verloren (IOException)");
                CloseConnection();
                OnConnectionStatusChanged(false, "Verbindung zum Server verloren.");
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("[ERROR] ReceiveMessagesAsync: Stream wurde geschlossen (ObjectDisposedException)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] ReceiveMessagesAsync: Allgemeiner Fehler: {ex.Message}");
                Console.WriteLine($"[ERROR] ReceiveMessagesAsync: Typ: {ex.GetType().Name}");
                Console.WriteLine($"[ERROR] ReceiveMessagesAsync: StackTrace: {ex.StackTrace}");
                CloseConnection();
                OnConnectionStatusChanged(false, $"Fehler: {ex.Message}");
            }
            finally
            {
                //Console.WriteLine("[DEBUG] ReceiveMessagesAsync: Nachrichtenempfangsschleife beendet");
            }
        }

        private void OnMessageReceived(Message message)
        {
            //Console.WriteLine($"[DEBUG] OnMessageReceived: Event für Nachricht vom Typ {message.Type}");
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
        }

        private void OnConnectionStatusChanged(bool isConnected, string reason)
        {
            //Console.WriteLine($"[DEBUG] OnConnectionStatusChanged: Event für Status {isConnected}, Grund: {reason}");
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(isConnected, reason));
        }
    }
}