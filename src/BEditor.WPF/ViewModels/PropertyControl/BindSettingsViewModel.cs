using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Bindings;
using BEditor.Core.Data;
using System.Windows.Input;
using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class BindSettingsViewModel<T> : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs bindpathArgs = new(nameof(BindPath));
        private string bindPath;


        public BindSettingsViewModel(IBindable<T> bindable)
        {
            Bindable = bindable;
            BindPath = bindable.BindHint;

            OKCommand.Subscribe(() =>
            {
                if (Bindable.GetBindable(bindPath, out var ret))
                {
                    Core.Command.CommandManager.Do(new Bindings.BindCommand<T>(Bindable, ret));
                }
            });

            DisconnectCommand.Subscribe(() => Core.Command.CommandManager.Do(new Bindings.Disconnect<T>(Bindable)));
        }


        public IBindable<T> Bindable { get; }
        public string BindPath
        {
            get => bindPath;
            set => SetValue(value, ref bindPath, bindpathArgs);
        }
        public ReactiveCommand OKCommand { get; } = new();
        public ReactiveCommand DisconnectCommand { get; } = new();
    }
}
