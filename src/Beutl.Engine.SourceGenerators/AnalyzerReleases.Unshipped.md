### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
BESG001 | Beutl.Engine.SourceGenerators | Warning | EngineObjectResourceGenerator
BESG002 | Beutl.Engine.SourceGenerators | Warning | FallbackTypeGenerator
BESG003 | Beutl.Engine.SourceGenerators | Warning | MetadataCallbackPurityAnalyzer
BESG004 | Beutl.Engine.SourceGenerators | Warning | MetadataCallbackPurityAnalyzer, best-effort: it sees only the static fields and properties the callback body names directly, so a static method the body calls, a member reached through a static readonly instance, and mutation of the object a static readonly field holds all stay invisible
BESG005 | Beutl.Engine.SourceGenerators | Warning | RenderNodeChangeMarkingAnalyzer, best-effort: it reports only a direct assignment, written inside the node's own type, to an instance field or auto-property that the node's Process reads, with no assignment to HasChanges reachable from the assigning member; an assignment made through another type, through a virtual call, by mutating a collection in place, on a branch that skips a HasChanges written elsewhere in the same member, or inside Process itself (where memoization is legitimate) all stay invisible, so the runtime cross-check in Beutl.Engine - not this rule - is what covers them
