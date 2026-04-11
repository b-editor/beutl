using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IOutputContext : IDisposable, IJsonSerializable
{
    OutputExtension Extension { get; }

    CoreObject Object { get; }

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

    public abstract bool TryCreateControl(IEditorContext editorContext, [NotNullWhen(true)] out Control? control);

    public abstract bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IOutputContext? context);

    public abstract bool IsSupported(Type type);
}
