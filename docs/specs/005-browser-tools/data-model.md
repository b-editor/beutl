# Data model
- BrowserBookmark: URL and title; identity is the canonical absolute URL.
- BrowserDownloadRecord: source URL, full local path, completion timestamp; bounded to the latest 200 records.
- BrowserProfile: version 1; engine (Google/Bing), suggestions enabled, history recording enabled, bookmarks (max 200), completed downloads (max 200).
- Browser tab: in-memory URL history and zoom; settings changes and history-clear notifications apply across tabs.

Only HTTP(S) URLs without user info may be stored. Clearing download records preserves files and bookmarks. Settings and metadata are local; disabled suggestions make no remote requests.
