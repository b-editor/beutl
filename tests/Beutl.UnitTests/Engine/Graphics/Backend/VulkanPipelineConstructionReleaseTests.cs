using System.Text;
using System.Text.RegularExpressions;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// Pins that a Vulkan graphics-pipeline type releases its device objects when its constructor throws.
/// </summary>
/// <remarks>
/// Such a constructor creates shader modules, a descriptor set layout and a pipeline layout before it asks
/// the driver for the pipeline, and the driver can reject that pipeline - a specialization constant the
/// shader does not declare is enough. Construction then never completes, so nothing will ever call
/// <c>Dispose</c> on the instance and every handle made along the way stays on the device until the context
/// is destroyed; repeat it per plugin load and the leak grows without bound. Making a real driver reject a
/// pipeline is not portable across MoltenVK, SwiftShader and desktop drivers, and the leak has no
/// observation point short of the validation layer's object tracker, so the release is pinned at the source
/// instead - over every type that creates a graphics pipeline, found by what it calls rather than by name,
/// so a type added later is covered without editing this test.
/// </remarks>
[TestFixture]
public sealed class VulkanPipelineConstructionReleaseTests
{
    private static readonly Regex s_typeName = new(@"\bclass\s+(?<name>\w+)", RegexOptions.Compiled);

    /// <summary>Matches a call that makes a device object, but not a <c>...CreateInfo</c> struct.</summary>
    private static readonly Regex s_deviceObjectCreation = new(@"\bCreate[A-Z]\w*\s*\(", RegexOptions.Compiled);

    private static readonly Regex s_releasingCatch = new(
        @"catch\s*\{[^{}]*\bDispose\s*\(\s*\)\s*;[^{}]*\bthrow\s*;[^{}]*\}",
        RegexOptions.Compiled);

    [Test]
    public void EveryGraphicsPipelineConstructor_ReleasesWhatItCreatedWhenItThrows()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "the repository root was not found above the test binaries");

        string backend = Path.Combine(
            directory!.FullName, "src", "Beutl.Engine", "Graphics", "Backend", "Vulkan");
        Assert.That(Directory.Exists(backend), Is.True, $"the Vulkan backend was not found at {backend}");

        var inspected = new List<string>();
        var offenders = new List<string>();
        foreach (string path in Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories))
        {
            string source = WithoutLiteralsAndComments(File.ReadAllText(path));
            if (!source.Contains("CreateGraphicsPipelines(", StringComparison.Ordinal))
                continue;

            Match typeName = s_typeName.Match(source);
            Assert.That(typeName.Success, Is.True, $"no type declaration was found in {path}");

            string name = typeName.Groups["name"].Value;
            string fileName = Path.GetFileName(path);
            foreach (string body in ConstructorBodies(source, name))
            {
                inspected.Add($"{fileName}:{name}");

                Match creation = s_deviceObjectCreation.Match(body);
                if (!creation.Success)
                    continue;

                Match releasing = s_releasingCatch.Match(body);
                if (!releasing.Success)
                {
                    offenders.Add($"{fileName}: {name} has no catch that disposes and rethrows");
                    continue;
                }

                int guardStart = body.IndexOf("try", StringComparison.Ordinal);
                if (guardStart < 0 || guardStart > creation.Index)
                {
                    offenders.Add(
                        $"{fileName}: {name} creates a device object outside the guarded region");
                }
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                inspected,
                Is.Not.Empty,
                "the scan must find the constructors that create a graphics pipeline");
            Assert.That(
                offenders,
                Is.Empty,
                "a constructor that throws leaves no instance to dispose, so it must release the device "
                + "objects it already created: " + string.Join("; ", offenders));
        }
    }

    /// <summary>Every constructor body of <paramref name="typeName"/>, brace matched.</summary>
    private static IEnumerable<string> ConstructorBodies(string source, string typeName)
    {
        var declaration = new Regex($@"\b(?:public|internal|private|protected)\s+{Regex.Escape(typeName)}\s*\(");
        foreach (Match match in declaration.Matches(source))
        {
            int parameters = MatchingIndex(source, match.Index + match.Length - 1, '(', ')');
            if (parameters < 0)
                continue;

            int open = source.IndexOf('{', parameters);
            if (open < 0)
                continue;

            // A constructor with a `: this(...)` or `: base(...)` initializer still opens its body at the
            // next brace, but a `=>` bodied one never does, so anything but a brace means there is no body.
            if (source.AsSpan(parameters + 1, open - parameters - 1).Contains('=')
                || source.AsSpan(parameters + 1, open - parameters - 1).Contains(';'))
            {
                continue;
            }

            int close = MatchingIndex(source, open, '{', '}');
            if (close < 0)
                continue;

            yield return source[(open + 1)..close];
        }
    }

    private static int MatchingIndex(string source, int start, char open, char close)
    {
        int depth = 0;
        for (int i = start; i < source.Length; i++)
        {
            if (source[i] == open)
            {
                depth++;
            }
            else if (source[i] == close)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Blanks out string literals and comments, keeping every other character at its original index so a
    /// brace inside a shader source or a comment cannot throw the matching off.
    /// </summary>
    private static string WithoutLiteralsAndComments(string source)
    {
        var result = new StringBuilder(source);
        int i = 0;
        while (i < source.Length)
        {
            if (source.AsSpan(i).StartsWith("\"\"\""))
            {
                int end = source.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                i = Blank(result, i, end < 0 ? source.Length : end + 3);
            }
            else if (source[i] == '"' || source[i] == '\'')
            {
                char quote = source[i];
                int end = i + 1;
                while (end < source.Length && source[end] != quote)
                    end += source[end] == '\\' ? 2 : 1;
                i = Blank(result, i, Math.Min(end + 1, source.Length));
            }
            else if (source.AsSpan(i).StartsWith("//"))
            {
                int end = source.IndexOf('\n', i);
                i = Blank(result, i, end < 0 ? source.Length : end);
            }
            else if (source.AsSpan(i).StartsWith("/*"))
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = Blank(result, i, end < 0 ? source.Length : end + 2);
            }
            else
            {
                i++;
            }
        }

        return result.ToString();
    }

    private static int Blank(StringBuilder result, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (result[i] != '\n')
                result[i] = ' ';
        }

        return end;
    }
}
