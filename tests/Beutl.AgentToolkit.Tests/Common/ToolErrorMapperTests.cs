using Beutl.AgentToolkit.Common;
using Beutl.Graphics.Rendering;
using Beutl.Media;

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
    public void An_edit_that_outwaited_the_user_maps_to_user_editing()
    {
        ToolError error = ToolErrorMapper.Map(new Beutl.AgentToolkit.Sessions.UserEditingException());

        Assert.That(error.Code, Is.EqualTo(ErrorCode.UserEditing));
    }

    [Test]
    public void Unmapped_argument_exception_shows_the_parameter_name_as_given()
    {
        ToolError error = ToolErrorMapper.Map(Catch(
            () => throw new ArgumentException("Invalid argument.", "frames[0].duration")));

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain(nameof(ArgumentException)));
            Assert.That(error.Message, Does.Contain("parameter 'frames[0].duration'"));
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

    [TestCase(16384, false)]
    [TestCase(16384, true)]
    [TestCase(null, false)]
    [TestCase(null, true)]
    public void Allocation_failure_exposes_the_device_size_and_limit_without_inner_exception_text(int? limit, bool wrapped)
    {
        string secret = Path.Combine(Path.GetTempPath(), "secret-project.bep");
        var allocation = new RenderTargetAllocationException(new PixelSize(16385, 9216), limit, new IOException(secret));
        Exception exception = wrapped ? new AggregateException(secret, allocation) : allocation;

        ToolError error = ToolErrorMapper.Map(exception);

        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo("render_target_allocation_failed"));
            Assert.That(error.Message, Does.Contain("16385x9216"));
            if (limit.HasValue)
                Assert.That(error.Message, Does.Contain("16384 pixels"));
            Assert.That(error.Message, Does.Not.Contain(secret));
            Assert.That(error.Hint, Does.Contain("scale"));
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
