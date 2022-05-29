using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using BeUtl.Configuration;
using BeUtl.Models;

using Firebase.Auth;
using Firebase.Auth.Providers;
using Firebase.Auth.Repository;
using Firebase.Auth.UI;

using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;

using Grpc.Core;

namespace BeUtl.Services;

public class AccountService
{
    private readonly FirestoreDb _db;

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
        });

        _db = CreateFirestoreDbAuthentication();
        GlobalConfiguration.Instance.ConfigurationChanged += OnConfigurationChanged;
        FirebaseUI.Instance.Client.AuthStateChanged += Client_AuthStateChanged;
    }

    private void Client_AuthStateChanged(object? sender, UserEventArgs e)
    {
        FirebaseUI.Instance.Client.AuthStateChanged -= Client_AuthStateChanged;
        PullSettings();
    }

    public async void PullSettings()
    {
        if (GetUser() is User user)
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

    private async void OnConfigurationChanged(object? sender, ConfigurationBase e)
    {
        if (GetUser() is User user)
        {
            DocumentReference docRef = _db.Collection("users").Document($"{user.Uid}/settings/{GetPath(e)}");

            JsonNode json = new JsonObject();
            e.WriteToJson(ref json);
            Dictionary<string, object> dictionary = json.ToDictionary();

            await docRef.SetAsync(dictionary);
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

    private static User? GetUser()
    {
        return FirebaseUI.Instance.Client.User;
    }

    private static FirestoreDb CreateFirestoreDbAuthentication()
    {
        // Create a custom authentication mechanism for Email/Password authentication
        // If the authentication is successful, we will get back the current authentication token and the refresh token
        // The authentication expires every hour, so we need to use the obtained refresh token to obtain a new authentication token as the previous one expires
        //var authProvider = new FirebaseAuthProvider(new FirebaseConfig(firebaseApiKey));
        //var auth = authProvider.SignInWithEmailAndPasswordAsync(emailAddress, password).Result;
        var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
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
