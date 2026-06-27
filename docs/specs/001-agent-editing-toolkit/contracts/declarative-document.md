# Contract: Declarative Document, Merge Patch, and Schema Descriptor

This pins the three JSON shapes the agent works with. The document mirrors Beutl's runtime object shape (`$type` + `Id` + property keys) and is a **normalized inline view** the toolkit assembles — it is NOT byte-for-byte the on-disk `CoreSerializer` output for the Scene→Element relationship, because on disk a `Scene` stores its elements under an `"Elements"` node as **Include/Exclude glob patterns** referencing separate `.belm` files (or embedded), not an inline array (`Scene.cs:143,265,271-276,320-328`; the CLR `Children` property is `[NotAutoSerialized]`, the serialized key is `"Elements"`). The toolkit normalizes that multi-file representation into one inline document for the agent and maps edits back to it. Property values and leaf object shapes are exactly `CoreSerializer`'s; round-trip safety (FR-013) is over the normalized document.

## 1. Declarative Document

A node is a JSON object that mirrors `CoreSerializer.SerializeToJsonObject`:

```jsonc
{
  "$type": "Beutl.ProjectSystem.Scene",   // discriminator (JsonHelper.WriteDiscriminator)
  "Id": "8f3c…-…-…",                       // CoreObject.Id (Guid) — the identity anchor
  "FrameSize": [1920, 1080],               // a CoreProperty, keyed by CoreProperty.Name
  "Duration": "00:00:10.0000000",
  "Elements": [                            // normalized inline view; on disk this is Include/Exclude globs → .belm files
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
- Every node carries `$type` and `Id`. **Id rules** (creation vs reference, unambiguous): a **new** object **omits `Id`** → the toolkit mints a Guid and inserts it; a **supplied `Id`** MUST match an existing entity (then it is updated/merged) — a supplied `Id` not in the tree is `stale_handle` (omit it to create), and a supplied `Id` whose `$type` differs from the existing object's type is `validation_rejected` (no in-place type change — delete + insert a new object instead).
- Property keys are the exact `CoreProperty.Name`/`IProperty.Name` from the schema (PascalCase, e.g. `FrameSize`, not `frameSize`).
- Animatable properties appear under `"Animations"`; expressions under `"Expressions"`.
- Child collections are arrays of nodes; **collection identity is by member `Id`**, not array index. A Scene's elements appear under `"Elements"` (the normalized inline view; on disk this is Include/Exclude globs over `.belm` files); an Element's content appears under `"Objects"`.
- The document is **schema-versioned** (`schemaVersion`) — a mismatch is surfaced, never silently dropped (FR-031).

## 2. Merge Patch (RFC 7396, with id-keyed arrays)

A partial document with delete semantics:

```jsonc
// "Move the title element 2s later, make it bigger" — only the touched fields:
{
  "Elements": [                 // id-keyed merge: members matched by Id; unmentioned siblings left untouched
    { "Id": "<title-element-id>", "Start": "00:00:02", "Objects": [ { "Id": "<text-id>", "Size": 140 } ] }
  ]
}
```

**Apply algorithm** (`Apply(target, patch)`):
- if `patch` is an object: for each member, `null` ⇒ remove the key from `target`; otherwise `target[key] = Apply(target[key], value)` (DeepClone before reparenting — STJ single-parent).
- if `patch` is an **array of identity-bearing entities** (members carry `Id` — elements, content objects, keyframes): apply **id-keyed merge** — a member that **omits `Id`** is minted-and-inserted; a member whose `Id` matches a `target` member is merged into it (its `$type` must equal the existing one — a differing `$type` on an existing `Id` is `validation_rejected`); a member whose supplied `Id` is **not** in `target` is `stale_handle`; **`target` members not named in the patch are left unchanged**; a patch member `{ "Id": "…", "$delete": true }` (or the imperative `remove_*`) deletes one.
- otherwise (scalar, or an array of non-identified values): `target` becomes a DeepClone of `patch` (RFC 7396 wholesale replace).

**Important — identity-bearing arrays do NOT replace wholesale.** Strict RFC 7396 has no array identity, which would make a single-element patch delete its siblings (violating FR-028). The toolkit therefore applies **id-keyed merge** to arrays of `Id`-bearing entities (rule above): a patch touching one element leaves the rest intact. The merge yields the *desired document* with siblings preserved, and the reconciler then runs the **Id-based collection diff** (data-model §Change Set) to compute the minimal `insert/remove/move/update` operations. Only scalar / non-identified arrays replace wholesale. To delete a member, mark it `"$delete": true` or use the imperative `remove_element`/`remove_effect`. This is documented to the agent via the Skill recipes.

**Ordering.** Id-keyed merge **preserves existing members' relative order**. A new (omitted-`Id`) member is **appended** unless it carries a position directive — `"$index": N`, or `"$after"`/`"$before"` set to a sibling `Id`. To reorder an existing member, include it with such a directive → the reconciler emits a `move-child` operation (and the Change Set carries the resulting `index`). **Keyframes are the exception**: their order is always derived from `KeyTime` (the model re-sorts via `KeyFrames.Add(out index)`), so position directives on keyframes are ignored. The directives `$index`/`$after`/`$before` are **mutually exclusive** — supplying more than one on a single member is `validation_rejected`; an `$after`/`$before` that names a non-existent sibling `Id` is `stale_handle`.

**Deletion idempotence.** `{ "Id": "…", "$delete": true }` on a member that does **not** exist is an **idempotent no-op** (the desired absent state is already met) — this is deliberately distinct from a *plain* member referencing an unknown `Id`, which is `stale_handle` (a plain reference expects the entity to exist; a delete only asserts the end-state).

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
