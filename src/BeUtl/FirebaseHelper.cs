
using Firebase.Auth;

namespace BeUtl;

public static class FirebaseHelper
{
    public static IObservable<User?> GetUserObservable(this IFirebaseAuthClient client)
    {
        return new AuthStateObservable(client);
    }

    private sealed class AuthStateObservable : IObservable<User?>
    {
        private readonly IFirebaseAuthClient _client;

        public AuthStateObservable(IFirebaseAuthClient client)
        {
            _client = client;
        }

        public IDisposable Subscribe(IObserver<User?> observer)
        {
            observer.OnNext(_client.User);

            void handler(object? s, UserEventArgs e)
            {
                try
                {
                    observer.OnNext(e.User);
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }
            _client.AuthStateChanged += handler;

            return Disposable.Create(() =>
            {
                _client.AuthStateChanged -= handler;
                observer.OnCompleted();
            });
        }
    }
}
