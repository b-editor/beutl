using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Views.Properties;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public interface ISelectorPropertyViewModel
    {
        public IEnumerable<string> DisplayStrings { get; }
        public ReactiveCommand<int> Command { get; }
        public ReactiveCommand Reset { get; }
        public ReactiveCommand Bind { get; }
    }

    public sealed class SelectorPropertyViewModel<T>
        : ISelectorPropertyViewModel, IDisposable
        where T : IJsonObject, IEquatable<T>
    {
        private readonly CompositeDisposable _disposables = new();

        public SelectorPropertyViewModel(SelectorProperty<T> selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Command.Subscribe(index =>
            {
                var item = Property.PropertyMetadata is null ? default : Property.PropertyMetadata.ItemSource.ElementAtOrDefault(index);

                Property.ChangeSelect(item).Execute();
            }).AddTo(_disposables);
            Reset.Subscribe(() => Property.ChangeSelect(Property.PropertyMetadata!.DefaultIndex).Execute()).AddTo(_disposables);
            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<T>(Property!)
                };
                await window.ShowDialog(App.GetMainWindow());
            });
        }
        ~SelectorPropertyViewModel()
        {
            Dispose();
        }

        public IEnumerable<string> DisplayStrings => Metadata.Value?.DisplayStrings ?? Array.Empty<string>();
        public ReadOnlyReactivePropertySlim<SelectorPropertyMetadata<T>?> Metadata { get; }
        public SelectorProperty<T> Property { get; }

        public ReactiveCommand<int> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        public void Dispose()
        {
            Metadata.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
    public sealed class SelectorPropertyViewModel : ISelectorPropertyViewModel, IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public SelectorPropertyViewModel(SelectorProperty selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Command.Subscribe(index => Property.ChangeSelect(index).Execute()).AddTo(_disposables);
            Reset.Subscribe(() => Property.ChangeSelect(Property.PropertyMetadata?.DefaultIndex ?? 0).Execute()).AddTo(_disposables);
            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<int>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            });
        }
        ~SelectorPropertyViewModel()
        {
            Dispose();
        }

        public IEnumerable<string> DisplayStrings => Metadata.Value?.ItemSource ?? Array.Empty<string>();
        public ReadOnlyReactivePropertySlim<SelectorPropertyMetadata?> Metadata { get; }
        public SelectorProperty Property { get; }
        public ReactiveCommand<int> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        public void Dispose()
        {
            Metadata.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}