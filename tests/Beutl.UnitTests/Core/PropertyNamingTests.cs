using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

// A property exposed as XxxProperty, beside a value accessor Xxx, is registered, saved and recorded as Xxx.
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
    public void AnXxxPropertyMember_IsXxx_WhenScannedByReflection()
        => AssertNamedAndSavedAsCount(new ReflectedOwner(), owner => ((ReflectedOwner)owner).CountProperty);

    [Test]
    public void AnXxxPropertyMember_IsXxx_WhenScannedByGeneratedCode()
        => AssertNamedAndSavedAsCount(new GeneratedSuffixedOwner(), owner => ((GeneratedSuffixedOwner)owner).CountProperty);

    private static void AssertNamedAndSavedAsCount(EngineObject owner, Func<EngineObject, IProperty<int>> count)
    {
        count(owner).Value = 5;

        JsonObject json = CoreSerializer.SerializeToJsonObject(owner);
        var restored = (EngineObject)CoreSerializer.DeserializeFromJsonObject(json, owner.GetType());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count(owner).Name, Is.EqualTo("Count"));
            Assert.That((int?)json["Count"], Is.EqualTo(5));
            Assert.That(json.ContainsKey("CountProperty"), Is.False);
            Assert.That(count(restored).Value, Is.EqualTo(5));
        }
    }
}

internal sealed partial class GeneratedSuffixedOwner : EngineObject
{
    public GeneratedSuffixedOwner() => ScanProperties<GeneratedSuffixedOwner>();

    public IProperty<int> CountProperty { get; } = Property.CreateValue(1);
}
