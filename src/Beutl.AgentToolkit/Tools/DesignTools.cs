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
    IReadOnlyList<BackgroundDepthBand> DepthBands,
    BackgroundLayerSlot BaseLayer,
    IReadOnlyList<BackgroundLayerSlot> DepthLayers,
    BackgroundMotionSlot Motion,
    IReadOnlyList<string> DerivationNotes,
    IReadOnlyList<string> DeviationNotes,
    string UsageHint);

public sealed record BackgroundDepthBand(
    string Name,
    string Role,
    string TypicalContribution);

public sealed record BackgroundLayerSlot(
    string Slot,
    string TypicalCount,
    IReadOnlyList<BackgroundGrammarOption> Options,
    string DerivationHint);

public sealed record BackgroundMotionSlot(
    string Slot,
    string TypicalCount,
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
        [Description("Optional note on why the subject, mood, and keywords led to this hue, tone, and motion vocabulary. Recorded alongside the palette so later runs can tell a deliberate repeat from an accidental one.")]
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
                    "No derivation reason was supplied. Recording why the brief led to this hue and tone makes the anti-repeat check useful across runs, but the palette is usable without it."));
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
                "Use roles directly as bg-base, bg-accent, foreground, text-primary, and accent, or take them as a starting point and depart where the piece needs it. Deriving a fresh base hue per brief rather than reusing these numbers keeps successive runs from converging on one look. Warnings report that the hue band or structure resembles recent work; treat a repeat as intentional or revise it as you see fit.");
        });
    }

    [McpServerTool(Name = "get_background_grammar")]
    [Description("Returns a parametric vocabulary of background slots, options, and parameter ranges for motion graphics. It contains no fixed look pack and no finished JSON — it is a menu to derive from, not a spec to satisfy. Every slot and range is a starting point you can depart from; nothing here is checked or enforced anywhere in the toolkit.")]
    public ToolResult<BackgroundGrammarResponse> GetBackgroundGrammar(
        [Description("Optional brief excerpt. The response stays a grammar; supplying the brief tailors the usage hint toward this piece.")]
        string? brief = null)
    {
        return Execute(() => new BackgroundGrammarResponse(
            SchemaVersion.Current,
            [
                new BackgroundDepthBand("background", "base layer", "A full-frame surface reads behind all shots."),
                new BackgroundDepthBand("midground", "depth layer", "A texture, particle, vignette, or geometric system crosses the frame without competing with text."),
                new BackgroundDepthBand("foreground", "focal/accent layer", "A foreground or accent system reads per designed beat, separate from the base surface.")
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
                            new BackgroundParameterRange("hueOffsetsDegrees", "number[]", "-45..45 from bg-base/bg-accent", "Harmony hues from derive_palette stay coherent with the rest of the palette; unrelated hues widen it."),
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
                                new BackgroundParameterRange("count", "integer", "24..180", "Derive from tempo and density; a count that stands in for a named material tends to read as filler."),
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
                                new BackgroundParameterRange("zBand", "enum", "midground|foreground", "Foreground accents compete with hero text; place them where that competition is the point.")
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
                            new BackgroundParameterRange("durationBeats", "number", "4..32", "Longer than foreground beats, so background drift reads as a bed under the foreground events rather than as one of them."),
                            new BackgroundParameterRange("offsetPx", "number", "4..120", "Derive from frame size and emotional temperature."),
                            new BackgroundParameterRange("phaseOffset", "number", "0..1", "Stagger repeated layers so they do not move as one sheet."),
                            new BackgroundParameterRange("easing", "enum", "linear|sine-in-out|cubic-in-out", "Avoid harsh easing for background-only drift.")
                        ]),
                    new BackgroundGrammarOption(
                        "parallax",
                        "Separate background, midground, and foreground bands move at different amplitudes or speeds.",
                        [
                            new BackgroundParameterRange("bandCount", "integer", "1..5", "Three bands (background, midground, foreground) read as full depth; one or two read as flat and deliberate."),
                            new BackgroundParameterRange("speedRatio", "number[]", "1:1.5..3.5 foreground/background", "Foreground accents move more than background texture."),
                            new BackgroundParameterRange("directionDegrees", "number", "0..360", "Derive from the shot's compositional axis."),
                            new BackgroundParameterRange("maxOffsetPx", "number", "12..180", "Keep text and backing plates aligned; parallaxing text a viewer has to read costs legibility.")
                        ])
                ],
                string.IsNullOrWhiteSpace(brief)
                    ? "Derive motion vocabulary from the piece's own direction rather than from these option names."
                    : $"Derive motion vocabulary from this brief excerpt rather than from these option names: {brief.Trim()}"),
            [
                "Working the direction through subject -> hue/tone -> material -> motion vocabulary keeps the layers coherent with each other.",
                "derive_palette gives colors with the contrast relationships already solved, which saves re-checking them by hand.",
                "Three depth bands (background, midground, foreground) is the density that reads as built rather than assembled; fewer is a legitimate choice for sparse work.",
                "Naming Elements/Objects by palette role before converting to concrete #AARRGGBB values keeps later edits legible.",
                "This grammar is a set of slots and ranges, not a finished patch: concrete values come from the brief, beat grid, frame size, and message hierarchy."
            ],
            [
                "Dropping a depth band trades built-up density for air; expect a flatter, quieter frame.",
                "Hand-picking colors instead of calling derive_palette means the contrast relationships are yours to verify.",
                "Colors outside the derived palette roles widen the palette; check them against the background they sit on.",
                "A static background in motion graphics puts the whole motion budget on the foreground; that reads as deliberate restraint when the foreground carries it."
            ],
            "Use this as a slot grammar: pick a base option, the depth layers you want, and a motion option, then derive concrete parameters from the brief. The ranges are starting points rather than final values, and the response is not JSON to paste into apply_edit."));
    }
}
