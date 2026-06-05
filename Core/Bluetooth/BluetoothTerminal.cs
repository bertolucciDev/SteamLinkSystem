using System.Diagnostics;
using System.Text;
using Core.Logging;

namespace Core.Bluetooth;

public sealed class BluetoothTerminal : IAsyncDisposable, IDisposable
{
    private const string Prompt = "[bluetooth]#";
    private readonly SemaphoreSlim _commandQueue = new(1, 1);
    private readonly object _outputLock = new();
    private readonly StringBuilder _outputBuffer = new();
    private readonly SemaphoreSlim _outputSignal = new(0, int.MaxValue);
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutReader;
    private Task? _stderrReader;
    private CancellationTokenSource? _readerCancellation;
    private bool _disposed;

    public bool IsActive => _process is { HasExited: false } && !_disposed;

    public void Start()
    {
        if (IsActive)
            return;

        ThrowIfDisposed();
        _readerCancellation = new CancellationTokenSource();
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bluetoothctl",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        try
        {
            Logger.Info("Starting persistent bluetoothctl process", "BluetoothTerminal");
            _process.Start();
            _stdin = _process.StandardInput;
            _stdin.AutoFlush = true;
            _stdoutReader = Task.Run(() => ReadStreamAsync(_process.StandardOutput, _readerCancellation.Token));
            _stderrReader = Task.Run(() => ReadStreamAsync(_process.StandardError, _readerCancellation.Token));
        }
        catch (Exception ex)
        {
            Logger.Error($"Unable to start bluetoothctl: {ex.Message}", "BluetoothTerminal");
            CleanupProcess();
            throw;
        }
    }

    public Task<string> SendCommand(string command) => SendCommand(command, TimeSpan.FromSeconds(8), CancellationToken.None);

    public async Task<string> SendCommand(string command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        ThrowIfDisposed();
        if (!IsActive)
            Start();

        await _commandQueue.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var startIndex = CurrentOutputLength();
            Logger.Debug($"bluetoothctl <= {command}", "BluetoothTerminal");
            await _stdin!.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            return await WaitForCommandOutputAsync(startIndex, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _commandQueue.Release();
        }
    }

    private async Task<string> WaitForCommandOutputAsync(int startIndex, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var quietSince = DateTime.UtcNow;
        var lastLength = startIndex;

        while (!linkedCts.IsCancellationRequested)
        {
            var snapshot = GetOutputSince(startIndex);
            var currentLength = CurrentOutputLength();
            if (currentLength != lastLength)
            {
                lastLength = currentLength;
                quietSince = DateTime.UtcNow;
            }

            if (snapshot.Contains(Prompt, StringComparison.OrdinalIgnoreCase))
                return snapshot;

            if (snapshot.Length > 0 && DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(350))
                return snapshot;

            try
            {
                await _outputSignal.WaitAsync(TimeSpan.FromMilliseconds(100), linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var output = GetOutputSince(startIndex);
        Logger.Warning($"bluetoothctl command timed out after {timeout.TotalSeconds:0.0}s", "BluetoothTerminal");
        return output;
    }

    private async Task ReadStreamAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[512];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                lock (_outputLock)
                {
                    _outputBuffer.Append(buffer, 0, read);
                    if (_outputBuffer.Length > 64 * 1024)
                        _outputBuffer.Remove(0, _outputBuffer.Length - 48 * 1024);
                }
                _outputSignal.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed)
                Logger.Warning($"bluetoothctl stream reader stopped: {ex.Message}", "BluetoothTerminal");
        }
    }

    private int CurrentOutputLength()
    {
        lock (_outputLock)
            return _outputBuffer.Length;
    }

    private string GetOutputSince(int startIndex)
    {
        lock (_outputLock)
        {
            var safeStart = Math.Clamp(startIndex, 0, _outputBuffer.Length);
            return _outputBuffer.ToString(safeStart, _outputBuffer.Length - safeStart);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BluetoothTerminal));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CleanupProcess();
        _commandQueue.Dispose();
        _outputSignal.Dispose();
        Logger.Info("Bluetooth terminal disposed", "BluetoothTerminal");
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_stdoutReader != null)
            await Task.WhenAny(_stdoutReader, Task.Delay(250)).ConfigureAwait(false);
        if (_stderrReader != null)
            await Task.WhenAny(_stderrReader, Task.Delay(250)).ConfigureAwait(false);
    }

    private void CleanupProcess()
    {
        try
        {
            _readerCancellation?.Cancel();
            if (_process is { HasExited: false })
            {
                try
                {
                    _stdin?.WriteLine(BluetoothCommands.Quit());
                }
                catch
                {
                }

                if (!_process.WaitForExit(500))
                    _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Error while stopping bluetoothctl: {ex.Message}", "BluetoothTerminal");
        }
        finally
        {
            _stdin?.Dispose();
            _process?.Dispose();
            _readerCancellation?.Dispose();
            _stdin = null;
            _process = null;
            _readerCancellation = null;
        }
    }
}
