# Feature Specification: Browser tools

**Feature Branch**: `yuto-trd/web-browser-tool-tab`
**Created**: 2026-09-09
**Status**: Implemented
**Input**: Add download history, in-page search and zoom, bookmarks, and search/privacy settings.

## User Scenarios & Testing

### User Story 1 - Reuse downloaded media (P1)
A user reopens completed downloads, opens their containing directory, retries a download, or adds a saved file to the current timeline.
Acceptance: completed downloads survive restart; missing files produce an error; removing history never removes files.

### User Story 2 - Read and search pages (P1)
A user finds text with next/previous navigation and zooms a page between 50% and 200%, with a reset to 100%.
Acceptance: queries containing quotes work; no-match feedback appears; navigation reapplies the tab's zoom; closing search clears its indication.

### User Story 3 - Save reference pages (P2)
A user adds a bookmark through an inline URL/name form on an empty tab, then opens or removes saved bookmark cards. Bookmark actions are absent from the menu.
Acceptance: duplicate URLs are not added twice; bookmarks survive restart; blank and credential-bearing URLs are rejected.

### User Story 4 - Control search and local data (P1)
In the global settings dialog Browser page, a user selects Google or Bing, disables remote suggestions, disables download-history recording, clears local history, or deletes browser cookies.
Acceptance: disabling suggestions cancels pending requests in all tabs; searches use the selected engine; clearing history preserves bookmarks and downloaded files; cookie deletion requires an explicit in-app confirmation and reports unsupported adapters.

## Requirements
- FR-001: Persist bounded download history, bookmarks, and settings across app restarts, shared across tabs.
- FR-002: Save changes atomically; surface write errors and preserve corrupt input for recovery.
- FR-003: Record source URL, file path, and completion time for successful downloads only.
- FR-004: Do not store credentials in URLs. Explicit bookmark removal and history clearing affect metadata only.
- FR-005: Implement keyboard and visible controls for find and zoom without triggering editor commands while editing browser UI.
- FR-006: All new user-facing labels are available in English and Japanese.

- FR-007: Show bookmark cards and an inline add form on blank tabs. The top toolbar has no bookmark/star button.
- FR-008: Browser-local history and download options use inline panels, not ContentDialog. Closing or replacing an unconfirmed download panel cancels the pending choice.

## Assumptions and Limits
- The browser is an embedded editor tool, not a standalone general-purpose browser.
- Local data belongs to the Beutl profile. The browser menu opens the global settings Browser page. Preference changes save immediately. Cookie clearing requires a live browser tab and is separate from local history clearing and does not claim to erase browser caches or every site's storage.
- Page search operates on visible text in the top-level document. Frame contents, shadow DOM, canvas text, and browser-internal pages are outside this page-text search. Zoom changes page content through CSS and is not native viewport zoom.

## Success Criteria
All four workflows pass deterministic tests. Local state survives a save/reload round trip. No disabled remote-suggestion request updates the UI. Existing address and download tests stay green.
