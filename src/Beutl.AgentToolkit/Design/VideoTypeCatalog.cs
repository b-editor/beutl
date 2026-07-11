using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;

namespace Beutl.AgentToolkit.Design;

public sealed record VideoTypeProfile(
    string Name,
    string Description,
    string WhenToUse,
    IReadOnlyList<string> BriefSignals,
    IReadOnlyList<string> WorkflowSteps,
    VideoTypeGateProfile GateProfile)
{
    public VideoTypeSummary ToSummary()
        => new(Name, Description, WhenToUse, BriefSignals);
}

public sealed record VideoTypeSummary(
    string Name,
    string Description,
    string WhenToUse,
    IReadOnlyList<string> BriefSignals);

public sealed record VideoTypeGateProfile(
    bool ImpliedAllowStillness = false,
    bool ImpliedAllowMinimalDensity = false,
    bool ForceMotionGraphicsIntentOff = false,
    bool SuppressTempoAnalysis = false,
    bool SuppressTempoUnlessExplicitHighTempo = false,
    bool SuppressBackgroundRichness = false,
    bool SuppressPaletteBalance = false,
    bool SuppressLayerDensityPlanGate = false,
    bool SuppressCaptionRoleHierarchy = false,
    bool SuppressCutRhythm = false,
    bool RewordCutRhythmForTransitions = false,
    bool RunTransitionVocabulary = false,
    bool RunTimelineCoverage = false,
    bool RunLogoIntroMotionArc = false)
{
    public static VideoTypeGateProfile None { get; } = new();

    public IReadOnlyList<string> DescribeAdjustments()
    {
        List<string> adjustments = [];
        if (ImpliedAllowStillness)
        {
            adjustments.Add("allowStillness");
        }

        if (ImpliedAllowMinimalDensity)
        {
            adjustments.Add("allowMinimalDensity");
        }

        if (ForceMotionGraphicsIntentOff)
        {
            adjustments.Add("motionGraphicsIntent off");
        }

        if (SuppressTempoAnalysis)
        {
            adjustments.Add("tempo/beat-grid checks skipped");
        }
        else if (SuppressTempoUnlessExplicitHighTempo)
        {
            adjustments.Add("tempo/beat-grid checks require explicit high-tempo styleProfile");
        }

        if (SuppressBackgroundRichness)
        {
            adjustments.Add("background-richness advisories skipped");
        }

        if (SuppressPaletteBalance)
        {
            adjustments.Add("paletteBalance advisory skipped");
        }

        if (SuppressLayerDensityPlanGate)
        {
            adjustments.Add("motion-graphics layer-density plan gate skipped");
        }

        if (SuppressCaptionRoleHierarchy)
        {
            adjustments.Add("caption-role hierarchy overload advisories skipped");
        }

        if (SuppressCutRhythm)
        {
            adjustments.Add("cut-rhythm advisories skipped");
        }
        else if (RewordCutRhythmForTransitions)
        {
            adjustments.Add("cut-rhythm advisories framed as transition consistency");
        }

        if (RunTransitionVocabulary)
        {
            adjustments.Add("transitionVocabulary advisory enabled");
        }

        if (RunTimelineCoverage)
        {
            adjustments.Add("timelineCoverage advisory enabled");
        }

        if (RunLogoIntroMotionArc)
        {
            adjustments.Add("logo-intro motionArc advisory enabled");
        }

        return adjustments.Count == 0 ? ["none"] : adjustments;
    }
}

