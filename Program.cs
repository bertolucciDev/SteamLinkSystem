using Core.Bluetooth;
using Core.Logging;
using Screens;

await using var bluetooth = new BluetoothService();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    Logger.Info("Ctrl+C requested; exiting cleanly", "Program");
    bluetooth.Dispose();
    Environment.Exit(0);
};

try
{
    Logger.Info("SteamLinkSystem starting", "Program");
    var mainMenu = new MainMenu(bluetooth);
    await mainMenu.ShowAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    Logger.Error($"Unhandled application error: {ex}", "Program");
    Console.Error.WriteLine($"SteamLinkSystem encountered an unrecoverable error: {ex.Message}");
}
finally
{
    Logger.Info("SteamLinkSystem stopped", "Program");
}
