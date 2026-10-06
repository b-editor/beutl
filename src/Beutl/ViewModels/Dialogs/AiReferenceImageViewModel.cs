using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// One picture guiding a generation, shown with the preview the user picked it
/// by. Removing it is the item's own business so a list of them binds without
/// each row having to reach back through its parent.
/// </summary>
internal sealed class AiReferenceImageViewModel : IDisposable
{
    private readonly Action<AiReferenceImageViewModel> _remove;
    private bool _disposed;

    internal AiReferenceImageViewModel(
        string path,
        Ref<Bitmap> preview,
        Action<AiReferenceImageViewModel> remove)
    {
        Path = path;
        Preview = preview;
        _remove = remove;
        Remove = new ReactiveCommand();
        Remove.Subscribe(() => _remove(this));
    }

    public string Path { get; }

    public Ref<Bitmap> Preview { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public ReactiveCommand Remove { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Remove.Dispose();
        Preview.Dispose();
    }
}
