using System.Windows.Input;

namespace Synthtax.Admin.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    { _executeAsync = executeAsync; _canExecute = canExecute; }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(() => { execute(); return Task.CompletedTask; }, canExecute) { }

    public bool CanExecute(object? p) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? p)
    {
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try { await _executeAsync(); }
        finally { _isExecuting = false; RaiseCanExecuteChanged(); }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class RelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _executeAsync;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Func<T?, Task> executeAsync, Func<bool>? canExecute = null)
    { _executeAsync = executeAsync; _canExecute = canExecute; }

    public bool CanExecute(object? p) => _canExecute?.Invoke() ?? true;
    public async void Execute(object? p) => await _executeAsync(p is T t ? t : default);
    public event EventHandler? CanExecuteChanged;
}
