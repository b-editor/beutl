using System.Reactive.Subjects;
using Avalonia.Headless.NUnit;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Threading;
using Beutl.ViewModels.Editors;
using Moq;
using AvaDispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class PathFigurePreviewLifetimeTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Disposing_the_editor_stops_evaluating_the_current_figure(bool replaceFigure)
    {
        var expression = new CountingPointExpression();
        var figure = new PathFigure();
        figure.StartPoint.Expression = expression;
        using var value = new BehaviorSubject<PathFigure?>(figure);
        using var editor = new PathFigureEditorViewModel(CreateProperty(value));
        DrainPreviewUpdates();
        if (replaceFigure)
        {
            figure = new PathFigure();
            figure.StartPoint.Expression = expression;
            value.OnNext(figure);
            DrainPreviewUpdates();
        }

        Assert.That(expression.EvaluationCount, Is.GreaterThan(0));
        editor.Dispose();
        DrainPreviewUpdates();
        int evaluationsAtDisposal = expression.EvaluationCount;

        figure.IsClosed.CurrentValue = true;
        DrainPreviewUpdates();

        Assert.Multiple(() =>
        {
            Assert.That(editor.PreviewPath.Value, Is.Null);
            Assert.That(expression.EvaluationCount, Is.EqualTo(evaluationsAtDisposal),
                "An editor that has been closed must release the final geometry resource subscription.");
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void An_empty_figure_clears_the_preview_and_can_be_replaced(bool initiallyEmpty)
    {
        var expression = new CountingPointExpression();
        var figure = new PathFigure();
        figure.StartPoint.Expression = expression;
        using var value = new BehaviorSubject<PathFigure?>(initiallyEmpty ? null : figure);
        using var editor = new PathFigureEditorViewModel(CreateProperty(value));
        DrainPreviewUpdates();

        value.OnNext(null);
        DrainPreviewUpdates();
        int evaluationsAfterClearing = expression.EvaluationCount;
        figure.IsClosed.CurrentValue = true;
        DrainPreviewUpdates();

        Assert.Multiple(() =>
        {
            Assert.That(editor.PreviewPath.Value, Is.Null);
            Assert.That(expression.EvaluationCount, Is.EqualTo(evaluationsAfterClearing));
        });

        value.OnNext(figure);
        DrainPreviewUpdates();
        Assert.That(editor.PreviewPath.Value, Is.Not.Null);
    }

    private static IPropertyAdapter<PathFigure> CreateProperty(IObservable<PathFigure?> value)
    {
        var property = new Mock<IPropertyAdapter<PathFigure>>();
        property.SetupGet(x => x.PropertyType).Returns(typeof(PathFigure));
        property.SetupGet(x => x.ImplementedType).Returns(typeof(PathFigure));
        property.SetupGet(x => x.DisplayName).Returns("Figure");
        property.Setup(x => x.GetAttributes()).Returns([]);
        property.Setup(x => x.GetObservable()).Returns(value);
        return property.Object;
    }

    private static void DrainPreviewUpdates()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        RenderThread.Dispatcher.Invoke(static () => { }, DispatchPriority.Low, timeout.Token);
        AvaDispatcher.UIThread.RunJobs();
    }

    private sealed class CountingPointExpression : IExpression<Point>
    {
        private int _evaluationCount;

        public int EvaluationCount => Volatile.Read(ref _evaluationCount);

        public string ExpressionString => "0";

        public Point Evaluate(ExpressionContext context)
        {
            Interlocked.Increment(ref _evaluationCount);
            return new Point(0, 0);
        }

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }
}
