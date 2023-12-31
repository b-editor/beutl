using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

using Beutl.Extensibility;

using FluentAvalonia.UI.Controls;

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

    public IReactiveProperty<bool> IsEnabled { get; } = new ReactiveProperty<bool>(true);

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

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public bool OpenToolTab(IToolContext item)
    {
        return false;
    }

    private sealed class CommandsImpl(TextEditorContext context) : IKnownEditorCommands
    {
        public async ValueTask<bool> OnSave()
        {
            try
            {
                await File.WriteAllTextAsync(context.EdittingFile, context.Text.Value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

public class TextEditor : TextBox
{
    public TextEditor()
    {
        this[!TextProperty] = new Binding("Text.Value", BindingMode.TwoWay);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);
}

[Export]
public sealed class SampleEditorExtension : EditorExtension
{
    public override string Name => "SampleEditorExtension";

    public override string DisplayName => "SampleEditorExtension";

    public override FilePickerFileType GetFilePickerFileType()
    {
        return new FilePickerFileType("Text File")
        {
            Patterns = new[] { "*.txt", "*.scene" }
        };
    }

    public override IconSource? GetIcon()
    {
        return new SymbolIconSource
        {
            Symbol = Symbol.Add
        };
    }

    public override bool MatchFileExtension(string ext)
    {
        return ext is ".txt" or ".scene";
    }

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

    public override bool TryCreateEditor(string file, [NotNullWhen(true)] out Control? editor)
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
