using System.Windows.Input;

namespace Syncrowatch.App.Wizard;

public sealed class AsyncCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null,
    Action<Exception>? onError = null) : ICommand
{
    private readonly Func<Task> _execute = execute;
    private readonly Func<bool> _canExecute = canExecute ?? (() => true);
    private readonly Action<Exception>? _onError = onError;
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && _canExecute();

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _onError?.Invoke(exception);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }
}
