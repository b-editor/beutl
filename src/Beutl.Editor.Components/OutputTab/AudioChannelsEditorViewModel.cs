using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;
using Beutl.Language;
using Beutl.Media.Encoding;

namespace Beutl.Editor.Components.OutputTab;

internal sealed class AudioChannelsEditorViewModel(IPropertyAdapter<int> property, AudioEncoderSettings settings)
    : PropertyEditorViewModel<int, int>(property)
{
    private int[] _values = [];
    private WeakReference<EnumEditor>? _editor;
    private bool _refreshing;

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor editor)
        {
            _editor = new WeakReference<EnumEditor>(editor);
            UpdateItems(PropertyAdapter.GetValue());
        }
    }

    protected override int ToEdit(int value)
    {
        UpdateItems(value);
        return Array.IndexOf(_values, value);
    }

    protected override int FromEdit(int edit) => _values[edit];

    public override void ConfirmEdit(int oldValue, int newValue)
    {
        if (!IsDisposed && !_refreshing && newValue >= 0 && newValue < _values.Length)
            settings.SetChannels(_values[newValue]);
    }

    private void UpdateItems(int current)
    {
        int[] values = current is 1 or 2 or 6 or 8 ? [1, 2, 6, 8] : [1, 2, 6, 8, current];
        if (_editor == null || !_editor.TryGetTarget(out EnumEditor? editor)
            || (editor.Items != null && _values.SequenceEqual(values)))
            return;

        _refreshing = true;
        try
        {
            _values = values;
            editor.Items = values.Select(value => new EnumItem(Label(value), null, value)).ToArray();
            // Replacing an extra saved value clears the combo even when its index stays the same.
            editor.SelectedIndex = Array.IndexOf(values, current);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static string Label(int channels) => channels switch
    {
        1 => Strings.SpeakerLayout_Mono,
        2 => Strings.SpeakerLayout_Stereo,
        6 => Strings.SpeakerLayout_Surround51,
        8 => Strings.SpeakerLayout_Surround71,
        _ => channels.ToString(),
    };
}
