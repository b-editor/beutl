using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Tests.Common;

public class SchemaVersionTests
{
    [Test]
    public void Stamp_AddsCurrentSchemaVersion()
    {
        var json = new JsonObject();

        SchemaVersion.Stamp(json);

        Assert.That(json["schemaVersion"]!.GetValue<string>(), Is.EqualTo(SchemaVersion.Current));
    }

    [Test]
    public void EnsureKnown_RejectsUnknownVersion()
    {
        var ex = Assert.Throws<SchemaVersionMismatchException>(() =>
            SchemaVersion.EnsureKnown("future-version"));

        Assert.That(ex!.Code, Is.EqualTo(ErrorCode.SchemaVersionMismatch));
    }
}
