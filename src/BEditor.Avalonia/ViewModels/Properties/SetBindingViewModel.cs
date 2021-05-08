using System;
using System.Reactive.Disposables;

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
            TargetID = bindable.ObserveProperty(b => b.TargetID).ToReadOnlyReactivePropertySlim().AddTo(_disposables);

            OKCommand.Subscribe(str =>
            {
                if (Guid.TryParse(str, out var id) && Bindable.GetBindable(id, out var ret))
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
        public ReadOnlyReactivePropertySlim<Guid?> TargetID { get; }
        public ReactiveCommand<string> OKCommand { get; } = new();
        public ReactiveCommand DisconnectCommand { get; } = new();

        public void Dispose()
        {
            TargetID.Dispose();
            OKCommand.Dispose();
            DisconnectCommand.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}