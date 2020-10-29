using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace BEditor_Electron.ViewModels.Helper {
    public class DelegateCommand<T> : ICommand {
        public event EventHandler CanExecuteChanged;

        private Action<T> action;

        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => action?.Invoke((T)parameter);

        public DelegateCommand() { }
        public DelegateCommand(Action<T> action) {
            this.action = action;
        }

        public void Subscribe(Action<T> action) {
            this.action = action;
        }
    }
    public class DelegateCommand : ICommand {
        public event EventHandler CanExecuteChanged;

        private Action action;

        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => action?.Invoke();

        public DelegateCommand() { }
        public DelegateCommand(Action action) {
            this.action = action;
        }

        public void Subscribe(Action action) {
            this.action = action;
        }
    }
}
