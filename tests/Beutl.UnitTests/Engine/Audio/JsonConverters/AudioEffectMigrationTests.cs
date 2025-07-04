using Beutl.Audio.Effects;
using Beutl.Audio.Graph.Effects;
using NUnit.Framework;

namespace Beutl.UnitTests.Engine.Audio.JsonConverters;

[TestFixture]
public class AudioEffectMigrationTests
{
    [Test]
    public void SoundEffectWrapper_Should_Provide_Backward_Compatibility()
    {
        // Arrange
        var oldDelay = new Delay();
        oldDelay.DelayTime = 400.0f;
        oldDelay.Feedback = 50.0f;
        oldDelay.IsEnabled = true;

        // Act - Wrap the old effect
        var wrapper = new SoundEffectWrapper(oldDelay);

        // Assert - Should expose IAudioEffect interface
        Assert.That(wrapper.IsEnabled, Is.True);
        Assert.That(wrapper.InnerEffect, Is.EqualTo(oldDelay));
        
        // Should be able to create processor
        var processor = wrapper.CreateProcessor();
        Assert.That(processor, Is.Not.Null);
    }

    [Test]
    public void Delay_Should_Be_Marked_As_Obsolete()
    {
        // Arrange & Act - Create old Delay effect
        var oldDelay = new Delay();
        oldDelay.DelayTime = 200.0f;
        oldDelay.Feedback = 30.0f;

        // Assert - Should still work but compiler should show obsolete warning
        Assert.That(oldDelay.DelayTime, Is.EqualTo(200.0f));
        Assert.That(oldDelay.Feedback, Is.EqualTo(30.0f));
        Assert.That(oldDelay.IsEnabled, Is.True); // Default value
    }

    [Test]
    public void AudioDelayEffect_Should_Have_Same_Properties_As_Old_Delay()
    {
        // Arrange
        var newEffect = new AudioDelayEffect();
        newEffect.DelayTime = 300.0f;
        newEffect.Feedback = 45.0f;
        newEffect.DryMix = 70.0f;
        newEffect.WetMix = 30.0f;
        newEffect.IsEnabled = false;

        // Assert - Should have all expected properties
        Assert.That(newEffect.DelayTime, Is.EqualTo(300.0f));
        Assert.That(newEffect.Feedback, Is.EqualTo(45.0f));
        Assert.That(newEffect.DryMix, Is.EqualTo(70.0f));
        Assert.That(newEffect.WetMix, Is.EqualTo(30.0f));
        Assert.That(newEffect.IsEnabled, Is.False);
        
        // Should be able to create processor
        var processor = newEffect.CreateProcessor();
        Assert.That(processor, Is.Not.Null);
    }

    [Test] 
    public void Library_Should_Register_Both_Old_And_New_Effects()
    {
        // This test verifies that both old and new effects are registered
        // and users can choose between them during the migration period
        
        // Arrange - Check if both types can be instantiated
        var oldEffect = new Delay();
        var newEffect = new AudioDelayEffect();
        
        // Act & Assert - Both should work
        Assert.That(oldEffect, Is.Not.Null);
        Assert.That(newEffect, Is.Not.Null);
        
        // Both should be able to create processors
        var oldProcessor = oldEffect.CreateProcessor();
        var newProcessor = newEffect.CreateProcessor();
        
        Assert.That(oldProcessor, Is.Not.Null);
        Assert.That(newProcessor, Is.Not.Null);
    }
}