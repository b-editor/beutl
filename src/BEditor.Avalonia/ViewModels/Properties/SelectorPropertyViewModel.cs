using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;

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
        public ReactiveCommand CopyID { get; }
    }

    public sealed class SelectorPropertyViewModel<T>
        : ISelectorPropertyViewModel, IDisposable
        where T : IJsonObject, IEquatable<T>
    {
        private readonly CompositeDisposable _disposables = new();

        public SelectorPropertyViewModel(SelectorProperty<T> property)
        {
            Property = property;

            Command.Where(i => i != Property.Index)
                .Subscribe(index =>
            {
                var item = Property.PropertyMetadata is null ? default : Property.PropertyMetadata.ItemSource.ElementAtOrDefault(index);

                Property.ChangeSelect(item).Execute();
            }).AddTo(_disposables);

            Reset.Where(_ => Property.Index != (Property.PropertyMetadata?.DefaultIndex ?? 0))
                .Subscribe(_ => Property.ChangeSelect(Property.PropertyMetadata!.DefaultIndex).Execute())
                .AddTo(_disposables);

            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<T>(Property!)
                };
                await window.ShowDialog(App.GetMainWindow());
            });

            CopyID.Subscribe(async () => await Application.Current.Clipboard.SetTextAsync(Property.Id.ToString())).AddTo(_disposables);
        }

        ~SelectorPropertyViewModel()
        {
            Dispose();
        }

        public IEnumerable<string> DisplayStrings => Property.PropertyMetadata?.DisplayStrings ?? Array.Empty<string>();

        public SelectorProperty<T> Property { get; }

        public ReactiveCommand<int> Command { get; } = new();

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public void Dispose()
        {
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            CopyID.Dispose();
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

            Command.Where(i => i != Property.Index)
                .Subscribe(index => Property.ChangeSelect(index).Execute())
                .AddTo(_disposables);

            Reset.Where(_ => Property.Index != (Property.PropertyMetadata?.DefaultIndex ?? 0))
                .Subscribe(_ => Property.ChangeSelect(Property.PropertyMetadata?.DefaultIndex ?? 0).Execute())
                .AddTo(_disposables);

            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<int>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            });

            CopyID.Subscribe(async () => await Application.Current.Clipboard.SetTextAsync(Property.Id.ToString())).AddTo(_disposables);
        }

        ~SelectorPropertyViewModel()
        {
            Dispose();
        }

        public IEnumerable<string> DisplayStrings => Property.PropertyMetadata?.ItemSource ?? Array.Empty<string>();

        public SelectorProperty Property { get; }

        public ReactiveCommand<int> Command { get; } = new();

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public void Dispose()
        {
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            CopyID.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}