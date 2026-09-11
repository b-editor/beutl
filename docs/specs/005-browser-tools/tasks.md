# Tasks: Browser tools

## Foundation
- [x] T001 Implement BrowserProfile and persistence tests in the browser module and Beutl.UnitTests.
## US1: Downloads
- [x] T002 Record completed downloads and add open-folder, retry, import, and remove-history actions.
## US2: Page tools
- [x] T003 Implement find next/previous, feedback, close, and bounded per-tab zoom with script tests.
## US3: Bookmarks
- [x] T004 Implement bookmark add/open/remove and persistence/UI tests.
## US4: Privacy
- [x] T005 Implement engine selection, remote-suggestion cancellation, history recording/clearing, and explicit cookie deletion.
## Validation
- [x] T006 Run browser regression suites and inspect compiled bindings and lifecycle cleanup.

Dependencies: T001 precedes T002/T004/T005. T003 can be researched independently. T006 follows every implementation task. Work remains in the current branch; optional branch/commit hooks are not requested.

- [x] T007 Move browser preferences into the global SettingsDialog Browser page and route the browser menu through the application settings host.

- [x] T008 Display bookmarks on blank tabs, provide bookmarking on the blank tab, and replace browser ContentDialog flows with inline panels.

- [x] T009 Refine the blank-page hierarchy, search entry point, empty state, and bookmark cards; remove the toolbar star and add a validated inline URL/name form.

- [x] T010 Align browser settings with common settings rows and redesign download history as responsive per-file cards.

- [x] T011 Rebase onto Avalonia 12 main, migrate UI/async-import APIs, preserve host-owned disposal, and verify browser/terminal regressions.
