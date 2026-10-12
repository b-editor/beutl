namespace Beutl.UnitTests.TestInfrastructure;

public static class RepositoryPaths
{
    // The nearest directory above the test binaries that holds Beutl.slnx.
    public static string Root
    {
        get
        {
            for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate the Beutl repository root.");
        }
    }
}
