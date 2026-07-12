using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Direct cover for <see cref="SkslSnippetMerger"/> (feature 004, C1.3/D2): the whole-word rename that prefixes
/// every top-level declaration of a fused snippet with <c>feN_</c> so two merged snippets cannot redefine a shared
/// global. The regression under test is the swizzle-safety fix: a bare uniform/const/function reference is renamed,
/// but a member/swizzle access on some other value (<c>c.r</c> when a uniform is named <c>r</c>) MUST NOT be — the
/// prior <c>\b</c>-only pattern matched after the <c>.</c> and corrupted the access to <c>c.feN_r</c>.
/// </summary>
[TestFixture]
public class SkslSnippetMergerTests
{
    // A snippet whose uniform name (`r`) also appears as a swizzle member (`c.r`). Only the bare `r` may be renamed.
    private const string SwizzleCollisionSnippet =
        "uniform half r;\nhalf4 apply(half4 c) { return half4(c.r * r, c.g, c.b, c.a); }";

    private const string IdentitySnippet =
        "half4 apply(half4 c) { return c; }";

    // A snippet exercising every top-level declaration kind the merger renames: a uniform, a file-scope const, a
    // helper function, and the apply entry.
    private const string DeclarationsSnippet =
        "uniform float scale;\n"
        + "const float3 LUMA = float3(0.2126, 0.7152, 0.0722);\n"
        + "half4 boost(half4 c) { return half4(c.rgb * scale, c.a); }\n"
        + "half4 apply(half4 c) { return boost(c) * half4(LUMA, 1.0); }";

