using System.Reflection;
using System.Text.RegularExpressions;
using Beutl.Language;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Language;

// The self-update scripts name resources as @(Type.Key), which SimpleTemplateRenderer looks up only
// on the user's machine, so a missing key would break the update there.
[TestFixture]
public sealed partial class UpdateScriptTemplateKeyTests
{
    // The types UpdateDialogViewModel renders the scripts with.
    private static readonly Type[] s_types = [typeof(Strings), typeof(MessageStrings)];

    [TestCase("linux-update.sh")]
    [TestCase("osx-update.sh")]
    public void Every_template_key_names_a_resource(string script)
    {
        string text = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "Beutl", "Resources", script));
        string[] keys = [.. TemplateKeyRegex().Matches(text).Select(match => match.Groups[1].Value).Distinct()];

        Assert.That(keys, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (string key in keys)
            {
                string[] parts = key.Split('.');
                Type? type = parts.Length == 2 ? s_types.FirstOrDefault(t => t.Name == parts[0]) : null;
                Assert.That(type?.GetProperty(parts[^1], BindingFlags.Public | BindingFlags.Static), Is.Not.Null, key);
            }
        });
    }

    [GeneratedRegex(@"@\(([^)]+)\)")]
    private static partial Regex TemplateKeyRegex();
}
