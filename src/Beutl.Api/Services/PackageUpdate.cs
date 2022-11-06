using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public sealed class PackageUpdate
{
    public PackageUpdate(Package package, Release? oldVersion, Release newVersion)
    {
        Package = package;
        OldVersion = oldVersion;
        NewVersion = newVersion;
    }

    public Package Package { get; }

    public Release? OldVersion { get; }

    public Release NewVersion { get; }
}
