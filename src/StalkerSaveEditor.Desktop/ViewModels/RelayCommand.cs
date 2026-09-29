using System.Windows.Input;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Func<Task>? _executeAsync;
    private readonly Func<bool>? _canExecute;
    private bool _running;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// Async commands: the compiler picks this overload for <c>async () =&gt; …</c> lambdas, which would otherwise
    /// become <c>async void</c> (an exception after the first await would crash the app). The command owns the task,
    /// refuses a second run while one is in flight and logs a failure instead of rethrowing it.
    /// </summary>
    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _executeAsync = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _running;

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);

    public void Execute(object? parameter)
    {
        if (_execute is not null)
        {
            _execute();
            return;
        }

        if (_running) return;
        _ = RunAsync();
    }

    /// <summary>Runs the command and completes when it finishes (tests, callers that need to wait).</summary>
    public async Task RunAsync()
    {
        if (_executeAsync is null)
        {
            _execute!();
            return;
        }

        if (_running) return;
        _running = true;
        NotifyCanExecuteChanged();
        try
        {
            await _executeAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Core.Diagnostics.AppLog.Error("command failed", exception);
        }
        finally
        {
            _running = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CommandNotifier.Raise(this, CanExecuteChanged);
}

public sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    private readonly Action<T?> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<T?, bool>? _canExecute = canExecute;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (parameter is T typed) return _canExecute?.Invoke(typed) ?? true;
        if (parameter is null && default(T) is null) return _canExecute?.Invoke(default) ?? true;
        return false; // Execute would ignore a parameter of another type, so the button must not look enabled.
    }

    public void Execute(object? parameter)
    {
        if (parameter is T typed) _execute(typed);
        else if (parameter is null && default(T) is null) _execute(default);
    }

    public void NotifyCanExecuteChanged() => CommandNotifier.Raise(this, CanExecuteChanged);
}

/// <summary>
/// Buttons read their Command on CanExecuteChanged, which Avalonia allows only on the UI thread; a notification
/// raised from a worker thread is posted there instead of crashing the app.
/// </summary>
internal static class CommandNotifier
{
    public static void Raise(object sender, EventHandler? handler)
    {
        if (handler is null) return;
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess()) handler(sender, EventArgs.Empty);
        else dispatcher.Post(() => handler(sender, EventArgs.Empty));
    }
}
