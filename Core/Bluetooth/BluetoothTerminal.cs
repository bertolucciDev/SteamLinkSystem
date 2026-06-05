using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Core.Bluetooth;

/// <summary>
/// Manages a persistent bluetoothctl interactive session.
/// 
/// This service maintains a long-lived bluetoothctl process and communicates
/// via stdin/stdout. This is required because bluetoothctl is a stateful
/// interactive tool, not designed for isolated one-shot command execution.
/// 
/// Reference: PRD Section 3.4 (Stateful Linux Tool Awareness)
/// </summary>
public class BluetoothTerminal : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _isInitialized = false;
    private bool _disposed = false;
    private readonly object _lock = new();

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _process != null && !_process.HasExited && _isInitialized;
            }
        }
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized)
                return;

            try
            {
                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/bluetoothctl",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = false,
                        CreateNoWindow = true
                    }
                };

                _process.Start();

                _stdin = _process.StandardInput;
                _stdout = _process.StandardOutput;

                _stdin.AutoFlush = true;

                Logger.Info("Bluetooth terminal initialized", "BluetoothTerminal");
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize Bluetooth terminal: {ex.Message}", "BluetoothTerminal");
                throw;
            }
        }
    }

    public async Task<string> SendCommandAsync(string command)
    {
        lock (_lock)
        {
            if (!IsActive)
                throw new InvalidOperationException("Bluetooth terminal is not active");

            if (_stdin == null || _stdout == null)
                throw new InvalidOperationException("Terminal streams are not initialized");
        }

        try
        {
            Logger.Debug($"Sending command: {command}", "BluetoothTerminal");

            lock (_lock)
            {
                _stdin!.WriteLine(command);
            }

            var output = await ReadUntilPromptAsync();

            Logger.Debug($"Command output: {output.Length} chars", "BluetoothTerminal");

            return output;
        }
        catch (Exception ex)
        {
            Logger.Error($"Command failed: {ex.Message}", "BluetoothTerminal");
            throw;
        }
    }

    private async Task<string> ReadUntilPromptAsync()
    {
        var output = new StringBuilder();
        var buffer = new char[1024];
        var timeout = TimeSpan.FromSeconds(5);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            lock (_lock)
            {
                if (_stdout == null)
                    throw new InvalidOperationException("Stdout stream is not initialized");
            }

            if (_stdout!.Peek() > -1)
            {
                int charsRead = await _stdout!.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead > 0)
                {
                    output.Append(buffer, 0, charsRead);
                    stopwatch.Restart();
                }
            }
            else
            {
                await Task.Delay(50);
            }
        }

        return output.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            try
            {
                _stdin?.Dispose();
                _stdout?.Dispose();
                _process?.Kill();
                _process?.Dispose();

                Logger.Info("Bluetooth terminal disposed", "BluetoothTerminal");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during terminal disposal: {ex.Message}", "BluetoothTerminal");
            }
            finally
            {
                _disposed = true;
                _isInitialized = false;
                _process = null;
                _stdin = null;
                _stdout = null;
            }
        }
    }
}
