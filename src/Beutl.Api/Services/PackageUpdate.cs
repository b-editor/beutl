using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public sealed class PackageUpdate(Package package, Release? oldVersion, Release newVersion)
{
    public Package Package { get; } = package;

    public Release? OldVersion { get; } = oldVersion;

    public Release NewVersion { get; } = newVersion;
}
