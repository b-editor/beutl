using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Reactive;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

// Picks an encoder's codec from a searchable list, as the font of a text is picked. The codecs are many, so they are
// found by name rather than by scrolling. The codec changes only when the pick is confirmed: each change asks the
// FFmpeg worker for the formats and options of the new codec.
internal sealed class CodecEditorViewModel : IPropertyEditorContext
{
    private readonly IPropertyAdapter<CodecRecord> _property;
    private readonly CodecRecord[] _codecs;
    private readonly string _pinnedItemsKey;
    private readonly CompositeDisposable _disposables = [];
    private bool _pickerOpen;

    public CodecEditorViewModel(
        IPropertyAdapter<CodecRecord> property,
        PropertyEditorExtension extension,
        IReadOnlyList<object> choices,
        string pinnedItemsKey)
    {
        _property = property;
        Extension = extension;
        _codecs = [.. choices.OfType<CodecRecord>()];
        _pinnedItemsKey = pinnedItemsKey;
    }

    public PropertyEditorExtension Extension { get; }

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);

        if (visitor is DropDownPickerEditor editor)
        {
            editor.Header = _property.DisplayName;

            _property.GetObservable()
                .Subscribe(codec => editor.Text = codec?.LongName)
                .DisposeWith(_disposables);

            editor.AddDisposableHandler(DropDownPickerEditor.DropDownClickEvent, OnDropDownClick)
                .DisposeWith(_disposables);
        }
    }

    private void OnDropDownClick(object? sender, RoutedEventArgs e)
    {
        if (_pickerOpen || sender is not DropDownPickerEditor editor) return;

        var viewModel = new CodecPickerViewModel(_codecs, _pinnedItemsKey);
        CodecRecord? current = _property.GetValue();
        viewModel.SelectedItem.Value = viewModel.Items.FirstOrDefault(i => Equals(i.UserData, current));

        var flyout = new SearchablePickerFlyout(viewModel) { NoResultsText = Strings.CodecPicker_NoResults };
        // A search that matches nothing leaves no selection; keep the codec.
        flyout.Confirmed += (_, _) =>
        {
            if (viewModel.SelectedItem.Value?.UserData is CodecRecord codec && !codec.Equals(_property.GetValue()))
            {
                _property.SetValue(codec);
            }
        };
        flyout.Closed += (_, _) => _pickerOpen = false;
        _pickerOpen = true;
        flyout.ShowAt(editor);
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
