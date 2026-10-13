public static class VersionConfigurator
{
    public static DotNetBuildSettings SetVersions(this DotNetBuildSettings settings, string ver, string asmVer, string infoVer)
        => Versions(ver, asmVer, infoVer).Aggregate(settings, (s, p) => s.SetProperty(p.Key, p.Value));

    public static DotNetMSBuildSettings SetVersions(this DotNetMSBuildSettings settings, string ver, string asmVer, string infoVer)
        => Versions(ver, asmVer, infoVer).Aggregate(settings, (s, p) => s.SetProperty(p.Key, p.Value));

    public static DotNetPackSettings SetVersions(this DotNetPackSettings settings, string ver, string asmVer, string infoVer)
        => Versions(ver, asmVer, infoVer).Aggregate(settings, (s, p) => s.SetProperty(p.Key, p.Value));

    public static DotNetPublishSettings SetVersions(this DotNetPublishSettings settings, string ver, string asmVer, string infoVer)
        => Versions(ver, asmVer, infoVer).Aggregate(settings, (s, p) => s.SetProperty(p.Key, p.Value));

    private static (string Key, object Value)[] Versions(string ver, string asmVer, string infoVer) =>
    [
        ("AssemblyVersion", asmVer),
        ("InformationalVersion", infoVer),
        ("IncludeSourceRevisionInInformationalVersion", false),
        ("Version", ver),
    ];
}
