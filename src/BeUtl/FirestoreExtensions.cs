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
            if (_snapshot != null)
            {
                observer.OnNext(_snapshot);
            }
            else
            {
                observer.OnNext(await _docRef.GetSnapshotAsync());
            }
        }

        protected override async void Deinitialize()
        {
            if (_listener != null)
            {
                await _listener.StopAsync();
                _listener = null;
                _snapshot = null;
            }
        }

        protected override void Initialize()
        {
            _listener = _docRef.Listen(snapshot =>
            {
                _snapshot = snapshot;
                if (snapshot.Exists)
                    PublishNext(snapshot);
                else
                    PublishCompleted();
            });
        }
    }
}
