using Beutl.Animation;
using Beutl.Animation.Animators;

namespace Beutl.UnitTests.Engine.Animation;

public class AnimatorRegistryTests
{
    [Test]
    public void CreateAnimator_ShouldReturnCorrectAnimatorType()
    {
        var animator = AnimatorRegistry.CreateAnimator<int>();
        Assert.That(animator, Is.InstanceOf<Int32Animator>());
    }

    [Test]
    public void GetAnimatorType_ShouldReturnCorrectAnimatorType()
    {
        var animatorType = AnimatorRegistry.GetAnimatorType(typeof(int));
        Assert.That(animatorType, Is.EqualTo(typeof(Int32Animator)));
    }

    [Test]
    public void GetAnimatorType_ShouldReturnDefaultAnimatorForUnknownType()
    {
        var animatorType = AnimatorRegistry.GetAnimatorType(typeof(DateTime));
        Assert.That(animatorType, Is.EqualTo(typeof(AnimatorRegistry._Animator<DateTime>)));
    }

    [Test]
    public void RegisterAnimator_ShouldAddAnimatorToRegistry()
    {
        AnimatorRegistry.RegisterAnimator(typeof(MyAnimator), type => type == typeof(MyStruct));
        var animatorType = AnimatorRegistry.GetAnimatorType(typeof(MyStruct));
        Assert.That(animatorType, Is.EqualTo(typeof(MyAnimator)));
    }

    [Test]
    public void RegisterAnimator_Generic_ShouldAddAnimatorToRegistry()
    {
        AnimatorRegistry.RegisterAnimator<MyStruct, MyAnimator>();
        var animatorType = AnimatorRegistry.GetAnimatorType(typeof(MyStruct));
        Assert.That(animatorType, Is.EqualTo(typeof(MyAnimator)));
    }

    [Test]
    public void RegisterAnimator_GenericWithCondition_ShouldAddAnimatorToRegistry()
    {
        AnimatorRegistry.RegisterAnimator<MyStruct, MyAnimator>(type => type == typeof(MyStruct));
        var animatorType = AnimatorRegistry.GetAnimatorType(typeof(MyStruct));
        Assert.That(animatorType, Is.EqualTo(typeof(MyAnimator)));
    }

    [Test]
    public void DefaultAnimator_Interpolate_ShouldReturnNewValue()
    {
        var animator = new AnimatorRegistry._Animator<int>();
        const int oldValue = 5;
        const int newValue = 10;
        const float progress = 0.5f;

        int result = animator.Interpolate(progress, oldValue, newValue);

        Assert.That(result, Is.EqualTo(newValue));
    }

    private class MyAnimator : Animator<MyStruct>
    {
        public override MyStruct Interpolate(float progress, MyStruct oldValue, MyStruct newValue)
        {
            return newValue;
        }
    }

    private readonly record struct MyStruct(int Value);
}
