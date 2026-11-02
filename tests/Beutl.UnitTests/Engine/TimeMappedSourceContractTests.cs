using System.Reflection;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine;

// A type that shows OffsetPosition plays a timeline of its own. Split, Trim and Slip keep that playback
// continuous only through ITimeMappedSource, so a source type without the contract would replay or skip its
// content after a cut (#3642). A presenter re-times the object it shows instead, and a fallback stands in for
// a type that did not load.
[TestFixture]
public sealed class TimeMappedSourceContractTests
{
    private static readonly Assembly[] s_assemblies = [typeof(EngineObject).Assembly, typeof(Element).Assembly];

    private static IEnumerable<TestCaseData> TypesShowingAnOffsetPosition()
        => s_assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters
                && typeof(EngineObject).IsAssignableFrom(type)
                && !typeof(IPresenter).IsAssignableFrom(type) && !typeof(IFallback).IsAssignableFrom(type)
                && type.GetProperty("OffsetPosition", BindingFlags.Public | BindingFlags.Instance) != null
                && type.GetConstructor(Type.EmptyTypes) != null)
            .Where(type => Shown((EngineObject)Activator.CreateInstance(type)!, "OffsetPosition") != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new TestCaseData(type).SetArgDisplayNames(type.Name));

    [Test]
    public void TheScan_FindsTheBuiltInSources()
    {
        Type[] found = [.. TypesShowingAnOffsetPosition().Select(data => (Type)data.Arguments[0]!)];

        Assert.That(found, Is.SupersetOf(new[]
        {
            typeof(SourceVideo), typeof(SourceSound), typeof(SceneSound), typeof(SceneDrawable)
        }));
    }

    [TestCaseSource(nameof(TypesShowingAnOffsetPosition))]
    public void ATypeShowingAnOffsetPosition_MapsItThroughTheContract(Type type)
    {
        var obj = (EngineObject)Activator.CreateInstance(type)!;

        Assert.That(obj, Is.InstanceOf<ITimeMappedSource>());
        var source = (ITimeMappedSource)obj;
        Assert.Multiple(() =>
        {
            Assert.That(source.OffsetPosition, Is.SameAs(Shown(obj, "OffsetPosition")));
            Assert.That(source.Speed, Is.SameAs(Shown(obj, "Speed")));
        });
    }

    private static IProperty? Shown(EngineObject obj, string name)
        => obj.GetDisplayProperties().SingleOrDefault(property => property.Name == name);
}
