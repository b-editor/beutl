# Product Event Contract v1

## Names

`app.session.start`, `app.session.end`, `project.create`, `project.open`, `project.save`, `asset.add`, `editor.first_edit`, `editor.action_summary`, `preview.first_frame`, `preview.playback_summary`, `media.export`, `project.package_export`, `extension.catalog`, `extension.manage`, `extension.load`, `agent.install`, `agent.host`, `agent.session_attach`, and `agent.tool_summary` are the complete v1 product name set.

## Privacy rule

Every tag is checked against an explicit allowlist before export. Strings that represent paths, URIs, names, content, prompts, arguments, results, exception messages, stacks, account data, or CLR type names are not valid attributes. Unknown event names or attributes are rejected.
The desktop repeats the complete name, instrumentation scope/version, resource, required-tag,
tag-name, tag-type, value, and length validation immediately before OTLP serialization. A direct
Activity from a friend assembly therefore cannot bypass the product recorder.

## Outcomes

The only outcome values are `success`, `partial`, `failed`, `cancelled`, `blocked`, and `queued`.
An immediate extension install that falls back successfully to the reconciliation queue completes
as one `queued` event, not a `failed` plus `queued` pair. When usage analytics is enabled,
PackageTools receives the validated desktop session identifier only through its private child-process
environment; it is never a command-line argument, feedback URL parameter, crash-handler value, or
diagnostic payload. PackageTools records the queued operation's final `success`, `partial`, `failed`,
or `cancelled` outcome. Deferred uninstall queue transitions also record `queued`.

## Attributes

The product resource uses `beutl.telemetry.stream=product`, `beutl.analytics.schema=v1`,
`beutl.installation.id`, `beutl.session.id`, `beutl.first_seen_month`, `service.version`,
`beutl.release.channel`, `os.type`, `process.architecture`, and `beutl.renderer`.
`service.name` is exactly `beutl.desktop` or `beutl.package-tools`. The product activity
source is `Beutl.ProductAnalytics` at version `v1`; the aggregate-only quality meter is
`Beutl.Quality` at version `v1`.

Each product span uses `beutl.event.id` and `beutl.outcome`; optional validated values are
limited to `beutl.trigger`, `beutl.error_code`, `beutl.duration_ms`, `beutl.feature.id`,
`beutl.count.bucket`, `beutl.resolution.bucket`, and `beutl.project.size.bucket`.

`editor.action_summary`, `preview.playback_summary`, and `agent.tool_summary` aggregate repeated operations for five
minutes and flush their current aggregate at normal shutdown. Their count uses the fixed
`1`, `2-5`, `6-10`, `11-50`, or `51+` bucket. A period retains at most 256 exact trusted
feature IDs; later distinct values use the fixed `overflow` feature identifier.

## Safe diagnostics

Remote diagnostics are limited to the `Beutl.SafeDiagnostics` category. Its `component`
is one of `app`, `project`, `preview`, `export`, `package`, `extension`, `agent`, or
`telemetry`; its `code` uses the fixed product error-code vocabulary; and its `outcome`
uses the fixed outcome vocabulary. Arbitrary log bodies, exception messages, and tokens
are rejected.
