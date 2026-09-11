# Implementation Plan: Browser tools

## Technical Context
C#/.NET 10, Avalonia 12.1.2, NativeWebView 12.1.0, FluentAvalonia 3.1.0, NUnit. Existing browser code stays inside Beutl.Editor.Components; no public API or dependency changes.

## Constitution Check
MIT/GPL boundary unchanged. Compiled XAML bindings retained. New persistence, query selection, and UI lifecycle behavior receive NUnit coverage. No source generator or CI changes.

## Design
- BrowserProfile owns atomic JSON persistence in the Beutl home, bounded observable bookmark/download lists, search engine, and privacy preferences; all tabs share one instance.
- WebBrowserTabView.Tools owns inline tab panels. Blank tabs show bookmark cards; history and download options replace the browser surface without ContentDialog. Bookmark creation lives in an inline form on the blank page, beside the saved bookmark cards.
- WebSearchSuggestions accepts an engine choice. A profile change cancels current suggestion requests and configures every active address box.
- Page tools use serialized script arguments and normalized script results. Zoom is per tab and reapplied after navigation. Cookie clearing uses the native cookie manager when available.
- Download completion appends metadata only after the file exists; download-history actions reuse the downloader and await IElementAdder.AddAsync, passing ElementSource.File requests and reporting returned failures.

## Validation
Profile round trips, malformed data, deduplication/limits, engine URL encoding, privacy cancellation, bookmark/history UI actions, page-tool script behavior, existing browser regression tests. Manually disclose platform-specific page/iframe limitations.

## Global settings integration
BrowserSettingsPage is registered in SettingsDialog. Its ViewModel saves preferences immediately and synchronizes with BrowserProfile. The browser menu requests the application-scoped IBrowserSettingsHost through IEditorContext; floating-window owners are retained. BrowserWebViewRegistry supplies a live cookie manager, with an explanatory disabled state when no browser session is available.

## Settings and download presentation
BrowserSettingsPage uses the shared OptionsDisplayItem rows, compact toggles, section headings, spacing, and right-aligned actions used by other settings pages. Download history presents one typed card per record with filename, location, source host, time, and direct actions; unavailable local files disable open/import while retaining retry. Record changes refresh the visible panel.
