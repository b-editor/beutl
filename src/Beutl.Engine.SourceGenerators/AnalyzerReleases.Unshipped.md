### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
BESG001 | Beutl.Engine.SourceGenerators | Warning | EngineObjectResourceGenerator
BESG002 | Beutl.Engine.SourceGenerators | Warning | FallbackTypeGenerator
BESG003 | Beutl.Engine.SourceGenerators | Warning | MetadataCallbackPurityAnalyzer
BESG004 | Beutl.Engine.SourceGenerators | Warning | MetadataCallbackPurityAnalyzer, best-effort: it sees only the static fields and properties the callback body names directly, so a static method the body calls, a member reached through a static readonly instance, and mutation of the object a static readonly field holds all stay invisible
