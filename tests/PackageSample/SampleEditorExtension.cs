using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;

using Beutl.Controls;
using Beutl.Framework;

using Reactive.Bindings;

namespace PackageSample;

public sealed class TextEditorContext : IEditorContext
{
    public TextEditorContext(string file, SampleEditorExtension extension)
    {
        Extension = extension;
        EdittingFile = file;
        Text.Value = File.ReadAllText(file);
        Commands = new CommandsImpl(this);
    }

    public EditorExtension Extension { get; }

    public string EdittingFile { get; }

    public IKnownEditorCommands? Commands { get; }

    public ReactiveProperty<string> Text { get; } = new();

    public void CloseToolTab(IToolContext item)
    {
    }

    public void Dispose()
    {
    }

    public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext
    {
        return default;
    }

    public T? FindToolTab<T>() where T : IToolContext
    {
        return default;
    }

    public bool OpenToolTab(IToolContext item)
    {
        return false;
    }

    private sealed class CommandsImpl : IKnownEditorCommands
    {
        private readonly TextEditorContext _context;

        public CommandsImpl(TextEditorContext context) => _context = context;

        public async ValueTask<bool> OnSave()
        {
            try
            {
                await File.WriteAllTextAsync(_context.EdittingFile, _context.Text.Value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

public class TextEditor : TextBox, IEditor, IStyleable
{
    public TextEditor()
    {
        this[!TextProperty] = new Binding("Text.Value", BindingMode.TwoWay);
    }

    Type IStyleable.StyleKey => typeof(TextBox);

    public void Close()
    {
    }
}

[Export]
public sealed class SampleEditorExtension : EditorExtension
{
    public override Geometry? Icon => FluentIconsRegular.Add.GetGeometry();

    public override string[] FileExtensions { get; } =
    {
        "txt",
        "scene",
    };

    public override IObservable<string> FileTypeName => Observable.Return("Text File");

    public override string Name => "SampleEditorExtension";

    public override string DisplayName => "SampleEditorExtension";

    public override bool TryCreateContext(string file, [NotNullWhen(true)] out IEditorContext? context)
    {
        context = null;
        if (file.EndsWith(".txt") || file.EndsWith(".scene"))
        {
            context = new TextEditorContext(file, this);
            return true;
        }
        else
        {
            return false;
        }
    }

    public override bool TryCreateEditor(string file, [NotNullWhen(true)] out IEditor? editor)
    {
        editor = null;
        if (file.EndsWith(".txt") || file.EndsWith(".scene"))
        {
            editor = new TextEditor();
            return true;
        }
        else
        {
            return false;
        }
    }
}
