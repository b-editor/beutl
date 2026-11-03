using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Beutl.Api.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public class PluginLoadContextTests
{
    private readonly List<PluginLoadContext> _contexts = [];
    private readonly List<string> _directories = [];
    private string _dependency = null!;

    [SetUp]
    public void SetUp()
    {
        // The name is new in every test: an assembly that reaches the default context stays there.
        _dependency = "Beutl.PluginLoadContextTest." + Guid.NewGuid().ToString("N");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (PluginLoadContext context in _contexts) context.Unload();
        _contexts.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        foreach (string directory in _directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Windows keeps a loaded assembly's file open, and one in the default context until the process exits.
                TestContext.Out.WriteLine(ex.Message);
            }
        }

        _directories.Clear();
    }

    [Test]
    public void A_dependency_asked_for_through_the_default_context_is_found_and_the_plugin_shares_it()
    {
        Assembly plugin = LoadPlugin();

        Assembly loaded = Invoke(plugin, "AskDefaultContext");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AssemblyLoadContext.GetLoadContext(loaded), Is.SameAs(AssemblyLoadContext.Default));
            Assert.That(Invoke(plugin, "ReferencedDependency"), Is.SameAs(loaded));
        }
    }

    [Test]
    public void The_default_context_gets_a_version_as_new_as_the_one_asked_for()
    {
        CreateContext("[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]");
        CreateContext("[assembly: System.Reflection.AssemblyVersion(\"2.0.0.0\")]");

        Assembly loaded = AssemblyLoadContext.Default.LoadFromAssemblyName(
            new AssemblyName(_dependency) { Version = new Version(2, 0, 0, 0) });

        Assert.That(loaded.GetName().Version, Is.EqualTo(new Version(2, 0, 0, 0)));
    }

    [Test]
    public void An_assembly_that_a_plugin_has_loaded_is_not_loaded_again_in_the_default_context()
    {
        Assembly plugin = LoadPlugin();
        Assembly referenced = Invoke(plugin, "ReferencedDependency");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AssemblyLoadContext.GetLoadContext(referenced), Is.InstanceOf<PluginLoadContext>());
            Assert.That(() => Invoke(plugin, "AskDefaultContext"),
                Throws.TargetInvocationException.With.InnerException.TypeOf<FileNotFoundException>());
            Assert.That(AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.GetName().Name), Does.Not.Contain(_dependency));
        }
    }

    [Test]
    public void An_assembly_that_no_plugin_has_is_still_reported_as_missing()
    {
        LoadPlugin();

        Assert.That(
            () => AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(_dependency + ".Unknown")),
            Throws.TypeOf<FileNotFoundException>());
    }

    // The default context holds one copy of a name and cannot replace it.
    [Test]
    public void A_newer_version_than_the_default_context_holds_is_still_reported_as_missing()
    {
        CreateContext("[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]");
        AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(_dependency));
        CreateContext("[assembly: System.Reflection.AssemblyVersion(\"2.0.0.0\")]");

        Assert.That(
            () => AssemblyLoadContext.Default.LoadFromAssemblyName(
                new AssemblyName(_dependency) { Version = new Version(2, 0, 0, 0) }),
            Throws.TypeOf<FileNotFoundException>());
    }

    // Extensions load in parallel, and each context looks for a copy in the other before it loads its own. The default
    // context takes longer to get there, so the plugin starts later and later to pass through where the two overlap.
    [Test]
    public void An_assembly_asked_for_in_both_contexts_at_once_keeps_one_identity()
    {
        const int Rounds = 60;
        int secondCopies = 0;
        for (int round = 0; round < Rounds; round++)
        {
            string name = $"{_dependency}.{round}";
            string directory = CreateDirectory();
            TestPackages.Compile(directory, name, "public class Marker { }");
            var context = new PluginLoadContext(directory);
            int delay = round * 1250;
            int waiting = 2;

            // Spins, as a blocking wait wakes the threads too far apart.
            void WaitForTheOther()
            {
                Interlocked.Decrement(ref waiting);
                while (Volatile.Read(ref waiting) > 0)
                {
                }
            }

            Task<Assembly?> inDefault = Task.Factory.StartNew(() =>
            {
                WaitForTheOther();
                try
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name));
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }, TaskCreationOptions.LongRunning);
            Task<Assembly> inPlugin = Task.Factory.StartNew(() =>
            {
                WaitForTheOther();
                Thread.SpinWait(delay);
                return context.LoadFromAssemblyName(new AssemblyName(name));
            }, TaskCreationOptions.LongRunning);

            if (inDefault.Result is { } assembly && !ReferenceEquals(assembly, inPlugin.Result))
                secondCopies++;

            // More contexts would move where the two overlap.
            context.Unload();
        }

        Assert.That(secondCopies, Is.Zero);
    }

    [Test]
    public void An_unloaded_plugin_no_longer_answers_the_default_context()
    {
        LoadPlugin();
        _contexts[0].Unload();
        _contexts.Clear();

        Assert.That(
            () => AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(_dependency)),
            Throws.TypeOf<FileNotFoundException>());
    }

    [Test]
    public void A_plugin_that_shares_a_dependency_with_the_default_context_can_still_be_collected()
    {
        WeakReference context = LoadShareAndUnloadPlugin();

        for (int i = 0; context.IsAlive && i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(context.IsAlive, Is.False);
    }

    [Test]
    public void A_dependency_in_the_default_context_finds_the_native_library_its_package_ships()
    {
        string fileName = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        string runtimes = Path.Combine(AppContext.BaseDirectory, "runtimes");
        string? library = new[] { RuntimeInformation.RuntimeIdentifier, "osx" }
            .Select(runtime => Path.Combine(runtimes, runtime, "native", "libHarfBuzzSharp" + fileName))
            .FirstOrDefault(File.Exists);
        if (library is null)
            Assert.Ignore("The test output has no native library for this runtime.");

        string directory = CreateDirectory();
        string native = Path.Combine(directory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
        Directory.CreateDirectory(native);
        File.Copy(library, Path.Combine(native, (OperatingSystem.IsWindows() ? "" : "lib") + "beutl-plugin-native" + fileName));
        TestPackages.Compile(directory, _dependency, """
            using System;
            using System.Runtime.InteropServices;

            public static class NativeProbe
            {
                [DllImport("beutl-plugin-native", EntryPoint = "hb_version_string")]
                private static extern IntPtr Version();

                public static bool Call() => Version() != IntPtr.Zero;
            }
            """);
        _contexts.Add(new PluginLoadContext(directory));

        Assembly loaded = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(_dependency));

        Assert.That(loaded.GetType("NativeProbe")!.GetMethod("Call")!.Invoke(null, null), Is.True);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference LoadShareAndUnloadPlugin()
    {
        Assembly plugin = LoadPlugin();
        Invoke(plugin, "AskDefaultContext");
        Invoke(plugin, "ReferencedDependency");
        PluginLoadContext context = _contexts[0];
        _contexts.Clear();
        context.Unload();
        return new WeakReference(context);
    }

    private Assembly LoadPlugin()
    {
        string directory = CreateDirectory();
        string dependency = TestPackages.Compile(directory, _dependency, "public class Marker { }");
        TestPackages.Compile(directory, _dependency + ".Plugin", $$"""
            using System.Reflection;
            using System.Runtime.Loader;

            public static class Entry
            {
                public static Assembly AskDefaultContext() =>
                    AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("{{_dependency}}"));

                public static Assembly ReferencedDependency() => typeof(Marker).Assembly;
            }
            """, dependency);
        var context = new PluginLoadContext(directory);
        _contexts.Add(context);
        return context.LoadFromAssemblyPath(Path.Combine(directory, _dependency + ".Plugin.dll"));
    }

    private void CreateContext(string dependencySource)
    {
        string directory = CreateDirectory();
        TestPackages.Compile(directory, _dependency, dependencySource);
        _contexts.Add(new PluginLoadContext(directory));
    }

    private static Assembly Invoke(Assembly plugin, string method) =>
        (Assembly)plugin.GetType("Entry")!.GetMethod(method)!.Invoke(null, null)!;

    private string CreateDirectory()
    {
        string directory = Directory.CreateTempSubdirectory("beutl-plugin-load-context-").FullName;
        _directories.Add(directory);
        return directory;
    }
}
