using System;
using System.IO;
using Beutl.Configuration;

namespace Beutl.UnitTests.Configuration;

public class PreferencesDefaultTests
{
    [Test]
    public void Default_UsesBEUTL_HOME_AndPersists()
    {
        string dir = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "prefs-home");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, dir);

        // Ensure preferences file doesn't exist before
        string file = Path.Combine(dir, "preferences.json");
        if (File.Exists(file)) File.Delete(file);

        // First access triggers static initialization using BEUTL_HOME
        Preferences.Default.Set("k", 123);
        int v = Preferences.Default.Get("k", 0);
        Assert.That(v, Is.EqualTo(123));
        Assert.That(File.Exists(file), Is.True);
    }
}

