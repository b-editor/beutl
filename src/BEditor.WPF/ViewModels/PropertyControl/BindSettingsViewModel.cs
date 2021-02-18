using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

using BEditor.Data;
using BEditor.Data.Bindings;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class BindSettingsViewModel<T> : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs bindpathArgs = new(nameof(BindPath));
        private string? bindPath;


        public BindSettingsViewModel(IBindable<T> bindable)
        {
            Bindable = bindable;
            BindPath = bindable.BindHint;

            OKCommand.Subscribe(() =>
            {
                if (Bindable.GetBindable(bindPath, out var ret))
                {
                    bindable.Bind<T>(ret).Execute();
                }
            });

            DisconnectCommand.Subscribe(() => bindable.Disconnect().Execute());
        }


        public IBindable<T> Bindable { get; }
        public string? BindPath
        {
            get => bindPath;
            set => SetValue(value, ref bindPath, bindpathArgs);
        }
        public ReactiveCommand OKCommand { get; } = new();
        public ReactiveCommand DisconnectCommand { get; } = new();
    }
}
