namespace Beutl.UnitTests.Core;

[TestFixture]
public sealed class StaticPropertyReplacementTests
{
    private sealed class EquivalentValue(Guid target)
    {
        public Guid Target { get; } = target;
        public override bool Equals(object? obj) => obj is EquivalentValue;
        public override int GetHashCode() => 0;
    }

    private sealed class Holder : CoreObject
    {
        public static readonly CoreProperty<EquivalentValue?> ValueProperty =
            ConfigureProperty<EquivalentValue?, Holder>(nameof(Value))
                .Accessor(obj => obj.Value, (obj, value) => obj.Value = value).Register();

        private EquivalentValue? _value;
        public bool ThrowOnSet { get; set; }

        public EquivalentValue? Value
        {
            get => _value;
            set
            {
                if (ThrowOnSet) throw new InvalidOperationException("Setter failed.");
                SetAndRaise(ValueProperty, ref _value, value);
            }
        }
    }

    [Test]
    public void ReplaceValue_StaticAccessor_ReplacesEquivalentReferenceOnce()
    {
        var original = new EquivalentValue(Guid.NewGuid());
        var replacement = new EquivalentValue(Guid.NewGuid());
        var holder = new Holder { Value = original };
        int changes = 0;
        holder.PropertyChanged += (_, args) => changes += args.PropertyName == "Value" ? 1 : 0;

        holder.ReplaceValue(Holder.ValueProperty, replacement);

        Assert.That(holder.Value, Is.SameAs(replacement));
        Assert.That(changes, Is.EqualTo(1));
        holder.Value = original;
        Assert.That(holder.Value, Is.SameAs(replacement));
    }

    [Test]
    public void ReplaceValue_StaticAccessor_DoesNotForceReentrantOrdinaryAssignment()
    {
        var original = new EquivalentValue(Guid.NewGuid());
        var replacement = new EquivalentValue(Guid.NewGuid());
        var holder = new Holder { Value = original };
        int changes = 0;
        holder.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "Value" && ++changes == 1)
                holder.Value = original;
        };

        holder.ReplaceValue(Holder.ValueProperty, replacement);

        Assert.That(holder.Value, Is.SameAs(replacement));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void ReplaceValue_ThrowingStaticAccessor_DoesNotLeakReplacementIntent()
    {
        var original = new EquivalentValue(Guid.NewGuid());
        var replacement = new EquivalentValue(Guid.NewGuid());
        var holder = new Holder { Value = original, ThrowOnSet = true };
        Assert.Throws<InvalidOperationException>(() => holder.ReplaceValue(Holder.ValueProperty, replacement));
        holder.ThrowOnSet = false;

        holder.Value = replacement;

        Assert.That(holder.Value, Is.SameAs(original));
    }
}
