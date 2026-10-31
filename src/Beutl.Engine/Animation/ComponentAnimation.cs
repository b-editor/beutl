using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;
using Beutl.Validation;
using ValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl.Animation;

// A hierarchical container, not an EngineObject: its tracks inherit the owning object's time origin.
public sealed partial class ComponentAnimation<T, TComponent> : Hierarchical, IComponentAnimation,
    IComponentAnimation<T>, IKeyFrameExpression, IComponentInterpolator<TComponent>
{
    private static readonly ComponentSet<T, TComponent> s_set = ComponentAnimations.GetComponentSet<T, TComponent>();
    private readonly AnimatableProperty<TComponent>[] _components;
    private static readonly Animator<T> s_animator = AnimatorRegistry.CreateAnimator<T>();

    public ComponentAnimation()
    {
        ScanProperties<ComponentAnimation<T, TComponent>>();
        BaseValue.Edited += OnEdited;
        UnitSource.Edited += OnEdited;
        UnitSource.ValueChanged += (_, e) =>
        {
            if (e.OldValue != null) e.OldValue.AttachedToHierarchy -= OnUnitSourceAttached;
            if (e.NewValue != null) e.NewValue.AttachedToHierarchy += OnUnitSourceAttached;
        };
        _components = s_set.Components.Select(definition =>
        {
            var property = new AnimatableProperty<TComponent>(default!);
            property.SetAttributes(definition.Name, []);
            property.SetOwnerObject(this);
            property.Edited += OnEdited;
            RegisterProperty(property);
            return property;
        }).ToArray();
    }

    public IProperty<T> BaseValue { get; } = Property.CreateValue(default(T)!);

    // Relative values may have unit changes in an existing animation. Retain that unit schedule on separation.
    public IProperty<KeyFrameAnimation<T>?> UnitSource { get; } = Property.CreateValue<KeyFrameAnimation<T>?>(null);

    public IReadOnlyList<IProperty> Components => _components;
    public Type ValueType => typeof(T);
    public string ExpressionString => "[Components]";
    public event EventHandler? Edited;
    private void OnEdited(object? sender, EventArgs e) => Edited?.Invoke(this, EventArgs.Empty);
    // Undo restores the unit schedule before its attachment establishes the owner's time origin.
    private void OnUnitSourceAttached(object? sender, HierarchyAttachmentEventArgs e) => OnEdited(sender, EventArgs.Empty);
    public bool Validate(out string? error) { error = null; return true; }
    public T Evaluate(ExpressionContext context) => EvaluateComponents(context);

    public T EvaluateAt(MediaTime time) => EvaluateComponents(new EvaluationContext(time));

    private T EvaluateComponents(EvaluationContext context)
    {
        T value = UnitSource.Value is { } source ? source.GetAnimatedValue(context.Time)! : BaseValue.Value;
        for (int i = 0; i < _components.Length; i++)
            value = s_set.Components[i].Set(value, _components[i].Evaluate(context));
        return value;
    }

    public T GetEditingValue(MediaTime time)
    {
        T value = UnitSource.Value is { } source ? source.GetAnimatedValue(time)! : BaseValue.Value;
        for (int i = 0; i < _components.Length; i++)
        {
            var component = _components[i];
            TComponent current = component.Value;
            if (component.HasExpression)
                current = component.Evaluate(new EvaluationContext(time));
            else if (component.GetKeyFrameAnimation() is { KeyFrames.Count: > 0 } animation)
            {
                var (before, after) = animation.GetPreviousAndNextKeyFrame(animation.ToLocal(time));
                current = ((KeyFrame<TComponent>)(after ?? before)!).Value.Value!;
            }
            value = s_set.Components[i].Set(value, current);
        }
        return value;
    }

    public void SetValue(T value, MediaTime time)
    {
        T previous = GetEditingValue(time);
        for (int i = 0; i < _components.Length; i++)
        {
            TComponent next = s_set.Components[i].Get(value);
            if (EqualityComparer<TComponent>.Default.Equals(s_set.Components[i].Get(previous), next)) continue;
            var component = _components[i];
            if (component.GetKeyFrameAnimation() is { } animation && animation.KeyFrames.Count > 0)
            {
                var (before, after) = animation.GetPreviousAndNextKeyFrame(animation.ToLocal(time));
                ((KeyFrame<TComponent>)(after ?? before)!).Value.Value = next;
            }
            else component.Value = next;
        }
        // A unit change applies to the whole value; replace an inherited unit schedule only for that explicit edit.
        if (s_set.UnitChanged?.Invoke(previous, value) == true)
            UnitSource.Value = null;
        BaseValue.Value = value;
    }

    // Canvas drags shift both adjacent keys, as they do for an unsplit vector or point.
    public void TransformValues(Func<T, T> transform, MediaTime time)
    {
        T basis = EvaluateAt(time);
        for (int i = 0; i < _components.Length; i++)
        {
            var component = _components[i];
            var definition = s_set.Components[i];
            TComponent Apply(TComponent value) => definition.Get(transform(definition.Set(basis, value)));
            if (component.GetKeyFrameAnimation() is { KeyFrames.Count: > 0 } animation)
            {
                var (before, after) = animation.GetPreviousAndNextKeyFrame(animation.ToLocal(time));
                if (after?.KeyTime.Value == animation.ToLocal(time)) before = null;
                if (before is KeyFrame<TComponent> previous) previous.Value.Value = Apply(previous.Value.Value!);
                if (after is KeyFrame<TComponent> next) next.Value.Value = Apply(next.Value.Value!);
            }
            else component.Value = Apply(component.Value);
        }
    }

    TComponent IComponentInterpolator<TComponent>.Interpolate(KeyFrameAnimation<TComponent> animation,
        float progress, TComponent oldValue, TComponent newValue, MediaTime previousTime, MediaTime nextTime)
    {
        int index = Array.FindIndex(_components, property => ReferenceEquals(property.GetKeyFrameAnimation(), animation));
        var definition = s_set.Components[index];
        T previous = BaseValue.Value, next = previous;
        if (UnitSource.Value is { } source)
        {
            previous = source.GetAnimatedValue(animation.ToScene(previousTime))!;
            next = source.GetAnimatedValue(animation.ToScene(nextTime))!;
        }
        return definition.Get(s_animator.Interpolate(progress,
            definition.Set(previous, oldValue), definition.Set(next, newValue)));
    }

    internal static ComponentAnimation<T, TComponent> Create(IProperty<T> property)
    {
        var result = new ComponentAnimation<T, TComponent>();
        result.BaseValue.Value = property.Value;
        var source = property.GetKeyFrameAnimation();
        if (source != null && s_set.UnitChanged != null)
            result.UnitSource.Value = (KeyFrameAnimation<T>)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(source), typeof(KeyFrameAnimation<T>));
        for (int i = 0; i < result._components.Length; i++)
        {
            var component = result._components[i];
            var definition = s_set.Components[i];
            component.Value = definition.Get(property.Value);
            if (source != null)
            {
                var animation = new KeyFrameAnimation<TComponent>();
                animation.UseGlobalClock.Value = source.UseGlobalClock.Value;
                foreach (var key in source.KeyFrames.Cast<KeyFrame<T>>())
                {
                    // Reuse the easing codec, including extension easings and preserved recovery data.
                    JsonObject json = CoreSerializer.SerializeToJsonObject(key);
                    json.Remove(nameof(Id));
                    json.Remove("$type");
                    json.Remove(nameof(KeyFrame<T>.Value));
                    var copy = new KeyFrame<TComponent>();
                    CoreSerializer.PopulateFromJsonObject(copy, json);
                    copy.Value.Value = definition.Get(key.Value.Value!);
                    animation.KeyFrames.Add(copy);
                }
                component.SetKeyFrameAnimation(animation);
            }
        }
        result.Configure(property);
        return result;
    }

    void IPropertyAnimation.Configure(IProperty owner) => Configure((IProperty<T>)owner);

    internal void Configure(IProperty<T> owner)
    {
        for (int i = 0; i < _components.Length; i++)
        {
            var definition = s_set.Components[i];
            _components[i].DefaultValue = definition.Get(owner.DefaultValue);
            _components[i].SetAttributes(definition.Name,
                [ComponentAnimations.GetDisplayAttribute(definition.Name),
                    .. owner.GetAttributes()?.Where(x => x is NumberStepAttribute or UnitAttribute or DisplayFormatAttribute or RangeAttribute) ?? []]);
            if (owner.GetValidator() is IValidator<T> validator)
                _components[i].SetValidator(new ComponentValidator(validator, definition, this));
        }
    }

    private sealed class ComponentValidator(IValidator<T> validator, ComponentDefinition<T, TComponent> definition,
        ComponentAnimation<T, TComponent> animation) : IValidator<TComponent>
    {
        public string? Validate(ValidationContext context, TComponent? value)
            => validator.Validate(context, definition.Set(animation.BaseValue.Value, value!));

        public bool TryCoerce(ValidationContext context, ref TComponent? value)
        {
            T? composite = definition.Set(animation.BaseValue.Value, value!);
            bool coerced = validator.TryCoerce(context, ref composite);
            if (coerced) value = definition.Get(composite!);
            return coerced;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Animations", _components.Where(p => p.GetPropertyAnimation() != null)
            .ToDictionary(p => p.Name, p => p.GetPropertyAnimation()!));
        context.SetValue("Expressions", _components.Where(p => p.SerializeExpression() != null)
            .ToDictionary(p => p.Name, p => p.SerializeExpression()!));
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        var animations = context.GetValue<Dictionary<string, IPropertyAnimation>>("Animations");
        var expressions = context.GetValue<Dictionary<string, JsonNode>>("Expressions");
        foreach (var property in _components)
        {
            if (context.Contains("Animations"))
                property.SetPropertyAnimation(animations?.GetValueOrDefault(property.Name));
            if (expressions?.TryGetValue(property.Name, out var expression) == true) property.DeserializeExpression(expression);
            else if (context.Contains("Expressions")) property.Expression = null;
        }
    }
}
