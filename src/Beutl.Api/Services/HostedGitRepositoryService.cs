using System.Net;
using System.Text.Json;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Language;
using Refit;

namespace Beutl.Api.Services;

public enum HostedGitError
{
    AuthenticationRequired,
    Unavailable,
    NotFound,
    RepositoryLimit,
    TokenLimit,
    AccountChanged,
    Conflict,
    RequestFailed,
}

public sealed class HostedGitApiException : Exception
{
    internal HostedGitApiException(HostedGitError error, string message, HttpStatusCode? statusCode = null)
        : base(message)
    {
        Error = error;
        StatusCode = statusCode;
    }

    public HostedGitError Error { get; }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class HostedGitRepositoryService : IBeutlApiResource
{
    private const int MaxNameLength = 80;
    private readonly BeutlApiApplication _app;

    internal HostedGitRepositoryService(BeutlApiApplication app)
    {
        _app = app;
    }

    public static bool IsValidRepositoryName(string? name)
        => name is { Length: > 0 and <= MaxNameLength }
            && !string.IsNullOrWhiteSpace(name)
            && !name.Any(static c => char.IsControl(c) || c is '/' or '\\');

    public static string CreateDesktopTokenName()
        => CreateDesktopTokenName(Environment.MachineName);

    internal static string CreateDesktopTokenName(string machineName)
    {
        const string prefix = "Beutl Desktop (";
        string machine = new string(machineName.Where(static c => !char.IsControl(c)).ToArray()).Trim();
        int maxMachineLength = MaxNameLength - prefix.Length - 1;
        if (machine.Length > maxMachineLength)
        {
            int length = char.IsHighSurrogate(machine[maxMachineLength - 1]) ? maxMachineLength - 1 : maxMachineLength;
            machine = machine[..length];
        }

        return machine.Length == 0 ? "Beutl Desktop" : $"{prefix}{machine})";
    }

    public async Task<IReadOnlyList<GitRepositorySummary>> ListAsync(
        CancellationToken cancellationToken = default, AuthenticatedUser? expectedUser = null)
        => (await SendAsync(_app.GitRepositories.List, cancellationToken, expectedUser, collection: true)
            .ConfigureAwait(false)).Repositories;

    public Task<GitRepositorySummary> CreateAsync(
        string name, Guid creationId, AuthenticatedUser expectedUser, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedUser);
        var request = new CreateGitRepositoryRequest(ValidateName(name), ValidateId(creationId), expectedUser.Profile.Id);
        return SendAsync((auth, token) => _app.GitRepositories.Create(auth, request, token),
            cancellationToken, expectedUser, collection: true);
    }

    public Task<GitRepositorySummary> GetAsync(
        Guid repositoryId, CancellationToken cancellationToken = default, AuthenticatedUser? expectedUser = null)
    {
        string id = ValidateId(repositoryId);
        return SendAsync((auth, token) => _app.GitRepositories.Get(auth, id, token), cancellationToken, expectedUser);
    }

    public Task<GitRepositorySummary> RenameAsync(Guid repositoryId, string name, CancellationToken cancellationToken = default)
    {
        string id = ValidateId(repositoryId);
        var request = new RenameGitRepositoryRequest(ValidateName(name));
        return SendAsync((auth, token) => _app.GitRepositories.Rename(auth, id, request, token), cancellationToken);
    }