    // The core regression: a swizzle access sharing a uniform's name is left intact; only the bare reference renames.
    [Test]
    public void Merge_UniformNameCollidingWithSwizzle_RenamesOnlyTheBareReference()
    {
        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(SwizzleCollisionSnippet), SkslSource.Snippet(IdentitySnippet)]);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Does.Contain("uniform half fe0_r;"), "the top-level uniform is prefixed to fe0_r");
            Assert.That(merged, Does.Contain("c.r * fe0_r"),
                "the bare uniform reference renames to fe0_r but the swizzle access c.r stays intact");
            Assert.That(merged, Does.Not.Contain("c.fe0_r"),
                "the swizzle member c.r must never be rewritten to c.fe0_r (the \\b-after-dot regression)");

            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
            Assert.That(error, Is.Null, $"the merged program compiles ({error})");
            Assert.That(effect, Is.Not.Null);
        });
    }

    // Characterization: every top-level declaration kind (uniform, file-scope const, helper, apply) is prefixed feN_.
    [Test]
    public void Merge_TopLevelDeclarations_AreAllPrefixed()
    {
        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(DeclarationsSnippet)]);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Does.Contain("uniform float fe0_scale;"), "the uniform is prefixed");
            Assert.That(merged, Does.Contain("fe0_LUMA"), "the file-scope const is prefixed");
            Assert.That(merged, Does.Contain("fe0_boost"), "the helper function is prefixed");
            Assert.That(merged, Does.Contain("fe0_apply(half4"), "the apply entry is prefixed");

            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
            Assert.That(error, Is.Null, $"the merged program compiles ({error})");
            Assert.That(effect, Is.Not.Null);
        });
    }

    // Characterization: two snippets sharing a global name (both declare `apply` and `scale`) merge without a
    // redefinition because each is prefixed with its own index, and main chains fe0_apply then fe1_apply.
    [Test]
    public void Merge_TwoSnippetsSharingNames_ArePrefixedIndependently()
    {
        string shared = "uniform float scale;\nhalf4 apply(half4 c) { return half4(c.rgb * scale, c.a); }";

        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(shared), SkslSource.Snippet(shared)]);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Does.Contain("fe0_scale").And.Contain("fe1_scale"),
                "each snippet's uniform gets its own index prefix, so the shared name never redefines");
            Assert.That(merged, Does.Contain("_fused = fe0_apply(_fused);").And.Contain("_fused = fe1_apply(_fused);"),
                "main chains both prefixed apply entries in node order");

            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
            Assert.That(error, Is.Null, $"the merged program compiles ({error})");
            Assert.That(effect, Is.Not.Null);
        });
    }

    // SkSL accepts a MUTABLE top-level global ('float gain = 1.0;'); without a rename, two snippets sharing the
    // name compile standalone but redefine it in the merged program.
    [Test]
    public void Merge_MutableTopLevelGlobals_ArePrefixedIndependently()
    {
        string shared =
            "float gain = 0.25;\nhalf4 apply(half4 c) { gain = gain + 0.25; return half4(c.rgb * gain, c.a); }";

        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(shared), SkslSource.Snippet(shared)]);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Does.Contain("fe0_gain").And.Contain("fe1_gain"),
                "each snippet's mutable global gets its own index prefix, so the shared name never redefines");

            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
            Assert.That(error, Is.Null, $"the merged program compiles ({error})");
            Assert.That(effect, Is.Not.Null);
        });
    }

    // A comma-separated mutable-global list declares every name; collecting only the first declarator leaves a
    // trailing name shared by two snippets unrenamed, redefining it in the merged program even though each snippet
    // compiles standalone.
    [Test]
    public void Merge_MultiDeclaratorMutableGlobals_PrefixesEveryDeclarator()
    {
        string shared =
            "float gain = 1.0, bias = 0.0;\n"
            + "half4 apply(half4 c) { bias = bias + 0.1; return half4(c.rgb * gain + bias, c.a); }";

        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(shared), SkslSource.Snippet(shared)]);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Does.Contain("fe0_bias").And.Contain("fe1_bias"),
                "every declarator in the list is prefixed, including the trailing ones");

            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
            Assert.That(error, Is.Null, $"the merged program compiles ({error})");
            Assert.That(effect, Is.Not.Null);
        });
    }

    // CANARY: the merger's whole-word rename does NOT exclude struct bodies, which is sound only while SkSL
    // itself rejects function-local struct statements (top-level structs are already rejected at Snippet()). If a
    // Skia upgrade starts ACCEPTING this, a field sharing a top-level name would be renamed in its declaration but
    // not in its dot-qualified accesses, and the merger needs a struct-body exclusion — this test failing is that
    // signal.
    [Test]
    public void FunctionLocalStruct_IsRejectedBySkSLItself()
    {
        string snippet =
            "uniform half r;\n"
            + "half4 apply(half4 c) { struct S { half r; }; S s; s.r = c.g; return half4(c.r * r, s.r, c.b, c.a); }";

        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(snippet)]);

        using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
        Assert.That(error, Does.Contain("struct"),
            "SkSL now accepts a function-local struct: the merger's rename can desynchronize a field declaration "
            + "from its dot-qualified accesses and needs a struct-body exclusion (see Prefix)");
    }

    // A signature split across lines ('float3\nhelper(...)') and a second definition after a same-line body close
    // ('} half4 apply(') have no single line matching a per-line signature regex; a missed rename lets two snippets
    // sharing the name redefine it (or leaves the generated main's feN_apply call unresolved) in the merged program.
    [Test]
    public void Merge_MultiLineAndSameLineSignatures_AreRenamed()
    {
        string multiLine =
            "uniform float scale;\nfloat3\nhelper(half4 c) { return c.rgb * scale; }\n"
            + "half4 apply(half4 c) { return half4(helper(c), c.a); }";
        string sameLine =
            "float3 helper(half4 c) { return c.rgb; } half4 apply(half4 c) { return half4(helper(c), c.a); }";

        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(multiLine), SkslSource.Snippet(sameLine)]);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Does.Contain("fe0_helper").And.Contain("fe1_helper"),
                "a multi-line signature and a same-line post-brace signature are both renamed");

            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
            Assert.That(error, Is.Null, $"the merged program compiles ({error})");
            Assert.That(effect, Is.Not.Null);
        });
    }
}
