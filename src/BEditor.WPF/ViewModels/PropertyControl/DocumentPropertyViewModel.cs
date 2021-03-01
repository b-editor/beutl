using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.Views.PropertyControls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class DocumentPropertyViewModel : IDisposable
    {
        private string oldvalue;
        private readonly CompositeDisposable disposables = new();

        public DocumentPropertyViewModel(DocumentProperty property)
        {
            Property = property;
            oldvalue = Property.Value;

            Reset.Subscribe(() => Property.ChangeText(Property.PropertyMetadata?.DefaultText ?? "").Execute()).AddTo(disposables);
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<string>(Property));
                window.ShowDialog();
            }).AddTo(disposables);
            GotFocus.Subscribe(_ => oldvalue = Property.Value).AddTo(disposables);
            LostFocus.Subscribe(text =>
            {
                Property.Value = oldvalue;

                Property.ChangeText(text).Execute();
            }).AddTo(disposables);
            TextChanged.Subscribe(text =>
            {
                Property.Value = text;

                AppData.Current.Project!.PreviewUpdate(Property.GetParent2()!);
            }).AddTo(disposables);
        }
        ~DocumentPropertyViewModel()
        {
            Dispose();
        }

        public DocumentProperty Property { get; }
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public ReactiveCommand<string> GotFocus { get; } = new();
        public ReactiveCommand<string> LostFocus { get; } = new();
        public ReactiveCommand<string> TextChanged { get; } = new();

        public void Dispose()
        {
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
