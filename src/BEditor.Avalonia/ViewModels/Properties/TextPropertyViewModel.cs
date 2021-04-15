using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.Views.Properties;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class TextPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();
        private string _oldvalue;

        public TextPropertyViewModel(TextProperty property)
        {
            Property = property;
            _oldvalue = Property.Value;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Reset.Subscribe(() => Property.ChangeText(Property.PropertyMetadata?.DefaultText ?? string.Empty).Execute()).AddTo(_disposables);
            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<string>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);
            GotFocus.Subscribe(_ => _oldvalue = Property.Value).AddTo(_disposables);
            LostFocus.Subscribe(text =>
            {
                Property.Value = _oldvalue;

                Property.ChangeText(text).Execute();
            }).AddTo(_disposables);
            TextChanged.Subscribe(text =>
            {
                Property.Value = text;

                AppModel.Current.Project!.PreviewUpdate(Property.GetParent2()!);
            }).AddTo(_disposables);
        }
        ~TextPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<TextPropertyMetadata?> Metadata { get; }
        public TextProperty Property { get; }
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public ReactiveCommand<string> GotFocus { get; } = new();
        public ReactiveCommand<string> LostFocus { get; } = new();
        public ReactiveCommand<string> TextChanged { get; } = new();

        public void Dispose()
        {
            Metadata.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            GotFocus.Dispose();
            LostFocus.Dispose();
            TextChanged.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}