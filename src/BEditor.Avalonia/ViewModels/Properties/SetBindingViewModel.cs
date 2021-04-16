using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

using BEditor.Data;
using BEditor.Data.Bindings;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class SetBindingViewModel<T> : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();


        public SetBindingViewModel(IBindable<T> bindable)
        {
            Bindable = bindable;
            BindPath = bindable.ObserveProperty(b => b.TargetHint).ToReadOnlyReactivePropertySlim().AddTo(_disposables);

            OKCommand.Subscribe(() =>
            {
                if (Bindable.GetBindable(BindPath.Value, out var ret))
                {
                    bindable.Bind<T>(ret).Execute();
                }
            }).AddTo(_disposables);

            DisconnectCommand.Subscribe(() => bindable.Disconnect().Execute()).AddTo(_disposables);
        }
        ~SetBindingViewModel()
        {
            Dispose();
        }


        public IBindable<T> Bindable { get; private set; }
        public ReadOnlyReactivePropertySlim<string?> BindPath { get; }
        public ReactiveCommand OKCommand { get; } = new();
        public ReactiveCommand DisconnectCommand { get; } = new();

        public void Dispose()
        {
            BindPath.Dispose();
            OKCommand.Dispose();
            DisconnectCommand.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}