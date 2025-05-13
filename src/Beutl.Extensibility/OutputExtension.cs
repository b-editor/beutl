using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IOutputContext : IDisposable, IJsonSerializable
{
    OutputExtension Extension { get; }

    string TargetFile { get; }

    IReactiveProperty<string> Name { get; }

    IReadOnlyReactiveProperty<bool> IsIndeterminate { get; }

    IReadOnlyReactiveProperty<bool> IsEncoding { get; }

    IReadOnlyReactiveProperty<double> Progress { get; }

    event EventHandler? Started;

    event EventHandler? Finished;
}

public interface ISupportOutputPreset
{
    void Apply(JsonObject preset);

    JsonObject ToPreset();
}

public abstract class OutputExtension : Extension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    public abstract IconSource? GetIcon();

    public abstract bool TryCreateControl(IEditorContext editorContext, [NotNullWhen(true)] out Control? control);

    public abstract bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IOutputContext? context);

    public virtual bool IsSupported(string file)
    {
        return MatchFileExtension(Path.GetExtension(file));
    }

    // extはピリオドを含む
    public abstract bool MatchFileExtension(string ext);
}
