using System.ComponentModel;
using System.Runtime.InteropServices;
using StalkerSaveEditor.Core.Companion;

namespace StalkerSaveEditor.Core.Hotkeys;

internal sealed class WindowsRegisterHotKeyBackend : IGlobalHotkeyBackend
{
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private readonly object _gate = new();
    private readonly Dictionary<int, CompanionHotkeyBinding> _bindings = [];
    private Thread? _messageThread;
    private TaskCompletionSource? _threadReady;
    private TaskCompletionSource? _started;
    private TaskCompletionSource? _stopped;
    private uint _threadId;
    private Action<CompanionHotkeyBinding>? _onPressed;

    public async Task StartAsync(
        IReadOnlyList<CompanionHotkeyBinding> bindings,
        Action<CompanionHotkeyBinding> onPressed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(onPressed);
        cancellationToken.ThrowIfCancellationRequested();
        Task started;
        lock (_gate)
        {
            if (_messageThread is not null)
            {
                throw new InvalidOperationException("The Windows hotkey backend is already running.");
            }

            _bindings.Clear();
            _onPressed = onPressed;
            _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _threadReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            started = _started.Task;
            _messageThread = new Thread(() => RunMessageLoop(bindings))
            {
                IsBackground = true,
                Name = "Stalker Save Editor hotkeys",
            };
            _messageThread.Start();
        }

        try
        {
            await started.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? stopped;
        Task? threadReady;
        uint threadId;
        lock (_gate)
        {
            if (_messageThread is null)
            {
                return;
            }

            stopped = _stopped?.Task;
            threadReady = _threadReady?.Task;
            threadId = _threadId;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (threadId == 0 && threadReady is not null)
        {
            await threadReady.ConfigureAwait(false);
            lock (_gate)
            {
                threadId = _threadId;
            }
        }

        if (threadId != 0 && !NativeMethods.PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1444) // ERROR_INVALID_THREAD_ID means startup already failed and the loop has exited.
            {
                throw new Win32Exception(error, "Could not stop the Windows hotkey message loop.");
            }
        }

        if (stopped is not null)
        {
            await stopped.ConfigureAwait(false);
        }

        lock (_gate)
        {
            _messageThread = null;
            _threadReady = null;
            _started = null;
            _stopped = null;
            _threadId = 0;
            _bindings.Clear();
            _onPressed = null;
        }
    }

    private void RunMessageLoop(IReadOnlyList<CompanionHotkeyBinding> bindings)
    {
        var registered = new List<int>();
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            _ = NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            _threadReady?.TrySetResult();
            var id = 1;
            foreach (var binding in bindings)
            {
                if (!NativeMethods.RegisterHotKey(
                    IntPtr.Zero,
                    id,
                    GetNativeModifiers(binding.Gesture.Modifiers),
                    (uint)char.ToUpperInvariant(binding.Gesture.Key)))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"Could not register global hotkey {binding.Gesture}; it may be in use by another application.");
                }

                _bindings.Add(id, binding);
                registered.Add(id);
                id++;
            }

            _started?.TrySetResult();
            int messageResult;
            while ((messageResult = NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0)) > 0)
            {
                if (message.Message == WmHotkey && _bindings.TryGetValue(unchecked((int)message.WParam.ToUInt64()), out var pressed))
                {
                    _onPressed?.Invoke(pressed);
                }
            }

            if (messageResult < 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The Windows hotkey message loop failed.");
            }

        }
        catch (Exception exception)
        {
            _started?.TrySetException(new HotkeyRegistrationException(exception.Message, exception));
        }
        finally
        {
            foreach (var id in registered)
            {
                _ = NativeMethods.UnregisterHotKey(IntPtr.Zero, id);
            }

            lock (_gate)
            {
                _threadId = 0;
            }

            _threadReady?.TrySetResult();
            _stopped?.TrySetResult();
        }
    }

    private static uint GetNativeModifiers(HotkeyModifiers modifiers)
    {
        var result = ModNoRepeat;
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            result |= ModControl;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= ModAlt;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= ModShift;
        }

        return result;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
        internal static extern int GetMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum);

        [DllImport("user32.dll", EntryPoint = "PeekMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum, uint remove);

        [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
        internal static extern uint GetCurrentThreadId();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }
}
