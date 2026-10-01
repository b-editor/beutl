using System.Text.RegularExpressions;
using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.HeadlessUITests;

internal static class BrowserAdBlockTestRules
{
    // Known, small filters exercise rule semantics without depending on CI scheduling.
    // The production request and regex deadlines are covered by the matching budget tests.
    internal static BrowserAdBlockRules Parse(string text, TimeProvider? timeProvider = null) =>
        BrowserAdBlockRules.Parse(text, timeProvider ?? new FrozenTimeProvider(), Regex.InfiniteMatchTimeout);

    internal sealed class FrozenTimeProvider : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }
}