    public Task DeleteAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        string id = ValidateId(repositoryId);
        return SendAsync(async (auth, token) =>
        {
            await _app.GitRepositories.Delete(auth, id, token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<GitAccessTokenSummary>> ListTokensAsync(
        Guid repositoryId, CancellationToken cancellationToken = default)
    {
        string id = ValidateId(repositoryId);
        return (await SendAsync((auth, token) => _app.GitRepositories.ListTokens(auth, id, token), cancellationToken)
            .ConfigureAwait(false)).Tokens;
    }

    public Task<CreatedGitAccessToken> CreateTokenAsync(
        Guid repositoryId, CreateGitAccessTokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string id = ValidateId(repositoryId);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxNameLength || request.Name.Any(char.IsControl))
            throw new ArgumentException(Strings.HostedGit_InvalidTokenName, nameof(request));
        if (request.Scope is not ("read" or "write"))
            throw new ArgumentException(Strings.HostedGit_InvalidTokenScope, nameof(request));

        return SendAsync((auth, token) => _app.GitRepositories.CreateToken(auth, id, request with { Name = request.Name.Trim() }, token),
            cancellationToken);
    }

    public Task RevokeTokenAsync(Guid repositoryId, Guid tokenId, CancellationToken cancellationToken = default)
        => RevokeTokenAsync(ValidateId(repositoryId), ValidateId(tokenId), cancellationToken);

    // Keep the cleanup on the issuing account, including after cancellation of the original operation.
    public async Task<T> UseDesktopTokenAsync<T>(
        Guid repositoryId, AuthenticatedUser expectedUser, Func<string, CancellationToken, Task<T>> setup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedUser);
        ArgumentNullException.ThrowIfNull(setup);
        string id = ValidateId(repositoryId);
        var request = new CreateGitAccessTokenRequest(CreateDesktopTokenName(), "write");
        CreatedGitAccessToken? issued = null;
        try
        {
            CreatedGitAccessToken result = await SendAsync(async (auth, token) =>
            {
                issued = await _app.GitRepositories.CreateToken(auth, id, request, token).ConfigureAwait(false);
                return issued;
            }, cancellationToken, expectedUser).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureExpectedUser(expectedUser);
            return await setup(result.Token, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (issued is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await RevokeTokenAsync(id, issued.Id, cleanup.Token, expectedUser).ConfigureAwait(false);
                }
                catch
                {
                    // Cleanup may be unavailable after sign-out or a connection failure.
                }
            }

            throw;
        }
    }

    private Task RevokeTokenAsync(
        string repositoryId, string tokenId, CancellationToken cancellationToken, AuthenticatedUser? expectedUser = null)
        => SendAsync(async (auth, token) =>
        {
            await _app.GitRepositories.RevokeToken(auth, repositoryId, tokenId, token).ConfigureAwait(false);
            return true;
        }, cancellationToken, expectedUser);

    private void EnsureExpectedUser(AuthenticatedUser? expectedUser)
    {
        if (expectedUser is not null && !ReferenceEquals(expectedUser, _app.AuthenticatedUser.Value))
            throw new HostedGitApiException(HostedGitError.AccountChanged, Strings.HostedGit_AccountChanged);
    }

    private async Task<T> SendAsync<T>(
        Func<string, CancellationToken, Task<T>> send, CancellationToken cancellationToken,
        AuthenticatedUser? expectedUser = null, bool collection = false)
    {
        EnsureExpectedUser(expectedUser);
        try
        {
            var result = await _app.SendAuthenticatedAsync(send, cancellationToken, expectedUser).ConfigureAwait(false);
            return result.Value;
        }
        catch (AuthenticationRequiredException)
        {
            EnsureExpectedUser(expectedUser);
            throw new HostedGitApiException(HostedGitError.AuthenticationRequired, Strings.HostedGit_AuthenticationRequired);
        }
        catch (ApiException ex)
        {
            throw MapError(ex, collection);
        }
        catch (HttpRequestException)
        {
            throw new HostedGitApiException(HostedGitError.Unavailable, Strings.HostedGit_Unavailable);
        }
    }

    private static HostedGitApiException MapError(ApiException exception, bool collection)
    {
        (HostedGitError error, string message) = exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized => (HostedGitError.AuthenticationRequired, Strings.HostedGit_AuthenticationRequired),
            HttpStatusCode.ServiceUnavailable => (HostedGitError.Unavailable, Strings.HostedGit_Unavailable),
            HttpStatusCode.NotFound when collection => (HostedGitError.Unavailable, Strings.HostedGit_Unavailable),
            HttpStatusCode.NotFound => (HostedGitError.NotFound, Strings.HostedGit_NotFound),
            HttpStatusCode.Conflict => ReadApiMessage(exception.Content) switch
            {
                "Repository limit reached" => (HostedGitError.RepositoryLimit, Strings.HostedGit_RepositoryLimit),
                "Access token limit reached" => (HostedGitError.TokenLimit, Strings.HostedGit_TokenLimit),
                "The authenticated account changed; retry with the original account" =>
                    (HostedGitError.AccountChanged, Strings.HostedGit_AccountChanged),
                _ => (HostedGitError.Conflict, Strings.HostedGit_Conflict),
            },
            _ => (HostedGitError.RequestFailed, Strings.HostedGit_RequestFailed),
        };
        return new HostedGitApiException(error, message, exception.StatusCode);
    }

    private static string? ReadApiMessage(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return null;
        try
        {
            using JsonDocument json = JsonDocument.Parse(content);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ValidateName(string name)
        => IsValidRepositoryName(name) ? name.Trim() : throw new ArgumentException(Strings.HostedGit_InvalidRepositoryName, nameof(name));

    private static string ValidateId(Guid id)
        => id != Guid.Empty ? id.ToString("D") : throw new ArgumentException(Strings.HostedGit_InvalidRepositoryId, nameof(id));
}
