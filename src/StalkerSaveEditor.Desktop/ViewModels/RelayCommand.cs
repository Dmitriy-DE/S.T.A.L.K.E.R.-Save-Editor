using System.Windows.Input;

namespace StalkerSaveEditor.Desktop.ViewModels;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool>? _canExecute = canExecute;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

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
        return _canExecute?.Invoke(default) ?? true;
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
