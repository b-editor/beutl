using System.Reflection;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Core;

[TestFixture]
[NonParallelizable]
public sealed class LogTests
{
    [Test]
    public void CreateLoggerUsesFallbackWhenFactoryIsNotConfigured()
    {
        FieldInfo field = typeof(Log).GetField("s_loggerFactory", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (ILoggerFactory?)field.GetValue(null);

        field.SetValue(null, null);
        try
        {
            ILogger? logger = null;
            Assert.DoesNotThrow(() => logger = Log.CreateLogger("FallbackLogger"));

            Assert.That(Log.IsLoggerFactoryConfigured, Is.False);
            Assert.That(logger, Is.Not.Null);
        }
        finally
        {
            field.SetValue(null, original);
        }
    }
}
