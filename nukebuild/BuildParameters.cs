using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using Nuke.Common;
using Nuke.Common.IO;
using static Nuke.Common.IO.PathConstruction;

//partial class Build
//{
//    [Parameter(Name = "configuration")]
//    public string Configuration { get; set; }

//    [Parameter(Name = "skip-tests")]
//    public bool SkipTests { get; set; }

//    [Parameter(Name = "force-nuget-version")]
//    public string ForceNugetVersion { get; set; }

//    public class BuildParameters
//    {
//        public string Configuration { get; }
//        public bool SkipTests { get; }
//        public string MainRepo { get; }
//        public string MasterBranch { get; }
//        public string ReleaseConfiguration { get; }
//        public string MSBuildSolution { get; }
//        public bool IsLocalBuild { get; }
//        public bool IsRelease { get; }
//        public string Version { get; }
//        public AbsolutePath ArtifactsDir { get; }
//        public AbsolutePath NugetIntermediateRoot { get; }
//        public AbsolutePath NugetRoot { get; }
//        public AbsolutePath ZipRoot { get; }
//        public string DirSuffix { get; }
//        //public List<string> BuildDirs { get; }
//        public string FileZipSuffix { get; }
//        public AbsolutePath ZipCoreArtifacts { get; }
//        public AbsolutePath ZipNuGetArtifacts { get; }

//        public BuildParameters(Build b)
//        {
//            // ARGUMENTS
//            Configuration = b.Configuration ?? "Release";
//            SkipTests = b.SkipTests;

//            // CONFIGURATION
//            MainRepo = "https://github.com/AvaloniaUI/Avalonia";
//            MasterBranch = "refs/heads/master";
//            ReleaseConfiguration = "Release";
//            MSBuildSolution = RootDirectory / "Beutl.sln";

//            // PARAMETERS
//            IsLocalBuild = NukeBuild.IsLocalBuild;

//            IsRelease = StringComparer.OrdinalIgnoreCase.Equals(ReleaseConfiguration, Configuration);

//            // VERSION
//            Version = b.ForceNugetVersion ?? GetVersion();

//            // DIRECTORIES
//            ArtifactsDir = RootDirectory / "artifacts";
//            NugetRoot = ArtifactsDir / "nuget";
//            NugetIntermediateRoot = RootDirectory / "build-intermediate" / "nuget";
//            ZipRoot = ArtifactsDir / "zip";
//            //BuildDirs = GlobDirectories(RootDirectory, "**bin").Concat(GlobDirectories(RootDirectory, "**obj")).ToList();
//            DirSuffix = Configuration;
//            FileZipSuffix = Version + ".zip";
//            ZipCoreArtifacts = ZipRoot / ("Beutl-" + FileZipSuffix);
//            ZipNuGetArtifacts = ZipRoot / ("Beutl-NuGet-" + FileZipSuffix);
//        }

//        static string GetVersion()
//        {
//            var xdoc = XDocument.Load(RootDirectory / "Directory.Build.props");
//            return xdoc.Descendants().First(x => x.Name.LocalName == "Version").Value;
//        }
//    }
//}
