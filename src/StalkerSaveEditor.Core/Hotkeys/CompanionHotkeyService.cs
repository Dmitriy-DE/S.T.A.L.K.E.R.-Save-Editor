using StalkerSaveEditor.Core.Companion;

namespace StalkerSaveEditor.Core.Hotkeys;

#pragma warning disable CA1710 // This public event payload already derives from EventArgs; retain its established API name.
public sealed class CompanionHotkeyCommandResult(
    CompanionHotkeyAction action,
    bool succeeded,
    CompanionProtocolReply? reply,
    string? error) : EventArgs
{
    public CompanionHotkeyAction Action { get; } = action;

    public bool Succeeded { get; } = succeeded;

    public CompanionProtocolReply? Reply { get; } = reply;

    public string? Error { get; } = error;
}
#pragma warning restore CA1710

public sealed class CompanionHotkeyService : IAsyncDisposable
{
    private readonly CompanionProtocolClient _protocolClient;
    private readonly IGlobalHotkeyBackend _backend;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private int _active;
    private int _backendStarted;
    private int _pollingMayBeEnabled;
    private int _commandInFlight;
    private string? _lastError;

    public CompanionHotkeyService(CompanionProtocolClient protocolClient)
        : this(protocolClient, GlobalHotkeyBackendFactory.Create())
    {
    }

    internal CompanionHotkeyService(CompanionProtocolClient protocolClient, IGlobalHotkeyBackend backend)
    {
        ArgumentNullException.ThrowIfNull(protocolClient);
        ArgumentNullException.ThrowIfNull(backend);
        _protocolClient = protocolClient;
        _backend = backend;
    }

    public event EventHandler<CompanionHotkeyCommandResult>? CommandCompleted;

    public bool IsActive => Volatile.Read(ref _active) != 0;

    public string? LastError => Volatile.Read(ref _lastError);

    public HotkeyLayout? Layout { get; private set; }

    public async Task StartAsync(HotkeyLayout? layout = null, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActive || Volatile.Read(ref _backendStarted) != 0 || Volatile.Read(ref _pollingMayBeEnabled) != 0)
            {
                throw new InvalidOperationException("Global companion hotkeys are active or need to be stopped first.");
            }

