using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

// A property is registered, saved and recorded under its member's name, even one ending in Property such as
// EditorConfig.EnablePointerLockInProperty.
[TestFixture]
public class PropertyNamingTests
{
    [SuppressResourceClassGeneration]
    private sealed class ReflectedOwner : EngineObject
    {
        public ReflectedOwner() => ScanProperties<ReflectedOwner>();

        public IProperty<int> CountProperty { get; } = Property.CreateValue(1);
    }

    [Test]
    public void AMemberEndingInProperty_KeepsItsName_WhenScannedByReflection()
        => AssertNamedAndSavedAsCountProperty(new ReflectedOwner(), owner => ((ReflectedOwner)owner).CountProperty);

    [Test]
    public void AMemberEndingInProperty_KeepsItsName_WhenScannedByGeneratedCode()
        => AssertNamedAndSavedAsCountProperty(new GeneratedSuffixedOwner(), owner => ((GeneratedSuffixedOwner)owner).CountProperty);

    private static void AssertNamedAndSavedAsCountProperty(EngineObject owner, Func<EngineObject, IProperty<int>> count)
    {
        count(owner).Value = 5;

        JsonObject json = CoreSerializer.SerializeToJsonObject(owner);
        var restored = (EngineObject)CoreSerializer.DeserializeFromJsonObject(json, owner.GetType());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count(owner).Name, Is.EqualTo("CountProperty"));
            Assert.That((int?)json["CountProperty"], Is.EqualTo(5));
            Assert.That(json.ContainsKey("Count"), Is.False);
            Assert.That(count(restored).Value, Is.EqualTo(5));
        }
    }
}

internal sealed partial class GeneratedSuffixedOwner : EngineObject
{
    public GeneratedSuffixedOwner() => ScanProperties<GeneratedSuffixedOwner>();

    public IProperty<int> CountProperty { get; } = Property.CreateValue(1);
}
