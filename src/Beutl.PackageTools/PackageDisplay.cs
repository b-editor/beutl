using Beutl.PackageTools.Properties;

using FluentTextTable;

using NuGet.Packaging;

namespace Beutl.PackageTools;

public static class PackageDisplay
{
    public static void Show(
        HashSet<(PackageIdentity, Release?)>? installs,
        HashSet<(PackageIdentity, Release?)>? updates,
        HashSet<(PackageIdentity, Release?)>? uninstalls)
    {
        if (installs != null)
        {
            Console.WriteLine($"\n{Resources.Installs}");
            foreach ((PackageIdentity package, Release? release) in installs)
            {
                if (release == null)
                    Console.WriteLine($"  {package} [{Resources.Local}]");
                else
                    Console.WriteLine($"  {package} [{Resources.Remote}]");
            }
        }

        if (updates != null)
        {
            Console.WriteLine($"\n{Resources.Updates}");
            foreach ((PackageIdentity package, Release? release) in updates)
            {
                if (release == null)
                    Console.WriteLine($"  {package} [{Resources.Local}]");
                else
                    Console.WriteLine($"  {package} [{Resources.Remote}]");
            }
        }

        if (uninstalls != null)
        {
            Console.WriteLine($"\n{Resources.Uninstalls}");
            foreach ((PackageIdentity package, Release? _) in uninstalls)
            {
                Console.WriteLine($"  {package}");
            }
        }
    }

    public static void ShowLicenses((PackageIdentity, LicenseMetadata)[] licenses)
    {
        static string LicenseToString(LicenseMetadata license)
        {
            if (license.Type == LicenseType.Expression)
            {
                return $"{license.LicenseExpression} ({license.LicenseUrl})";
            }
            else
            {
                return license.License;
            }
        }
        Console.WriteLine();

        var items = licenses
            .Select(x => (x.Item1.Id, x.Item1.Version.ToString(), LicenseToString(x.Item2)))
            .ToArray();
        
        Build
            .TextTable<(string PackageId, string Version, string License)>(builder =>
                builder.Columns.Add(x => x.PackageId).NameAs(Resources.PackageId)
                    .Columns.Add(x => x.Version).NameAs(Resources.Version)
                    .Columns.Add(x => x.License).NameAs(Resources.License))
            .WriteLine(items);
    }
}
