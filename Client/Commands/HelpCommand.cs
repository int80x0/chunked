using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Console;
using Client.Utils;
using Spectre.Console;

namespace Client.Commands
{
    public class HelpCommand(Dictionary<string, ConsoleCommand> commands, Logger logger) : ConsoleCommand("help",
        "Shows help for available commands",
        "[command]",
        "/help download",
        GetAutocompleteSuggestionsAsync)
    {
        private static Task<List<string>> GetAutocompleteSuggestionsAsync(string[] args)
        {
            return Task.FromResult<List<string>>(args.Length == 0 ?
                ["download", "list", "help"]
                : []);
        }

        public override Task ExecuteAsync(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    ShowGeneralHelp();
                }
                else
                {
                    var commandName = args[0].ToLower();
                    if (commandName.StartsWith($"/"))
                    {
                        commandName = commandName[1..];
                    }
                    
                    if (commands.TryGetValue(commandName, out var command))
                    {
                        ShowCommandHelp(command);
                    }
                    else
                    {
                        WriteError($"Unknown command '/{commandName}'.");
                        ShowGeneralHelp();
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Error displaying help: {ex.Message}");
                WriteError($"Error displaying help: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private void ShowGeneralHelp()
        {
            var table = new Table
            {
                Title = new TableTitle("Available Commands")
            };
            table.AddColumn("Command");
            table.AddColumn("Description");
            table.AddColumn("Usage");
            
            table.AddRow("[blue]/help[/]", "Shows help for available commands", "/help [command]");
            table.AddRow("[blue]/exit[/]", "Exits the application", "/exit");
            
            foreach (var cmd in commands.Values.OrderBy(c => c.Name))
            {
                if (cmd.Name != "help")
                {
                    table.AddRow(
                        $"[blue]/{cmd.Name}[/]", 
                        cmd.Description, 
                        $"/{cmd.Name} {cmd.Usage}");
                }
            }

            AnsiConsole.Write(table);
            
            WriteInfo("For detailed help on a specific command, type: /help <command>");
        }

        private static void ShowCommandHelp(ConsoleCommand command)
        {
            var panel = new Panel(Markup.Escape(command.Description))
            {
                Header = new PanelHeader($"Command: /{command.Name}"),
                Padding = new Padding(1, 1),
                Border = BoxBorder.Rounded
            };
            
            AnsiConsole.Write(panel);
            
            AnsiConsole.MarkupLine("[yellow]Usage:[/]");
            AnsiConsole.MarkupLine($"  [blue]/{command.Name} {command.Usage}[/]");

            if (string.IsNullOrEmpty(command.Example)) return;
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Example:[/]");
            AnsiConsole.MarkupLine($"  [green]{command.Example}[/]");
        }
    }
}