using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;

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

            Reset.Where(_ => Property.Value != (Property.PropertyMetadata?.DefaultText ?? string.Empty))
                .Subscribe(_ => Property.ChangeText(Property.PropertyMetadata?.DefaultText ?? string.Empty).Execute())
                .AddTo(_disposables);

            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<string>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);

            CopyID.Subscribe(async () => await Application.Current.Clipboard.SetTextAsync(Property.Id.ToString())).AddTo(_disposables);

            GotFocus.Subscribe(_ => _oldvalue = Property.Value).AddTo(_disposables);

            LostFocus.Where(i => i != Property.Value)
                .Subscribe(text =>
            {
                Property.Value = _oldvalue;

                Property.ChangeText(text).Execute();
            }).AddTo(_disposables);

            TextChanged.Subscribe(async text =>
            {
                Property.Value = text;

                await (AppModel.Current.Project!).PreviewUpdateAsync(Property.GetParent<ClipElement>()!);
            }).AddTo(_disposables);
        }

        ~TextPropertyViewModel()
        {
            Dispose();
        }

        public TextProperty Property { get; }

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public ReactiveCommand<string> GotFocus { get; } = new();

        public ReactiveCommand<string> LostFocus { get; } = new();

        public ReactiveCommand<string> TextChanged { get; } = new();

        public void Dispose()
        {
            Reset.Dispose();
            Bind.Dispose();
            CopyID.Dispose();
            GotFocus.Dispose();
            LostFocus.Dispose();
            TextChanged.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}