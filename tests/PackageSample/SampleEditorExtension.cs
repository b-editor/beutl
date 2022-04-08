using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

using BeUtl;
using BeUtl.Controls;
using BeUtl.Framework;

namespace PackageSample;

public class TextEditor : TextBox, IEditor, IStyleable
{
    public TextEditor(string file, SampleEditorExtension extension)
    {
        Extension = extension;
        EdittingFile = file;
        Text = File.ReadAllText(file);
    }

    public ViewExtension Extension { get; }

    public string EdittingFile { get; }

    Type IStyleable.StyleKey => typeof(TextBox);

    public void Close()
    {
    }
}

public sealed class SampleEditorExtension : EditorExtension
{
    public override Geometry? Icon { get; } = FluentIconsRegular.Add.GetGeometry();

    public override string[] FileExtensions { get; } =
    {
        "txt"
    };

    public override ResourceReference<string> FileTypeName => "S.SamplePackage.SampleEditorExtension.FileTypeName";

    public override string Name => "SampleEditorExtension";

    public override string DisplayName => "SampleEditorExtension";

    public override bool TryCreateEditor(string file, out IEditor? editor)
    {
        editor = null;
        if (file.EndsWith(".txt"))
        {
            editor = new TextEditor(file, this);
            return true;
        }
        else
        {
            return false;
        }
    }
}
