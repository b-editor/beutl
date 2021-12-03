using Avalonia.Controls;

using BEditorNext.Framework;

namespace BEditorNext.Views;

public sealed partial class EditView : UserControl, IStorableControl
{
    public EditView()
    {
        InitializeComponent();
    }

    public string FileName { get; private set; } = Path.GetTempFileName();

    public DateTime LastSavedTime { get; }

    public void Restore(string filename)
    {
    }

    public void Save(string filename)
    {
    }
}
