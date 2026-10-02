using System.IO.Pipes;
using StalkerSaveEditor.Core.Hotkeys;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Hotkeys;

public sealed class X11HotkeyHelperTests
{
    private static readonly CompanionHotkeyBinding Heal = new(CompanionHotkeyAction.Heal, new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 'H'));
    private static readonly CompanionHotkeyBinding Save = new(CompanionHotkeyAction.QuickSave, new HotkeyGesture(HotkeyModifiers.Shift, 'S'));

    [Fact]
    public void A_binding_survives_the_wire_format()
    {
        Assert.Equal("bind Heal 3 H", X11HotkeyHelper.FormatBinding(Heal));
        Assert.Equal(Heal, X11HotkeyHelper.ParseBinding(X11HotkeyHelper.FormatBinding(Heal)));
        Assert.Equal(Save, X11HotkeyHelper.ParseBinding(X11HotkeyHelper.FormatBinding(Save)));
    }

    [Theory]
    [InlineData("bind Heal 3")]
    [InlineData("bind Nothing 3 H")]
    [InlineData("bind Heal 9 H")]
    [InlineData("bind Heal 3 h")]
    [InlineData("bind Heal 3 HH")]
    [InlineData("bind 7 3 H")]
    [InlineData("grab Heal 3 H")]
    public void Anything_but_a_well_formed_binding_is_refused(string line) =>
        Assert.Throws<FormatException>(() => X11HotkeyHelper.ParseBinding(line));

    [Fact]
    public async Task The_editor_and_the_helper_start_relay_presses_and_stop()
    {
        using var link = new HelperLink(new FakeBackend());
        var backend = new HelperProcessHotkeyBackend(() => link);
        var pressed = new List<CompanionHotkeyAction>();
        var seen = new SemaphoreSlim(0);

        await backend.StartAsync([Heal, Save], binding =>
        {
            lock (pressed) pressed.Add(binding.Action);
            seen.Release();
        }, CancellationToken.None);

        Assert.Equal([Heal, Save], link.Backend.Bindings);
        link.Backend.Press(Save);
        link.Backend.Press(Heal);
        Assert.True(await seen.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await seen.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal([CompanionHotkeyAction.QuickSave, CompanionHotkeyAction.Heal], pressed);

        await backend.StopAsync(CancellationToken.None);

        Assert.Equal(0, await link.ExitCode.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(link.Backend.Stopped);
    }

    [Fact]
    public async Task A_key_another_program_holds_is_reported_by_the_helper_and_reaches_the_editor()
    {
        using var link = new HelperLink(new FakeBackend { StartError = "An X11 global hotkey is already grabbed by another application." });
        var backend = new HelperProcessHotkeyBackend(() => link);

        var error = await Assert.ThrowsAsync<HotkeyRegistrationException>(() => backend.StartAsync([Heal], _ => { }, CancellationToken.None));

        Assert.Contains("already grabbed", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, await link.ExitCode.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task The_helper_ends_when_the_editor_goes_away()
    {
        var fake = new FakeBackend();
        using var input = new AnonymousPipeServerStream(PipeDirection.Out);
        using var inputClient = new AnonymousPipeClientStream(PipeDirection.In, input.ClientSafePipeHandle);
        var output = new StringWriter();
        var run = Task.Run(() => X11HotkeyHelper.Run(new StreamReader(inputClient), TextWriter.Synchronized(output), fake));
        using (var writer = new StreamWriter(input, leaveOpen: true))
        {
            await writer.WriteLineAsync(X11HotkeyHelper.FormatBinding(Heal));
            await writer.WriteLineAsync("start");
        }

        // The editor dies without saying "stop": its end of the pipe closes.
        input.Dispose();

        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(fake.Stopped);
        Assert.StartsWith("ready", output.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeBackend : IGlobalHotkeyBackend
    {
        private Action<CompanionHotkeyBinding>? _onPressed;

        public string? StartError { get; init; }

        public IReadOnlyList<CompanionHotkeyBinding> Bindings { get; private set; } = [];

        public bool Stopped { get; private set; }

        public Task StartAsync(IReadOnlyList<CompanionHotkeyBinding> bindings, Action<CompanionHotkeyBinding> onPressed, CancellationToken cancellationToken)
        {
            if (StartError is not null) throw new HotkeyRegistrationException(StartError, new InvalidOperationException());
            Bindings = bindings;
            _onPressed = onPressed;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public void Press(CompanionHotkeyBinding binding) => _onPressed?.Invoke(binding);
    }

    /// <summary>The helper's main loop on a thread, joined to the editor's side by two pipes, as a process would be.</summary>
    private sealed class HelperLink : IHotkeyHelperProcess
    {
        private readonly AnonymousPipeServerStream _toHelper = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromHelper = new(PipeDirection.In);
        private readonly AnonymousPipeClientStream _helperInput;
        private readonly AnonymousPipeClientStream _helperOutput;

        public HelperLink(FakeBackend backend)
        {
            Backend = backend;
            _helperInput = new AnonymousPipeClientStream(PipeDirection.In, _toHelper.ClientSafePipeHandle);
            _helperOutput = new AnonymousPipeClientStream(PipeDirection.Out, _fromHelper.ClientSafePipeHandle);
            Input = new StreamWriter(_toHelper);
            Output = new StreamReader(_fromHelper);
            ExitCode = Task.Run(() =>
            {
                using var writer = new StreamWriter(_helperOutput);
                var code = X11HotkeyHelper.Run(new StreamReader(_helperInput), writer, backend);
                return code;
            });
        }

        public FakeBackend Backend { get; }

        public Task<int> ExitCode { get; }

        public TextWriter Input { get; }

        public TextReader Output { get; }

        public async Task<bool> WaitForExitAsync(TimeSpan timeout)
        {
            try
            {
                await ExitCode.WaitAsync(timeout);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        public void Kill() => _toHelper.Dispose();

        public void Dispose()
        {
            _toHelper.Dispose();
            _fromHelper.Dispose();
            _helperInput.Dispose();
            _helperOutput.Dispose();
        }
    }
}
