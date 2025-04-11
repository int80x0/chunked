using System.Text;
using System.Text.RegularExpressions;
using Client.Utils;
using Spectre.Console;

namespace Client.Console
{
    public partial class ConsoleCommandHandler(Logger logger)
    {
        private readonly Dictionary<string, ConsoleCommand> _commands = new();
        private bool _isRunning = false;
        private readonly List<string> _commandHistory = [];
        private int _historyIndex = -1;
        private string _currentInput = string.Empty;
        private readonly StringBuilder _buffer = new();
        private int _cursorPosition = 0;
        private List<string> _currentSuggestions = [];
        private int _suggestionIndex = -1;

        public void RegisterCommand(ConsoleCommand command)
        {
            if (_commands.ContainsKey(command.Name))
            {
                logger.Warning($"Command '{command.Name}' is already registered. The old command will be overwritten.");
            }

            _commands[command.Name] = command;
            logger.Debug($"Command '{command.Name}' registered.");
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
            logger.Info(
                "ConsoleCommandHandler started. Press 'Tab' for autocompletion, arrow keys for command history.");

            AnsiConsole.MarkupLine("[green]Available commands:[/]");
            foreach (var cmd in _commands.Values)
            {
                AnsiConsole.MarkupLine($"  [blue]/{cmd.Name}[/] - {cmd.Description}");
            }

            AnsiConsole.MarkupLine("  [blue]/help[/] - Shows help for a command");
            AnsiConsole.MarkupLine("  [blue]/exit[/] - Exits the application");
            AnsiConsole.WriteLine();

            while (_isRunning)
            {
                AnsiConsole.Markup("[green]>[/] ");
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

                var parts = MyRegex().Matches(input)
                    .Select(m => m.Value.Trim('"'))
                    .ToArray();

                if (parts.Length == 0)
                {
                    continue;
                }

                var commandName = parts[0].ToLower();
                if (!commandName.StartsWith($"/"))
                {
                    commandName = "/" + commandName;
                }

                var args = parts.Skip(1).ToArray();

                try
                {
                    switch (commandName)
                    {
                        case "/exit":
                            _isRunning = false;
                            logger.Info("Application is shutting down...");
                            break;

                        case "/help":
                            if (args.Length == 0)
                            {
                                AnsiConsole.MarkupLine("[green]Available commands:[/]");
                                foreach (var cmd in _commands.Values)
                                {
                                    AnsiConsole.MarkupLine($"  [blue]/{cmd.Name}[/] - {cmd.Description}");
                                }

                                AnsiConsole.MarkupLine("  [blue]/help[/] - Shows help for a command");
                                AnsiConsole.MarkupLine("  [blue]/exit[/] - Exits the application");
                            }
                            else
                            {
                                string cmdName = args[0].ToLower();
                                if (cmdName.StartsWith("/"))
                                {
                                    cmdName = cmdName.Substring(1);
                                }

                                if (_commands.TryGetValue(cmdName, out var cmd))
                                {
                                    AnsiConsole.MarkupLine($"[green]Help for '/{cmdName}':[/]");
                                    AnsiConsole.MarkupLine($"  [white]Description:[/] {cmd.Description}");
                                    AnsiConsole.MarkupLine($"  [white]Usage:[/] /{cmd.Name} {cmd.Usage}");
                                    AnsiConsole.MarkupLine($"  [white]Example:[/] {cmd.Example}");
                                }
                                else
                                {
                                    AnsiConsole.MarkupLine($"[red]Unknown command '/{cmdName}'.[/]");
                                }
                            }

                            break;

                        default:
                            string commandNameWithoutSlash = commandName.StartsWith("/")
                                ? commandName.Substring(1)
                                : commandName;

                            if (_commands.TryGetValue(commandNameWithoutSlash, out var command))
                            {
                                await command.ExecuteAsync(args);
                            }
                            else
                            {
                                AnsiConsole.MarkupLine(
                                    $"[red]Unknown command '{commandName}'. Type '/help' to see all available commands.[/]");
                            }

                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Error executing command '{commandName}': {ex.Message}");
                    AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
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
                            _buffer.Append(_historyIndex == _commandHistory.Count
                                ? _currentInput
                                : _commandHistory[_historyIndex]);

                            _cursorPosition = _buffer.Length;
                            RefreshLine();
                        }

                        break;

                    case ConsoleKey.Tab:
                        if (_buffer.Length > 0)
                        {
                            var input = _buffer.ToString();
                            var parts = input.Split(' ');

                            if (parts.Length == 1)
                            {
                                var prefix = parts[0].ToLower();
                                if (!prefix.StartsWith($"/"))
                                {
                                    prefix = "/" + prefix;
                                }

                                if (_currentSuggestions.Count == 0 || _suggestionIndex == -1)
                                {
                                    _currentSuggestions = _commands.Keys
                                        .Select(c => "/" + c)
                                        .Where(c => c.StartsWith(prefix))
                                        .OrderBy(c => c)
                                        .ToList();

                                    if (prefix == "/help" || prefix.StartsWith("/help"))
                                    {
                                        _currentSuggestions.Add("/help");
                                    }
                                    else if (prefix == "/exit" || prefix.StartsWith("/exit"))
                                    {
                                        _currentSuggestions.Add("/exit");
                                    }

                                    _suggestionIndex = 0;
                                }
                                else
                                {
                                    _suggestionIndex = (_suggestionIndex + 1) % _currentSuggestions.Count;
                                }

                                if (_currentSuggestions.Count > 0)
                                {
                                    var suggestion = _currentSuggestions[_suggestionIndex];
                                    _buffer.Clear();
                                    _buffer.Append(suggestion);
                                    _cursorPosition = _buffer.Length;
                                    RefreshLine();
                                }
                            }
                            else
                            {
                                var commandName = parts[0].ToLower();
                                if (commandName.StartsWith($"/"))
                                {
                                    commandName = commandName[1..];
                                }

                                var currentArg = parts[^1].ToLower();

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
                                                    .Where(s => s.StartsWith(currentArg,
                                                        StringComparison.CurrentCultureIgnoreCase))
                                                    .ToList();
                                                _suggestionIndex = 0;
                                            }
                                            else
                                            {
                                                _suggestionIndex = (_suggestionIndex + 1) % _currentSuggestions.Count;
                                            }

                                            if (_currentSuggestions.Count > 0)
                                            {
                                                var suggestion = _currentSuggestions[_suggestionIndex];
                                                var newParts = parts.Take(parts.Length - 1).ToArray();
                                                var newInput = string.Join(" ", newParts) + " " + suggestion;

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

                            _currentSuggestions.Clear();
                            _suggestionIndex = -1;
                        }

                        break;
                }
            }
        }

        private void RefreshLine()
        {
            var curLeft = System.Console.CursorLeft;
            var curTop = System.Console.CursorTop;

            System.Console.SetCursorPosition(0, curTop);
            System.Console.Write("> " + _buffer + new string(' ', 50));
            System.Console.SetCursorPosition(2 + _cursorPosition, curTop);
        }

        [GeneratedRegex(@"[\""].+?[\""]|[^ ]+")]
        private static partial Regex MyRegex();
    }
}