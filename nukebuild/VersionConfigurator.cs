public static class VersionConfigurator
{
    public static DotNetBuildSettings SetVersions(this DotNetBuildSettings settings, string ver, string asmVer, string infoVer)
    {
        return settings
            .SetProperty("AssemblyVersion", asmVer)
            .SetProperty("InformationalVersion", infoVer)
            .SetProperty("IncludeSourceRevisionInInformationalVersion", false)
            .SetProperty("Version", ver);
    }

    public static DotNetMSBuildSettings SetVersions(this DotNetMSBuildSettings settings, string ver, string asmVer, string infoVer)
    {
        return settings
            .SetProperty("AssemblyVersion", asmVer)
            .SetProperty("InformationalVersion", infoVer)
            .SetProperty("IncludeSourceRevisionInInformationalVersion", false)
            .SetProperty("Version", ver);
    }

    public static DotNetPackSettings SetVersions(this DotNetPackSettings settings, string ver, string asmVer, string infoVer)
    {
        return settings
            .SetProperty("AssemblyVersion", asmVer)
            .SetProperty("InformationalVersion", infoVer)
            .SetProperty("IncludeSourceRevisionInInformationalVersion", false)
            .SetProperty("Version", ver);
    }

    public static DotNetPublishSettings SetVersions(this DotNetPublishSettings settings, string ver, string asmVer, string infoVer)
    {
        return settings
            .SetProperty("AssemblyVersion", asmVer)
            .SetProperty("InformationalVersion", infoVer)
            .SetProperty("IncludeSourceRevisionInInformationalVersion", false)
            .SetProperty("Version", ver);
    }
}
