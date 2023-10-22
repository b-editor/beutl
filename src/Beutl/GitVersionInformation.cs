#pragma warning disable CS0436

namespace Beutl;

// GitVersion.MsBuildとの互換性のため
internal static class GitVersionInformation
{
    public const string InformationalVersion = $"{ThisAssembly.BuildVersion3Components}{ThisAssembly.PrereleaseVersion}+{ThisAssembly.GitCommitId}";
    public const string NuGetVersion = ThisAssembly.NuGetPackageVersion;
    public const string NuGetVersionV2 = ThisAssembly.NuGetPackageVersion;
    public const string SemVer = ThisAssembly.BuildVersion3Components + ThisAssembly.PrereleaseVersion + ThisAssembly.SemVerBuildSuffix;
    public const string AssemblySemVer = ThisAssembly.AssemblyVersion;
    public const string FullBuildMetaData = ThisAssembly.GitCommitId;
}
