using System.Collections.Concurrent;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class PropertyExpressionTests
{
    [TestCase(false, "\"abc\"")]
    [TestCase(true, "\"abc\"")]
    [TestCase(false, "1 / (int)Time")]
    [TestCase(true, "1 / (int)Time")]
    [TestCase(false, "object value = \"abc\"; value")]
    [TestCase(true, "object value = \"abc\"; value")]
    public void FailedExpression_UsesCurrentValue(bool animated, string source)
    {
        IProperty<float> property = animated ? new AnimatableProperty<float>(0) : new SimpleProperty<float>(0);
        property.SetAttributes("Opacity", []);
        property.CurrentValue = 75;
        property.Expression = new StringExpression<float>(source);

        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero, property);
        int edits = 0;
        property.Edited += (_, _) => edits++;

        Assert.That(property.GetValue(context), Is.EqualTo(75));
        Assert.Multiple(() =>
        {
            Assert.That(property.ExpressionError, Is.Not.Empty);
            Assert.That(context.IsEvaluating(property), Is.False);
            Assert.That(property.CurrentValue, Is.EqualTo(75));
            Assert.That(edits, Is.Zero, "Evaluation diagnostics must not enter undo history.");
        });
        property.Expression = null;
        Assert.That(property.ExpressionError, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecoveringExpression_ClearsErrorAndLogsOnlyOncePerAssignment(bool animated)
    {
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        using var logs = new CaptureLoggerProvider();
        Log.LoggerFactory.AddProvider(logs);
        IProperty<float> property = animated ? new AnimatableProperty<float>(7) : new SimpleProperty<float>(7);
        property.SetAttributes("Value", []);
        property.Expression = new StringExpression<float>("10 / (int)Time");
        var failingContext = new CompositionContext(TimeSpan.Zero);

        Assert.That(property.GetValue(failingContext), Is.EqualTo(7));
        Assert.That(property.GetValue(failingContext), Is.EqualTo(7));
        Assert.That(property.GetValue(new CompositionContext(TimeSpan.FromSeconds(2))), Is.EqualTo(5));
        Assert.That(property.ExpressionError, Is.Null);
        Assert.That(property.GetValue(failingContext), Is.EqualTo(7));
        Assert.That(logs.Errors, Has.Count.EqualTo(1));

        property.Expression = new StringExpression<float>("20 / (int)Time");
        Assert.That(property.ExpressionError, Is.Null);
        Assert.That(property.GetValue(failingContext), Is.EqualTo(7));
        Assert.That(logs.Errors, Has.Count.EqualTo(2));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task ReplacingExpression_DoesNotShareDiagnosticsWithPendingEvaluation(bool animated, bool oldFailure)
    {
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        using var logs = new CaptureLoggerProvider();
        Log.LoggerFactory.AddProvider(logs);
        IProperty<float> property = animated ? new AnimatableProperty<float>(7) : new SimpleProperty<float>(7);
        property.SetAttributes("Value", []);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        property.Expression = new TestExpression(() =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The pending evaluation was not released.");
            return oldFailure ? throw new ExpressionException("Old failure") : 42;
        });
        Task<float> pending = Task.Run(() => property.GetValue(new CompositionContext(TimeSpan.Zero)));
        try
        {
            Assert.That(started.Wait(TimeSpan.FromSeconds(10)), Is.True);
            property.Expression = new TestExpression(() => throw new ExpressionException("New failure"));
            if (!oldFailure)
            {
                Assert.That(property.GetValue(new CompositionContext(TimeSpan.Zero)), Is.EqualTo(7));
                Assert.That(property.ExpressionError, Is.EqualTo("New failure"));
            }

            release.Set();
            Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(oldFailure ? 7 : 42));
            Assert.That(property.ExpressionError, Is.EqualTo(oldFailure ? null : "New failure"));
            Assert.That(property.GetValue(new CompositionContext(TimeSpan.Zero)), Is.EqualTo(7));
            Assert.That(property.ExpressionError, Is.EqualTo("New failure"));
            Assert.That(logs.Errors.Count(error => error?.Message == "New failure"), Is.EqualTo(1));
        }
        finally
        {
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedExpression_WhenLoggingThrows_StillUsesFallback(bool animated)
    {
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        using var logs = new CaptureLoggerProvider(throwOnLog: true);
        Log.LoggerFactory.AddProvider(logs);
        IProperty<float> property = animated ? new AnimatableProperty<float>(7) : new SimpleProperty<float>(7);
        property.Expression = new TestExpression(() => throw new ExpressionException("Evaluation failed"));
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero, property);

        Assert.That(property.GetValue(context), Is.EqualTo(7));
        Assert.Multiple(() =>
        {
            Assert.That(property.ExpressionError, Is.EqualTo("Evaluation failed"));
            Assert.That(context.IsEvaluating(property), Is.False);
            Assert.That(logs.Errors, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void FailedExpression_FallsBackToAnimationAtTheRequestedTime()
    {
        var property = new AnimatableProperty<float>(7);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 20, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2), Value = 80, Easing = new LinearEasing() });
        property.Animation = animation;
        property.Expression = new StringExpression<float>("throw new InvalidOperationException();");

        Assert.That(property.GetValue(new CompositionContext(TimeSpan.FromSeconds(1))), Is.EqualTo(50));
        Assert.That(property.Animation, Is.SameAs(animation));
    }

    [Test]
    public void EvaluateGraphics_WithInvalidOpacityExpression_PreservesTheFrame()
    {
        var scene = new Scene { Duration = TimeSpan.FromSeconds(2) };
        var shape = new RectShape();
        shape.Opacity.CurrentValue = 75;
        shape.Opacity.Expression = new StringExpression<float>("\"abc\"");
        var element = new Element { Length = scene.Duration };
        element.Objects.Add(shape);
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.Zero);

        Assert.That(frame.Objects, Has.Length.EqualTo(1));
        Assert.That(((RectShape.Resource)frame.Objects[0]).Opacity, Is.EqualTo(75));
    }

    private sealed class TestExpression(Func<float> evaluate) : IExpression<float>
    {
        public string ExpressionString => "test";

        public float Evaluate(ExpressionContext context) => evaluate();

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }

    private sealed class CaptureLoggerProvider(bool throwOnLog = false) : ILoggerProvider
    {
        private bool _disposed;
        private readonly bool _throwOnLog = throwOnLog;
        public ConcurrentQueue<Exception?> Errors { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

        public void Dispose() => _disposed = true;

        private sealed class CaptureLogger(CaptureLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => !provider._disposed && category == "PropertyExpression";

            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(level))
                {
                    provider.Errors.Enqueue(exception);
                    if (provider._throwOnLog) throw new InvalidOperationException("Logging failed");
                }
            }
        }
    }
}
