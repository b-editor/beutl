using System.Text.Json;
using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserAdBlockCosmeticTests
{
    [TestCase("bar.test")]
    [TestCase("notfoo.test")]
    [TestCase("foo.test.other")]
    public void UnrelatedExceptionsDoNotRemoveScopedCosmeticRules(string exceptionDomain)
    {
        var rules = BrowserAdBlockRules.Parse($"foo.test##.ad\n{exceptionDomain}#@#.ad");
        using var native = JsonDocument.Parse(rules.ToWebKitJson());
        var rule = native.RootElement.EnumerateArray().Single();
        Assert.That(rule.GetProperty("action").GetProperty("selector").GetString(), Is.EqualTo(".ad"));
        var trigger = rule.GetProperty("trigger");
        Assert.That(trigger.GetProperty("if-domain").EnumerateArray().Select(d => d.GetString()), Is.EqualTo(new[] { "*foo.test" }));
        Assert.That(trigger.TryGetProperty("unless-domain", out _), Is.False);
    }

    [TestCase("foo.test,bar.test", "foo.test", "*bar.test")]
    [TestCase("foo.test,bar.test", "child.foo.test", "*bar.test")]
    [TestCase("child.foo.test,bar.test", "foo.test", "*bar.test")]
    public void OnlyOverlappingIncludeDomainsAreOmitted(string includes, string exception, string remaining)
    {
        var rules = BrowserAdBlockRules.Parse($"{includes}##.ad\n{exception}#@#.ad");
        using var native = JsonDocument.Parse(rules.ToWebKitJson());
        var trigger = native.RootElement.EnumerateArray().Single().GetProperty("trigger");
        Assert.That(trigger.GetProperty("if-domain").EnumerateArray().Select(d => d.GetString()), Is.EqualTo(new[] { remaining }));
        Assert.That(trigger.TryGetProperty("unless-domain", out _), Is.False);
    }

    [TestCase("foo.test", "foo.test")]
    [TestCase("foo.test", "child.foo.test")]
    [TestCase("child.foo.test", "foo.test")]
    [TestCase("foo.test", "")]
    public void ExceptionsStillProtectOverlappingSites(string include, string exception)
    {
        var rules = BrowserAdBlockRules.Parse($"{include}##.ad\n{exception}#@#.ad");
        using var native = JsonDocument.Parse(rules.ToWebKitJson());
        Assert.That(native.RootElement.GetArrayLength(), Is.Zero);
    }
}
