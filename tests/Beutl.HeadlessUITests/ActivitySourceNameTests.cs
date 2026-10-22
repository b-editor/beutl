using System.Diagnostics;
using System.Reflection;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

// The app's tracer listens to sources by name, so a misspelled name drops their traces silently (#2844).
[TestFixture]
public sealed class ActivitySourceNameTests
{
    [Test]
    public async Task Every_activity_source_is_subscribed_and_every_subscription_names_a_source()
    {
        using var httpClient = new HttpClient();
        await using var api = new BeutlApiApplication(httpClient, new ExtensionProvider());
        HashSet<string> sources = [api.ActivitySource.Name];
        List<string> instanceOwners = [];
        foreach (FieldInfo field in BeutlAssemblies()
                     .SelectMany(LoadableTypes)
                     .Where(type => !type.ContainsGenericParameters)
                     .SelectMany(type => type.GetFields(
                         BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.DeclaredOnly))
                     .Where(field => field.FieldType == typeof(ActivitySource)))
        {
            if (field.IsStatic)
                sources.Add(((ActivitySource)field.GetValue(null)!).Name);
            else
                instanceOwners.Add(field.DeclaringType!.FullName!);
        }

        List<string> subscribed = Telemetry.GetActivitySourceNames(new TelemetryConfig
        {
            Beutl_Application = true,
            Beutl_PackageManagement = true,
            Beutl_Api_Client = true,
            Beutl_Logging = true,
        });

        Assert.Multiple(() =>
        {
            // A source owned per instance is read above; a new owner has to be added there.
            Assert.That(instanceOwners, Is.EquivalentTo(new[] { typeof(BeutlApiApplication).FullName }));
            Assert.That(sources, Is.EquivalentTo(subscribed.Distinct()));
        });
    }

    // The app and the Beutl libraries it loads.
    private static IEnumerable<Assembly> BeutlAssemblies()
    {
        var found = new Dictionary<string, Assembly>();
        var pending = new Queue<Assembly>([typeof(Telemetry).Assembly]);
        while (pending.TryDequeue(out Assembly? assembly))
        {
            if (!found.TryAdd(assembly.GetName().Name!, assembly))
                continue;

            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name!.StartsWith("Beutl", StringComparison.Ordinal) && !found.ContainsKey(reference.Name))
                    pending.Enqueue(Assembly.Load(reference));
            }
        }

        return found.Values;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
