using System;
using System.Windows.Input;

namespace Radio
{
    public class Command : ICommand
    {
        private Action<object> _action;
        private Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public Command(Action action, Func<object, bool> canExecute = null) : this(obj => action(), canExecute)
        {
        }

        public Command(Action<object> action, Func<object, bool> canExecute = null)
        {
            _action = action;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null ? true : _canExecute(parameter);
        }

        public void Execute(object parameter = null)
        {
            _action?.Invoke(parameter);
        }
    }
}