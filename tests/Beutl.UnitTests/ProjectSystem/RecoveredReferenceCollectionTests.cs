using System.Collections;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

public class RecoveredReferenceCollectionTests
{
    [SuppressResourceClassGeneration]
    public sealed class Holder : EngineObject
    {
        public Holder() => ScanProperties<Holder>();
        public IProperty<object?> Value { get; } = Property.Create<object?>();
        public IProperty<Element?> ExpressionValue { get; } = Property.Create<Element?>();
    }

    public sealed class CompositeExpression : IExpression<Element?>, IReferenceRewritable
    {
        public List<Reference<Element>> Targets { get; set; } = [];
        public string State { get; set; } = "plugin-state";
        [JsonIgnore]
        public string ExpressionString => State;
        public Element? Evaluate(ExpressionContext context) => Targets.FirstOrDefault().Value;
        public bool Validate(out string? error) { error = null; return true; }
        public IReferenceRewritable CreateReferenceRewriteTarget()
            => new CompositeExpression { Targets = Targets.ToList(), State = State };
        public void RewriteReferences(IReferenceRewriteContext context) => Targets = context.Rewrite(Targets);
    }

    public sealed class ThrowingList<TException> : ReadOnlyCollection<Reference<Element>>
        where TException : Exception, new()
    {
        public ThrowingList(IList<Reference<Element>> items) : base(items)
            => throw new TargetInvocationException(new TException());
        public ThrowingList(Reference<Element> item) : base(new[] { item }) { }
    }

    [TestCase("array")]
    [TestCase("list")]
    [TestCase("hashset")]
    [TestCase("sortedset")]
    [TestCase("queue")]
    [TestCase("stack")]
    [TestCase("dictionary")]
    [TestCase("sorteddictionary")]
    public void MigrationPreservesImmutableTypeComparersAndOrder(string kind)
    {
        Guid oldId = Guid.NewGuid();
        Reference<Element>[] values = [new(oldId), new(Guid.NewGuid())];
        var equality = EqualityComparer<Reference<Element>>.Create((a, b) => a.Id == b.Id, a => a.Id.GetHashCode());
        var order = Comparer<Reference<Element>>.Create((a, b) => a.Id.CompareTo(b.Id));
        object collection = kind switch
        {
            "array" => ImmutableArray.CreateRange(values),
            "list" => ImmutableList.CreateRange(values),
            "hashset" => ImmutableHashSet.CreateRange(equality, values),
            "sortedset" => ImmutableSortedSet.CreateRange(order, values),
            "queue" => ImmutableQueue.CreateRange(values),
            "stack" => ImmutableStack.CreateRange(values.Reverse()),
            "dictionary" => ImmutableDictionary.Create<string, Reference<Element>>(StringComparer.OrdinalIgnoreCase, equality)
                .Add("Target", values[0]),
            "sorteddictionary" => ImmutableSortedDictionary.Create<string, Reference<Element>>(StringComparer.OrdinalIgnoreCase, equality)
                .Add("Target", values[0]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var holder = new Holder();
        holder.Value.CurrentValue = collection;
        Element target = Migrate(holder, oldId);
        object result = holder.Value.CurrentValue!;
        Assert.That(result.GetType(), Is.EqualTo(collection.GetType()));
        if (result is IDictionary dictionary)
        {
            Assert.That(((Reference<Element>)dictionary["TARGET"]!).Value, Is.SameAs(target));
            Assert.That(result.GetType().GetProperty("ValueComparer")!.GetValue(result), Is.SameAs(equality));
        }
        else
        {
            Reference<Element>[] rewritten = ((IEnumerable)result).Cast<Reference<Element>>().ToArray();
            Assert.That(rewritten.Single(x => x.Id == target.Id).Value, Is.SameAs(target));
            if (kind is not "hashset" and not "sortedset")
                Assert.That(rewritten.Select(x => x.Id), Is.EqualTo(new[] { target.Id, values[1].Id }));
            if (kind is "hashset" or "sortedset")
                Assert.That(result.GetType().GetProperty("KeyComparer")!.GetValue(result),
                    Is.SameAs(kind == "hashset" ? (object)equality : order));
        }
    }

    [TestCase(typeof(OutOfMemoryException))]
    [TestCase(typeof(AccessViolationException))]
    [TestCase(typeof(OperationCanceledException))]
    public void MigrationDoesNotSwallowFatalReadOnlyListConstructorFailures(Type exceptionType)
    {
        Guid oldId = Guid.NewGuid();
        var holder = new Holder();
        holder.Value.CurrentValue = Activator.CreateInstance(typeof(ThrowingList<>).MakeGenericType(exceptionType),
            new Reference<Element>(oldId));
        var exception = Assert.Throws<TargetInvocationException>(() => Migrate(holder, oldId));
        Assert.That(exception!.GetBaseException().GetType(), Is.EqualTo(exceptionType));
    }

    [Test]
    public void MigrationRewritesCompositeExpressionAndPreservesSerializedState()
    {
        Guid oldId = Guid.NewGuid();
        var expression = new CompositeExpression { Targets = [new(oldId)], State = "custom-state" };
        var holder = new Holder();
        holder.ExpressionValue.Expression = Expression.CreateFromNode<Element?>(Expression.ToNode(expression));
        Element target = Migrate(holder, oldId);
        var rewritten = (CompositeExpression)holder.ExpressionValue.Expression!;
        Assert.That(rewritten.Targets.Single().Value, Is.SameAs(target));
        var restored = (CompositeExpression)Expression.CreateFromNode<Element?>(Expression.ToNode(rewritten))!;
        Assert.That(restored.Targets.Single().Id, Is.EqualTo(target.Id));
        Assert.That(restored.State, Is.EqualTo("custom-state"));
        Assert.That(expression.Targets.Single().Id, Is.EqualTo(oldId));
    }

    private static Element Migrate(Holder holder, Guid oldId)
    {
        string root = Path.GetTempPath();
        var target = new Element { Uri = new Uri(Path.Combine(root, "target.belm")) };
        var owner = new Element { Uri = new Uri(Path.Combine(root, "owner.belm")) };
        owner.AddObject(holder);
        var scene = new Scene { Uri = new Uri(Path.Combine(root, "scene.scene")) };
        scene.Children.Add(target);
        scene.Children.Add(owner);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[oldId] = target.Id;
        typeof(Scene).GetMethod("MigrateRecoveredElementReferences", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scene, null);
        return target;
    }
}
