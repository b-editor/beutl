using System.Reflection;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Reactive.Bindings;

namespace Beutl.UnitTests.TestInfrastructure;

// Signs a user in by setting BeutlApiApplication's private state, so tests need no sign-in round trip.
public static class ApiTestAuthentication
{
    public static ProfileResponse CreateProfileResponse(string id = "test-user")
        => new()
        {
            Id = id,
            Name = "test",
            DisplayName = "Test User",
            Bio = null,
            IconId = null,
            IconUrl = null,
        };

    public static AuthenticatedUser CreateUser(
        BeutlApiApplication app,
        string id = "test-user",
        string token = "token")
        => new(
            new Profile(CreateProfileResponse(id), app),
            new AuthResponse
            {
                Token = token,
                RefreshToken = $"{token}-refresh",
                Expiration = DateTime.UtcNow.AddHours(1),
            },
            app,
            DateTime.UtcNow);

    public static AuthenticatedUser SignIn(
        BeutlApiApplication app,
        string id = "test-user",
        string token = "token")
        => SignIn(app, CreateUser(app, id, token));

    public static AuthenticatedUser SignIn(BeutlApiApplication app, AuthenticatedUser user)
    {
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(app)!).Value = user;
        return user;
    }
}
