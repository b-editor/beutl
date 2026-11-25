namespace Beutl.Extensions.MediaFoundation.Tests;

[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeSetUp]
    public void SetUp()
    {
        if (OperatingSystem.IsWindows())
        {
            // The decoder lifecycle tests inspect tracked wrappers. Configure SharpGen before
            // any fixture creates a wrapper and freezes these settings.
            SharpGen.Runtime.Configuration.EnableObjectTracking = true;
            SharpGen.Runtime.Configuration.EnableReleaseOnFinalizer = true;
            SharpGen.Runtime.Configuration.UseThreadStaticObjectTracking = true;
        }
    }
}
