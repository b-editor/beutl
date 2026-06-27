# Contract: Declarative Document, Merge Patch, and Schema Descriptor

This pins the three JSON shapes the agent works with. All three mirror Beutl's own `CoreSerializer` output, so reading a project, patching it, and writing it back stay in the same shape (round-trip safety, FR-013).

## 1. Declarative Document

A node is a JSON object that mirrors `CoreSerializer.SerializeToJsonObject`:

```jsonc
{
  "$type": "Beutl.ProjectSystem.Scene",   // discriminator (JsonHelper.WriteDiscriminator)
  "Id": "8f3c…-…-…",                       // CoreObject.Id (Guid) — the identity anchor
  "FrameSize": [1920, 1080],               // a CoreProperty, keyed by CoreProperty.Name
  "Duration": "00:00:10.0000000",
  "Children": [                            // a child collection (array of typed nodes)
    {
      "$type": "Beutl.ProjectSystem.Element",
      "Id": "…",
      "Start": "00:00:00", "Length": "00:00:03", "ZIndex": 0,
      "Objects": [
        {
          "$type": "Beutl.Graphics.Shapes.TextBlock",
          "Id": "…",
          "Text": "Title",
          "Size": 96,
          "Animations": {                  // animatable property → keyframe animation
            "Opacity": {
              "$type": "…KeyFrameAnimation`1[…]",
              "KeyFrames": [
                { "$type": "…", "Id": "…", "KeyTime": "00:00:00", "Value": 0, "Easing": { "$type": "Beutl.Animation.Easings.LinearEasing" } },
                { "$type": "…", "Id": "…", "KeyTime": "00:00:01", "Value": 1 }
              ]
            }
          },
          "Expressions": { }               // property → expression (when used)
        }
      ]
    }
  ]
}
```

**Rules**:
- Every node carries `$type` and `Id`. New nodes an agent authors **may omit `Id`** — the toolkit mints a Guid (research §3); supplying an `Id` not present in the live tree on an edit is an error path the reconciler defines.
- Property keys are the exact `CoreProperty.Name`/`IProperty.Name` from the schema (PascalCase, e.g. `FrameSize`, not `frameSize`).
- Animatable properties appear under `"Animations"`; expressions under `"Expressions"`.
- Child collections are arrays of nodes; **collection identity is by member `Id`**, not array index.
- The document is **schema-versioned** (`schemaVersion`) — a mismatch is surfaced, never silently dropped (FR-031).

## 2. Merge Patch (RFC 7396)

A partial document with delete semantics:

```jsonc
// "Move the title element 2s later, make it bigger, fade it out" — only the touched fields:
{
  "Children": [                 // NOTE: arrays replace wholesale under RFC 7396 (see below)
    { "Id": "<title-element-id>", "Start": "00:00:02", "Objects": [ { "Id": "<text-id>", "Size": 140 } ] }
  ]
}
```

**Apply algorithm** (`Apply(target, patch)`):
- if `patch` is an object: for each member, `null` ⇒ remove the key from `target`; otherwise `target[key] = Apply(target[key], value)` (DeepClone before reparenting — STJ single-parent).
- otherwise (`array`/scalar): `target` becomes a DeepClone of `patch` (wholesale replace).

**Important — arrays replace wholesale.** RFC 7396 has no array element identity. So a patch that only mentions one child in a `Children`/`Objects` array would, by the raw merge, **replace the entire array**. The toolkit therefore treats merge-patch as a way to derive the *desired document*, then performs the **Id-based collection diff** (data-model §Change Set) to compute minimal `insert/remove/move/update` operations. Practically: an agent's merge-patch for a collection should include the full set of members it wants (each keyed by `Id`), or use the imperative `add_element`/`remove_element` assists for single-item collection edits. This rule is documented to the agent via the Skill recipes.

## 3. Schema Descriptor (from `get_schema`)

```jsonc
{
  "schemaVersion": "1",
  "types": [
    {
      "type": "Beutl.Graphics.FilterEffects.Brightness",
      "$type": "Beutl.Graphics.FilterEffects.Brightness",
      "category": "FilterEffect",
      "baseFields": [
        { "name": "Id", "valueType": "System.Guid" },
        { "name": "IsEnabled", "valueType": "System.Boolean", "default": true }
      ],
      "properties": [
        {
          "name": "Amount",
          "valueType": "System.Single",
          "display": { "name": "Amount", "description": "…" },
          "range": { "min": 0, "max": 100 },     // from [Range]
          "step": 1,                              // from [NumberStep]
          "default": 100,                         // IProperty.DefaultValue
          "animatable": true,                     // IProperty.IsAnimatable
          "supportsExpression": true
        }
      ]
    }
  ]
}
```

**Field sources** (research §4): `properties[]` from `EngineObject.Properties` (`IProperty.Name`/`ValueType`/`DefaultValue`/`IsAnimatable`/`SupportsExpression`/`GetAttributes()`), `baseFields[]` from `PropertyRegistry.GetRegistered(type)`, `category` from `LibraryService`/`KnownLibraryItemFormats`, `$type` via `JsonHelper.WriteDiscriminator`. A property with a custom `JsonConverter` records a `converter`/encoding note instead of a structural description. The descriptor enumerates **built-in and installed-extension** types (FR-022); the headless host must run type registration so `LibraryService.Current` is populated.

## Conformance notes

- The document shape an agent writes back **must** be deserializable by the same Beutl runtime version (FR-013); cross-version handling is governed by Beutl's serializer (`schemaVersion` surfaces a mismatch rather than dropping content).
- Values are validated on apply (`[Range]` coercion etc.); the schema's `range`/`step` let the agent stay in-range proactively, and `plan_edit` reports any coercion before commit.
