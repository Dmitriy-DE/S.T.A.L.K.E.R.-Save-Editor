using System.Diagnostics;
using System.Globalization;

namespace StalkerSaveEditor.Core.Hotkeys;

/// <summary>
/// X11 global hotkeys in a process of their own. Xlib wants <c>XInitThreads</c> before any other Xlib call and has
/// one error handler per process; inside the editor both are shared with the UI toolkit. The helper is the same
/// executable started with <see cref="Argument"/>: it opens its own display, grabs the keys and reports presses,
/// so nothing it does to Xlib can touch the editor's window.
///
/// Protocol (text lines). Parent → helper: <c>bind ACTION MODIFIERS KEY</c> for every binding, then <c>start</c>;
/// later <c>stop</c> or end of input. Helper → parent: <c>ready</c> or <c>error MESSAGE</c>, then
/// <c>pressed ACTION</c> for every key press. The helper ends when its input ends, so it cannot outlive the editor.
/// </summary>
public static class X11HotkeyHelper
{
    public const string Argument = "--x11-hotkey-helper";

    private static Func<ProcessStartInfo>? _launch;

    /// <summary>
    /// The application says how to start itself as the helper. Hosts that never call this (tests, the CLI) keep the
    /// in-process backend.
    /// </summary>
    public static void UseHelperProcess(Func<ProcessStartInfo> launch) => _launch = launch ?? throw new ArgumentNullException(nameof(launch));

    internal static Func<ProcessStartInfo>? Launch => _launch;

    /// <summary>The helper's main loop. Returns the process exit code.</summary>
    public static int Run(TextReader input, TextWriter output) => Run(input, output, new LinuxX11HotkeyBackend());

    internal static int Run(TextReader input, TextWriter output, IGlobalHotkeyBackend backend)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var bindings = new List<CompanionHotkeyBinding>();
        try
        {
            while (true)
            {
                var line = input.ReadLine();
                if (line is null) return 0;
                if (line == "start") break;
                bindings.Add(ParseBinding(line));
            }

            var gate = new object();
            void Send(string text)
            {
                lock (gate)
                {
                    output.WriteLine(text);
                    output.Flush();
                }
            }

            backend.StartAsync(bindings, pressed => Send("pressed " + pressed.Action), CancellationToken.None).GetAwaiter().GetResult();
            Send("ready");
            // Nothing else is expected from the parent: "stop" or the end of input (the editor is gone) both end here.
            while (input.ReadLine() is { } command && command != "stop")
            {
            }

            backend.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException or FormatException or DllNotFoundException or IOException)
        {
            try
            {
                output.WriteLine("error " + exception.Message.ReplaceLineEndings(" "));
                output.Flush();
            }
            catch (IOException)
            {
            }

            return 1;
        }
    }

    internal static string FormatBinding(CompanionHotkeyBinding binding) => string.Create(
        CultureInfo.InvariantCulture, $"bind {binding.Action} {(int)binding.Gesture.Modifiers} {binding.Gesture.Key}");

    internal static CompanionHotkeyBinding ParseBinding(string line)
    {
        var parts = line.Split(' ');
        if (parts is not ["bind", var action, var modifiers, [var key]] ||
            !Enum.TryParse<CompanionHotkeyAction>(action, ignoreCase: false, out var parsedAction) || !Enum.IsDefined(parsedAction) ||
            !int.TryParse(modifiers, NumberStyles.None, CultureInfo.InvariantCulture, out var bits) || bits is < 0 or > 7 ||
            !char.IsAsciiLetterUpper(key))
        {
            throw new FormatException("Unexpected hotkey helper command: " + line);
        }

        return new CompanionHotkeyBinding(parsedAction, new HotkeyGesture((HotkeyModifiers)bits, key));
    }
}

/// <summary>The editor's side of <see cref="X11HotkeyHelper"/>: starts the helper, sends the bindings, relays presses.</summary>
internal sealed class HelperProcessHotkeyBackend(Func<IHotkeyHelperProcess> start) : IGlobalHotkeyBackend
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private IHotkeyHelperProcess? _process;
    private Task? _reader;

    public async Task StartAsync(
        IReadOnlyList<CompanionHotkeyBinding> bindings,
        Action<CompanionHotkeyBinding> onPressed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(onPressed);
        if (_process is not null) throw new InvalidOperationException("The X11 hotkey helper is already running.");
        var process = start();
        _process = process;
        try
        {
            foreach (var binding in bindings) await process.Input.WriteLineAsync(X11HotkeyHelper.FormatBinding(binding)).ConfigureAwait(false);
            await process.Input.WriteLineAsync("start").ConfigureAwait(false);
            await process.Input.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StartTimeout);
            string? answer;
            try
            {
                answer = await process.Output.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HotkeyRegistrationException("The X11 hotkey helper did not answer.", new TimeoutException());
            }

            if (answer != "ready")
            {
                var reason = answer is null
                    ? "The X11 hotkey helper ended before it was ready."
                    : answer.StartsWith("error ", StringComparison.Ordinal) ? answer["error ".Length..] : "Unexpected helper answer: " + answer;
                throw new HotkeyRegistrationException(reason, new InvalidOperationException(answer ?? "end of output"));
            }
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var byAction = bindings.ToDictionary(binding => binding.Action.ToString(), StringComparer.Ordinal);
        _reader = Task.Run(async () =>
        {
            try
            {
                while (await process.Output.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (line.StartsWith("pressed ", StringComparison.Ordinal) && byAction.TryGetValue(line["pressed ".Length..], out var binding))
                    {
                        onPressed(binding);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }, CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null) return;
        try
        {
            try
            {
                await process.Input.WriteLineAsync("stop").ConfigureAwait(false);
                await process.Input.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                process.Input.Close();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The helper is already gone.
            }

            if (!await process.WaitForExitAsync(StopTimeout).ConfigureAwait(false)) process.Kill();
            if (_reader is { } reader)
            {
                await Task.WhenAny(reader, Task.Delay(StopTimeout, CancellationToken.None)).ConfigureAwait(false);
            }
        }
        finally
        {
            _reader = null;
            process.Dispose();
        }
    }
}

internal interface IHotkeyHelperProcess : IDisposable
{
    TextWriter Input { get; }

    TextReader Output { get; }

    Task<bool> WaitForExitAsync(TimeSpan timeout);

    void Kill();
}

internal sealed class HotkeyHelperProcess : IHotkeyHelperProcess
{
    private readonly Process _process;

    public HotkeyHelperProcess(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.CreateNoWindow = true;
        _process = Process.Start(startInfo) ?? throw new HotkeyRegistrationException(
            "The X11 hotkey helper could not be started.", new InvalidOperationException(startInfo.FileName));
    }

    public TextWriter Input => _process.StandardInput;

    public TextReader Output => _process.StandardOutput;

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var limit = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public void Dispose() => _process.Dispose();
}
