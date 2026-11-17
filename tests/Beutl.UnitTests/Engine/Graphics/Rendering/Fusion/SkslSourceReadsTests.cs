using Beutl.Graphics.Shaders;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
public sealed class SkslSourceReadsTests
{
    private static IEnumerable<TestCaseData> CurrentPixelReads()
    {
        yield return Case("SamplesAtTheEntryCoordinate", "half4 main(float2 c) { return src.eval(c); }");
        yield return Case("DoesNotSampleAtAll", "half4 main(float2 c) { return half4(c.x / 100.0, 0, 0, 1); }");
        yield return Case(
            "UsesTheCoordinateForEverythingElse",
            """
            uniform float width;
            float hash(float2 p) { p += 1.0; return fract(sin(dot(p, float2(12.9, 78.2))) * 43758.5); }
            half4 main(float2 c) {
                half4 s = src.eval(c);
                float2 uv = c / float2(width, 1.0) - 0.5;
                if (c.x == 1.0 || c.y <= 2.0 || c.x != c.y) { uv *= 2.0; }
                return half4(s.rgb * half(hash(floor(c * 0.5)) + uv.x), s.a);
            }
            """);
        yield return Case(
            "IgnoresSamplesInComments",
            """
            // return src.eval(c + 1.0);
            half4 main(float2 c) { /* src.eval(float2(0)) */ return src.eval(c).a * half4(1); }
            """);
        yield return Case(
            "ReturnsTheCoordinate",
            "float2 f(float2 c) { return c; } half4 main(float2 c) { return src.eval(c) + half4(half2(f(c)), 0, 0); }");
        yield return Case(
            "IndexesTheCoordinate",
            "half4 main(float2 c) { return src.eval(c) * half(c[0] + c[int(c[1]) / 2 - int(c[1]) / 2]); }");
        yield return Case(
            "GroupsTheCoordinateInExpressions",
            """
            half4 main(float2 c) {
                float2 uv = (c) / 2.0 + (c.x + 1.0) * (c).y;
                return src.eval(c) * half(length((uv)));
            }
            """);
    }

    private static IEnumerable<TestCaseData> ReadsElsewhere()
    {
        yield return Case("SamplesAnOffset", "half4 main(float2 c) { return src.eval(c + float2(1, 0)); }");
        yield return Case("SamplesAConstant", "half4 main(float2 c) { return src.eval(float2(0)); }");
        yield return Case(
            "SamplesInAHelper",
            "half4 f(float2 p) { return src.eval(p); } half4 main(float2 c) { return f(c); }");
        yield return Case("AssignsTheCoordinate", "half4 main(float2 c) { c = c * 2.0; return src.eval(c); }");
        yield return Case("AddsToTheCoordinate", "half4 main(float2 c) { c += 1.0; return src.eval(c); }");
        yield return Case("AssignsAComponent", "half4 main(float2 c) { c.x = 0.0; return src.eval(c); }");
        yield return Case("IncrementsAComponent", "half4 main(float2 c) { c.x++; return src.eval(c); }");
        yield return Case("PreIncrementsAComponent", "half4 main(float2 c) { ++c.y; return src.eval(c); }");
        yield return Case(
            "ShadowsTheCoordinate",
            "half4 main(float2 c) { if (c.x > 1.0) { float2 c = float2(0); return src.eval(c); } return half4(0); }");
        yield return Case(
            "PassesTheCoordinateToAnInoutParameter",
            "void g(inout float2 p) { p += 1.0; } half4 main(float2 c) { g(c); return src.eval(c); }");
        yield return Case(
            "PassesTheCoordinateToAnOutParameter",
            "void g(out float2 p) { p = float2(0); } half4 main(float2 c) { g(c); return src.eval(c); }");
        yield return Case("AssignsAnIndexedComponent", "half4 main(float2 c) { c[1] = 0.0; return src.eval(c); }");
        yield return Case(
            "DeclaresTheCoordinateAfterAComma",
            "half4 main(float2 c) { float2 a = c, c = float2(0); return src.eval(c) + half4(half2(a), 0, 0); }");
        yield return Case("PreDecrementsAComponent", "half4 main(float2 c) { --c.x; return src.eval(c); }");
        yield return Case("DecrementsAComponent", "half4 main(float2 c) { c.y--; return src.eval(c); }");
        yield return Case("ShiftsAComponent", "half4 main(float2 c) { c.x <<= 1; return src.eval(c); }");
        yield return Case("DeclaresMainWithoutABody", "half4 main(float2 c);");
        yield return Case(
            "AssignsTheCoordinateInParentheses",
            "half4 main(float2 c) { (c) = float2(0); return src.eval(c); }");
        yield return Case(
            "AddsToAComponentInParentheses",
            "half4 main(float2 c) { (c).x += 1000.0; return src.eval(c); }");
        yield return Case(
            "AssignsAComponentInParentheses",
            "half4 main(float2 c) { (c.x) = 0.0; return src.eval(c); }");
        yield return Case(
            "IncrementsAComponentInNestedParentheses",
            "half4 main(float2 c) { ++((c).y); return src.eval(c); }");
    }

    [TestCaseSource(nameof(CurrentPixelReads))]
    public void ProgramReadingItsInputOnlyWhereItDraws_IsRecognized(string body)
        => Assert.That(SkslSource.WholeSource(WithSource(body)).ReadsSourceOnlyAtEntryCoordinate, Is.True);

    [TestCaseSource(nameof(ReadsElsewhere))]
    public void ProgramReadingItsInputElsewhere_IsNotRecognized(string body)
        => Assert.That(SkslSource.WholeSource(WithSource(body)).ReadsSourceOnlyAtEntryCoordinate, Is.False);

    [Test]
    public void CurrentPixelProgram_IsNotAWholeSourceRead()
        => Assert.That(
            SkslSource.CurrentPixel("half4 apply(half4 color) { return color; }").ReadsSourceOnlyAtEntryCoordinate,
            Is.False);

    private static string WithSource(string body) => "uniform shader src;\n" + body;

    private static TestCaseData Case(string name, string body) => new TestCaseData(body).SetName(name);
}
