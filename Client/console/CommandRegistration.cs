using Client.Commands;
using Client.Utils;
using LicenseSystem.Services;

namespace Client.Console
{
    public static class CommandRegistration
    {
        public static Dictionary<string, ConsoleCommand> RegisterConsoleCommands(
            ConsoleCommandHandler commandHandler,
            LicenseClient licenseClient,
            Logger logger)
        {
            logger.Info("Registering console commands...");

            var commands = new Dictionary<string, ConsoleCommand>();

            try
            {
                var downloadCommand = new DownloadCommand(licenseClient, logger);
                var listCommand = new ListCommand(licenseClient, logger);
                var infoCommand = new InfoCommand(licenseClient, logger);
                var statusCommand = new StatusCommand(licenseClient, logger);
                
                commands.Add(downloadCommand.Name, downloadCommand);
                commands.Add(listCommand.Name, listCommand);
                commands.Add(infoCommand.Name, infoCommand);
                commands.Add(statusCommand.Name, statusCommand);
                
                var helpCommand = new HelpCommand(commands, logger);
                commands.Add(helpCommand.Name, helpCommand);
                
                foreach (var command in commands.Values)
                {
                    commandHandler.RegisterCommand(command);
                }

                logger.Info($"Successfully registered {commands.Count} console commands.");
                return commands;
            }
            catch (Exception ex)
            {
                logger.Error($"Error registering console commands: {ex.Message}");
                throw;
            }
        }
    }
}