# Desktop usage telemetry

`Beutl.Editor/Services/UsageTelemetry.cs` records bounded, in-memory summaries
under the existing **Application** telemetry choice. All consent choices must be
configured. Revoking Application consent clears pending summaries and invalidates
in-flight measurements; enabling it takes effect without restarting.
Existing editors rescan their enabled effects when collection becomes enabled.
The scan runs on the UI thread and is skipped if the editor closes before it runs;
an already emitted effect type is not counted again after another consent change.
An observation discarded before emission can be collected again if the effect is
still enabled. Searches and history actions retain their starting consent period
across asynchronous waits and cannot be recorded in a later period.
Tab-interaction deduplication is scoped to an enabled collection period: opting in
does not create an interaction, but the first subsequent interaction is counted
even if the same tab was selected while collection was disabled.

The `Beutl.Usage` ActivitySource sends short `Usage.Summary` spans through the
existing OTLP trace exporter, approximately every minute and at orderly shutdown.
The existing resource supplies OS, app version and per-launch session ID. There is
no persistent installation identifier or local usage spool.

| Attribute | Value |
| --- | --- |
| `beutl.usage.schema_version` | Integer `1` |
| `beutl.usage.event` | Category below |
| `beutl.usage.tool` | Built-in tool/type category or `Extension` |
| `beutl.usage.feature` | Code-defined command, action, property or effect identifier |
| `beutl.usage.outcome` | `succeeded`, `failed`, `cancelled`, `skipped`, or empty |
| `beutl.usage.count` | Occurrences in the summary |
| `beutl.usage.duration_ms` | Sum of elapsed milliseconds |

| Event | Counting unit |
| --- | --- |
| `session.started`, `session.ended` | Consenting session start / orderly end |
| `session.heartbeat` | Running-time delta, including idle time |
| `operation` | Project create/open, scene save or export attempt and its outcome |
| `playback.started` | Normal/shuttle start, excluding loop re-arms |
| `tool.opened` | Tool context creation, including default/restored layouts |
| `tool.activated` | Focus/pointer interaction switching tools within an editor |
| `tool.command` | Built-in reactive command invocation (including programmatic execution) |
| `tool.action` | Annotated button/menu action, including method-bound commands |
| `tool.setting` | Specified enum/toggle mode changes after interaction |
| `editor.edit` | New committed history entry, excluding replay/rollback/no-op |
| `editor.property` | Distinct compiled property names in the committed transaction, without values |
| `editor.history` | Successful undo, redo or history jump |
| `effect.used` | Enabled audio/video effect type, once per editor lifetime |

Tool command discovery is limited to built-in assemblies. Nested AI workflows and
file-browser cloud storage are included; third-party commands are not inspected.
Add click-handler coverage with a **literal**, never bound,
`UsageTracking.Feature="Tool.Feature"` in XAML. Do not record project/file names,
paths, URLs, prompts, search terms, terminal input, parameters, property values,
exception messages or media content.
Use an identifier for the action, not a shared click-handler name: for example,
`GraphEditor.EaseIn` and `GraphEditor.EaseOut` remain separate, as do
`PathEditor.AddLineSegment` and `PathEditor.AddCubicSegment`. A submenu can inherit
its parent's annotation; the nearest annotation for that click event records it
once, even when both parent and child have annotations.

Animation and expression assignments/removals resolve to their owning compiled
property (for example, `RectShape.Opacity`). Direct, animation and expression
updates of the same property in one transaction contribute one property event;
neither expression text nor property values are collected.

One user action can cause a button event, command invocation and committed edit.
These categories are separate measurements, not a count of unique actions. Edits
carry the last interacted tool as context, which does not establish their origin.
Effect inventory includes loaded projects, not just newly added effects, and does
not measure rendered frames. Disabled effects and ancestors are excluded.

At most 1,024 dimension combinations are held between flushes. Crashes, failed
exports, retention and trace sampling can lose observations. Do not equate sessions
with unique users, uptime with active editing time, or missing observations with
proof of non-use. Existing diagnostic logs/traces are outside this usage schema.

The companion beutl-web change adds `/[lang]/admin/usage`. Its `docs/deployment.md`
documents the three server-side Grafana settings and TraceQL requirements. Sum
`span.beutl.usage.count`, not the number of summary spans.
Feature queries group by event/tool/feature/outcome. A disjoint session-start
query groups by event/OS/version, keeping every query within Tempo's five-label
grouping limit without double-counting session starts.

Run `dotnet test tests/Beutl.HeadlessUITests/Beutl.HeadlessUITests.csproj -f net10.0 --filter FullyQualifiedName~UsageTelemetryTests`.
