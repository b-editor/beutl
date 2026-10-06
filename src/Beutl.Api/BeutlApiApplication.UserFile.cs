using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Activity = System.Diagnostics.Activity;

namespace Beutl.Api;

public partial class BeutlApiApplication
{
    private static string UserFilePath => Path.Combine(Helper.AppRoot, UserFileName);

    public void SaveUser()
    {
        if (_authenticatedUser.Value is { } user)
        {
            SaveUser(user);
        }
    }

    private void SaveUser(AuthenticatedUser user)
    {
        lock (_authenticationGate)
        {
            if (_disposed || !ReferenceEquals(_authenticatedUser.Value, user))
                return;

            _persistAuthenticatedUser(user);
        }
    }

    private static void PersistAuthenticatedUser(AuthenticatedUser user)
    {
        (AuthResponse response, DateTime _) = user.GetAuthenticationState();
        string fileName = UserFilePath;
        string directory = Path.GetDirectoryName(fileName)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fileName)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var obj = new JsonObject
            {
                ["token"] = response.Token,
                ["refresh_token"] = response.RefreshToken,
                ["expiration"] = response.Expiration,
                ["profile"] = JsonSerializer.SerializeToNode(user.Profile.Response.Value),
            };
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var stream = new FileStream(temporaryPath, streamOptions))
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    obj.WriteTo(writer);
                    writer.Flush();
                }
                stream.Flush(true);
            }
            File.Move(temporaryPath, fileName, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        user.SetWriteTime(File.GetLastWriteTimeUtc(fileName));
    }

    public async Task RestoreUserAsync(Activity? activity, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using (await Lock.LockAsync(token))
        {
            long authenticationAttempt = BeginAuthenticationAttempt();
            activity?.AddEvent(new("Entered_AsyncLock"));

            AuthenticatedUser? user = await ReadUserAsync(token);
            if (user != null)
            {
                CommitAuthenticatedUser(user, authenticationAttempt, token);
                try
                {
                    await user.RefreshAsync(token);
                    await user.Profile.RefreshAsync(token, self: true);
                    token.ThrowIfCancellationRequested();
                    SaveUser(user);
                }
                catch
                {
                    SignOutIfCurrent(user);
                    throw;
                }
            }
        }
    }

    public async ValueTask<AuthenticatedUser?> ReadUserAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        string fileName = UserFilePath;
        if (File.Exists(fileName))
        {
            JsonNode? node = JsonNode.Parse(await File.ReadAllTextAsync(fileName, token));
            token.ThrowIfCancellationRequested();
            DateTime lastWriteTime = File.GetLastWriteTimeUtc(fileName);

            if (node != null)
            {
                ProfileResponse? profile = JsonSerializer.Deserialize<ProfileResponse>(node["profile"]);
                string? persistedToken = (string?)node["token"];
                string? refreshToken = (string?)node["refresh_token"];
                var expiration = (DateTime?)node["expiration"];

                if (profile != null
                    && persistedToken != null
                    && refreshToken != null
                    && expiration.HasValue)
                {
                    return new AuthenticatedUser(
                        new Profile(profile, this),
                        new AuthResponse
                        {
                            Expiration = expiration.Value,
                            RefreshToken = refreshToken,
                            Token = persistedToken,
                        },
                        this,
                        lastWriteTime);
                }
            }
        }

        return null;
    }
}
