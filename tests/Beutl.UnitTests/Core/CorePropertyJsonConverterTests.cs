using System.Text.Json;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class CorePropertyJsonConverterTests
{
    private sealed class Obj : CoreObject
    {
        public static readonly CoreProperty<int> P1Property;
        static Obj()
        {
            P1Property = ConfigureProperty<int, Obj>(nameof(P1)).DefaultValue(0).Register();
        }
        public int P1 { get => GetValue(P1Property); set => SetValue(P1Property, value); }
    }

    [Test]
    public void SerializeAndRead_CoreProperty_ByNameAndOwner()
    {
        var prop = Obj.P1Property;
        string json = JsonSerializer.Serialize<CoreProperty>(prop, JsonHelper.SerializerOptions);
        var read = JsonSerializer.Deserialize<CoreProperty>(json, JsonHelper.SerializerOptions);

        Assert.That(read, Is.Not.Null);
        Assert.That(read!.Name, Is.EqualTo(prop.Name));
        Assert.That(read.OwnerType, Is.EqualTo(prop.OwnerType));
    }
}

