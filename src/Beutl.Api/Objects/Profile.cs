using System.Collections.Specialized;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Text;

using Nito.AsyncEx;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Profile
{
    private readonly BeutlApiApplication _clients;
    private readonly ReactivePropertySlim<ProfileResponse> _response;

    public Profile(ProfileResponse response, BeutlApiApplication clients)
    {
        _clients = clients;
        _response = new(response);

        Id = response.Id;
        Name = response.Name;
        Biography = Response.Select(x => x.Bio).ToReadOnlyReactivePropertySlim()!;
        DisplayName = Response.Select(x => x.Display_name).ToReadOnlyReactivePropertySlim()!;
        Email = Response.Select(x => x.Email).ToReadOnlyReactivePropertySlim()!;
        TwitterUserName = Response.Select(x => x.Twitter_username).ToReadOnlyReactivePropertySlim()!;
        GitHubUserName = Response.Select(x => x.Github_username).ToReadOnlyReactivePropertySlim()!;
        YouTubeUrl = Response.Select(x => x.Youtube_url).ToReadOnlyReactivePropertySlim()!;
        BlogUrl = Response.Select(x => x.Blog_url).ToReadOnlyReactivePropertySlim()!;
        AvatarUrl = Response.Select(x => x.Avatar_url).ToReadOnlyReactivePropertySlim()!;
        PublicPackages = Response.Select(x => x.Public_packages).ToReadOnlyReactivePropertySlim()!;
    }

    public IReadOnlyReactiveProperty<ProfileResponse> Response => _response;

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyReactiveProperty<string> Biography { get; }

    public IReadOnlyReactiveProperty<string> DisplayName { get; }

    public IReadOnlyReactiveProperty<string> Email { get; }

    public IReadOnlyReactiveProperty<string> TwitterUserName { get; }

    public IReadOnlyReactiveProperty<string> GitHubUserName { get; }

    public IReadOnlyReactiveProperty<string> YouTubeUrl { get; }

    public IReadOnlyReactiveProperty<string> BlogUrl { get; }

    public IReadOnlyReactiveProperty<string?> AvatarUrl { get; }

    public IReadOnlyReactiveProperty<int> PublicPackages { get; }

    public MyAsyncLock Lock => _clients.Lock;

    public async Task RefreshAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.Refresh", ActivityKind.Client);

        _response.Value = await _clients.Users.GetUserAsync(Name);
    }

    public async Task UpdateAsync(UpdateProfileRequest request)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.Update", ActivityKind.Client);

        _response.Value = await _clients.Users.PatchAsync(Name, request);
    }

    public Task UpdateAsync(
        long? avatarId = null,
        string? bio = null,
        string? blogUrl = null,
        string? displayName = null,
        string? githubUsername = null,
        string? twitterUsername = null,
        string? youtubeUrl = null)
    {
        return UpdateAsync(new UpdateProfileRequest(
            avatar_id: avatarId,
            bio: bio,
            blog_url: blogUrl,
            display_name: displayName,
            github_username: githubUsername,
            twitter_username: twitterUsername,
            youtube_url: youtubeUrl));
    }

    public async Task<Package> AddPackageAsync(string name, CreatePackageRequest request)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.AddPackage", ActivityKind.Client);

        PackageResponse response = await _clients.Packages.PostAsync(name, request);
        return new Package(this, response, _clients);
    }

    public Task<Package> AddPackageAsync(string name)
    {
        return AddPackageAsync(name, new CreatePackageRequest(
            description: "",
            display_name: "",
            short_description: "",
            tags: Array.Empty<string>(),
            website: ""));
    }

    public async Task<Package[]> GetPackagesAsync(int start = 0, int count = 30)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.GetPackages", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        return await (await _clients.Users.GetPackagesAsync(Name, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await _clients.Packages.GetPackageAsync(x.Name))
            .Select(x => new Package(this, x, _clients))
            .ToArrayAsync();
    }

    public async Task<Asset> AddAssetAsync(string name, CreateVirtualAssetRequest request)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.AddAsset", ActivityKind.Client);
        activity?.SetTag("virtual", true);
        AssetMetadataResponse response = await _clients.Assets.PostAsync(Name, name, request);
        return new Asset(this, response, _clients);
    }

    public async Task<Asset> AddAssetAsync(string name, FileStream stream, string contentType)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.AddAsset", ActivityKind.Client);
        activity?.SetTag("virtual", false);

        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        // 手動でエンコードを行う。
        string headerValue = string.Format("form-data; name=\"{0}\"; filename=\"{1}\"", name, stream.Name);
        byte[] headerValueByteArray = Encoding.UTF8.GetBytes(headerValue);
        var encodingHeaderValue = new StringBuilder();
        foreach (byte b in headerValueByteArray)
        {
            encodingHeaderValue.Append((char)b);
        }

        streamContent.Headers.Add("Content-Disposition", encodingHeaderValue.ToString());

        var multiPartContent = new MultipartFormDataContent
        {
            streamContent
        };

        AssetMetadataResponse response = await _clients.Assets.PostAsync(Name, name, multiPartContent);

        return new Asset(this, response, _clients);
    }

    public async Task<Asset[]> GetAssetsAsync(int start = 0, int count = 30)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.GetAssets", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        return await (await _clients.Users.GetAssetsAsync(Name, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await _clients.Assets.GetAssetAsync(Name, x.Name))
            .Select(x => new Asset(this, x, _clients))
            .ToArrayAsync();
    }

    public async Task<Asset> GetAssetAsync(long id)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.GetAsset", ActivityKind.Client);
        AssetMetadataResponse response = await _clients.Assets.GetAsset2Async(id);
        return new Asset(this, response, _clients);
    }

    public async Task<Asset> GetAssetAsync(string name)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.GetAsset", ActivityKind.Client);
        AssetMetadataResponse response = await _clients.Assets.GetAssetAsync(Name, name);
        return new Asset(this, response, _clients);
    }
}
