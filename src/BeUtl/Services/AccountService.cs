using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Configuration;
using BeUtl.Models;

using Firebase.Auth;
using Firebase.Auth.Providers;
using Firebase.Auth.Repository;
using Firebase.Auth.UI;
using Firebase.Storage;

using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;

using Grpc.Core;

namespace BeUtl.Services;

public sealed class AccountService
{
    internal readonly FirestoreDb _db;
    internal readonly FirebaseStorage _storage;

    public AccountService()
    {
        FirebaseUI.Initialize(new FirebaseUIConfig
        {
            ApiKey = Constants.FirebaseKey,
            AuthDomain = $"{Constants.FirebaseProjectId}.firebaseapp.com",
            Providers = new FirebaseAuthProvider[]
            {
                new GoogleProvider(),
                new EmailProvider(),
            },
            PrivacyPolicyUrl = "https://github.com/b-editor/BeUtl",
            TermsOfServiceUrl = "https://github.com/b-editor/BeUtl",
            IsAnonymousAllowed = false,
            AutoUpgradeAnonymousUsers = false,
            UserRepository = new FileUserRepository("beutl"),
            AnonymousUpgradeConflict = conflict => conflict.SignInWithPendingCredentialAsync(true)
        });

        _db = CreateFirestoreDbAuthentication();
        _storage = new FirebaseStorage(
            $"{Constants.FirebaseProjectId}.appspot.com",
            new FirebaseStorageOptions
            {
                AuthTokenAsyncFactory = () => FirebaseUI.Instance.Client.User.GetIdTokenAsync(),
            });

        GlobalConfiguration.Instance.ConfigurationChanged += async (_, e) => await PushSettings(e);

        GlobalConfiguration.Instance.BackupConfig
            .GetObservable(BackupConfig.BackupSettingsProperty)
            .CombineLatest(FirebaseUI.Instance.Client.GetUserObservable())
            .Where(t => t.Second != null && t.First)
            .Subscribe(async _ => await PullAllSettings());
    }

#pragma warning disable CA1822
    public User? User => FirebaseUI.Instance.Client.User;
#pragma warning restore CA1822

    public async ValueTask DeleteAccount(User user)
    {
        DocumentReference docRef = _db.Collection("users").Document($"{user.Uid}");
        await docRef.DeleteAsync();

        await _storage.Child($"users/{user.Uid}").DeleteAsync();

        await user.DeleteAsync();
    }

    public async ValueTask UploadProfileImage(User user, Stream stream)
    {
        string downloadLink = await _storage
            .Child("users")
            .Child(user.Uid)
            .Child("profile.jpg")
            .PutAsync(stream, default, mimeType: "image/jpeg");

        await user.ChangePhotoUrlAsync(downloadLink);
    }

    public async ValueTask PullSettings(params ConfigurationBase[] configurations)
    {
        async ValueTask PullSection(ConfigurationBase config, User user)
        {
            DocumentReference docRef = _db.Collection("users").Document($"{user.Uid}/settings/{GetPath(config)}");

            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();
            JsonNode? node = JsonSerializer.SerializeToNode(snapshot.ToDictionary());

            if (node != null && config != null)
            {
                config.ReadFromJson(node);
            }
        }

        if (User is User user)
        {
            foreach (ConfigurationBase item in configurations)
            {
                await PullSection(item, user);
            }
        }
    }

    public async ValueTask PullAllSettings()
    {
        if (User is User user)
        {
            CollectionReference collectionRef = _db.Collection("users").Document($"{user.Uid}").Collection("settings");

            foreach (DocumentSnapshot snapshot in (await collectionRef.GetSnapshotAsync()).Documents)
            {
                JsonNode? node = JsonSerializer.SerializeToNode(snapshot.ToDictionary());
                ConfigurationBase? config = GetConfig(snapshot.Id);

                if (node != null && config != null)
                {
                    config.ReadFromJson(node);
                }
            }
        }
    }

    public async ValueTask PushSettings(params ConfigurationBase[] configurations)
    {
        async ValueTask PushSection(ConfigurationBase config, User user)
        {
            DocumentReference docRef = _db.Collection("users").Document($"{user.Uid}/settings/{GetPath(config)}");

            JsonNode json = new JsonObject();
            config.WriteToJson(ref json);
            Dictionary<string, object> dictionary = json.ToDictionary();

            await docRef.SetAsync(dictionary);
        }

        if (User is User user && GlobalConfiguration.Instance.BackupConfig.BackupSettings)
        {
            foreach (ConfigurationBase config in configurations)
            {
                await PushSection(config, user);
            }
        }
    }

    private static ConfigurationBase? GetConfig(string path)
    {
        return path switch
        {
            "view" => GlobalConfiguration.Instance.ViewConfig,
            "graphics" => GlobalConfiguration.Instance.GraphicsConfig,
            "font" => GlobalConfiguration.Instance.FontConfig,
            "extension" => GlobalConfiguration.Instance.ExtensionConfig,
            _ => null,
        };
    }

    private static string GetPath(ConfigurationBase c)
    {
        return c switch
        {
            ViewConfig => "view",
            GraphicsConfig => "graphics",
            FontConfig => "font",
            ExtensionConfig => "extension",
            _ => throw new NotImplementedException()
        };
    }

    private static FirestoreDb CreateFirestoreDbAuthentication()
    {
        var callCredentials = CallCredentials.FromInterceptor(async (_, metadata) =>
        {
            User? user = FirebaseUI.Instance.Client.User;
            if (user != null)
            {
                string? token = await user.GetIdTokenAsync();

                if (string.IsNullOrEmpty(token)) return;

                metadata.Clear();
                metadata.Add("Authorization", $"Bearer {token}");
            }
        });
        var credentials = ChannelCredentials.Create(new SslCredentials(), callCredentials);

        // Create a custom Firestore Client using custom credentials
        var grpcChannel = new Channel("firestore.googleapis.com", credentials);
        var grcpClient = new Firestore.FirestoreClient((ChannelBase)grpcChannel);
        var firestoreClient = new FirestoreClientImpl(grcpClient, FirestoreSettings.GetDefault());

        return FirestoreDb.Create(Constants.FirebaseProjectId, firestoreClient);
    }
}
