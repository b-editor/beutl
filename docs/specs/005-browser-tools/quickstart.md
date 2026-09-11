# Validation
1. Download media, reopen the history, open its folder, retry, and import its existing file.
2. Bookmark a page, reopen the browser/profile, navigate from the list, and remove the entry.
3. Search page text containing quotes, move next/previous, close find, and change/reset zoom; navigate and check zoom persists within the tab.
4. Switch Google/Bing; disable suggestions while a request is pending; verify no stale results appear. Disable history recording and download again.
5. Clear local history and verify files/bookmarks remain; explicitly confirm cookie removal and check unsupported adapters report an error.
6. Run scoped browser NUnit/headless suites.

## Recorded validation (2026-09-09)
- Scoped NUnit unit tests: 55 passed. Browser headless UI tests: 27 passed.
- Native WKWebView fixture: inline-split text yields two matches; next/previous wrap; quoted/backslash literal yields one match; missing and cleared queries yield zero.
- Native WKWebView zoom: 150% produces CSS zoom 1.5; reset restores the fixture original 1.05.
- Actual Google and Bing suggestion endpoints returned the expected query/array format.
- Windows/Linux native rendering was not exercised; common adapters and serialized script-result formats were source-checked.

## Global settings relocation validation
- SettingsDialog registers and directly navigates to BrowserSettingsPage.
- Preference bindings save immediately; cookie clearing requires explicit confirmation and tolerates page disposal during asynchronous completion.
- The browser menu delegates to the app settings host using its own top-level window, including floating-window ownership. The host reuses its existing dialog.
- Scoped browser/settings headless suite: 31 passed. New XAML uses compiled bindings. Internal host/service and lifetime design review found no medium-or-higher issues.
- Cookie deletion requires a live browser tab; the settings page explains this when unavailable.

## Inline browser surfaces validation
- Blank tabs render live bookmark cards with open/remove actions; the inline add form creates bookmarks, and bookmark menu entries are absent.
- Download history and download choices use inline panels. Replacing/closing/canceling a choice does not start a download or dismiss a newer panel.
- Browser/settings headless UI suite: 35 passed. Browser runtime source has no ContentDialog references.

## New-tab visual validation
The 35-test browser/settings UI suite passes, including form validation, bookmark creation/removal/opening, and toolbar-star absence. Skia headless captures were visually reviewed at 320px and 640px widths, in both empty and populated bookmark states. Optional captures can be requested with BEUTL_BROWSER_CAPTURE pointing to an output directory.

## Settings and download-history visual validation
Shared settings-card structure and immediate-save bindings are covered by the 35-test browser/settings UI suite. Settings and download-history captures were reviewed; history actions wrap into two rows at 320px. Removing history preserves the file, and missing-file cards disable opening. No ContentDialog was introduced.
