using System;
using System.Reactive.Disposables;

using BEditor.Data;
using BEditor.Data.Bindings;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class BindSettingsViewModel<T> : IDisposable
    {
        private readonly CompositeDisposable disposables = new();


        public BindSettingsViewModel(IBindable<T> bindable)
        {
            Bindable = bindable;
            TargetID = bindable.ObserveProperty(b => b.TargetID).ToReadOnlyReactivePropertySlim().AddTo(disposables);

            OKCommand.Subscribe(() =>
            {
                if (Bindable.GetBindable(TargetID.Value, out var ret))
                {
                    bindable.Bind<T>(ret).Execute();
                }
            }).AddTo(disposables);

            DisconnectCommand.Subscribe(() => bindable.Disconnect().Execute()).AddTo(disposables);
        }
        ~BindSettingsViewModel()
        {
            Dispose();
        }


        public IBindable<T> Bindable { get; private set; }
        public ReadOnlyReactivePropertySlim<Guid?> TargetID { get; }
        public ReactiveCommand OKCommand { get; } = new();
        public ReactiveCommand DisconnectCommand { get; } = new();

        public void Dispose()
        {
            TargetID.Dispose();
            OKCommand.Dispose();
            DisconnectCommand.Dispose();
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}