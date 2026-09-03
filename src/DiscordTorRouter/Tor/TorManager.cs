using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using DiscordTorRouter.Infrastructure;

namespace DiscordTorRouter.Tor;

public sealed partial class TorManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private TaskCompletionSource _ready = NewReadySource();
    public TorStatus Status { get; private set; } = TorStatus.Stopped;
    public int BootstrapProgress { get; private set; }
    public event EventHandler<TorStatus>? StatusChanged;
    public event EventHandler<int>? BootstrapProgressChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false }) return;
            _process?.Dispose();
            _process = null;
            if (!File.Exists(AppPaths.TorExecutable)) throw new FileNotFoundException("Tor Expert Bundle não encontrado.", AppPaths.TorExecutable);
            if (!File.Exists(AppPaths.TorConfig)) throw new FileNotFoundException("torrc não encontrado.", AppPaths.TorConfig);
            Directory.CreateDirectory(AppPaths.TorDataDirectory);
            await StopStaleOwnedTorAsync(cancellationToken);
            _ready = NewReadySource();
            SetBootstrapProgress(0);
            SetStatus(TorStatus.Starting);
            AppLog.Info("Starting Tor");
            var info = new ProcessStartInfo
            {
                FileName = AppPaths.TorExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(AppPaths.TorExecutable)!
            };
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add(AppPaths.TorConfig);
            info.ArgumentList.Add("--DataDirectory");
            info.ArgumentList.Add(AppPaths.TorDataDirectory);
            info.ArgumentList.Add("--__OwningControllerProcess");
            info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += HandleOutput;
            _process.ErrorDataReceived += HandleOutput;
            _process.Exited += (_, _) =>
            {
                if (Status is not TorStatus.Stopping and not TorStatus.Stopped)
                {
                    SetStatus(TorStatus.Error);
                    _ready.TrySetException(new InvalidOperationException($"Tor encerrou com código {_process?.ExitCode}."));
                }
            };
            if (!_process.Start()) throw new InvalidOperationException("Não foi possível iniciar o Tor.");
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            SetStatus(TorStatus.Bootstrapping);
        }
        catch
        {
            SetStatus(TorStatus.Error);
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        await _ready.Task.WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
        await WaitUntilReadyAsync(cancellationToken);
    }

    public async Task RequestNewCircuitAsync(CancellationToken cancellationToken = default)
    {
        if (Status != TorStatus.Connected) throw new InvalidOperationException("Tor não está conectado.");
        var cookiePath = Path.Combine(AppPaths.TorDataDirectory, "control_auth_cookie");
        var cookie = Convert.ToHexString(await File.ReadAllBytesAsync(cookiePath, cancellationToken));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", 9051, cancellationToken);
        await using var stream = client.GetStream();
        var command = Encoding.ASCII.GetBytes($"AUTHENTICATE {cookie}\r\nSIGNAL NEWNYM\r\nQUIT\r\n");
        await stream.WriteAsync(command, cancellationToken);
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var response = await reader.ReadToEndAsync(cancellationToken);
        var lines = response.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Count(line => line.StartsWith("250", StringComparison.Ordinal)) < 2 || lines.Any(line => line.StartsWith('4') || line.StartsWith('5')))
            throw new IOException($"Tor ControlPort rejeitou NEWNYM: {response.Trim()}");
        AppLog.Info("Requested a new Tor circuit");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process is null) { SetStatus(TorStatus.Stopped); return; }
            SetStatus(TorStatus.Stopping);
            AppLog.Info("Stopping Tor");
            if (!_process.HasExited)
            {
                try
                {
                    await SendControlCommandAsync("SIGNAL SHUTDOWN", cancellationToken);
                    await _process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
                {
                    AppLog.Warn("Tor did not stop through ControlPort; terminating its process tree");
                    if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync(cancellationToken);
                }
            }
            _process.Dispose();
            _process = null;
            SetStatus(TorStatus.Stopped);
        }
        finally { _gate.Release(); }
    }

    private static async Task StopStaleOwnedTorAsync(CancellationToken cancellationToken)
    {
        if (!IsTorPortInUse()) return;

        AppLog.Warn("Tor ports are already in use; attempting to stop a stale app-owned Tor process");
        try
        {
            var response = await SendControlCommandAsync("SIGNAL SHUTDOWN", cancellationToken);
            if (!response.Contains("250 OK", StringComparison.Ordinal))
                throw new IOException($"ControlPort rejeitou o encerramento: {response.Trim()}");

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (IsTorPortInUse() && DateTime.UtcNow < deadline)
                await Task.Delay(100, cancellationToken);

            if (IsTorPortInUse()) throw new TimeoutException("O processo Tor anterior não liberou as portas a tempo.");
            AppLog.Info("Stale app-owned Tor process stopped");
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
        {
            throw new InvalidOperationException(
                "As portas 9050/9051 estão ocupadas. Feche a outra instância do Tor ou aguarde alguns segundos e tente novamente.", ex);
        }
    }

    private static bool IsTorPortInUse()
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return listeners.Any(endpoint => endpoint.Port is 9050 or 9051);
    }

    private static async Task<string> SendControlCommandAsync(string commandText, CancellationToken cancellationToken)
    {
        var cookiePath = Path.Combine(AppPaths.TorDataDirectory, "control_auth_cookie");
        var cookie = Convert.ToHexString(await File.ReadAllBytesAsync(cookiePath, cancellationToken));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", 9051, cancellationToken);
        await using var stream = client.GetStream();
        var command = Encoding.ASCII.GetBytes($"AUTHENTICATE {cookie}\r\n{commandText}\r\nQUIT\r\n");
        await stream.WriteAsync(command, cancellationToken);
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private void HandleOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        var safeLine = e.Data.ReplaceLineEndings(" ");
        var bootstrap = BootstrapRegex().Match(safeLine);
        if (bootstrap.Success)
        {
            var progress = int.Parse(bootstrap.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            SetBootstrapProgress(progress);
            AppLog.Info($"Tor bootstrap {progress}%");
        }
        else AppLog.Info($"Tor: {safeLine}");
        if (safeLine.Contains("Bootstrapped 100%", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(TorStatus.Connected);
            _ready.TrySetResult();
        }
    }

    private void SetStatus(TorStatus value)
    {
        Status = value;
        StatusChanged?.Invoke(this, value);
    }

    private void SetBootstrapProgress(int value)
    {
        BootstrapProgress = value;
        BootstrapProgressChanged?.Invoke(this, value);
    }

    private static TaskCompletionSource NewReadySource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    [GeneratedRegex(@"Bootstrapped\s+(\d+)%", RegexOptions.IgnoreCase)] private static partial Regex BootstrapRegex();
    public async ValueTask DisposeAsync() { await StopAsync(); _gate.Dispose(); }
}
