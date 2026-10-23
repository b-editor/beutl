using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class TransactionRollbackTests
{
    [Test]
    public void Patch_apply_failure_rolls_back_prior_mutations()
    {
        var root = new ThrowingCoreObject();
        using var session = new AgentToolkitTestSession(root);
        EditTools tools = session.CreateEditTools();

        JsonObject patch = new()
        {
            [nameof(ThrowingCoreObject.First)] = 42,
            [nameof(ThrowingCoreObject.Throwing)] = 1
        };

        var result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(root.First.Value, Is.Zero);
        });
    }

    private sealed class ThrowingCoreObject : CoreObject
    {
        public ThrowingCoreObject()
        {
            ScanProperties<ThrowingCoreObject>();
            Throwing.ValueChanged += (_, e) =>
            {
                if (e.NewValue != 0)
                    throw new InvalidOperationException("Injected patch failure.");
            };
        }

        public IProperty<int> First { get; } = new ValueOnlyProperty<int>(0);

        public IProperty<int> Throwing { get; } = new ValueOnlyProperty<int>(0);
    }
}
