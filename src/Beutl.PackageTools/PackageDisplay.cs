using Beutl.PackageTools.Properties;

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
}
