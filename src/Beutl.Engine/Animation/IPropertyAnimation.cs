namespace Beutl.Animation;

// An animation at the bottom of a property's expression chain.
public interface IPropertyAnimation : IExpression, ICoreObject, INotifyEdited, IHierarchical
{
    Type ValueType { get; }

    // Adopts the settings of the property holding it, such as its validator. The property calls it on attachment
    // and whenever its validator changes.
    void Configure(IProperty owner);
}

public interface IPropertyAnimation<T> : IPropertyAnimation, IExpression<T>
{
    T EvaluateAt(MediaTime time);
}

public interface IComponentAnimation : IPropertyAnimation
{
    IReadOnlyList<IProperty> Components { get; }
}

public interface IComponentAnimation<T> : IComponentAnimation, IPropertyAnimation<T>
{
    T GetEditingValue(MediaTime time);

    void SetValue(T value, MediaTime time);

    void TransformValues(Func<T, T> transform, MediaTime time);
}

internal interface IComponentInterpolator<T>
{
    T Interpolate(KeyFrameAnimation<T> animation, float progress, T previous, T next, MediaTime previousTime, MediaTime nextTime);
}
