using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Design;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record DerivePaletteResponse(
    string SchemaVersion,
    DerivedPalette Palette,
    IReadOnlyList<PaletteRepeatWarning> Warnings,
    string DirectionReasonStatus,
    string UsageHint);

public sealed record BackgroundGrammarResponse(
    string SchemaVersion,
    IReadOnlyList<BackgroundDepthBand> MinimumDepthBands,
    BackgroundLayerSlot BaseLayer,
    IReadOnlyList<BackgroundLayerSlot> DepthLayers,
    BackgroundMotionSlot Motion,
    IReadOnlyList<string> DerivationRules,
    IReadOnlyList<string> DeviationRules,
    string UsageHint);

public sealed record BackgroundDepthBand(
    string Name,
    string RequiredRole,
    string MinimumVisibleContribution);

public sealed record BackgroundLayerSlot(
    string Slot,
    string RequiredCount,
    IReadOnlyList<BackgroundGrammarOption> Options,
    string DerivationHint);

public sealed record BackgroundMotionSlot(
    string Slot,
    string RequiredCount,
    IReadOnlyList<BackgroundGrammarOption> Options,
    string DerivationHint);

public sealed record BackgroundGrammarOption(
    string Name,
    string Description,
    IReadOnlyList<BackgroundParameterRange> Parameters);

public sealed record BackgroundParameterRange(
    string Name,
    string Type,
    string Range,
    string DerivationHint);

[McpServerToolType]
public sealed class DesignTools(AgentSessionManager sessions) : ToolBase
{
    [McpServerTool(Name = "derive_palette")]
    [Description("Derives a deterministic role-tagged palette from a brief-derived base hue, tonal seed, and harmony scheme. No bundled fixed palettes are returned. The output guarantees text-primary contrast >= 4.5:1 against bg-base and bg-accent, and foreground/accent contrast >= 3.0:1 against bg-base by construction. The response includes recent creative-memory warnings when the hue band or supplied structural signature repeats recent work.")]
    public ToolResult<DerivePaletteResponse> DerivePalette(
        [Description("Brief-derived base hue in degrees. Values wrap into 0..360; use the direction notes to explain why the subject led to this hue.")]
        double baseHueDegrees,
        [Description("Brief-derived tonal seed: dark, light, or balanced.")]
        string tonalSeed = "dark",
        [Description("Harmony scheme: analogous, complementary, split-complementary, triadic, tetradic, or monochromatic.")]
        string harmonyScheme = "analogous",
        [Description("Brief-derived saturation seed from 0..1. Values are clamped to the quality band 0.18..0.72.")]
        double saturation = 0.58,
        [Description("Recorded reason explaining why the subject, mood, and keywords led to the supplied hue, tone, and motion vocabulary. Skills require this in notes before authoring.")]
        string? derivationReason = null,
        [Description("Optional authored structural signature, such as diagonal editorial grid or sequential poster stack. Used for deterministic anti-repeat warnings against creative memory.")]
        string? structuralSignature = null)
    {
        return Execute(() =>
        {
            DerivedPalette palette;
            try
            {
                palette = ColorHarmonyEngine.Derive(baseHueDegrees, tonalSeed, harmonyScheme, saturation);
            }
            catch (ArgumentException ex)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    ex.Message,
                    ex.ParamName,
                    "Use harmonyScheme=analogous|complementary|split-complementary|triadic|tetradic|monochromatic and tonalSeed=dark|light|balanced."));
            }

            List<PaletteRepeatWarning> warnings = ColorHarmonyEngine
                .FindRepeatWarnings(palette, structuralSignature, sessions.GetRecentCreativeFingerprints())
                .ToList();
            string reasonStatus;
            if (string.IsNullOrWhiteSpace(derivationReason))
            {
                reasonStatus = "missing";
                warnings.Insert(0, new PaletteRepeatWarning(
                    "derivationReason",
                    "missing",
                    [],
                    "No derivation reason was supplied. The bundled skills disallow using this palette until notes record why the brief led to this hue, tone, and motion vocabulary."));
            }
            else
            {
                reasonStatus = "recorded";
            }

