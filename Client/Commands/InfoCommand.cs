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
    public class InfoCommand : ConsoleCommand
    {
        private readonly LicenseClient _client;
        private readonly Logger _logger;
        private TaskCompletionSource<string> _infoTcs = new();
        private bool _isWaitingForInfo = false;

        public InfoCommand(LicenseClient client, Logger logger)
            : base(
                "info",
                "Shows information about your license and connection",
                "",
                "/info",
                null!)
        {
            _client = client;
            _logger = logger;

            _client.MessageReceived += OnMessageReceived!;
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (!_isWaitingForInfo)
                return;

            var message = e.Message;
            try
            {
                if (!message.Content.StartsWith('{') || !message.Content.Contains("\"LicenseInfo\"")) return;
                _logger?.Debug("Received license information");
                _infoTcs.TrySetResult(message.Content);
                _isWaitingForInfo = false;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error processing info message: {ex.Message}");
                _infoTcs.TrySetException(ex);
                _isWaitingForInfo = false;
            }
        }

        public override async Task ExecuteAsync(string[] args)
        {
            try
            {
                _logger?.Info("Requesting license information from server");
                WriteInfo("Requesting license information from server...");

                _infoTcs = new TaskCompletionSource<string>();
                _isWaitingForInfo = true;

                await _client.SendCommandAsync("info");

                _logger?.Debug("Waiting for server response");
                WriteInfo("Waiting for server response...");

                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(_infoTcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _isWaitingForInfo = false;
                    WriteError("Timeout waiting for server response.");
                    return;
                }
                
                var infoJson = await _infoTcs.Task;
                var info = JsonSerializer.Deserialize<LicenseInfoResponse>(infoJson);

                if (info == null)
                {
                    WriteError("Failed to parse license information from server.");
                    return;
                }

                _logger?.Info("Received license information from server");
                
                var licenseText = 
                    $"[green]Username:[/] {Markup.Escape(info.LicenseInfo.Username)}\n" +
                    $"[green]License Key:[/] {Markup.Escape(info.LicenseInfo.LicenseKey)}\n" +
                    $"[green]Expiration Date:[/] {info.LicenseInfo.ExpirationDate:yyyy-MM-dd HH:mm}\n" +
                    $"[green]Status:[/] {(info.LicenseInfo.IsActive ? "[lime]Active[/]" : "[red]Inactive[/]")}\n" +
                    $"[green]First Login:[/] {info.LicenseInfo.FirstLogin:yyyy-MM-dd HH:mm}\n" +
                    $"[green]Last Login:[/] {info.LicenseInfo.LastLogin:yyyy-MM-dd HH:mm}\n" +
                    $"[green]IP Address:[/] {Markup.Escape(info.LicenseInfo.IpAddress)}\n" +
                    $"[green]Client ID:[/] {Markup.Escape(info.LicenseInfo.ClientId)}\n" +
                    $"[green]Rate Limit:[/] {info.LicenseInfo.RateLimit}";
                
                var panel = new Panel(Align.Center(new Markup(licenseText)))
                {
                    Header = new PanelHeader("License Information"),
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(2, 1, 2, 1)
                };

                AnsiConsole.Write(panel);
                
                var connectionText =
                    $"[green]Server Address:[/] {Markup.Escape(info.ConnectionInfo.ServerAddress)}\n" +
                    $"[green]Connected Since:[/] {info.ConnectionInfo.ConnectedSince.ToString("yyyy-MM-dd HH:mm:ss")}\n" +
                    $"[green]Ping:[/] {info.ConnectionInfo.Ping}ms";

                var connectionPanel = new Panel(Align.Center(new Markup(connectionText)))
                {
                    Header = new PanelHeader("Connection Information"),
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(2, 1, 2, 1)
                };

                AnsiConsole.Write(connectionPanel);
                
                var daysRemaining = (info.LicenseInfo.ExpirationDate - DateTime.Now).Days;
                if (daysRemaining <= 7)
                {
                    WriteWarning($"Your license will expire in {daysRemaining} days. Please consider renewal.");
                }
                else
                {
                    WriteInfo($"Your license is valid for {daysRemaining} more days.");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error retrieving license information: {ex.Message}");
                WriteError($"Error retrieving license information: {ex.Message}");
            }
        }
    }
}