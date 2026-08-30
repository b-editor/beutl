using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class CurrentPixelFilterEffectTests
{
    private static readonly Rect s_bounds = new(3, 5, 16, 9);

    [Test]
    public void MigratedEffects_RecordCurrentPixelStagesWithPreservedUniforms()
    {
        var invert = new Invert();
        invert.Amount.CurrentValue = 25;
        invert.ExcludeAlphaChannel.CurrentValue = false;
        ShaderDescription invertShader = Record(invert);
        AssertFloatUniform(invertShader, "amount", 0.25f);
        AssertIntegerUniform(invertShader, "excludeAlpha", 0);

        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 250;
        gamma.Strength.CurrentValue = 40;
        ShaderDescription gammaShader = Record(gamma);
        AssertFloatUniform(gammaShader, "gamma", 2.5f);
        AssertFloatUniform(gammaShader, "strength", 0.4f);

        var threshold = new Threshold();
        threshold.Value.CurrentValue = 33;
        threshold.Smoothness.CurrentValue = 7;
        threshold.Strength.CurrentValue = 60;
        ShaderDescription thresholdShader = Record(threshold);
        AssertFloatUniform(thresholdShader, "threshold", 0.33f);
        AssertFloatUniform(thresholdShader, "smoothness", 0.07f);
        AssertFloatUniform(thresholdShader, "strength", 0.6f);

        var negaposi = new Negaposi();
        negaposi.Red.CurrentValue = 255;
        negaposi.Green.CurrentValue = 128;
        negaposi.Blue.CurrentValue = 0;
        negaposi.Strength.CurrentValue = 75;
        ShaderDescription negaposiShader = Record(negaposi);
        AssertFloatUniform(
            negaposiShader,
            "negaColor",
            Color.SrgbToLinear(1),
            Color.SrgbToLinear(128 / 255f),
            Color.SrgbToLinear(0));
        AssertFloatUniform(negaposiShader, "strength", 0.75f);

        var colorKey = new ColorKey();
        colorKey.Color.CurrentValue = new Color(128, 64, 128, 255);
        colorKey.Range.CurrentValue = 20;
        colorKey.Boundary.CurrentValue = 3;
        ShaderDescription colorKeyShader = Record(colorKey);
        Vector4 linearKeyColor = colorKey.Color.CurrentValue.ToLinear();
        AssertFloatUniform(
            colorKeyShader,
            "keyColor",
            new Vector3(linearKeyColor.X, linearKeyColor.Y, linearKeyColor.Z));
        AssertFloatUniform(colorKeyShader, "range", 0.2f);
        AssertFloatUniform(colorKeyShader, "boundary", 0.03f);

        var chromaKey = new ChromaKey();
        chromaKey.Color.CurrentValue = new Color(128, 64, 128, 255);
        chromaKey.HueRange.CurrentValue = 90;
        chromaKey.SaturationRange.CurrentValue = 25;
        chromaKey.Boundary.CurrentValue = 4;
        ShaderDescription chromaKeyShader = Record(chromaKey);
        AssertFloatUniform(
            chromaKeyShader,
            "keyColor",
            new Vector3(64 / 255f, 128 / 255f, 1));
        AssertFloatUniform(chromaKeyShader, "hueRange", 0.25f);
        AssertFloatUniform(chromaKeyShader, "saturationRange", 0.25f);
        AssertFloatUniform(chromaKeyShader, "boundary", 0.04f);

        var grading = new ColorGrading();
        grading.Exposure.CurrentValue = 1.5f;
        grading.Contrast.CurrentValue = 25;
        grading.ContrastPivot.CurrentValue = 0.25f;
        grading.Saturation.CurrentValue = 15;
        grading.Vibrance.CurrentValue = -20;
        grading.Hue.CurrentValue = 45;
        grading.Temperature.CurrentValue = 30;
        grading.Tint.CurrentValue = -35;
        grading.LowRange.CurrentValue = 80;
        grading.HighRange.CurrentValue = 20;
        grading.Shadows.CurrentValue = new GradingColor(0.1f, 0.2f, 0.3f);
        grading.Midtones.CurrentValue = new GradingColor(0.4f, 0.5f, 0.6f);
        grading.Highlights.CurrentValue = new GradingColor(0.7f, 0.8f, 0.9f);
        grading.Lift.CurrentValue = new GradingColor(-0.1f, 0, 0.1f);
        grading.Gamma.CurrentValue = new GradingColor(-1, 0.5f, 2);
        grading.Gain.CurrentValue = new GradingColor(-1, 0.5f, 2);
        grading.Offset.CurrentValue = new GradingColor(-0.2f, 0, 0.2f);
        ShaderDescription gradingShader = Record(grading);
        AssertFloatUniform(gradingShader, "exposure", 1.5f);
        AssertFloatUniform(gradingShader, "contrast", 0.25f);
        AssertFloatUniform(gradingShader, "contrastPivot", 0.25f);
        AssertFloatUniform(gradingShader, "saturation", 0.15f);
        AssertFloatUniform(gradingShader, "vibrance", -0.2f);
        AssertFloatUniform(gradingShader, "hue", 45);
        AssertFloatUniform(gradingShader, "temperature", 0.3f);
        AssertFloatUniform(gradingShader, "tint", -0.35f);
        AssertFloatUniform(gradingShader, "lowRange", 0.2f);
        AssertFloatUniform(gradingShader, "highRange", 0.8f);
        AssertFloatUniform(gradingShader, "shadows", 0.1f, 0.2f, 0.3f);
        AssertFloatUniform(gradingShader, "midtones", 0.4f, 0.5f, 0.6f);
        AssertFloatUniform(gradingShader, "highlights", 0.7f, 0.8f, 0.9f);
        AssertFloatUniform(gradingShader, "lift", -0.1f, 0, 0.1f);
        AssertFloatUniform(gradingShader, "gamma", 0.001f, 0.5f, 2);
        AssertFloatUniform(gradingShader, "gain", 0, 0.5f, 2);
        AssertFloatUniform(gradingShader, "offset", -0.2f, 0, 0.2f);
    }

    [TestCaseSource(nameof(ConstantShaderEffects))]
    public void ConstantShaderEffects_ReuseParsedSource(Func<FilterEffect> factory)
    {
        SkslSource first = Record(factory()).Source;
        SkslSource second = Record(factory()).Source;

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void KeyColorAlpha_DoesNotChangeTheKeyColorUniform()
    {
        var firstColorKey = new ColorKey
        {
            Color = { CurrentValue = new Color(32, 64, 128, 255) },
        };
        var secondColorKey = new ColorKey
        {
            Color = { CurrentValue = new Color(224, 64, 128, 255) },
        };
        var firstChromaKey = new ChromaKey
        {
            Color = { CurrentValue = new Color(32, 64, 128, 255) },
        };
        var secondChromaKey = new ChromaKey
        {
            Color = { CurrentValue = new Color(224, 64, 128, 255) },
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                KeyColorUniform(Record(firstColorKey)),
                Is.EqualTo(KeyColorUniform(Record(secondColorKey))));
            Assert.That(
                KeyColorUniform(Record(firstChromaKey)),
                Is.EqualTo(KeyColorUniform(Record(secondChromaKey))));
        });
    }

    [Test]
    public void MigratedEffects_CanCompileInFusedPrograms()
    {
        ShaderDescription[] descriptions =
        [
            Record(new Invert()),
            Record(new Gamma()),
            Record(new Threshold()),
            Record(new Negaposi()),
            Record(new ColorKey()),
            Record(new ChromaKey()),
            Record(new ColorGrading()),
        ];

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            descriptions.Select(static description => new SkslSnippetStage(description)).ToArray(),
            SkslBackendBudgetResolver.Portable);

        Assert.Multiple(() =>
        {
            Assert.That(programs.Sum(static program => program.StageCount), Is.EqualTo(descriptions.Length));
            Assert.That(programs, Has.Some.Matches<SkslMergedProgram>(static program => program.StageCount > 1));
            Assert.That(programs, Has.All.Matches<SkslMergedProgram>(
                static program => !program.RequiresStandaloneExecution));
        });

        foreach (SkslMergedProgram program in programs)
        {
            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(program.Source, out string? error);
            Assert.Multiple(() =>
            {
                Assert.That(error, Is.Null);
                Assert.That(effect, Is.Not.Null);
            });
        }
    }

    [Test]
    public void MigratedIdentityConfigurations_PreservePremultipliedLinearPixels()
    {
        var invert = new Invert();
        invert.Amount.CurrentValue = 0;

        var gamma = new Gamma();
        gamma.Strength.CurrentValue = 0;

        var negaposi = new Negaposi();
        negaposi.Strength.CurrentValue = 0;

        var colorKey = new ColorKey();
        colorKey.Color.CurrentValue = Colors.Black;
        colorKey.Range.CurrentValue = 5;
        colorKey.Boundary.CurrentValue = 2;

        var chromaKey = new ChromaKey();
        chromaKey.Color.CurrentValue = Colors.Lime;
        chromaKey.HueRange.CurrentValue = 1;
        chromaKey.SaturationRange.CurrentValue = 1;
        chromaKey.Boundary.CurrentValue = 1;

        FilterEffect[] effects = [invert, gamma, negaposi, colorKey, chromaKey, new ColorGrading()];
        foreach (FilterEffect effect in effects)
        {
            (float[] before, float[] after) = Render(effect, new SKColor(230, 40, 20, 180));
            AssertPixel(
                after,
                before,
                0.003f,
                $"{effect.GetType().Name} changed an identity-configured premultiplied pixel");
        }
    }

    [Test]
    public void Threshold_ZeroStrength_PreservesEffectItemPremultipliedLumaSemantics()
    {
        var threshold = new Threshold();
        threshold.Strength.CurrentValue = 0;

        (float[] before, float[] after) = Render(threshold, new SKColor(230, 40, 20, 180));
        float luma = before[0] * 0.2126f + before[1] * 0.7152f + before[2] * 0.0722f;

        AssertPixel(
            after,
            [luma, luma, luma, luma],
            0.003f,
            "Threshold no longer matches its premultiplied luma behavior");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Invert_ActiveAmount_MatchesPremultipliedReference(bool excludeAlpha)
    {
        const float amount = 0.35f;
        var effect = new Invert
        {
            Amount = { CurrentValue = amount * 100 },
            ExcludeAlphaChannel = { CurrentValue = excludeAlpha },
        };

        (float[] before, float[] after) = Render(effect, new SKColor(190, 72, 28, 164));
        float alpha = before[3];
        float outputAlpha = excludeAlpha ? alpha : Mix(alpha, 1 - alpha, amount);
        float[] expected =
        [
            Mix(before[0] / alpha, 1 - before[0] / alpha, amount) * outputAlpha,
            Mix(before[1] / alpha, 1 - before[1] / alpha, amount) * outputAlpha,
            Mix(before[2] / alpha, 1 - before[2] / alpha, amount) * outputAlpha,
            outputAlpha,
        ];

        AssertPixel(after, expected, 0.003f, "Invert no longer matches its premultiplied reference");
    }

    [Test]
    public void Gamma_ActiveAmount_MatchesPremultipliedReference()
    {
        const float gamma = 1.8f;
        const float strength = 0.65f;
        var effect = new Gamma
        {
            Amount = { CurrentValue = gamma * 100 },
            Strength = { CurrentValue = strength * 100 },
        };

        (float[] before, float[] after) = Render(effect, new SKColor(190, 72, 28, 164));
        float alpha = before[3];
        float[] expected =
        [
            Mix(before[0] / alpha, MathF.Pow(before[0] / alpha, 1 / gamma), strength) * alpha,
            Mix(before[1] / alpha, MathF.Pow(before[1] / alpha, 1 / gamma), strength) * alpha,
            Mix(before[2] / alpha, MathF.Pow(before[2] / alpha, 1 / gamma), strength) * alpha,
            alpha,
        ];

        AssertPixel(after, expected, 0.003f, "Gamma no longer matches its premultiplied reference");
    }

    [Test]
    public void Negaposi_ActiveStrength_MatchesPremultipliedReference()
    {
        const float strength = 0.6f;
        var effect = new Negaposi
        {
            Red = { CurrentValue = 224 },
            Green = { CurrentValue = 160 },
            Blue = { CurrentValue = 96 },
            Strength = { CurrentValue = strength * 100 },
        };

        (float[] before, float[] after) = Render(effect, new SKColor(190, 72, 28, 164));
        float alpha = before[3];
        float[] key =
        [
            Color.SrgbToLinear(224 / 255f),
            Color.SrgbToLinear(160 / 255f),
            Color.SrgbToLinear(96 / 255f),
        ];
        float[] expected =
        [
            Mix(before[0] / alpha, key[0] - before[0] / alpha, strength) * alpha,
            Mix(before[1] / alpha, key[1] - before[1] / alpha, strength) * alpha,
            Mix(before[2] / alpha, key[2] - before[2] / alpha, strength) * alpha,
            alpha,
        ];

        AssertPixel(after, expected, 0.003f, "Negaposi no longer matches its premultiplied reference");
    }

    [Test]
    public void Threshold_ActiveStrength_MatchesPremultipliedReference()
    {
        const float threshold = 0.15f;
        const float smoothness = 0.2f;
        const float strength = 0.7f;
        var effect = new Threshold
        {
            Value = { CurrentValue = threshold * 100 },
            Smoothness = { CurrentValue = smoothness * 100 },
            Strength = { CurrentValue = strength * 100 },
        };

        (float[] before, float[] after) = Render(effect, new SKColor(190, 72, 28, 164));
        float luma = before[0] * 0.2126f + before[1] * 0.7152f + before[2] * 0.0722f;
        float thresholdValue = SmoothStep(
            threshold - smoothness * 0.5f,
            threshold + smoothness * 0.5f,
            luma);
        float expected = Mix(luma, thresholdValue, strength);

        AssertPixel(
            after,
            [expected, expected, expected, expected],
            0.003f,
            "Threshold no longer matches its active premultiplied reference");
    }

    /// <remarks>
    /// <see cref="Threshold.Smoothness"/> defaults to 0, which collapses the smoothstep band onto the
    /// threshold and divides by zero inside it. Equal edges are undefined in the shading languages, and the
    /// backends disagree: Skia's CPU backend answers NaN and Metal answers 0. Because the entry point returns
    /// <c>half4(t)</c>, a NaN is written into alpha as well, so every pixel whose luma lands on the threshold
    /// leaves the stage non-finite. The cases below are the landings that are exact on every backend: a zero
    /// threshold over a transparent premultiplied pixel, and a full threshold over white. An interior
    /// threshold is reachable too, but only by hunting a colour whose premultiplied linear luma lands on it,
    /// and such a colour only lands where it was hunted - the sRGB-to-linear conversion is free to round
    /// differently per backend, and one F16 ulp off the threshold takes the hard-step branch instead of the
    /// boundary these cases are about.
    /// </remarks>
    [TestCaseSource(nameof(CollapsedThresholdBandCases))]
    public void Threshold_ACollapsedBand_IsFiniteAndKeepsTheThresholdValue(float value, SKColor input)
    {
        var effect = new Threshold
        {
            Value = { CurrentValue = value },
            Smoothness = { CurrentValue = 0 },
        };

        (float[] before, float[] after) = Render(effect, input);
        float luma = (before[0] * 0.2126f) + (before[1] * 0.7152f) + (before[2] * 0.0722f);

        Assert.Multiple(() =>
        {
            Assert.That(
                luma,
                Is.EqualTo(value / 100f),
                "the case only reaches the collapsed band if the input luma lands exactly on the threshold");
            Assert.That(
                after,
                Has.All.Matches<float>(float.IsFinite),
                $"Threshold Value = {value} with Smoothness = 0 emitted a non-finite component");
        });

        AssertPixel(
            after,
            [0.5f, 0.5f, 0.5f, 0.5f],
            0.003f,
            "Threshold no longer carries the threshold value a band of positive width has there");
    }

    /// <remarks>
    /// The band is centred on the threshold, so a band of any positive width evaluates to exactly 0.5 at the
    /// threshold. Pinning that across the smoothness range is what makes 0.5 the collapsed band's value
    /// rather than a fresh choice: the alternative hard step - <c>step(threshold, luma)</c>, which is 1 -
    /// would make the output jump as the slider crosses zero, and at Value = 0 it would turn every fully
    /// transparent pixel the stage covers opaque white. Value = 0 is also the landing this drives from,
    /// because that is the case the sentence above argues about and because a transparent premultiplied
    /// pixel carries luma 0 on every backend.
    /// </remarks>
    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(50f)]
    [TestCase(100f)]
    public void Threshold_AtTheThreshold_IsHalfWhateverTheSmoothness(float smoothness)
    {
        var effect = new Threshold
        {
            Value = { CurrentValue = 0 },
            Smoothness = { CurrentValue = smoothness },
        };

        // A fully transparent premultiplied pixel carries luma 0, which is exactly this threshold.
        (_, float[] after) = Render(effect, new SKColor(0, 0, 0, 0));

        AssertPixel(
            after,
            [0.5f, 0.5f, 0.5f, 0.5f],
            0.003f,
            $"Threshold Smoothness = {smoothness} moved the value the stage carries at its own threshold");
    }

    [Test]
    public void KeyEffects_MatchingAndNonMatchingColors_PreserveEffectItemMasks()
    {
        var input = new SKColor(64, 128, 224, 160);
        var matchingColor = new Color(input.Alpha, input.Red, input.Green, input.Blue);
        FilterEffect[] matching =
        [
            new ColorKey
            {
                Color = { CurrentValue = matchingColor },
                Range = { CurrentValue = 5 },
                Boundary = { CurrentValue = 2 },
            },
            new ChromaKey
            {
                Color = { CurrentValue = matchingColor },
                HueRange = { CurrentValue = 5 },
                SaturationRange = { CurrentValue = 5 },
                Boundary = { CurrentValue = 2 },
            },
        ];
        FilterEffect[] nonMatching =
        [
            new ColorKey
            {
                Color = { CurrentValue = Colors.Black },
                Range = { CurrentValue = 5 },
                Boundary = { CurrentValue = 2 },
            },
            new ChromaKey
            {
                Color = { CurrentValue = Colors.Black },
                HueRange = { CurrentValue = 5 },
                SaturationRange = { CurrentValue = 5 },
                Boundary = { CurrentValue = 2 },
            },
        ];

        foreach (FilterEffect effect in matching)
        {
            (_, float[] after) = Render(effect, input);
            AssertPixel(after, [0, 0, 0, 0], 0.003f, effect.GetType().Name);
        }

        foreach (FilterEffect effect in nonMatching)
        {
            (float[] before, float[] after) = Render(effect, input);
            AssertPixel(after, before, 0.003f, effect.GetType().Name);
        }
    }

    [Test]
    public void ColorGrading_ActiveExposure_MatchesPremultipliedReference()
    {
        var effect = new ColorGrading
        {
            Exposure = { CurrentValue = 1 },
        };

        (float[] before, float[] after) = Render(effect, new SKColor(40, 20, 10, 180));
        float[] expected = [before[0] * 2, before[1] * 2, before[2] * 2, before[3]];

        AssertPixel(after, expected, 0.003f, "ColorGrading no longer applies exposure in linear light");
    }

    private static ShaderDescription Record(FilterEffect effect)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);

        context.ApplyTransactional(effect, resource);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items.OfType<IFEItem_Custom>(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        });

        var item = (FEItem_Shader)items.Single();
        Assert.That(item.Description.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
        return item.Description;
    }

    private static IEnumerable<TestCaseData> CollapsedThresholdBandCases()
    {
        // Smoothness already ships at 0, so the band is collapsed as delivered and one drag of the Value
        // slider to either end of its range is the whole distance between the defaults and this singularity.
        yield return new TestCaseData(0f, new SKColor(0, 0, 0, 0))
            .SetName("Threshold_ACollapsedBand_IsFiniteAndKeepsTheThresholdValue_TransparentPixelAtZero");
        yield return new TestCaseData(100f, new SKColor(255, 255, 255, 255))
            .SetName("Threshold_ACollapsedBand_IsFiniteAndKeepsTheThresholdValue_WhiteAtFull");
    }

    private static IEnumerable<TestCaseData> ConstantShaderEffects()
    {
        yield return new TestCaseData((Func<FilterEffect>)(static () => new ChromaKey()))
            .SetName("ChromaKey_ReusesParsedSource");
        yield return new TestCaseData((Func<FilterEffect>)(static () => new ColorGrading()))
            .SetName("ColorGrading_ReusesParsedSource");
        yield return new TestCaseData((Func<FilterEffect>)(static () => new ColorKey()))
            .SetName("ColorKey_ReusesParsedSource");
        yield return new TestCaseData((Func<FilterEffect>)(static () => new Curves()))
            .SetName("Curves_ReusesParsedSource");
        yield return new TestCaseData((Func<FilterEffect>)(static () => new Gamma()))
            .SetName("Gamma_ReusesParsedSource");
        yield return new TestCaseData((Func<FilterEffect>)(static () => new Invert()))
            .SetName("Invert_ReusesParsedSource");
        yield return new TestCaseData((Func<FilterEffect>)(static () => new Negaposi()))
            .SetName("Negaposi_ReusesParsedSource");
        yield return new TestCaseData((Func<FilterEffect>)(static () => new Threshold()))
            .SetName("Threshold_ReusesParsedSource");
    }

    private static void AssertFloatUniform(
        ShaderDescription description,
        string name,
        params float[] expected)
    {
        ShaderUniformValue actual = Bind(description, name);
        Assert.That(actual.IsInteger, Is.False);
        Assert.That(actual.Floats, Has.Length.EqualTo(expected.Length));
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.That(
                actual.Floats![index],
                Is.EqualTo(expected[index]).Within(1e-6),
                $"uniform '{name}' component {index}");
        }
    }

    private static void AssertFloatUniform(
        ShaderDescription description,
        string name,
        Vector3 expected)
        => AssertFloatUniform(description, name, expected.X, expected.Y, expected.Z);

    private static float[] KeyColorUniform(ShaderDescription description)
        => Bind(description, "keyColor").Floats!;

    private static void AssertIntegerUniform(
        ShaderDescription description,
        string name,
        params int[] expected)
    {
        ShaderUniformValue actual = Bind(description, name);
        Assert.Multiple(() =>
        {
            Assert.That(actual.IsInteger, Is.True);
            Assert.That(actual.Integers, Is.EqualTo(expected));
        });
    }

    private static ShaderUniformValue Bind(ShaderDescription description, string name)
    {
        ShaderUniformBinding binding = description.Uniforms.Single(item => item.Name == name);
        SkslUniformDeclaration declaration = description.Source.Uniforms[name];
        var token = new RenderExecutionSessionToken();
        var execution = new ShaderExecutionContext(
            token,
            s_bounds,
            s_bounds,
            s_bounds,
            PixelRect.FromRect(s_bounds, 1),
            EffectiveScale.At(1),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            intent: RenderIntent.Preview,
            purpose: RenderRequestPurpose.Frame);
        try
        {
            return binding.Bind(declaration, execution);
        }
        finally
        {
            token.Complete();
        }
    }

    private static (float[] Before, float[] After) Render(FilterEffect effect, SKColor color)
    {
        using var backing = new CpuRenderTarget(1, 1);
        backing.Value.Canvas.Clear(color);
        backing.Value.Canvas.Flush();
        using Bitmap beforeBitmap = backing.Snapshot();
        float[] before = ReadPixel(beforeBitmap);
        using var targets = new EffectTargets
        {
            new EffectTarget(
                backing,
                new Rect(0, 0, 1, 1),
                EffectiveScale.At(1),
                new PixelRect(0, 0, 1, 1)),
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 1, 1));
        context.ApplyTransactional(effect, resource);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        activator.Apply(context);
        activator.Flush(false);

        using Bitmap afterBitmap = targets.Single().RenderTarget!.Snapshot();
        return (before, ReadPixel(afterBitmap));
    }

    private static float[] ReadPixel(Bitmap bitmap)
        => bitmap.GetPixelSpan<ushort>()[..4]
            .ToArray()
            .Select(static bits => (float)BitConverter.UInt16BitsToHalf(bits))
            .ToArray();

    private static float Mix(float first, float second, float amount)
        => first + (second - first) * amount;

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return amount * amount * (3 - 2 * amount);
    }

    private static void AssertPixel(float[] actual, float[] expected, float tolerance, string message)
    {
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.That(
                actual[index],
                Is.EqualTo(expected[index]).Within(tolerance),
                $"{message}; channel {index}");
        }
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height)
    {
        private static SKSurface CreateSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                   width,
                   height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("A CPU effect-test surface could not be created.");
    }
}
