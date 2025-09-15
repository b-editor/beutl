using System;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class JsonImmediateResolveTests
{
    private sealed class Obj : CoreObject {}

    [Test]
    public void Resolve_CallbackInvokedWhenObjectAlreadyKnown()
    {
        var ctx = new JsonSerializationContext(typeof(object), NullSerializationErrorNotifier.Instance);
        using var scope = ThreadLocalSerializationContext.Enter(ctx);

        bool called = false;
        Guid id = Guid.NewGuid();
        ctx.Resolve(id, o => { called = ((ICoreObject)o).Id == id; });

        var obj = new Obj { Id = id };
        ctx.AfterDeserialized(obj);

        Assert.That(called, Is.True);
    }
}

