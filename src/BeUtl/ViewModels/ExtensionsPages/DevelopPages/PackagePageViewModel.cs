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
            .Subscribe(s => Name.Value = ActualName.Value = s)
            .DisposeWith(_disposables);

        observable.Select(d => d.GetValue<string>("displayName"))
            .Subscribe(s => DisplayName.Value = ActualDisplayName.Value = s)
            .DisposeWith(_disposables);

        observable.Select(d => d.GetValue<string>("description"))
            .Subscribe(s => Description.Value = ActualDescription.Value = s)
            .DisposeWith(_disposables);

        Save.Subscribe(async () =>
        {
            await Reference.UpdateAsync(new Dictionary<string, object>
            {
                ["name"] = Name.Value,
                ["displayName"] = DisplayName.Value,
                ["description"] = Description.Value
            });
        }).DisposeWith(_disposables);

        DiscardChanges.Subscribe(async () =>
        {
            DocumentSnapshot snapshot = await Reference.GetSnapshotAsync();
            Name.Value = snapshot.GetValue<string>("name");
            DisplayName.Value = snapshot.GetValue<string>("displayName");
            Description.Value = snapshot.GetValue<string>("description");
        }).DisposeWith(_disposables);

        Delete.Subscribe(async () => await docRef.DeleteAsync()).DisposeWith(_disposables);
    }

    public DocumentReference Reference { get; }

    public ReactivePropertySlim<string> ActualName { get; } = new();

    public ReactivePropertySlim<string> ActualDisplayName { get; } = new();

    public ReactivePropertySlim<string> ActualDescription { get; } = new();

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public AsyncReactiveCommand Save { get; } = new();

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public void Dispose()
    {
        _disposables?.Dispose();
    }
}
