#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.Commands;

/// <summary>
/// An asynchronous command that does not take a parameter.
/// </summary>
public class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onException = null)
    : ICommand, INotifyPropertyChanged
{
    private readonly Func<Task> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool>? _canExecute = canExecute;
    private readonly Action<Exception>? _onException = onException;
    private bool _isExecuting;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (_isExecuting != value)
            {
                _isExecuting = value;
                OnPropertyChanged();
                RaiseCanExecuteChanged();
            }
        }
    }

    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanExecute(object? parameter) => !IsExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        IsExecuting = true;
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            WriteLog($"Error executing AsyncCommand: {ex.Message}", LogLevel.Error, ex);
            _onException?.Invoke(ex);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// An asynchronous command that takes a parameter of type T.
/// </summary>
public class AsyncCommand<T> : ICommand, INotifyPropertyChanged
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly Action<Exception>? _onException;
    private bool _isExecuting;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (_isExecuting != value)
            {
                _isExecuting = value;
                OnPropertyChanged();
                RaiseCanExecuteChanged();
            }
        }
    }

    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null, Action<Exception>? onException = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onException = onException;
    }

    public AsyncCommand(Func<T?, Task> execute, Func<bool> canExecute, Action<Exception>? onException = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = _ => canExecute();
        _onException = onException;
    }

    public bool CanExecute(object? parameter)
    {
        if (IsExecuting) return false;
        if (_canExecute == null) return true;

        if (parameter is T t) return _canExecute(t);
        if (parameter == null && default(T) == null) return _canExecute(default);

        return false;
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        IsExecuting = true;
        try
        {
            T? validParam = default;
            if (parameter is T t) validParam = t;
            else if (parameter == null && default(T) == null) validParam = default;

            await _execute(validParam);
        }
        catch (Exception ex)
        {
            WriteLog($"Error executing AsyncCommand<{typeof(T).Name}>: {ex.Message}", LogLevel.Error, ex);
            _onException?.Invoke(ex);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