public static class VideoTypeCatalog
{
    private static readonly VideoTypeProfile[] s_profiles =
    [
        new(
            "motion-graphics",
            "Authored vector, text, and effect-driven motion graphics.",
            "Use for original graphic promos, kinetic type, abstract scenes, explainers, and authored visual systems where beat grids, layered backgrounds, and foreground density are expected.",
            [
                "Brief asks for motion graphics, kinetic type, promo graphics, infographic motion, or authored visual effects.",
                "No source footage/photo inventory is the main driver.",
                "Brief mentions BPM, beat grid, layered background grammar, or foreground visual density."
            ],
            [
                "Call list_creative_directions for optional divergence stimulus, then record the authored concept before calling derive_palette.",
                "Call derive_palette with the brief-derived hue, tonal seed, harmony scheme, derivationReason, and structuralSignature.",
                "Call get_background_grammar and instantiate background, midground, foreground, and motion slots in notes before apply_edit.",
                "Call get_schema for any drawable, GeometryShape, media, effect, brush, or animation types whose property names are not already known.",
                "Build a beatGridPlan and quantitativePlanSheet before apply_edit when the brief names BPM, high tempo, or short kinetic beats.",
                "Record a cameraPlan before apply_edit: per shot choose locked, push-in, pull-back, pan, whip-pan, roll, or parallax, then author moves as animated transforms on a named [role:camera-rig] DrawableGroup so shots do not read as static slides — either nesting the shot's content as Children or pulling the contiguous timeline layers above the rig with PortalObject.Count (see get_examples insert-camera-rig-push-in / insert-camera-rig-portal).",
                "Use apply_edit in storyboard-first stages: background/surface, foreground structure, typography, then text backing plates.",
                "Call render_storyboard and preview_quality_risks with videoType:\"motion-graphics\" before adding effects or motion.",
                "Call list_effect_recipes, get_effect_recipe, and validate_shader when effect chains or SKSL fields are part of the look.",
                "Prefer real capability types over fakes: ParticleEmitter for particle fields, AudioWaveformDrawable/AudioSpectrumDrawable for music-reactive layers, TextBlock.SplitByCharacters for kinetic type, Rotation3DTransform for perspective moves, Pen.TrimStart/TrimEnd for line-draw reveals, and BlendMode/Clipping for mattes and wipes — get_schema exposes each surface.",
                "Call evaluate_edit_quality with videoType:\"motion-graphics\" and plannedForegroundElementsPerShot from the quantitativePlanSheet.",
                "Call final_preflight with videoType:\"motion-graphics\", requireAnimatedProperties:true, and the planned density target before export_video."
            ],
            VideoTypeGateProfile.None),
        new(
            "footage-cut",
            "Video-footage-driven cut edits such as vlogs, events, interviews, and source-clip assemblies.",
            "Use when source video or audio clips drive the timeline and the main work is inventory, trim, order, coverage, and overlay text.",
            [
                "Brief supplies or references video files.",
                "User says edit my clips, trim footage, interview, vlog, event recap, or B-roll.",
                "Narrative order, source audio, clip in/out, or music bed is central."
            ],
            [
                "Inventory media under the workspace before planning, then record clip names, duration notes, and usable ranges before apply_edit.",
                "When the workspace lacks needed clips, load beutl-agent-asset-sourcing and record provenance in assets/manifest.json before placing sourced media.",
                "Call get_schema for video, audio, and image source object types instead of guessing media property names.",
                "Build a cut list in notes with clip, in/out, order, target Element Start, and target Element Length before apply_edit.",
                "Use apply_edit to trim with Element Start/Length plus the media source start-offset property discovered from get_schema.",
                "Use read_document_summary after each cut-list stage to confirm narrative order, timing, and one EngineObject per ordinary Element.",
                "Handle audio explicitly in notes and apply_edit: source audio vs music bed, intended levels, and where overlays must not mask speech.",
                "Use measure_object_bounds for sparse text overlays, then keep overlay copy short enough for evaluate_edit_quality read-time checks.",
                "Call preview_quality_risks, suggest_quality_fixes, evaluate_edit_quality, and final_preflight with videoType:\"footage-cut\".",
                "Review the timelineCoverage advisory from quality tools and close unintended empty visible gaps before export_video.",
                "Skip derive_palette and get_background_grammar unless authored graphic overlays are requested; record that reason when you skip them."
            ],
            new VideoTypeGateProfile(
                ForceMotionGraphicsIntentOff: true,
                SuppressTempoUnlessExplicitHighTempo: true,
                SuppressBackgroundRichness: true,
                SuppressPaletteBalance: true,
                RunTransitionVocabulary: true,
                RunTimelineCoverage: true)),
        new(
            "slideshow",
            "Photo or still-image movies with music, captions, gentle motion, and consistent transitions.",
            "Use when still images are the source material and the edit needs ordering, per-photo durations, caption timing, and restrained Ken Burns-style motion.",
            [
                "Brief mentions photos plus music, photo movie, slideshow, album, memories, or still images.",
                "Per-photo duration, transition vocabulary, or caption read time is central.",
                "Stillness and sparse composition are expected rather than failures."
            ],
            [
                "Collect and order images with a rationale such as chronology, theme, or emotional progression before apply_edit.",
                "When photos are not supplied, load beutl-agent-asset-sourcing to source or generate the image collection and record provenance before apply_edit.",
                "Define a per-photo duration grid in notes, typically 2.5-4 seconds per image unless the brief requires otherwise.",
                "Choose one transition vocabulary and reuse it consistently; verify it later with render_storyboard subdivision frames.",
                "Call get_schema for image source and transform properties before authoring Ken Burns motion.",
                "Use apply_edit to create one or more image Elements per photo with explicit Start/Length values from the duration grid.",
                "Add minimal Ken Burns-style motion with one slow scale or translate direction per photo, alternating direction deliberately.",
                "Use measure_object_bounds for captions and backing plates, and keep captions within evaluate_edit_quality read-time rules.",
                "Call derive_palette only when caption/backing colors need palette support, not as a default background-grammar step.",
                "Call preview_quality_risks, suggest_quality_fixes, evaluate_edit_quality, and final_preflight with videoType:\"slideshow\".",
                "Review timelineCoverage and transition-consistency findings on the subdivided render_storyboard contact sheet before export_video."
            ],
            new VideoTypeGateProfile(
                ImpliedAllowStillness: true,
                ImpliedAllowMinimalDensity: true,
                ForceMotionGraphicsIntentOff: true,
                SuppressTempoAnalysis: true,
                SuppressLayerDensityPlanGate: true,
                RewordCutRhythmForTransitions: true,
                RunTransitionVocabulary: true,
                RunTimelineCoverage: true)),
        new(
            "lyric-captions",
            "Lyric videos, subtitles, captions, and text-synced pieces driven by per-line timing.",
            "Use when line timing, readable captions, and a consistent text role system are the core deliverable.",
            [
                "Brief provides lyrics, subtitles, captions, transcript, or asks for timestamp sync.",
                "The timing grid comes from lines or captions rather than BPM.",
                "Readability, contrast, backing plates, and per-line duration are central."
            ],
            [
                "Obtain or estimate per-line timestamps first, then record the sync table in notes before apply_edit.",
                "When a music bed must be acquired, load beutl-agent-asset-sourcing, record provenance in assets/manifest.json, then call analyze_audio_rhythm.",
                "Call derive_palette for contrast roles used by text, backing plates, and the simple background loop.",
                "Design one text role system in notes: hero line, echo or secondary line, and credit/caption support.",
                "Call get_schema for TextBlock, backing shapes, brushes, and any needed text animation properties before authoring.",
                "Use apply_edit to create one Element per lyric or caption line with Start/Length matching the sync table.",
                "Use measure_object_bounds on representative lines and backing plates before relying on render_still readability.",
                "Verify per-line read time and contrast with preview_quality_risks or evaluate_edit_quality before adding extra motion.",
                "Keep the background a simple consistent loop that never outcompetes the timed text; AudioSpectrumDrawable or AudioWaveformDrawable can render a genuinely music-reactive backdrop from the music bed.",
                "Call preview_quality_risks, suggest_quality_fixes, evaluate_edit_quality, and final_preflight with videoType:\"lyric-captions\"."
            ],
            new VideoTypeGateProfile(
                ImpliedAllowMinimalDensity: true,
                ForceMotionGraphicsIntentOff: true,
                SuppressTempoAnalysis: true,
                SuppressBackgroundRichness: true,
                SuppressCaptionRoleHierarchy: true)),
        new(
            "logo-intro",
            "Short single-shot logo, stinger, or intro animation.",
            "Use for 3-10 second logo reveals or single-subject intro animations where the work is a motion arc rather than many shots.",
            [
                "Brief says logo animation, intro, stinger, bumper, opener, reveal, or brand mark.",
                "Duration is about 3-10 seconds.",
                "Single subject, final lockup, anticipation, reveal, settle, or hold frame is central."
            ],
            [
                "Plan one 3-10 second shot and record the motion arc before authoring: anticipation, reveal, settle, and hold.",
                "Build the static end frame first with apply_edit so the logo lockup is correct before animation.",
                "Call measure_object_bounds to verify the logo, mark, and any type lockup are centered and safely framed.",
                "Call get_schema for transform, opacity, brush, effect, and easing properties before writing keyframes.",
                "Use apply_edit to animate toward the static end frame instead of assembling many separate shots.",
                "Invest in easing quality and secondary detail such as particles, strokes, texture, or subtle material effects.",
                "End on a stable hold frame of at least 1 second and verify it with render_still.",
                "Call render_storyboard with subdivisionLevel:2 or 3 to review the arc inside the single shot.",
                "Call preview_quality_risks, suggest_quality_fixes, evaluate_edit_quality, and final_preflight with videoType:\"logo-intro\".",
                "Do not pass allowStillness unless the brief explicitly accepts a static logo hold; otherwise motionContinuity remains a blocker."
            ],
            new VideoTypeGateProfile(
                ImpliedAllowMinimalDensity: true,
                SuppressTempoAnalysis: true,
                SuppressCutRhythm: true,
                RunLogoIntroMotionArc: true))
    ];

    public static IReadOnlyList<VideoTypeProfile> Profiles => s_profiles;

    public static IReadOnlyList<string> SupportedNames { get; }
        = s_profiles.Select(profile => profile.Name).ToArray();

    public static IReadOnlyList<VideoTypeSummary> Summaries { get; }
        = s_profiles.Select(profile => profile.ToSummary()).ToArray();

    public static VideoTypeProfile Resolve(string? videoType)
    {
        if (string.IsNullOrWhiteSpace(videoType))
        {
            return s_profiles[0];
        }

        string normalized = videoType.Trim();
        VideoTypeProfile? profile = s_profiles.FirstOrDefault(item =>
            string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (profile is not null)
        {
            return profile;
        }

        string supported = string.Join(", ", SupportedNames);
        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"Unknown videoType '{normalized}'. Supported values: {supported}.",
            "videoType",
            $"Use one of: {supported}."));
    }
}
