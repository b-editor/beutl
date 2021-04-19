using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

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
    public sealed class TextPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();
        private string oldvalue;

        public TextPropertyViewModel(TextProperty property)
        {
            Property = property;
            oldvalue = Property.Value;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(disposables);

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

                (AppData.Current.Project!).PreviewUpdate(Property.GetParent<ClipElement>()!);
            }).AddTo(disposables);
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
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}