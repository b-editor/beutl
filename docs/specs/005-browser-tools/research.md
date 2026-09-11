# Research
- NativeWebView 11.4.1 exposes cookies through TryGetCookieManager, but only clipboard/edit commands through TryGetCommandManager. It has no common find/zoom/cache-clear API.
- Source verified in AvaloniaUI/Avalonia.Controls.WebView tag 11.4.1. Use InvokeScript for page operations and avoid claiming full cache/storage deletion.
- GTK, WKWebView, and WebView2 serialize script results differently. Explicit JSON.stringify plus optional outer-string normalization gives a consistent host result.
- Native browser surfaces cannot be reliably covered by Avalonia content. History and download choices therefore use inline panels that replace the browser surface. Closing or replacing a download-choice panel cancels that choice without dismissing a newer panel.

## macOS frame navigation correction
Pinned WebView 11.4.1 forwards every non-null WKNavigationAction.TargetFrame to NavigationStarted without checking IsMainFrame, despite the generic documentation describing a top-level event. Therefore the macOS browser UI treats completed navigation (which reads WKWebView.URL) as authoritative. Explicit address/history/reload operations establish their own loading state; explicit media downloads do not depend on ambiguous subframe events.

A native WKWebView fixture emitted main-frame /top and subframe /widget policy callbacks, followed by completion for /top. The regression test failed before the fix and passed afterward; all 29 scoped browser UI tests passed.
