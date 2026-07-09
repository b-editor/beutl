using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
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
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            [nameof(ThrowingCoreObject.First)] = 42,
            [nameof(ThrowingCoreObject.Throwing)] = 1
        };

        var result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(root.First, Is.Zero);
        });
    }

    private sealed class ThrowingCoreObject : CoreObject
    {
        public static readonly CoreProperty<int> FirstProperty;
        public static readonly CoreProperty<int> ThrowingProperty;

        private int _first;

        static ThrowingCoreObject()
        {
            FirstProperty = ConfigureProperty<int, ThrowingCoreObject>(nameof(First))
                .Accessor(o => o.First, (o, v) => o.First = v)
                .Register();

            ThrowingProperty = ConfigureProperty<int, ThrowingCoreObject>(nameof(Throwing))
                .Accessor(o => o.Throwing, (o, v) => o.Throwing = v)
                .Register();
        }

        public int First
        {
            get => _first;
            set => SetAndRaise(FirstProperty, ref _first, value);
        }

        public int Throwing
        {
            get => 0;
            set => throw new InvalidOperationException("Injected patch failure.");
        }
    }
}