            return new DerivePaletteResponse(
                SchemaVersion.Current,
                palette,
                warnings,
                reasonStatus,
                "Use roles directly as bg-base, bg-accent, foreground, text-primary, and accent. Do not copy the numeric colors into future briefs; derive a fresh base hue and tonal seed from each brief, then call this tool again. If warnings are present, revise the direction or record why the repeat is intentional before apply_edit.");
        });
    }

    [McpServerTool(Name = "get_background_grammar")]
    [Description("Returns the parametric background recipe grammar for motion graphics. This is not finished JSON and contains no fixed look pack: agents derive concrete values from the brief after derive_palette.")]
    public ToolResult<BackgroundGrammarResponse> GetBackgroundGrammar(
        [Description("Optional brief excerpt. The response stays a grammar, but the usage hint reminds the agent to derive concrete values from this brief.")]
        string? brief = null)
    {
        return Execute(() => new BackgroundGrammarResponse(
            SchemaVersion.Current,
            [
                new BackgroundDepthBand("background", "base layer", "Full-frame surface is visible behind all shots."),
                new BackgroundDepthBand("midground", "one depth layer", "At least one texture, particle, vignette, or geometric system crosses the frame without competing with text."),
                new BackgroundDepthBand("foreground", "one readable focal/accent layer", "At least one foreground or accent system is visible per designed beat, separate from the base surface.")
            ],
            new BackgroundLayerSlot(
                "base layer",
                "exactly one",
                [
                    new BackgroundGrammarOption(
                        "multi-stop gradient",
                        "A full-frame gradient surface using palette roles, with soft falloff instead of a hard two-stop ramp.",
                        [
                            new BackgroundParameterRange("stopCount", "integer", "3..7", "Derive from complexity: quiet briefs use 3-4, energetic briefs use 5-7."),
                            new BackgroundParameterRange("hueOffsetsDegrees", "number[]", "-45..45 from bg-base/bg-accent", "Use derive_palette harmony hues; do not invent unrelated colors."),
                            new BackgroundParameterRange("stopOffsets", "number[]", "0..1 increasing", "Cluster stops around focal depth changes, not at uniform thirds by default."),
                            new BackgroundParameterRange("alpha", "number", "0.55..1.0", "Lower when foreground needs calmer readability."),
                            new BackgroundParameterRange("blurRadius", "number", "0..48", "Use higher blur for atmospheric depth, not for text backing.")
                        ]),
                    new BackgroundGrammarOption(
                        "shader",
                        "A procedural SKSL surface for material fields such as grain, ink, heat, glass, smoke, caustics, or paper fibers.",
                        [
                            new BackgroundParameterRange("shaderFamily", "enum", "grain|ink|heat|glass|smoke|caustic|paper-fiber", "Choose from the subject material, not from a default favorite."),
                            new BackgroundParameterRange("scale", "number", "0.25..4.0", "Small scale for texture, large scale for broad atmospheric fields."),
                            new BackgroundParameterRange("amplitude", "number", "0.03..0.35", "Keep subtle when text must stay primary."),
                            new BackgroundParameterRange("colorSource", "enum", "bg-base|bg-accent|accent-subtle", "Start from derived palette roles and modulate base.rgb rather than imposing a fixed color."),
                            new BackgroundParameterRange("validation", "tool", "validate_shader required for custom SKSL", "Compile-check before apply_edit.")
                        ])
                ],
                "Pick gradient when the brief calls for editorial clarity; pick shader when the subject implies material, atmosphere, or organic motion."),
            [
                new BackgroundLayerSlot(
                    "depth layer A",
                    "one required",
                    [
                        new BackgroundGrammarOption(
                            "particles",
                            "Small repeated marks, grains, nodes, sparks, or fragments that establish midground scale and rhythm.",
                            [
                                new BackgroundParameterRange("count", "integer", "24..180", "Derive from tempo and density; never use count as filler without a named material."),
                                new BackgroundParameterRange("sizePx", "number", "1..18", "Keep most particles below caption height."),
                                new BackgroundParameterRange("opacity", "number", "0.08..0.45", "Stay below text-primary contrast."),
                                new BackgroundParameterRange("zBand", "enum", "background|midground", "Prefer midground unless the particles are texture only."),
                                new BackgroundParameterRange("distribution", "enum", "field|diagonal|radial|edge-biased|clustered", "Derive from composition and subject motion.")
                            ]),
                        new BackgroundGrammarOption(
                            "geometric accents",
                            "Parseable lines, brackets, masks, vector fragments, crop marks, rings, or panels with a named job.",
                            [
                                new BackgroundParameterRange("count", "integer", "2..24", "Use fewer large accents for calm briefs, more small accents for kinetic briefs."),
                                new BackgroundParameterRange("strokeWidthPx", "number", "1..12", "Tie to hierarchy; thin marks support, thick marks become focal."),
                                new BackgroundParameterRange("opacity", "number", "0.12..0.70", "Raise only when the accent is a focal foreground layer."),
                                new BackgroundParameterRange("scale", "number", "0.05..1.40 of frame", "Avoid anonymous blobs; make the figure readable as a system."),
                                new BackgroundParameterRange("zBand", "enum", "midground|foreground", "Foreground accents must not compete with hero text unless that is the point.")
                            ]),
                        new BackgroundGrammarOption(
                            "vignette",
                            "Soft edge or focal falloff used to control readability and depth.",
                            [
                                new BackgroundParameterRange("strength", "number", "0.08..0.42", "Use the smallest value that improves hierarchy."),
                                new BackgroundParameterRange("radius", "number", "0.45..1.20 of frame diagonal", "Tight for focal reveals, wide for ambient polish."),
                                new BackgroundParameterRange("center", "point", "safe-area normalized 0.15..0.85", "Align with the planned focal point."),
                                new BackgroundParameterRange("colorRole", "enum", "bg-base|shadow|accent-muted", "Keep it inside derived palette roles.")
                            ])
                    ],
                    "Depth layer A must make the midground visible; it cannot be only a second flat full-frame plate."),
                new BackgroundLayerSlot(
                    "depth layer B",
                    "zero or one",
                    [
                        new BackgroundGrammarOption(
                            "particles",
                            "A second, quieter particle scale for foreground sparkle or background grain.",
                            [
                                new BackgroundParameterRange("count", "integer", "8..80", "Use only when it adds a second scale band."),
                                new BackgroundParameterRange("sizePx", "number", "2..32", "Differentiate from depth layer A."),
                                new BackgroundParameterRange("opacity", "number", "0.06..0.32", "Keep subordinate unless it is a transition accent."),
                                new BackgroundParameterRange("motionOffsetPx", "number", "2..80", "Tie to parallax or beat accents.")
                            ]),
                        new BackgroundGrammarOption(
                            "geometric accents",
                            "A second structural layer such as masks, crop marks, or letter fragments.",
                            [
                                new BackgroundParameterRange("count", "integer", "1..12", "Each accent needs a named visual job."),
                                new BackgroundParameterRange("role", "enum", "transition|texture|frame|focal-support", "Record the role in Element/Object names."),
                                new BackgroundParameterRange("opacity", "number", "0.10..0.55", "Keep it below the focal role unless it is the focal transition.")
                            ]),
                        new BackgroundGrammarOption(
                            "vignette",
                            "A second focal-control pass when the base and first depth layer reduce readability.",
                            [
                                new BackgroundParameterRange("strength", "number", "0.04..0.20", "Only as corrective hierarchy control."),
                                new BackgroundParameterRange("blend", "enum", "multiply|screen|normal", "Derive from light/dark tonal seed.")
                            ])
                    ],
                    "Use a second depth layer only when it creates a distinct scale, z-band, or material purpose.")
            ],
            new BackgroundMotionSlot(
                "motion",
                "one required for motion graphics unless the brief explicitly calls for a static poster",
                [
                    new BackgroundGrammarOption(
                        "drift",
                        "Slow continuous offset, opacity, shader phase, or gradient-center change that keeps the base alive.",
                        [
                            new BackgroundParameterRange("durationBeats", "number", "4..32", "Longer than foreground beats; background drift must not replace foreground events."),
                            new BackgroundParameterRange("offsetPx", "number", "4..120", "Derive from frame size and emotional temperature."),
                            new BackgroundParameterRange("phaseOffset", "number", "0..1", "Stagger repeated layers so they do not move as one sheet."),
                            new BackgroundParameterRange("easing", "enum", "linear|sine-in-out|cubic-in-out", "Avoid harsh easing for background-only drift.")
                        ]),
                    new BackgroundGrammarOption(
                        "parallax",
                        "Separate background, midground, and foreground bands move at different amplitudes or speeds.",
                        [
                            new BackgroundParameterRange("bandCount", "integer", "3 minimum", "Must cover background, midground, and foreground."),
                            new BackgroundParameterRange("speedRatio", "number[]", "1:1.5..3.5 foreground/background", "Foreground accents move more than background texture."),
                            new BackgroundParameterRange("directionDegrees", "number", "0..360", "Derive from the shot's compositional axis."),
                            new BackgroundParameterRange("maxOffsetPx", "number", "12..180", "Keep text and backing plates aligned; do not parallax required reading text.")
                        ])
                ],
                string.IsNullOrWhiteSpace(brief)
                    ? "Derive motion vocabulary from the recorded direction reason before authoring."
                    : $"Derive motion vocabulary from this brief excerpt and record the reason before authoring: {brief.Trim()}"),
            [
                "Record the brief-to-direction reason first: subject -> hue/tone -> material -> motion vocabulary.",
                "Call derive_palette before instantiating any color values.",
                "Instantiate at least three depth bands: background, midground, foreground.",
                "Use palette role names in notes and Element/Object names before converting to concrete #AARRGGBB values.",
                "Concrete values must come from the brief, beat grid, frame size, message hierarchy, and derived palette; this grammar is not a finished patch."
            ],
            [
                "Any missing depth band requires a recorded reason.",
                "Any skipped derive_palette call requires a recorded reason.",
                "Any use of colors outside the derived palette roles requires a recorded reason and a contrast check.",
                "Any static background in motion graphics requires a recorded reason."
            ],
            "Use this as a slot grammar: choose one base option, one required depth layer, optional second depth layer, and one motion option, then derive concrete parameters from the brief. Do not copy the ranges as final values and do not treat this response as JSON to paste into apply_edit."));
    }
}