            var selectedLayout = layout ?? HotkeyLayout.Default;
            await _backend.StartAsync(selectedLayout.Bindings, OnHotkeyPressed, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _backendStarted, 1);
            try
            {
                Volatile.Write(ref _pollingMayBeEnabled, 1);
                var reply = await _protocolClient.SendAsync("hotkeys", ["on"], cancellationToken).ConfigureAwait(false);
                EnsureAccepted(reply, "Enable companion hotkey polling");
                Layout = selectedLayout;
                Volatile.Write(ref _lastError, null);
                Volatile.Write(ref _active, 1);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _lastError, exception.Message);
                try
                {
                    await _backend.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    Volatile.Write(ref _backendStarted, 0);
                }
                catch (Exception stopException)
                {
                    Volatile.Write(ref _lastError, stopException.Message);
                    throw new AggregateException("Hotkey startup and native hook cleanup both failed.", exception, stopException);
                }

                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsActive && Volatile.Read(ref _backendStarted) == 0 && Volatile.Read(ref _pollingMayBeEnabled) == 0)
            {
                return;
            }

            Volatile.Write(ref _active, 0);
            List<Exception>? failures = null;
            if (Volatile.Read(ref _backendStarted) != 0)
            {
                try
                {
                    await _backend.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    Volatile.Write(ref _backendStarted, 0);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            if (Volatile.Read(ref _pollingMayBeEnabled) != 0)
            {
                try
                {
                    var reply = await _protocolClient.SendAsync("hotkeys", ["off"], CancellationToken.None).ConfigureAwait(false);
                    EnsureAccepted(reply, "Disable companion hotkey polling");
                    Volatile.Write(ref _pollingMayBeEnabled, 0);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            if (failures is { Count: > 0 })
            {
                var error = failures.Count == 1 ? failures[0] : new AggregateException(failures);
                Volatile.Write(ref _lastError, error.Message);
                throw error;
            }

            Volatile.Write(ref _lastError, null);
            Layout = null;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Dispose();
        }
    }

    private void OnHotkeyPressed(CompanionHotkeyBinding binding)
    {
        if (!IsActive || Interlocked.CompareExchange(ref _commandInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = RunCommandAsync(binding);
    }

    private async Task RunCommandAsync(CompanionHotkeyBinding binding)
    {
        CompanionHotkeyCommandResult result;
        try
        {
            var command = binding.Action switch
            {
                CompanionHotkeyAction.Heal => "heal",
                CompanionHotkeyAction.RepairEquipped => "repair_equipped",
                CompanionHotkeyAction.Mark => "mark",
                CompanionHotkeyAction.JumpLast => "jump_last",
                CompanionHotkeyAction.QuickSave => "quicksave",
                _ => throw new ArgumentOutOfRangeException(nameof(binding), binding.Action, "Unsupported hotkey action."),
            };
            var reply = await _protocolClient.SendAsync(command).ConfigureAwait(false);
            var succeeded = reply.Status == CompanionReplyStatus.Ok;
            Volatile.Write(ref _lastError, succeeded ? null : $"Game returned {reply.WireStatus}: {reply.Text}");
            result = new CompanionHotkeyCommandResult(
                binding.Action,
                succeeded,
                reply,
                succeeded ? null : LastError);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception.Message);
            result = new CompanionHotkeyCommandResult(binding.Action, false, null, exception.Message);
        }
        finally
        {
            Volatile.Write(ref _commandInFlight, 0);
        }

        try
        {
            CommandCompleted?.Invoke(this, result);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception.Message);
        }
    }

    private static void EnsureAccepted(CompanionProtocolReply reply, string operation)
    {
        if (reply.Status != CompanionReplyStatus.Ok)
        {
            throw new CompanionProtocolException($"{operation} failed: {reply.WireStatus} {reply.Text}".TrimEnd());
        }
    }
}

internal interface IGlobalHotkeyBackend
{
    Task StartAsync(
        IReadOnlyList<CompanionHotkeyBinding> bindings,
        Action<CompanionHotkeyBinding> onPressed,
        CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public static class HotkeyPlatformAvailability
{
    public static string? GetLinuxX11UnavailableReason(string? sessionType, string? display)
    {
        if (!string.IsNullOrWhiteSpace(display))
        {
            return null;
        }

        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            ? "Global hotkeys require X11/XWayland. This Wayland session has no XWayland display, so hotkeys do not work here."
            : "Global hotkeys require an X11 display, but DISPLAY is not set.";
    }
}

public sealed class HotkeyRegistrationException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);

internal static class GlobalHotkeyBackendFactory
{
    public static IGlobalHotkeyBackend Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRegisterHotKeyBackend();
        }

        if (OperatingSystem.IsLinux())
        {
            // In the application the keys are grabbed by a helper process with its own Xlib; hosts that did not
            // register one (tests, the CLI) use the same code in-process.
            return X11HotkeyHelper.Launch is { } launch
                ? new HelperProcessHotkeyBackend(() => new HotkeyHelperProcess(launch()))
                : new LinuxX11HotkeyBackend();
        }

        return new UnsupportedHotkeyBackend();
    }
}

internal sealed class UnsupportedHotkeyBackend : IGlobalHotkeyBackend
{
    public Task StartAsync(
        IReadOnlyList<CompanionHotkeyBinding> bindings,
        Action<CompanionHotkeyBinding> onPressed,
        CancellationToken cancellationToken) =>
        Task.FromException(new PlatformNotSupportedException("Global companion hotkeys are supported on Windows and X11/XWayland Linux only."));

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
