using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Bindings;
using BEditor.Core.Data;
using System.Windows.Input;
using BEditor.ViewModels.Helper;

namespace BEditor.ViewModels.PropertyControl
{
    public class BindSettingsViewModel<T> : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs usetwowatArgs = new(nameof(UseTwoWay));
        private static readonly PropertyChangedEventArgs bindpathArgs = new(nameof(BindPath));
        private bool useTwoWay;
        private string bindPath;


        public BindSettingsViewModel(IBindable<T> bindable)
        {
            Bindable = bindable;
            UseTwoWay = bindable.IsTwoWay();
            BindPath = bindable.BindHint;

            OKCommand = new DelegateCommand(() =>
            {
                if (Bindable.GetBindable(bindPath, out var ret))
                {
                    Core.Command.CommandManager.Do(new Bindings.BindCommand<T>(Bindable, ret, UseTwoWay));
                }
            });

            DisconnectCommand = new DelegateCommand(() =>
            {
                Core.Command.CommandManager.Do(new Bindings.BindCommand<T>(Bindable, null, UseTwoWay));
            });
        }


        public IBindable<T> Bindable { get; }
        public bool UseTwoWay
        {
            get => useTwoWay;
            set => SetValue(value, ref useTwoWay, usetwowatArgs);
        }
        public string BindPath
        {
            get => bindPath;
            set => SetValue(value, ref bindPath, bindpathArgs);
        }
        public ICommand OKCommand { get; }
        public ICommand DisconnectCommand { get; }
    }
}
