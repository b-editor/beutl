using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.Engine;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media.Source;

namespace Beutl.ViewModels.Editors;

internal static class FileSourceEditorObservables
{
    // Relinking reopens the same instance, so the outer property value does not change.
    public static IObservable<string?> ObserveLocalPath<T>(this IObservable<T?> values) where T : EngineObject, IFileSource
        => values.Select(source => Observable.Create<string?>(observer =>
        {
            string? GetPath() => source switch
            {
                MediaSource { HasUri: true } media => media.Uri.LocalPath,
                ModelSource { HasUri: true } model => model.Uri.LocalPath,
                _ => null
            };
            if (source == null)
            {
                observer.OnNext(null);
                return Disposable.Empty;
            }
            void Changed(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(IFileSource.Uri)) observer.OnNext(GetPath());
            }
            source.PropertyChanged += Changed;
            observer.OnNext(GetPath());
            return Disposable.Create(() => source.PropertyChanged -= Changed);
        })).Switch();
}
