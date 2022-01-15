namespace BeUtl.Framework;

public record PackageInfo
{
    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public Guid Id { get; init; }

    public PackageLicense License { get; init; }

    public string Description { get; init; } = string.Empty;

    public IList<string> Tags { get; init; } = new List<string>();
}
