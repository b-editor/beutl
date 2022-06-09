using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

using Google.Cloud.Firestore;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackagePageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public PackagePageViewModel(DocumentReference docRef)
    {
        Reference = docRef;
        IObservable<DocumentSnapshot> observable = Reference.ToObservable();

        observable.Select(d => d.GetValue<string>("name"))
            .Subscribe(s => Name.Value = s)
            .DisposeWith(_disposables);

        observable.Select(d => d.GetValue<string>("displayName"))
            .Subscribe(s => DisplayName.Value = s)
            .DisposeWith(_disposables);

        observable.Select(d => d.GetValue<string>("description"))
            .Subscribe(s => Description.Value = s)
            .DisposeWith(_disposables);

        Save.Subscribe(async () =>
        {
            await Reference.UpdateAsync(new Dictionary<string, object>
            {
                ["name"] = Name.Value,
                ["displayName"] = DisplayName.Value,
                ["description"] = Description.Value
            });
        });
    }

    public DocumentReference Reference { get; }

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public AsyncReactiveCommand Save { get; } = new();

    public void Dispose()
    {
        _disposables?.Dispose();
    }
}
