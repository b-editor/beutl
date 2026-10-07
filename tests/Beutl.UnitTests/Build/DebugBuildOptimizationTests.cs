using System.Diagnostics;
using System.Reflection;
using Beutl.Graphics;

namespace Beutl.UnitTests.Build;

[TestFixture]
public class DebugBuildOptimizationTests
{
#if DEBUG
    // A project built on its own without -c used to see an empty Configuration in Directory.Build.props
    // and compile Debug with Optimize=true, so its IL differed from a solution build's.
    [Test]
    public void DebugBuildsAreCompiledWithoutOptimizations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IsJitOptimizerDisabled(typeof(DebugBuildOptimizationTests).Assembly), Is.True);
            Assert.That(IsJitOptimizerDisabled(typeof(Rect).Assembly), Is.True);
        });
    }

    private static bool IsJitOptimizerDisabled(Assembly assembly)
        => assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true;
#endif
}
