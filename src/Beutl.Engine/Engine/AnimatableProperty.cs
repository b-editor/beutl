using Beutl.Animation;
using Beutl.Validation;

namespace Beutl.Engine;

public class AnimatableProperty<T> : PropertyBase<T>
{
    private IAnimation<T>? _animation;

    public AnimatableProperty(T defaultValue, IValidator<T>? validator = null)
        : base(defaultValue, validator)
    {
    }

    public override bool IsAnimatable => true;

    public bool SupportsExpression { get; } = true;

    private protected override bool SupportsExpressionCore => SupportsExpression;

    public override IAnimation<T>? Animation
    {
        get => _animation;
        set
        {
            if (_animation != value)
            {
                var oldValue = _animation;
                _animation = value;

                if (_animation != null && GetValidator() is IValidator<T> validator)
                {
                    _animation.Validator = validator;
                }

                AnimationChanged?.Invoke(_animation!);
                RaiseEdited();
                PropertyValueOwnership.Reparent(GetOwnerObject(), oldValue, value);

                if (oldValue is INotifyEdited oldEdited)
                    oldEdited.Edited -= OnChildEdited;
                if (_animation is INotifyEdited newEdited)
                    newEdited.Edited += OnChildEdited;
            }
        }
    }

    public event Action<IAnimation<T>?>? AnimationChanged;
}
