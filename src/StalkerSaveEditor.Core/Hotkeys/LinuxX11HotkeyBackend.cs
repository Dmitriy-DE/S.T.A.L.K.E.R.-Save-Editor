using StalkerSaveEditor.Core.Diagnostics;
using System.Runtime.InteropServices;
using StalkerSaveEditor.Core.Companion;

namespace StalkerSaveEditor.Core.Hotkeys;

internal sealed class LinuxX11HotkeyBackend : IGlobalHotkeyBackend
{
    private const uint ShiftMask = 1u << 0;
    private const uint LockMask = 1u << 1;
    private const uint ControlMask = 1u << 2;
    private const uint Mod1Mask = 1u << 3;
    private const uint Mod2Mask = 1u << 4;
    private const int KeyPress = 2;
    private const byte BadAccess = 10;
    private static readonly object XErrorHandlerGate = new();
    private static readonly XErrorHandler ErrorHandler = CaptureXError;
    private static readonly uint[] IgnoredLockModifiers = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];
    private readonly object _lifecycleGate = new();
    private readonly List<(byte KeyCode, uint Modifiers)> _grabbedKeys = [];
    private readonly Dictionary<byte, List<(uint Modifiers, CompanionHotkeyBinding Binding)>> _bindingsByKey = [];
    private Thread? _eventThread;
    private TaskCompletionSource? _started;
    private TaskCompletionSource? _stopped;
    private int _stopRequested;
    private IntPtr _display;
    private UIntPtr _rootWindow;
    private Action<CompanionHotkeyBinding>? _onPressed;
    private static int _lastXErrorCode;
    private static IntPtr _handlerDisplay;
    private static IntPtr _previousHandler;

    public async Task StartAsync(
        IReadOnlyList<CompanionHotkeyBinding> bindings,
        Action<CompanionHotkeyBinding> onPressed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(onPressed);
        cancellationToken.ThrowIfCancellationRequested();
        Task started;
        lock (_lifecycleGate)
        {
            if (_eventThread is not null)
            {
                throw new InvalidOperationException("The Linux X11 hotkey backend is already running.");
            }

            _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _stopRequested, 0);
            _onPressed = onPressed;
            started = _started.Task;
            _eventThread = new Thread(() => RunEventLoop(bindings))
            {
                IsBackground = true,
                Name = "Stalker Save Editor X11 hotkeys",
            };
            _eventThread.Start();
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
        lock (_lifecycleGate)
        {
            if (_eventThread is null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _stopRequested, 1);
            stopped = _stopped?.Task;
        }

        if (stopped is not null)
        {
            await stopped.ConfigureAwait(false);
        }

        lock (_lifecycleGate)
        {
            _eventThread = null;
            _started = null;
            _stopped = null;
        }
    }

    private void RunEventLoop(IReadOnlyList<CompanionHotkeyBinding> bindings)
    {
        try
        {
            // Xlib wants this before any other Xlib call in the process. The UI toolkit already makes that call at
            // start-up; repeating it is harmless and covers the headless hosts that have no toolkit.
            if (NativeMethods.XInitThreads() == 0)
            {
                throw new HotkeyRegistrationException("Xlib could not enable thread-safe access.", new InvalidOperationException());
            }

            _display = NativeMethods.XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
                var display = Environment.GetEnvironmentVariable("DISPLAY");
                var reason = HotkeyPlatformAvailability.GetLinuxX11UnavailableReason(sessionType, display);
                var message = reason ?? (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
                    ? "This Wayland session has no usable XWayland display; global hotkeys do not work here."
                    : "Could not open the configured X11 display.");
                throw new HotkeyRegistrationException(message, new InvalidOperationException());
            }

            _rootWindow = NativeMethods.XDefaultRootWindow(_display);
            RegisterBindings(bindings);
            _started?.TrySetResult();
            var registration = new FocusBoundRegistration(TrySetGrabbed, active: true);
            for (var tick = 0; Volatile.Read(ref _stopRequested) == 0; tick++)
            {
                // The keys are held only while the game has the focus (checked every ~300 ms).
                if (tick % 10 == 0) registration.Sync(GameHasFocus());

                DrainEvents();
                Thread.Sleep(30);
            }
        }
        catch (Exception exception)
        {
            _started?.TrySetException(exception is HotkeyRegistrationException
                ? exception
                : new HotkeyRegistrationException(exception.Message, exception));
        }
        finally
        {
            ReleaseDisplay();
            _stopped?.TrySetResult();
        }
    }

    private void RegisterBindings(IReadOnlyList<CompanionHotkeyBinding> bindings)
    {
        var byKey = new Dictionary<byte, List<(uint Modifiers, CompanionHotkeyBinding Binding)>>();
        foreach (var binding in bindings)
        {
            var keyName = char.ToLowerInvariant(binding.Gesture.Key).ToString();
            var symbol = ResolveKeySym(keyName);
            if (symbol == UIntPtr.Zero)
            {
                throw new HotkeyRegistrationException($"X11 does not recognize the key '{binding.Gesture.Key}'.", new InvalidOperationException());
            }

            var keyCode = NativeMethods.XKeysymToKeycode(_display, symbol);
            if (keyCode == 0)
            {
                throw new HotkeyRegistrationException($"X11 has no key code for '{binding.Gesture.Key}'.", new InvalidOperationException());
            }

            if (!byKey.TryGetValue(keyCode, out var keyBindings))
            {
                keyBindings = [];
                byKey.Add(keyCode, keyBindings);
            }

            keyBindings.Add((GetNativeModifiers(binding.Gesture.Modifiers), binding));
        }

        lock (XErrorHandlerGate)
        {
            _ = Interlocked.Exchange(ref _lastXErrorCode, 0);
            var previousHandler = InstallErrorHandler();
            try
            {
                foreach (var (keyCode, keyBindings) in byKey)
                {
                    foreach (var (modifiers, _) in keyBindings)
                    {
                        foreach (var lockModifiers in IgnoredLockModifiers)
                        {
                            var combined = modifiers | lockModifiers;
                            NativeMethods.XGrabKey(_display, keyCode, combined, _rootWindow, ownerEvents: 0, pointerMode: 1, keyboardMode: 1);
                            _grabbedKeys.Add((keyCode, combined));
                        }
                    }

                    _bindingsByKey.Add(keyCode, keyBindings);
                }

                _ = NativeMethods.XSync(_display, discard: 0);
            }
            finally
            {
                RestoreErrorHandler(previousHandler);
            }

            var error = Interlocked.Exchange(ref _lastXErrorCode, 0);
            if (error == BadAccess)
            {
                throw new HotkeyRegistrationException(
                    "An X11 global hotkey is already grabbed by another application.",
                    new InvalidOperationException("XGrabKey returned BadAccess."));
            }

            if (error != 0)
            {
                throw new HotkeyRegistrationException(
                    $"X11 rejected a global hotkey registration (error {error}).",
                    new InvalidOperationException("XGrabKey failed."));
            }
        }
    }

    /// <summary>False when X11 refused a grab: nothing stays half-grabbed and the caller retries later.</summary>
    private bool TrySetGrabbed(bool grab)
    {
        if (SetGrabbed(grab) || !grab) return true;
        _ = SetGrabbed(grab: false);
        return false;
    }

    private bool SetGrabbed(bool grab)
    {
        // Same guard as the first registration: without our handler Xlib's default one exits the process when another
        // application took the key while the editor had released it (BadAccess on re-grab).
        lock (XErrorHandlerGate)
        {
            _ = Interlocked.Exchange(ref _lastXErrorCode, 0);
            var previousHandler = InstallErrorHandler();
            try
            {
                foreach (var (keyCode, modifiers) in _grabbedKeys)
                {
                    if (grab) NativeMethods.XGrabKey(_display, keyCode, modifiers, _rootWindow, ownerEvents: 0, pointerMode: 1, keyboardMode: 1);
                    else NativeMethods.XUngrabKey(_display, keyCode, modifiers, _rootWindow);
                }

                _ = NativeMethods.XSync(_display, discard: 0);
            }
            finally
            {
                RestoreErrorHandler(previousHandler);
            }

            if (Interlocked.Exchange(ref _lastXErrorCode, 0) is var error and not 0)
            {
                AppLog.Warn($"X11 hotkey {(grab ? "re-grab" : "release")} failed (error {error}); another application may hold the key.");
                return false;
            }

            return true;
        }
    }

    /// <summary>The focused window or one of its parents has an X-Ray game's window class (Wine names it after the exe).</summary>
    private bool GameHasFocus()
    {
        _ = NativeMethods.XGetInputFocus(_display, out var window, out _);
        for (var depth = 0; depth < 8 && window.ToUInt64() > 1 && window != _rootWindow; depth++)
        {
            if (NativeMethods.XGetClassHint(_display, window, out var hint) != 0)
            {
                var name = Marshal.PtrToStringUTF8(hint.Name);
                var windowClass = Marshal.PtrToStringUTF8(hint.Class);
                if (hint.Name != IntPtr.Zero) _ = NativeMethods.XFree(hint.Name);
                if (hint.Class != IntPtr.Zero) _ = NativeMethods.XFree(hint.Class);
                if (GameWindowMatcher.IsGame(name) || GameWindowMatcher.IsGame(windowClass)) return true;
            }

            if (NativeMethods.XQueryTree(_display, window, out _, out var parent, out var children, out _) == 0) break;
            if (children != IntPtr.Zero) _ = NativeMethods.XFree(children);
            window = parent;
        }

        return false;
    }

    private void DrainEvents()
    {
        while (NativeMethods.XPending(_display) > 0)
        {
            _ = NativeMethods.XNextEvent(_display, out var nativeEvent);
            if (nativeEvent.Type != KeyPress || !_bindingsByKey.TryGetValue((byte)nativeEvent.Key.KeyCode, out var matches))
            {
                continue;
            }

            var state = nativeEvent.Key.State & ~(LockMask | Mod2Mask);
            foreach (var (modifiers, binding) in matches)
            {
                if (state == modifiers)
                {
                    _onPressed?.Invoke(binding);
                    break;
                }
            }
        }
    }

    private void ReleaseDisplay()
    {
        if (_display == IntPtr.Zero)
        {
            return;
        }

        foreach (var (keyCode, modifiers) in _grabbedKeys)
        {
            NativeMethods.XUngrabKey(_display, keyCode, modifiers, _rootWindow);
        }

        _ = NativeMethods.XSync(_display, discard: 0);
        _ = NativeMethods.XCloseDisplay(_display);
        _display = IntPtr.Zero;
        _rootWindow = UIntPtr.Zero;
        _grabbedKeys.Clear();
        _bindingsByKey.Clear();
    }

    private static uint GetNativeModifiers(HotkeyModifiers modifiers)
    {
        var result = 0u;
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            result |= ControlMask;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= Mod1Mask;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= ShiftMask;
        }

        return result;
    }

    private static UIntPtr ResolveKeySym(string keyName)
    {
        var utf8Name = Marshal.StringToCoTaskMemUTF8(keyName);
        try
        {
            return NativeMethods.XStringToKeysym(utf8Name);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8Name);
        }
    }

    /// <summary>
    /// The X error handler is one per process, and the UI toolkit has its own connection and its own handler. While
    /// ours is installed, only errors of this backend's connection are taken; anything else goes to the handler that
    /// was there before, so an unrelated toolkit error is neither swallowed nor mistaken for a refused grab.
    /// </summary>
    private static int CaptureXError(IntPtr display, ref NativeXErrorEvent errorEvent)
    {
        if (display == Volatile.Read(ref _handlerDisplay))
        {
            Interlocked.Exchange(ref _lastXErrorCode, errorEvent.ErrorCode);
            return 0;
        }

        var previous = Volatile.Read(ref _previousHandler);
        return previous == IntPtr.Zero
            ? 0
            : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previous)(display, ref errorEvent);
    }

    private IntPtr InstallErrorHandler()
    {
        Volatile.Write(ref _handlerDisplay, _display);
        var previous = NativeMethods.XSetErrorHandler(ErrorHandler);
        Volatile.Write(ref _previousHandler, previous);
        return previous;
    }

    private static void RestoreErrorHandler(IntPtr previous)
    {
        _ = NativeMethods.XSetErrorHandler(previous);
        Volatile.Write(ref _previousHandler, IntPtr.Zero);
        Volatile.Write(ref _handlerDisplay, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeClassHint
    {
        public IntPtr Name;
        public IntPtr Class;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, ref NativeXErrorEvent errorEvent);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeXErrorEvent
    {
        public int Type;
        public IntPtr Display;
        public UIntPtr ResourceId;
        public UIntPtr Serial;
        public byte ErrorCode;
        public byte RequestCode;
        public byte MinorCode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct NativeXEvent
    {
        [FieldOffset(0)]
        public int Type;

        [FieldOffset(0)]
        public NativeXKeyEvent Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeXKeyEvent
    {
        public int Type;
        public UIntPtr Serial;
        public int SendEvent;
        public IntPtr Display;
        public UIntPtr Window;
        public UIntPtr Root;
        public UIntPtr Subwindow;
        public UIntPtr Time;
        public int X;
        public int Y;
        public int XRoot;
        public int YRoot;
        public uint State;
        public uint KeyCode;
        public int SameScreen;
    }

    private static class NativeMethods
    {
        private const string X11 = "libX11.so.6";

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XInitThreads();

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr XOpenDisplay(IntPtr displayName);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern UIntPtr XDefaultRootWindow(IntPtr display);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern UIntPtr XStringToKeysym(IntPtr name);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte XKeysymToKeycode(IntPtr display, UIntPtr keySym);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void XGrabKey(IntPtr display, byte keyCode, uint modifiers, UIntPtr window, int ownerEvents, int pointerMode, int keyboardMode);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void XUngrabKey(IntPtr display, byte keyCode, uint modifiers, UIntPtr window);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XSync(IntPtr display, int discard);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XPending(IntPtr display);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XNextEvent(IntPtr display, out NativeXEvent nativeEvent);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XCloseDisplay(IntPtr display);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XGetInputFocus(IntPtr display, out UIntPtr focus, out int revertTo);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XGetClassHint(IntPtr display, UIntPtr window, out NativeClassHint hint);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XQueryTree(IntPtr display, UIntPtr window, out UIntPtr root, out UIntPtr parent, out IntPtr children, out uint childCount);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int XFree(IntPtr data);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSetErrorHandler")]
        internal static extern IntPtr XSetErrorHandler(XErrorHandler handler);

        [DllImport(X11, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSetErrorHandler")]
        internal static extern IntPtr XSetErrorHandler(IntPtr handler);
    }
}
