using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using Beutl;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;

using Reactive.Bindings;

namespace PackageSample;

public sealed class TextEditorContext : IEditorContext
{
    private int _disposed;
    public TextEditorContext(
        CoreObject obj,
        SampleEditorExtension extension,
        IEditorContextCloseService closeService)
    {
        Extension = extension;
        Object = obj;
        CloseService = new BoundCloseService(this, closeService);
        Text.Value = File.ReadAllText(obj.Uri!.LocalPath);
        Commands = new CommandsImpl(this);
    }

    public IEditorContextCloseService CloseService { get; }

    public EditorExtension Extension { get; }

    public CoreObject Object { get; }

    public IKnownEditorCommands? Commands { get; }

    public ReactiveProperty<string> Text { get; } = new();

    public IReactiveProperty<bool> IsEnabled { get; } = new ReactiveProperty<bool>(true);

    public ValueTask CloseToolTabAsync(IToolContext item)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Text.Dispose();
            IsEnabled.Dispose();
        }
        return ValueTask.CompletedTask;
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

    public async ValueTask<bool> OpenToolTabAsync(IToolContext item)
    {
        await item.DisposeAsync();
        return false;
    }

    private sealed class CommandsImpl(TextEditorContext context) : IKnownEditorCommands
    {
        public async ValueTask<bool> OnSave()
        {
            try
            {
                await File.WriteAllTextAsync(context.Object.Uri!.LocalPath, context.Text.Value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class BoundCloseService(
        TextEditorContext owner,
        IEditorContextCloseService closeService) : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken => closeService.HostToken;

        public EditorContextCloseRequest RequestClose(IEditorContext context)
        {
            return ReferenceEquals(context, owner)
                ? closeService.RequestClose(owner)
                : new EditorContextCloseRequest(
                    EditorContextCloseRequestStatus.NotOwned,
                    Task.CompletedTask);
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
            Patterns = ["*.txt", "*.scene"]
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

    public override bool TryCreateContext(CoreObject obj, IEditorContextServices services, [NotNullWhen(true)] out IEditorContext? context)
    {
        context = null;
        if (obj is Scene)
        {
            context = new TextEditorContext(obj, this, services.CloseService);
            return true;
        }
        else
        {
            return false;
        }
    }

    public override bool TryCreateEditor(CoreObject obj, [NotNullWhen(true)] out Control? editor)
    {
        editor = null;
        if (obj is Scene)
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
