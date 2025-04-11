using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Client.Console;
using Client.Models;
using Client.Utils;
using LicenseSystem.Services;
using Spectre.Console;

namespace Client.Commands
{
    public class ListCommand : ConsoleCommand
    {
        private readonly LicenseClient _client;
        private readonly Logger _logger;
        private TaskCompletionSource<string> _gameListTcs = new();
        private bool _isWaitingForGameList = false;

        public ListCommand(LicenseClient client, Logger logger)
            : base(
                "list",
                "Lists all available games on the server",
                "[category]",
                "/list",
                GetAutocompleteSuggestionsAsync)
        {
            _client = client;
            _logger = logger;

            _client.MessageReceived += OnMessageReceived!;
        }

        private static Task<List<string>> GetAutocompleteSuggestionsAsync(string[] args)
        {
            return Task.FromResult<List<string>>(args.Length == 0 ? ["all", "newest", "popular"] : []);
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (!_isWaitingForGameList)
                return;

            var message = e.Message;
            try
            {
                if (!message.Content.StartsWith('{') || !message.Content.Contains("\"Games\"")) return;
                _logger?.Debug("Received game list information");
                _gameListTcs.TrySetResult(message.Content);
                _isWaitingForGameList = false;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error processing game list message: {ex.Message}");
                _gameListTcs.TrySetException(ex);
                _isWaitingForGameList = false;
            }
        }

        public override async Task ExecuteAsync(string[] args)
        {
            var category = args.Length > 0 ? args[0].ToLower() : "all";

            try
            {
                _logger?.Info($"Requesting game list from server (category: {category})");
                WriteInfo($"Requesting game list from server (category: {category})...");

                _gameListTcs = new TaskCompletionSource<string>();
                _isWaitingForGameList = true;

                await _client.SendCommandAsync($"list {category}");

                _logger?.Debug("Waiting for server response");
                WriteInfo("Waiting for server response...");

                var timeoutTask = Task.Delay(10000);
                var completedTask = await Task.WhenAny(_gameListTcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _isWaitingForGameList = false;
                    WriteError("Timeout waiting for server response.");
                    return;
                }
                
                var gameListJson = await _gameListTcs.Task;
                var gameList = JsonSerializer.Deserialize<GameListResponse>(gameListJson);

                if (gameList?.Games == null || gameList.Games.Count == 0)
                {
                    WriteInfo("No games available in this category.");
                    return;
                }

                _logger?.Info($"Received {gameList.Games.Count} games from server");
                WriteSuccess($"Found {gameList.Games.Count} games:");
                
                var table = new Table();
                table.AddColumn("ID");
                table.AddColumn("Title");
                table.AddColumn("Size");
                table.AddColumn("Version");
                table.AddColumn("Upload Date");

                foreach (var game in gameList.Games)
                {
                    table.AddRow(
                        game.Id.ToString(),
                        game.Title,
                        FormatFileSize(game.Size),
                        game.Version,
                        game.UploadDate.ToString("yyyy-MM-dd HH:mm")
                    );
                }

                AnsiConsole.Write(table);
                
                WriteInfo("To download a game, use the /download command with the game ID or title.");
                WriteInfo("Example: /download \"Grand Theft Auto V\"");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error listing games: {ex.Message}");
                WriteError($"Error listing games: {ex.Message}");
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            var suffixIndex = 0;
            double size = bytes;
            
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }
    }
}