using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings;

namespace Beutl.Framework;

public interface IOutputContext : IDisposable
{
    OutputExtension Extension { get; }

    string TargetFile { get; }

    IReadOnlyReactiveProperty<bool> IsIndeterminate { get; }
    
    IReadOnlyReactiveProperty<bool> IsEncoding { get; }

    IReadOnlyReactiveProperty<double> Progress { get; }

    event EventHandler Started;

    event EventHandler Finished;
}

public abstract class OutputExtension : Extension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    public abstract IconSource? GetIcon();

    public abstract bool TryCreateControl(string file, [NotNullWhen(true)] out IControl? control);
    
    public abstract bool TryCreateContext(string file, [NotNullWhen(true)] out IOutputContext? context);

    public virtual bool IsSupported(string file)
    {
        return MatchFileExtension(Path.GetExtension(file));
    }

    // extはピリオドを含む
    public abstract bool MatchFileExtension(string ext);
}
