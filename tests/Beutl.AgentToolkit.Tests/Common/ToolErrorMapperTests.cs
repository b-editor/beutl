using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Tests.Common;

[TestFixture]
public sealed class ToolErrorMapperTests
{
    [Test]
    public void Unmapped_exception_exposes_type_throw_site_and_parameter_name()
    {
        ToolError error = ToolErrorMapper.Map(Catch(ThrowArgumentOutOfRange));

        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo("internal_error"));
            Assert.That(error.Message, Does.Contain(nameof(ArgumentOutOfRangeException)));
            Assert.That(error.Message, Does.Contain($"{nameof(ToolErrorMapperTests)}.{nameof(ThrowArgumentOutOfRange)}"));
            Assert.That(error.Message, Does.Contain("parameter 'duration'"));
            Assert.That(error.Hint, Does.Contain("server log"));
        });
    }

    [Test]
    public void Unmapped_argument_exception_with_path_like_parameter_name_stays_redacted()
    {
        string secret = Path.Combine(Path.GetTempPath(), "secret-project.bep");

        ToolError error = ToolErrorMapper.Map(Catch(
            () => throw new ArgumentException("Invalid argument.", secret)));

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain(nameof(ArgumentException)));
            Assert.That(error.Message, Does.Not.Contain(secret));
            Assert.That(error.Message, Does.Not.Contain("secret-project"));
        });
    }

    [Test]
    public void Unmapped_exception_message_stays_redacted()
    {
        string secret = Path.Combine(Path.GetTempPath(), "secret-project.bep");

        ToolError error = ToolErrorMapper.Map(Catch(() => throw new InvalidOperationException($"Cannot open '{secret}'.")));

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain(nameof(InvalidOperationException)));
            Assert.That(error.Message, Does.Not.Contain(secret));
        });
    }

    private static void ThrowArgumentOutOfRange()
    {
        throw new ArgumentOutOfRangeException("duration", "Duration must be positive.");
    }

    // Map reads Exception.TargetSite, which is only populated once the exception has been thrown.
    private static Exception Catch(Action thrower)
    {
        try
        {
            thrower();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Thrower did not throw.");
    }
}
