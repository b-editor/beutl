# Research
- NativeWebView 12.1.0 exposes cookies through TryGetCookieManager, but only clipboard/edit commands through TryGetCommandManager. It has no common find/zoom/cache-clear API.
- Source verified in AvaloniaUI/Avalonia.Controls.WebView tag 12.1.0. Use InvokeScript for page operations and avoid claiming full cache/storage deletion.
- GTK, WKWebView, and WebView2 serialize script results differently. Explicit JSON.stringify plus optional outer-string normalization gives a consistent host result.
- Native browser surfaces cannot be reliably covered by Avalonia content. History and download choices therefore use inline panels that replace the browser surface. Closing or replacing a download-choice panel cancels that choice without dismissing a newer panel.

## macOS frame navigation correction
Pinned WebView 12.1.0 still forwards every non-null WKNavigationAction.TargetFrame to NavigationStarted without checking IsMainFrame, despite the generic documentation describing a top-level event. Therefore the macOS browser UI treats completed navigation (which reads WKWebView.URL) as authoritative. Explicit address/history/reload operations establish their own loading state; explicit media downloads do not depend on ambiguous subframe events.

A native WKWebView fixture emitted main-frame /top and subframe /widget policy callbacks, followed by completion for /top. The regression test failed before the fix and passed afterward; all 29 scoped browser UI tests passed.

## Avalonia 12 rebase
Rebased onto origin/main 575e83aa0, using Avalonia 12.1.2, FluentAvalonia 3.1.0, Dock 12.1.0.6, and WebView 12.1.0. Focus events now use FocusChangedEventArgs; placeholders, cookie deletion, PNG encoding, and FA-prefixed menu/navigation controls use the current APIs. SettingsDialog retains main's loaded-frame navigation deferral.

The file-import source handler requires a requested fallback length and replaces it with decoded duration for video/audio. Browser imports await AddAsync with the original five-second fallback and surface ElementAddResult failures. A real editor test verifies two-second video/audio companions and one-step undo.

Disposed reused terminal/browser views cannot be reactivated by DataContext changes. The regression failed before the lifecycle guard and passed afterward. The guide update follows main's move from removed AI skill files to docs/extension-authoring/tool-tabs.md.
