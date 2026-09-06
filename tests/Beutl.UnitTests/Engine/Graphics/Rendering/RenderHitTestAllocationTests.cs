using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Guards what one contract evaluation allocates. A hit test runs once per pointer press but walks every
/// fragment on the path, so anything allocated here is paid once per fragment traversed.
/// </summary>
[TestFixture]
public sealed class RenderHitTestAllocationTests
{
    private const int Iterations = 20_000;
    private const int WarmupIterations = 200;

    // Measured at 48: one context object, an object header plus three fields. The headroom is for a
    // runtime that lays that object out differently, not for a second allocation - restoring either the
    // defensive copy of the caller's array or the wrapper around it lands past this ceiling.
    private const long CustomContextBytesPerEvaluationCeiling = 64;

    private static readonly Rect s_outputBounds = new(0, 0, 100, 100);
    private static readonly Point s_point = new(50, 50);
    private static readonly Func<Point, bool> s_inputHitTest = static point => point.X >= 40;

    private static readonly RenderHitTestInput[] s_inputs =
    [
        new(new Rect(0, 0, 40, 40), s_inputHitTest),
        new(new Rect(40, 0, 40, 40), s_inputHitTest),
    ];

    private static readonly RenderResourceBinding[] s_resources = [];

    private static readonly RenderHitTestContract s_custom =
        RenderHitTestContract.Custom(static (context, point) => context.OutputBounds.Contains(point));

    // What the clip nodes' custom bodies do: ask the inputs without building a predicate to do it.
    private static readonly RenderHitTestContract s_customAskingInputs =
        RenderHitTestContract.Custom(
            static (context, point) => RenderHitTestContract.AnyInputAccepts(context.Inputs, point));

    // The LINQ the clip nodes used to write, kept as the control for what the shared helper avoids.
    private static readonly RenderHitTestContract s_customAskingInputsWithLinq =
        RenderHitTestContract.Custom(
            static (context, point) => context.Inputs.Any(input => input.HitTest(point)));

    [Test]
    public void ContractsThatAnswerFromTheirArguments_DoNotAllocate()
    {
        long none = MeasureBytesPerEvaluation(RenderHitTestContract.None);
        long outputBounds = MeasureBytesPerEvaluation(RenderHitTestContract.OutputBounds);
        long anyInput = MeasureBytesPerEvaluation(RenderHitTestContract.AnyInput);

        TestContext.Out.WriteLine($"None: {none} bytes/evaluation");
        TestContext.Out.WriteLine($"OutputBounds: {outputBounds} bytes/evaluation");
        TestContext.Out.WriteLine($"AnyInput: {anyInput} bytes/evaluation");

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.Zero, "a contract that answers false must not allocate to say so");
            Assert.That(
                outputBounds,
                Is.Zero,
                "testing a point against a rect must not allocate");
            Assert.That(
                anyInput,
                Is.Zero,
                "asking each input in turn must not allocate a closure or a predicate to do it");
        });
    }

    [Test]
    public void ACustomContract_AllocatesOnlyTheContextItHandsTheCallback()
    {
        long bytes = MeasureBytesPerEvaluation(s_custom);

        TestContext.Out.WriteLine($"Custom: {bytes} bytes/evaluation");
        Assert.That(
            bytes,
            Is.LessThanOrEqualTo(CustomContextBytesPerEvaluationCeiling),
            "the context is the only thing a custom hit test needs; the inputs it reads are the caller's");
    }

    [Test]
    public void ACustomContractAskingItsInputs_CostsNoMoreThanTheContext()
    {
        long shared = MeasureBytesPerEvaluation(s_customAskingInputs);
        long linq = MeasureBytesPerEvaluation(s_customAskingInputsWithLinq);

        TestContext.Out.WriteLine($"AnyInputAccepts: {shared} bytes/evaluation");
        TestContext.Out.WriteLine($"LINQ Any: {linq} bytes/evaluation");

        Assert.Multiple(() =>
        {
            Assert.That(
                shared,
                Is.LessThanOrEqualTo(CustomContextBytesPerEvaluationCeiling),
                "asking the inputs must cost nothing beyond the context the callback was handed");
            Assert.That(
                linq,
                Is.GreaterThan(shared),
                "the control: the predicate closing over the point is what the shared helper avoids");
        });
    }

    [Test]
    public void ACustomContract_ReadsTheInputsTheCallerPassed()
    {
        RenderHitTestContract contract = RenderHitTestContract.Custom(
            static (context, point) => context.Inputs.Count == 2 && context.Inputs[1].HitTest(point));

        Assert.Multiple(() =>
        {
            Assert.That(
                contract.Evaluate(s_outputBounds, s_inputs, s_resources, s_point),
                Is.True,
                "the second input accepts a point at x=50");
            Assert.That(
                contract.Evaluate(s_outputBounds, s_inputs, s_resources, new Point(10, 10)),
                Is.False,
                "the second input rejects a point at x=10");
        });
    }

    [Test]
    public void TheAnyInputContract_AnswersForEveryInputInTurn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                RenderHitTestContract.AnyInput.Evaluate(
                    s_outputBounds,
                    s_inputs,
                    s_resources,
                    s_point),
                Is.True,
                "the second input accepts a point the first rejects");
            Assert.That(
                RenderHitTestContract.AnyInput.Evaluate(
                    s_outputBounds,
                    s_inputs,
                    s_resources,
                    new Point(10, 10)),
                Is.False,
                "no input accepts a point at x=10");
            Assert.That(
                RenderHitTestContract.AnyInput.Evaluate(
                    s_outputBounds,
                    [],
                    s_resources,
                    s_point),
                Is.False,
                "a fragment with no inputs is hit by nothing");
        });
    }

    private static long MeasureBytesPerEvaluation(RenderHitTestContract contract)
    {
        for (int index = 0; index < WarmupIterations; index++)
            _ = contract.Evaluate(s_outputBounds, s_inputs, s_resources, s_point);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < Iterations; index++)
            _ = contract.Evaluate(s_outputBounds, s_inputs, s_resources, s_point);
        long after = GC.GetAllocatedBytesForCurrentThread();

        return (after - before) / Iterations;
    }
}
