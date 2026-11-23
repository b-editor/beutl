using Beutl.AgentToolkit.Common;
using Beutl.Graphics.Effects;
using Beutl.NodeGraph;
using Beutl.Services;
using static Beutl.AgentToolkit.Schema.SamplePatchBuilder;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class SchemaGenerator
{
    public IReadOnlyList<EffectSummary> ListEffects(string? intent = null, bool includePropertyNames = true)
    {
        TypeRegistration.EnsureRegistered();
        return EnumerateFilterEffectTypes()
            .Select(type => CreateEffectSummary(type, includePropertyNames))
            .Select(summary => new { Summary = summary, Score = ScoreEffect(summary, intent) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Summary.Name, StringComparer.Ordinal)
            .Select(item => item.Summary)
            .ToArray();
    }

    public ShaderCompilationCheck ValidateShader(string effectType, string script)
    {
        TypeRegistration.EnsureRegistered();
        Type? type = ResolveFilterEffectType(effectType);
        if (type is null)
        {
            return new ShaderCompilationCheck(effectType, "unknown_type", $"No registered FilterEffect matches '{effectType}'.");
        }

        if (Activator.CreateInstance(type) is not IScriptCompilableEffect effect)
        {
            return new ShaderCompilationCheck(type.Name, "not_script_effect", $"{type.Name} does not accept a compilable script.");
        }

        ScriptCompilationResult result = effect.ValidateScript(script ?? string.Empty);
        string status = result.Status switch
        {
            ScriptCompilationStatus.Compiled => "compiled",
            ScriptCompilationStatus.Failed => "failed",
            _ => "unavailable",
        };
        return new ShaderCompilationCheck(type.Name, status, result.Error);
    }

    private Type? ResolveFilterEffectType(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string trimmed = token.Trim();
        return EnumerateFilterEffectTypes()
            .FirstOrDefault(type =>
                string.Equals(type.Name, trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.FullName, trimmed, StringComparison.Ordinal)
                || string.Equals(IdentityHelper.WriteDiscriminator(type), trimmed, StringComparison.Ordinal));
    }

    private IEnumerable<Type> EnumerateFilterEffectTypes()
        => EnumerateRegisteredTypes()
            .Where(item => MatchesCategory(KnownLibraryItemFormats.FilterEffect, item.Category))
            .Select(item => item.Type)
            .Distinct();

    private EffectSummary CreateEffectSummary(Type type, bool includePropertyNames)
    {
        string discriminator = IdentityHelper.WriteDiscriminator(type);
        TypeDescriptor descriptor = CreateDescriptor(KnownLibraryItemFormats.FilterEffect, type, discriminator, includeProperties: includePropertyNames);
        EffectMetadata metadata = GetEffectMetadata(type);
        return new EffectSummary(
            type.Name,
            descriptor.Type,
            descriptor.Discriminator,
            descriptor.DisplayName,
            descriptor.Description,
            metadata.IntentTags.ToArray(),
            includePropertyNames
                ? descriptor.Properties.Select(property => property.Name).ToArray()
                : [],
            metadata.Notes.ToArray());
    }

    private static EffectMetadata GetEffectMetadata(Type type)
    {
        if (s_effectMetadata.TryGetValue(type, out EffectMetadata? metadata))
        {
            return metadata;
        }

        return new EffectMetadata(InferEffectTags(type.Name), []);
    }

    private static string[] InferEffectTags(string name)
    {
        string lower = name.ToLowerInvariant();
        List<string> tags = ["effect"];
        foreach ((string[] keywords, string[] ruleTags) in s_inferredEffectTagRules)
        {
            if (keywords.Any(keyword => lower.Contains(keyword, StringComparison.Ordinal)))
            {
                tags.AddRange(ruleTags);
            }
        }

        return tags.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static int ScoreEffect(EffectSummary summary, string? intent)
    {
        return ScoreSearch(
            intent,
            summary.IntentTags,
            [summary.Name, summary.DisplayName ?? string.Empty, summary.Description ?? string.Empty],
            summary.Notes);
    }

    private static int ScoreRecipe(EffectRecipeSummary summary, string? intent)
    {
        return ScoreSearch(
            intent,
            summary.IntentTags,
            [summary.Name, summary.Description],
            summary.EffectNames
                .Concat(summary.Notes)
                .Append(summary.Semantic ?? string.Empty));
    }

    private static int ScoreSearch(
        string? query,
        IEnumerable<string> primaryTokens,
        IEnumerable<string> names,
        IEnumerable<string> secondaryTokens)
    {
        string[] tokens = SearchTokens(query);
        if (tokens.Length == 0)
        {
            return 1;
        }

        int score = 0;
        foreach (string token in tokens)
        {
            if (primaryTokens.Any(value => string.Equals(value, token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 10;
            }
            else if (primaryTokens.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 6;
            }
            else if (names.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 4;
            }
            else if (secondaryTokens.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }
        }

        return score;
    }

    private static Dictionary<Type, EffectMetadata> CreateEffectMetadata()
    {
        return new Dictionary<Type, EffectMetadata>
        {
            [typeof(Blur)] = Metadata(["soften", "glow", "depth"], ["Use inside FilterEffectGroup before shadow/color effects for soft halos."]),
            [typeof(RadialBlur)] = Metadata(["blur", "radial", "zoom", "spin", "motion"], ["Amount is zoom expansion in percent or the total spin angle in degrees; Center sets the origin and Samples controls smoothness."]),
            [typeof(MotionBlur)] = Metadata(["blur", "motion", "temporal", "shutter"], ["Samples the owning drawable's animation and preceding effects. Following effects apply after averaging, in parent coordinates. ShutterAngle sets exposure relative to the frame rate; Samples controls quality and cost."]),
            [typeof(Bokeh)] = Metadata(["blur", "lens", "depth"], []),
            [typeof(DropShadow)] = Metadata(["shadow", "glow", "depth"], ["Use zero offset for neon glow, non-zero offset for cast shadows."]),
            [typeof(InnerShadow)] = Metadata(["shadow", "depth", "inset"], []),
            [typeof(FlatShadow)] = Metadata(["shadow", "poster", "depth"], ["Good for flat editorial graphics and bold labels."]),
            [typeof(StrokeEffect)] = Metadata(["outline", "poster", "graphic"], ["Pair with FlatShadow for sticker-like typography or icon treatments."]),
            [typeof(DiagonalClipping)] = Metadata(["clip", "mask", "diagonal"], ["Width: zero keeps one side, positive keeps a band, negative removes it."]),
            [typeof(HighContrast)] = Metadata(["color", "grade", "contrast"], []),
            [typeof(HueRotate)] = Metadata(["color", "grade", "palette"], []),
            [typeof(LumaColor)] = Metadata(["color", "luma", "grade"], []),
            [typeof(Saturate)] = Metadata(["color", "grade", "palette"], []),
            [typeof(Threshold)] = Metadata(["color", "graphic", "poster"], []),
            [typeof(EdgeDetection)] = Metadata(["outline", "line-art", "mask"], []),
            [typeof(Brightness)] = Metadata(["color", "grade", "glow"], []),
            [typeof(OpacityEffect)] = Metadata(["color", "opacity", "fade"], ["Multiplies alpha. To fade parts in or out one after another, put it inside DelayAnimationEffect and keyframe Opacity (for example 0 to 100); a constant Opacity dims every part at once."]),
            [typeof(Sharpen)] = Metadata(["detail", "clarity", "edge"], []),
            [typeof(Gamma)] = Metadata(["color", "grade"], []),
            [typeof(ColorGrading)] = Metadata(["color", "grade", "cinematic"], []),
            [typeof(Curves)] = Metadata(["color", "grade", "cinematic"], []),
            [typeof(FilmGrain)] = Metadata(["grain", "noise", "film", "texture"], []),
            [typeof(Vignette)] = Metadata(["vignette", "darken", "edges", "grading"], ["Positive Amount values darken the edges. Negative values lighten the edges."]),
            [typeof(Invert)] = Metadata(["color", "graphic", "negative"], []),
            [typeof(LutEffect)] = Metadata(["color", "grade", "lut"], ["Requires a LUT source to have visible effect."]),
            [typeof(BlendEffect)] = Metadata(["composite", "blend", "layer"], []),
            [typeof(Negaposi)] = Metadata(["color", "negative", "graphic"], []),
            [typeof(ChromaKey)] = Metadata(["keying", "transparent", "video"], []),
            [typeof(ColorKey)] = Metadata(["keying", "transparent", "graphic"], []),
            [typeof(LumaKey)] = Metadata(["keying", "transparent", "brightness"], []),
            [typeof(SplitEffect)] = Metadata(["glitch", "split", "stylize"], []),
            [typeof(PartsSplitEffect)] = Metadata(["glitch", "split", "stylize"], []),
            [typeof(RepeatEffect)] = Metadata(["tile", "pattern", "scroll"], []),
            [typeof(TransformEffect)] = Metadata(["motion", "distort", "transform"], []),
            [typeof(MosaicEffect)] = Metadata(["glitch", "pixel", "stylize"], []),
            [typeof(ColorShift)] = Metadata(["glitch", "chromatic", "stylize"], []),
            [typeof(LensChromaticAberration)] = Metadata(["chromatic", "aberration", "lens", "stylize"],
                ["Amount is the percentage change in scale. Positive values expand red and contract blue. Negative values swap the fringe colors.",
                 "Center sets the origin of the scaling relative to the input bounds."]),
            [typeof(ShakeEffect)] = Metadata(["motion", "glitch", "shake"], ["Time-dependent effect; useful for animated jitter without explicit keyframes."]),
            [typeof(DisplacementMapEffect)] = Metadata(["distort", "map", "motion"], ["Pair with a map source or generated texture for visible displacement."]),
            [typeof(MeshWarp)] = Metadata(["distort", "warp", "mesh", "motion"], ["Rows and Columns count mesh cells. Keyframe Mesh to animate the control points together, and use Strength as a percentage of the deformation."]),
            [typeof(PolarCoordinates)] = Metadata(["distort", "polar", "ring", "warp"], ["Amount is a percentage; 0 is identity."]),
            [typeof(PathFollowEffect)] = Metadata(["motion", "path", "distort"], []),
            [typeof(LayerEffect)] = Metadata(["composite", "layer"], []),
            [typeof(DelayAnimationEffect)] = Metadata(["motion", "trail", "delay", "stagger"], ["Runs its child effect on each part (TextBlock.SplitByCharacters glyphs, PartsSplitEffect parts, group children) with a delay per step. Order picks which part goes first: Forward, Reverse, FromCenter, Random (RandomSeed), or by position."]),
            [typeof(PixelSortEffect)] = Metadata(["glitch", "pixel", "scanline", "gpu"], ["Runs on the Vulkan shader backend, which falls back to the bundled SwiftShader software rasterizer when no hardware GPU is present, so it stays active; the software path is slower."]),
            [typeof(CSharpScriptEffect)] = Metadata(["advanced", "script", "programmable", "glsl", "multi-pass"], ["Low-level fallback when C#/CustomEffect is permitted. Several GLSL passes without C# are a NodeGraphFilterEffect built with edit_node_graph; see get_node_graph_guide. This is not a declarative GPU-pass API. Inside Context.CustomEffect, CreateGlslShader(fragmentSource, inputCount) and shader.Render(execution, inputs, outputBounds, pushConstants) compose GPU passes. The caller owns returned targets; dispose intermediates, preserve the source on an empty preview result, and return the final target through execution.ForEach. Use the destination-callback overload for clamped size/scale. Keep shader source constant and pass changing values as push constants; GLSL inputs are linear premultiplied RGBA."]),
            [typeof(SKSLScriptEffect)] = Metadata(["advanced", "shader", "programmable", "organic", "procedural"], ["Requires shader source. Prefer for organic heat, ink, glass, smoke, grain, caustic, or procedural fields when blurred gradients look flat; call validate_shader to compile-check the script before apply_edit, since a compile error makes the effect a no-op: the source passes through unchanged."]),
            [typeof(GLSLScriptEffect)] = Metadata(["advanced", "shader", "gpu"], ["Needs GLSL shader source; runs on the Vulkan shader backend (hardware GPU, MoltenVK, or the bundled SwiftShader software fallback), so it does not require a dedicated GPU."]),
            [typeof(NodeGraphFilterEffect)] = Metadata(["advanced", "nodegraph", "programmable", "glsl", "multi-pass", "gpu"], ["Holds a node graph: GLSL shader passes with any number of inputs and intermediate images, and effect nodes, composed without C#. Add it with apply_edit, then build its graph with edit_node_graph; get_node_graph_guide has the contract and examples. A new one holds an empty graph, which passes the layer through."])
        };
    }

    private static EffectMetadata Metadata(IReadOnlyList<string> tags, IReadOnlyList<string> notes)
    {
        return new EffectMetadata(tags.Append("effect").Distinct(StringComparer.Ordinal).ToArray(), notes.ToArray());
    }
}
