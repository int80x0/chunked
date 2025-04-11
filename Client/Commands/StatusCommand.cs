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
    public class StatusCommand : ConsoleCommand
    {
        private readonly LicenseClient _client;
        private readonly Logger _logger;
        private TaskCompletionSource<string> _statusTcs = new();
        private bool _isWaitingForStatus = false;

        public StatusCommand(LicenseClient client, Logger logger)
            : base(
                "status",
                "Shows the current status of the server and your connection",
                "",
                "/status",
                null!)
        {
            _client = client;
            _logger = logger;

            _client.MessageReceived += OnMessageReceived!;
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (!_isWaitingForStatus)
                return;

            var message = e.Message;
            try
            {
                if (!message.Content.StartsWith('{') || !message.Content.Contains("\"ServerStatus\"")) return;
                _logger?.Debug("Received server status information");
                _statusTcs.TrySetResult(message.Content);
                _isWaitingForStatus = false;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error processing status message: {ex.Message}");
                _statusTcs.TrySetException(ex);
                _isWaitingForStatus = false;
            }
        }

        public override async Task ExecuteAsync(string[] args)
        {
            try
            {
                _logger?.Info("Requesting server status information");
                WriteInfo("Requesting server status information...");

                _statusTcs = new TaskCompletionSource<string>();
                _isWaitingForStatus = true;

                await _client.SendCommandAsync("status");

                _logger?.Debug("Waiting for server response");

                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(_statusTcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _isWaitingForStatus = false;
                    WriteError("Timeout waiting for server response.");
                    return;
                }
                
                var statusJson = await _statusTcs.Task;
                var status = JsonSerializer.Deserialize<ServerStatusResponse>(statusJson);

                if (status == null)
                {
                    WriteError("Failed to parse server status information.");
                    return;
                }

                _logger?.Info("Received server status information");
                
                var table = new Table
                {
                    Title = new TableTitle("Server Status"),
                };
                table.AddColumn("Property");
                table.AddColumn("Value");

                table.AddRow("Server Version", status.ServerStatus.Version);
                table.AddRow("Server Uptime", FormatTimeSpan(status.ServerStatus.Uptime));
                table.AddRow("Connected Users", status.ServerStatus.ConnectedUsers.ToString());
                table.AddRow("Total Users", status.ServerStatus.TotalUsers.ToString());
                table.AddRow("Active Licenses", status.ServerStatus.ActiveLicenses.ToString());
                table.AddRow("Expired Licenses", status.ServerStatus.ExpiredLicenses.ToString());

                AnsiConsole.Write(table);
                
                var connectionStatus = status.ConnectionStatus;
                var statusText = connectionStatus.IsConnected ? "[green]Connected[/]" : "[red]Disconnected[/]";
                var pingColor = GetPingColor(connectionStatus.Ping);

                AnsiConsole.MarkupLine($"Connection Status: {statusText}");
                AnsiConsole.MarkupLine($"Ping: [{pingColor}]{connectionStatus.Ping} ms[/]");
                AnsiConsole.MarkupLine($"Connected Since: {connectionStatus.ConnectedSince:yyyy-MM-dd HH:mm:ss}");
                AnsiConsole.MarkupLine($"Data Sent: {FormatBytes(connectionStatus.DataSent)}");
                AnsiConsole.MarkupLine($"Data Received: {FormatBytes(connectionStatus.DataReceived)}");
                
                if (status.SystemResources != null)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Server System Resources:[/]");
                    
                    var cpuProgress = new BarChart()
                        .Width(60)
                        .AddItem("CPU", status.SystemResources.CpuUsage, GetCpuColor(status.SystemResources.CpuUsage));
                    
                    AnsiConsole.Write(cpuProgress);
                    
                    var memoryChart = new BarChart()
                        .Width(60)
                        .AddItem("Memory", status.SystemResources.MemoryUsage, GetMemoryColor(status.SystemResources.MemoryUsage));
                    
                    AnsiConsole.Write(memoryChart);
                    
                    AnsiConsole.MarkupLine($"Disk Space: {FormatBytes(status.SystemResources.AvailableDiskSpace)} available");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error retrieving server status: {ex.Message}");
                WriteError($"Error retrieving server status: {ex.Message}");
            }
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.Days > 0)
            {
                return $"{timeSpan.Days} days, {timeSpan.Hours} hours, {timeSpan.Minutes} minutes";
            }

            return timeSpan.Hours > 0 ? $"{timeSpan.Hours} hours, {timeSpan.Minutes} minutes, {timeSpan.Seconds} seconds" : $"{timeSpan.Minutes} minutes, {timeSpan.Seconds} seconds";
        }

        private static string FormatBytes(long bytes)
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

        private static string GetPingColor(int ping)
        {
            return ping switch
            {
                < 50 => "lime",
                < 100 => "green",
                < 200 => "yellow",
                _ => "red"
            };
        }

        private static Color GetCpuColor(double cpuUsage)
        {
            return cpuUsage switch
            {
                < 50 => Color.Green,
                < 80 => Color.Yellow,
                _ => Color.Red
            };
        }

        private static Color GetMemoryColor(double memoryUsage)
        {
            return memoryUsage switch
            {
                < 50 => Color.Green,
                < 80 => Color.Yellow,
                _ => Color.Red
            };
        }
    }
}