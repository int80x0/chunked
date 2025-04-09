using System.Text;
using System.Text.RegularExpressions;
using Server.Utils;

namespace Server.Console
{
    public class ConsoleCommandHandler
    {
        private readonly Dictionary<string, ConsoleCommand> _commands = new();
        private readonly IServiceProvider _services;
        private readonly Logger _logger;
        private bool _isRunning = false;
        private readonly List<string> _commandHistory = new();
        private int _historyIndex = -1;
        private string _currentInput = string.Empty;
        private readonly StringBuilder _buffer = new();
        private int _cursorPosition = 0;
        private List<string> _currentSuggestions = new();
        private int _suggestionIndex = -1;

        public ConsoleCommandHandler(IServiceProvider services, Logger logger)
        {
            _services = services;
            _logger = logger;
        }

        public void RegisterCommand(ConsoleCommand command)
        {
            if (_commands.ContainsKey(command.Name))
            {
                _logger.Warning(
                    $"Befehl '{command.Name}' ist bereits registriert. Der alte Befehl wird überschrieben.");
            }

            _commands[command.Name] = command;
            _logger.Debug($"Befehl '{command.Name}' registriert.");
        }

        public void RegisterCommands(IEnumerable<ConsoleCommand> commands)
        {
            foreach (var command in commands)
            {
                RegisterCommand(command);
            }
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _logger.Info(
                "ConsoleCommandHandler gestartet. Drücke 'Tab' für Autovervollständigung, 'Pfeiltasten' für Befehlsverlauf.");

            System.Console.WriteLine("Verfügbare Befehle:");
            foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
            {
                System.Console.WriteLine($"  {cmd.Name} - {cmd.Description}");
            }

            System.Console.WriteLine("  help - Zeigt Hilfe zu einem Befehl an");
            System.Console.WriteLine("  exit - Beendet den Server");
            System.Console.WriteLine();

            while (_isRunning)
            {
                System.Console.Write("> ");
                var input = await ReadLineWithAutoCompleteAsync();

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                if (_commandHistory.Count == 0 || _commandHistory[^1] != input)
                {
                    _commandHistory.Add(input);
                }

                _historyIndex = _commandHistory.Count;

                string[] parts = Regex.Matches(input, @"[\""].+?[\""]|[^ ]+")
                    .Cast<Match>()
                    .Select(m => m.Value.Trim('"'))
                    .ToArray();

                if (parts.Length == 0)
                {
                    continue;
                }

                string commandName = parts[0].ToLower();
                string[] args = parts.Skip(1).ToArray();

                try
                {
                    switch (commandName)
                    {
                        case "exit":
                            _isRunning = false;
                            _logger.Info("Server wird beendet...");
                            break;

                        case "help":
                            if (args.Length == 0)
                            {
                                System.Console.WriteLine("Verfügbare Befehle:");
                                foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
                                {
                                    System.Console.WriteLine($"  {cmd.Name} - {cmd.Description}");
                                }

                                System.Console.WriteLine("  help - Zeigt Hilfe zu einem Befehl an");
                                System.Console.WriteLine("  exit - Beendet den Server");
                            }
                            else
                            {
                                string cmdName = args[0].ToLower();
                                if (_commands.TryGetValue(cmdName, out var cmd))
                                {
                                    System.Console.WriteLine($"Hilfe für '{cmdName}':");
                                    System.Console.WriteLine($"  Beschreibung: {cmd.Description}");
                                    System.Console.WriteLine($"  Verwendung: {cmd.Name} {cmd.Usage}");
                                    System.Console.WriteLine($"  Beispiel: {cmd.Example}");
                                }
                                else
                                {
                                    System.Console.WriteLine($"Unbekannter Befehl '{cmdName}'.");
                                }
                            }

                            break;

                        default:
                            if (_commands.TryGetValue(commandName, out var command))
                            {
                                await command.ExecuteAsync(args);
                            }
                            else
                            {
                                System.Console.WriteLine(
                                    $"Unbekannter Befehl '{commandName}'. Gib 'help' ein, um alle verfügbaren Befehle anzuzeigen.");
                            }

                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler bei der Ausführung des Befehls '{commandName}': {ex.Message}");
                    System.Console.WriteLine($"Fehler: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
        }

        private async Task<string> ReadLineWithAutoCompleteAsync()
        {
            _buffer.Clear();
            _cursorPosition = 0;
            _currentSuggestions.Clear();
            _suggestionIndex = -1;

            while (true)
            {
                var key = System.Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        System.Console.WriteLine();
                        return _buffer.ToString();

                    case ConsoleKey.Backspace:
                        if (_cursorPosition > 0)
                        {
                            _buffer.Remove(_cursorPosition - 1, 1);
                            _cursorPosition--;
                            RefreshLine();
                        }

                        break;

                    case ConsoleKey.Delete:
                        if (_cursorPosition < _buffer.Length)
                        {
                            _buffer.Remove(_cursorPosition, 1);
                            RefreshLine();
                        }

                        break;

                    case ConsoleKey.LeftArrow:
                        if (_cursorPosition > 0)
                        {
                            _cursorPosition--;
                            System.Console.SetCursorPosition(System.Console.CursorLeft - 1, System.Console.CursorTop);
                        }

                        break;

                    case ConsoleKey.RightArrow:
                        if (_cursorPosition < _buffer.Length)
                        {
                            _cursorPosition++;
                            System.Console.SetCursorPosition(System.Console.CursorLeft + 1, System.Console.CursorTop);
                        }

                        break;

                    case ConsoleKey.UpArrow:
                        if (_commandHistory.Count > 0)
                        {
                            if (_historyIndex == _commandHistory.Count)
                            {
                                _currentInput = _buffer.ToString();
                            }

                            _historyIndex = Math.Max(0, _historyIndex - 1);
                            _buffer.Clear();
                            _buffer.Append(_commandHistory[_historyIndex]);
                            _cursorPosition = _buffer.Length;
                            RefreshLine();
                        }

                        break;

                    case ConsoleKey.DownArrow:
                        if (_commandHistory.Count > 0)
                        {
                            _historyIndex = Math.Min(_commandHistory.Count, _historyIndex + 1);
                            _buffer.Clear();
                            if (_historyIndex == _commandHistory.Count)
                            {
                                _buffer.Append(_currentInput);
                            }
                            else
                            {
                                _buffer.Append(_commandHistory[_historyIndex]);
                            }

                            _cursorPosition = _buffer.Length;
                            RefreshLine();
                        }

                        break;

                    case ConsoleKey.Tab:
                        if (_buffer.Length > 0)
                        {
                            string input = _buffer.ToString();
                            string[] parts = input.Split(' ');

                            if (parts.Length == 1)
                            {
                                // Command autocomplete
                                string prefix = parts[0].ToLower();

                                if (_currentSuggestions.Count == 0 || _suggestionIndex == -1)
                                {
                                    _currentSuggestions = _commands.Keys
                                        .Where(c => c.StartsWith(prefix))
                                        .OrderBy(c => c)
                                        .ToList();

                                    if (prefix == "help" || prefix.StartsWith("help"))
                                    {
                                        _currentSuggestions.Add("help");
                                    }
                                    else if (prefix == "exit" || prefix.StartsWith("exit"))
                                    {
                                        _currentSuggestions.Add("exit");
                                    }

                                    _suggestionIndex = 0;
                                }
                                else
                                {
                                    _suggestionIndex = (_suggestionIndex + 1) % _currentSuggestions.Count;
                                }

                                if (_currentSuggestions.Count > 0)
                                {
                                    string suggestion = _currentSuggestions[_suggestionIndex];
                                    _buffer.Clear();
                                    _buffer.Append(suggestion);
                                    _cursorPosition = _buffer.Length;
                                    RefreshLine();
                                }
                            }
                            else
                            {
                                // Parameter autocomplete (depends on the command)
                                string commandName = parts[0].ToLower();
                                string currentArg = parts[^1].ToLower();

                                if (_commands.TryGetValue(commandName, out var command))
                                {
                                    if (command.GetAutocompleteSuggestions != null)
                                    {
                                        var suggestions =
                                            await command.GetAutocompleteSuggestions(parts.Skip(1).ToArray());

                                        if (suggestions.Count > 0)
                                        {
                                            if (_currentSuggestions.Count == 0 || _suggestionIndex == -1)
                                            {
                                                _currentSuggestions = suggestions
                                                    .Where(s => s.ToLower().StartsWith(currentArg))
                                                    .ToList();
                                                _suggestionIndex = 0;
                                            }
                                            else
                                            {
                                                _suggestionIndex = (_suggestionIndex + 1) % _currentSuggestions.Count;
                                            }

                                            if (_currentSuggestions.Count > 0)
                                            {
                                                string suggestion = _currentSuggestions[_suggestionIndex];
                                                string[] newParts = parts.Take(parts.Length - 1).ToArray();
                                                string newInput = string.Join(" ", newParts) + " " + suggestion;

                                                _buffer.Clear();
                                                _buffer.Append(newInput);
                                                _cursorPosition = _buffer.Length;
                                                RefreshLine();
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        break;

                    case ConsoleKey.Home:
                        _cursorPosition = 0;
                        System.Console.SetCursorPosition(2, System.Console.CursorTop);
                        break;

                    case ConsoleKey.End:
                        _cursorPosition = _buffer.Length;
                        System.Console.SetCursorPosition(2 + _buffer.Length, System.Console.CursorTop);
                        break;

                    default:
                        if (key.KeyChar >= 32 && key.KeyChar <= 126)
                        {
                            _buffer.Insert(_cursorPosition, key.KeyChar);
                            _cursorPosition++;
                            RefreshLine();

                            // Reset autocomplete suggestions when typing
                            _currentSuggestions.Clear();
                            _suggestionIndex = -1;
                        }

                        break;
                }
            }
        }

        private void RefreshLine()
        {
            int curLeft = System.Console.CursorLeft;
            int curTop = System.Console.CursorTop;

            System.Console.SetCursorPosition(0, curTop);
            System.Console.Write("> " + _buffer.ToString() + new string(' ', 50)); // Clear line
            System.Console.SetCursorPosition(2 + _cursorPosition, curTop);
        }
    }
}