using System.Reactive.Disposables;

using Firebase.Auth;

using Google.Cloud.Firestore;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Services;

public class PackageController
{
    private readonly AccountService _accountService;

    public PackageController(AccountService accountService)
    {
        _accountService = accountService;
    }

    public IDisposable SubscribePackages(
        Action<DocumentSnapshot> added,
        Action<DocumentSnapshot> removed,
        Action<DocumentSnapshot> modified,
        object? lockObject = null)
    {
        lockObject ??= new();
        FirestoreDb db = _accountService._db;
        User user = _accountService.User!;

        Query query = db.Collection($"users/{user.Uid}/packages");
        bool initial = true;
        FirestoreChangeListener listener = query.Listen(snapshot =>
        {
            IEnumerable<DocumentChange> enumerable
                = initial ? snapshot.Changes.OrderBy(i => i.Document.Id) : snapshot.Changes;
            initial = false;
            foreach (DocumentChange item in enumerable)
            {
                lock (lockObject)
                {
                    if (item.ChangeType == DocumentChange.Type.Added)
                    {
                        added(item.Document);
                    }
                    else if (item.ChangeType == DocumentChange.Type.Removed)
                    {
                        removed(item.Document);
                    }
                    else if (item.ChangeType == DocumentChange.Type.Modified)
                    {
                        modified(item.Document);
                    }
                }
            }
        });

        _ = query.GetSnapshotAsync();

        return Disposable.Create(listener, async listener => await listener.StopAsync());
    }

    public async ValueTask<DocumentReference?> NewPackage()
    {
        FirestoreDb? db = _accountService._db;
        User? user = _accountService.User;
        if (user == null)
            return null;

        CollectionReference colRef = db.Collection($"users/{user.Uid}/packages");
        return await colRef.AddAsync(new
        {
            description = "Description",
            shortDescription = "Short Description",
            displayName = "New Package",
            name = "New Package",
            visible = false
        });
    }
}
