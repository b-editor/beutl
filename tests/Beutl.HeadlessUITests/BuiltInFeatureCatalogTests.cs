using Beutl.Extensibility;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class BuiltInFeatureCatalogTests
{
    [Test]
    public void Catalog_CoversEveryFirstPartyExtensionWithAnExactStableIdentifier()
    {
        Type[] expectedTypes =
        [
            .. LoadPrimitiveExtensionTask.PrimitiveExtensions.Select(static extension => extension.GetType()),
            typeof(DefaultTutorialExtension)
        ];

        foreach (Type type in BuiltInFeatureCatalog.FeatureIds.Keys)
        {
            BuiltInFeatureCatalog.Register(type);
        }

        Assert.Multiple(() =>
        {
            Assert.That(expectedTypes.All(BuiltInFeatureCatalog.FeatureIds.ContainsKey), Is.True);
            Assert.That(BuiltInFeatureCatalog.FeatureIds.Values.Distinct().Count(),
                Is.EqualTo(BuiltInFeatureCatalog.FeatureIds.Count));
            Assert.That(BuiltInFeatureCatalog.FeatureIds.Values.All(id =>
                id.StartsWith("builtin/", StringComparison.Ordinal)
                && ProductAttributeNames.IsAllowedValue(ProductAttributeNames.FeatureId, id)), Is.True);
            Assert.That(BuiltInFeatureCatalog.FeatureIds.All(pair =>
                !pair.Value.Contains(pair.Key.Name, StringComparison.OrdinalIgnoreCase)
                && Telemetry.GetTrustedFeatureId(pair.Key) == pair.Value), Is.True);
        });
    }

    [Test]
    public void Catalog_UsesExactTypesAndBuiltInsSurviveThirdPartyUnloadCleanup()
    {
        BuiltInFeatureCatalog.Register(typeof(MainViewExtension));
        string expected = BuiltInFeatureCatalog.FeatureIds[typeof(MainViewExtension)];

        Telemetry.UnregisterTrustedFeature(typeof(MainViewExtension));

        Assert.Multiple(() =>
        {
            Assert.That(Telemetry.GetTrustedFeatureId(typeof(MainViewExtension)), Is.EqualTo(expected));
            Assert.That(Telemetry.GetTrustedFeatureId(typeof(DerivedMainViewExtension)), Is.EqualTo("generic"));
            Assert.That(Telemetry.GetTrustedFeatureId(typeof(UnknownExtension)), Is.EqualTo("generic"));
        });
    }

    private sealed class DerivedMainViewExtension : MainViewExtension;

    private sealed class UnknownExtension : Extension;
}
