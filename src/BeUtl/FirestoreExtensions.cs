using BeUtl.Reactive;

using Google.Cloud.Firestore;

namespace BeUtl;

public static class FirestoreExtensions
{
    public static IObservable<DocumentSnapshot> ToObservable(this DocumentReference docRef)
    {
        return new DocumentReferenceObservable(docRef);
    }

    private sealed class DocumentReferenceObservable : LightweightObservableBase<DocumentSnapshot>
    {
        private readonly DocumentReference _docRef;
        private FirestoreChangeListener? _listener;
        private DocumentSnapshot? _snapshot;

        public DocumentReferenceObservable(DocumentReference docRef)
        {
            _docRef = docRef;
        }

        protected override async void Subscribed(IObserver<DocumentSnapshot> observer, bool first)
        {
            DocumentSnapshot? snapshot = Volatile.Read(ref _snapshot);
            if (snapshot != null)
            {
                observer.OnNext(snapshot);
            }
            else
            {
                observer.OnNext(await _docRef.GetSnapshotAsync());
            }
        }

        protected override async void Deinitialize()
        {
            FirestoreChangeListener? listener = _listener;
            DocumentSnapshot? snapshot = _snapshot;

            Volatile.Write(ref _listener, null);
            Volatile.Write(ref _snapshot, null);
            if (listener != null && snapshot?.Exists != true)
            {
                await listener.StopAsync();
            }
        }

        protected override void Initialize()
        {
            Volatile.Write(ref _listener, _docRef.Listen(snapshot =>
            {
                Volatile.Write(ref _snapshot, snapshot);
                if (snapshot.Exists)
                    PublishNext(snapshot);
                else
                    PublishCompleted();
            }));
        }
    }
}
