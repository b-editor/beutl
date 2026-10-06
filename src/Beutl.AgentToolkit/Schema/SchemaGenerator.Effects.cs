using Beutl.AgentToolkit.Common;
using Beutl.Graphics.Effects;
using Beutl.NodeGraph;
using Beutl.Services;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class SchemaGenerator
{
    public IReadOnlyList<EffectSummary> ListEffects(string? intent = null, bool includePropertyNames = true)
    {
        TypeRegistration.EnsureRegistered();
        return EnumerateRegisteredTypes()
            .Where(item => MatchesCategory(KnownLibraryItemFormats.FilterEffect, item.Category))
            .Select(item => item.Type)
            .Distinct()
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

    private static Type? ResolveFilterEffectType(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string trimmed = token.Trim();
        return EnumerateRegisteredTypes()
            .Where(item => MatchesCategory(KnownLibraryItemFormats.FilterEffect, item.Category))
            .Select(item => item.Type)
            .Distinct()
            .FirstOrDefault(type =>
                string.Equals(type.Name, trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.FullName, trimmed, StringComparison.Ordinal)
                || string.Equals(IdentityHelper.WriteDiscriminator(type), trimmed, StringComparison.Ordinal));
    }

    private static EffectSummary CreateEffectSummary(Type type, bool includePropertyNames)
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
        if (lower.Contains("blur", StringComparison.Ordinal)
            || lower.Contains("shadow", StringComparison.Ordinal)
            || lower.Contains("stroke", StringComparison.Ordinal))
        {
            tags.AddRange(["glow", "depth", "outline"]);
        }

        if (lower.Contains("color", StringComparison.Ordinal)
            || lower.Contains("hue", StringComparison.Ordinal)
            || lower.Contains("saturate", StringComparison.Ordinal)
            || lower.Contains("brightness", StringComparison.Ordinal)
            || lower.Contains("contrast", StringComparison.Ordinal)
            || lower.Contains("gamma", StringComparison.Ordinal)
            || lower.Contains("threshold", StringComparison.Ordinal)
            || lower.Contains("invert", StringComparison.Ordinal)
            || lower.Contains("curve", StringComparison.Ordinal)
            || lower.Contains("luma", StringComparison.Ordinal))
        {
            tags.AddRange(["color", "grade"]);
        }

        if (lower.Contains("mosaic", StringComparison.Ordinal)
            || lower.Contains("pixel", StringComparison.Ordinal)
            || lower.Contains("shift", StringComparison.Ordinal)
            || lower.Contains("shake", StringComparison.Ordinal)
            || lower.Contains("split", StringComparison.Ordinal))
        {
            tags.AddRange(["glitch", "stylize"]);
        }

        if (lower.Contains("key", StringComparison.Ordinal))
        {
            tags.AddRange(["keying", "transparent"]);
        }

        if (lower.Contains("transform", StringComparison.Ordinal)
            || lower.Contains("displacement", StringComparison.Ordinal)
            || lower.Contains("path", StringComparison.Ordinal)
            || lower.Contains("delay", StringComparison.Ordinal)
            || lower.Contains("layer", StringComparison.Ordinal)
            || lower.Contains("blend", StringComparison.Ordinal))
        {
            tags.AddRange(["motion", "composite"]);
        }

        if (lower.Contains("script", StringComparison.Ordinal)
            || lower.Contains("nodegraph", StringComparison.Ordinal))
        {
            tags.AddRange(["advanced", "programmable"]);
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

    private static EffectMetadata GetEffectMetadataByName(string typeName)
    {
        KeyValuePair<Type, EffectMetadata> pair = s_effectMetadata
            .FirstOrDefault(item => string.Equals(item.Key.Name, typeName, StringComparison.Ordinal));
        return pair.Value ?? new EffectMetadata(InferEffectTags(typeName), []);
    }

    private static Dictionary<Type, EffectMetadata> CreateEffectMetadata()
    {
        return new Dictionary<Type, EffectMetadata>
        {
            [typeof(Blur)] = Metadata(["soften", "glow", "depth"], ["Use inside FilterEffectGroup before shadow/color effects for soft halos."]),
            [typeof(DropShadow)] = Metadata(["shadow", "glow", "depth"], ["Use zero offset for neon glow, non-zero offset for cast shadows."]),
            [typeof(InnerShadow)] = Metadata(["shadow", "depth", "inset"], []),
            [typeof(FlatShadow)] = Metadata(["shadow", "poster", "depth"], ["Good for flat editorial graphics and bold labels."]),
            [typeof(StrokeEffect)] = Metadata(["outline", "poster", "graphic"], ["Pair with FlatShadow for sticker-like typography or icon treatments."]),
            [typeof(HighContrast)] = Metadata(["color", "grade", "contrast"], []),
            [typeof(HueRotate)] = Metadata(["color", "grade", "palette"], []),
            [typeof(LumaColor)] = Metadata(["color", "luma", "grade"], []),
            [typeof(Saturate)] = Metadata(["color", "grade", "palette"], []),
            [typeof(Threshold)] = Metadata(["color", "graphic", "poster"], []),
            [typeof(Brightness)] = Metadata(["color", "grade", "glow"], []),
            [typeof(Gamma)] = Metadata(["color", "grade"], []),
            [typeof(ColorGrading)] = Metadata(["color", "grade", "cinematic"], []),
            [typeof(Curves)] = Metadata(["color", "grade", "cinematic"], []),
            [typeof(Invert)] = Metadata(["color", "graphic", "negative"], []),
            [typeof(LutEffect)] = Metadata(["color", "grade", "lut"], ["Requires a LUT source to have visible effect."]),
            [typeof(BlendEffect)] = Metadata(["composite", "blend", "layer"], []),
            [typeof(Negaposi)] = Metadata(["color", "negative", "graphic"], []),
            [typeof(ChromaKey)] = Metadata(["keying", "transparent", "video"], []),
            [typeof(ColorKey)] = Metadata(["keying", "transparent", "graphic"], []),
            [typeof(SplitEffect)] = Metadata(["glitch", "split", "stylize"], []),
            [typeof(PartsSplitEffect)] = Metadata(["glitch", "split", "stylize"], []),
            [typeof(TransformEffect)] = Metadata(["motion", "distort", "transform"], []),
            [typeof(MosaicEffect)] = Metadata(["glitch", "pixel", "stylize"], []),
            [typeof(ColorShift)] = Metadata(["glitch", "chromatic", "stylize"], []),
            [typeof(ShakeEffect)] = Metadata(["motion", "glitch", "shake"], ["Time-dependent effect; useful for animated jitter without explicit keyframes."]),
            [typeof(DisplacementMapEffect)] = Metadata(["distort", "map", "motion"], ["Pair with a map source or generated texture for visible displacement."]),
            [typeof(PathFollowEffect)] = Metadata(["motion", "path", "distort"], []),
            [typeof(LayerEffect)] = Metadata(["composite", "layer"], []),
            [typeof(DelayAnimationEffect)] = Metadata(["motion", "trail", "delay"], []),
            [typeof(PixelSortEffect)] = Metadata(["glitch", "pixel", "scanline", "gpu"], ["Runs on the Vulkan shader backend, which falls back to the bundled SwiftShader software rasterizer when no hardware GPU is present, so it stays active; the software path is slower."]),
            [typeof(CSharpScriptEffect)] = Metadata(["advanced", "script", "programmable", "glsl", "multi-pass"], ["Low-level fallback when built-in composition and declarative shader effects cannot express the required passes, and C#/CustomEffect is permitted. This is not a declarative GPU-pass API. Inside Context.CustomEffect, CreateGlslShader(fragmentSource, inputCount) and shader.Render(execution, inputs, outputBounds, pushConstants) compose GPU passes. The caller owns returned targets; dispose intermediates, preserve the source on an empty preview result, and return the final target through execution.ForEach. Use the destination-callback overload for clamped size/scale. Keep shader source constant and pass changing values as push constants; GLSL inputs are linear premultiplied RGBA."]),
            [typeof(SKSLScriptEffect)] = Metadata(["advanced", "shader", "programmable", "organic", "procedural"], ["Requires shader source. Prefer for organic heat, ink, glass, smoke, grain, caustic, or procedural fields when blurred gradients look flat; call validate_shader to compile-check the script before apply_edit, since a compile error makes the effect a no-op: the source passes through unchanged."]),
            [typeof(GLSLScriptEffect)] = Metadata(["advanced", "shader", "gpu"], ["Needs GLSL shader source; runs on the Vulkan shader backend (hardware GPU, MoltenVK, or the bundled SwiftShader software fallback), so it does not require a dedicated GPU."]),
            [typeof(NodeGraphFilterEffect)] = Metadata(["advanced", "nodegraph", "programmable"], ["Requires a node graph resource to be useful."])
        };
    }

    private static EffectMetadata Metadata(IReadOnlyList<string> tags, IReadOnlyList<string> notes)
    {
        return new EffectMetadata(tags.Append("effect").Distinct(StringComparer.Ordinal).ToArray(), notes.ToArray());
    }
}
