# Contract: Declarative Document, Merge Patch, and Schema Descriptor

This pins the three JSON shapes the agent works with. The document mirrors Beutl's runtime object shape (`$type` + `Id` + property keys) and is a **normalized inline view** the toolkit assembles — it is NOT byte-for-byte the on-disk `CoreSerializer` output for the Scene→Element relationship, because on disk a `Scene` stores its elements under an `"Elements"` node as **Include/Exclude glob patterns** referencing separate `.belm` files (or embedded), not an inline array (`Scene.cs:143,265,271-276,320-328`; the CLR `Children` property is `[NotAutoSerialized]`, the serialized key is `"Elements"`). The toolkit normalizes that multi-file representation into one inline document for the agent and maps edits back to it. Property values and leaf object shapes are exactly `CoreSerializer`'s; round-trip safety (FR-013) is over the normalized document.

## 1. Declarative Document

A node is a JSON object that mirrors `CoreSerializer.SerializeToJsonObject`:

```jsonc
{
  "$type": "[Beutl.ProjectSystem]:Scene", // discriminator (JsonHelper.WriteDiscriminator)
  "Id": "8f3c…-…-…",                       // CoreObject.Id (Guid) — the identity anchor
  "FrameSize": [1920, 1080],               // a CoreProperty, keyed by CoreProperty.Name
  "Duration": "00:00:10.0000000",
  "Elements": [                            // normalized inline view; on disk this is Include/Exclude globs → .belm files
    {
      "$type": "[Beutl.ProjectSystem]:Element",
      "Id": "…",
      "Start": "00:00:00", "Length": "00:00:03", "ZIndex": 0,
      "Objects": [
        {
          "$type": "[Beutl.Engine]Beutl.Graphics.Shapes:TextBlock",
          "Id": "…",
          "Text": "Title",
          "Size": 96,
          "Fill": {
            "$type": "[Beutl.Engine]Beutl.Media:LinearGradientBrush",
            "Id": "…",
            "GradientStops": [
              { "$type": "[Beutl.Engine]Beutl.Media:GradientStop", "Id": "…", "Offset": 0, "Color": "#FF1AD8FF" },
              { "$type": "[Beutl.Engine]Beutl.Media:GradientStop", "Id": "…", "Offset": 1, "Color": "#FFFF45B5" }
            ]
          },
          "FilterEffect": {
            "$type": "[Beutl.Engine]Beutl.Graphics.Effects:FilterEffectGroup",
            "Id": "…",
            "Children": [
              { "$type": "[Beutl.Engine]Beutl.Graphics.Effects:Blur", "Id": "…", "Sigma": "8,8" }
            ]
          },
          "Animations": {                  // animatable property → keyframe animation
            "Opacity": {
              "$type": "…KeyFrameAnimation`1[…]",
              "KeyFrames": [
                { "$type": "…", "Id": "…", "KeyTime": "00:00:00", "Value": 0, "Easing": "[Beutl.Engine]Beutl.Animation.Easings:LinearEasing" },
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
- Every node carries `$type` and `Id`. **Id rules** (creation vs reference, unambiguous): a **new** object **omits `Id`** → the toolkit mints a Guid and inserts it; a **supplied `Id`** MUST match an existing entity (then it is updated/merged) — a supplied `Id` not in the tree is `stale_handle` (omit it to create), and a supplied `Id` whose `$type` differs from the existing object's type is `validation_rejected` (no in-place type change — delete + insert a new object instead). After creating nodes, use `apply_edit`'s returned document or `read_document` to get the minted Ids for follow-up patches.
- Property keys are the exact `CoreProperty.Name`/`IProperty.Name` from the schema (PascalCase, e.g. `FrameSize`, not `frameSize`).
- Animatable properties appear under `"Animations"`; expressions under `"Expressions"`.
- Child collections are arrays of nodes; **collection identity is by member `Id`**, not array index. A Scene's elements appear under `"Elements"` (the normalized inline view; on disk this is Include/Exclude globs over `.belm` files); an Element's content appears under `"Objects"`. Nested editable objects use the same shape: brushes can be assigned to properties such as `Fill`, gradient stops live under `GradientStops`, and filter effect chains live under `FilterEffect.Children`.
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
- if `patch` is a `$type`-bearing object with **no `Id`** and the target object has a different `$type`, the object-valued property is **replaced** instead of recursively merged. This is how an agent changes a brush property from the default `SolidColorBrush` to a `LinearGradientBrush`, or swaps one effect-valued property for another type.
- if `patch` is an **array of identity-bearing entities** (members carry `Id` — elements, content objects, keyframes, effect children, gradient stops): apply **id-keyed merge** — a member that **omits `Id`** is minted-and-inserted; a member whose `Id` matches a `target` member is merged into it (its `$type` must equal the existing one — a differing `$type` on an existing `Id` is `validation_rejected`); a member whose supplied `Id` is **not** in `target` is `stale_handle`; **`target` members not named in the patch are left unchanged**; a patch member `{ "Id": "…", "$delete": true }` deletes one.
- otherwise (scalar, or an array of non-identified values): `target` becomes a DeepClone of `patch` (RFC 7396 wholesale replace).

**Important — identity-bearing arrays do NOT replace wholesale.** Strict RFC 7396 has no array identity, which would make a single-element patch delete its siblings (violating FR-028). The toolkit therefore applies **id-keyed merge** to arrays of `Id`-bearing entities (rule above): a patch touching one element, one effect child, or one gradient stop leaves the rest intact. The merge yields the *desired document* with siblings preserved, and the reconciler then runs the **Id-based collection diff** (data-model §Change Set) to compute the minimal `insert/remove/move/update` operations. Only scalar / non-identified arrays replace wholesale. To delete a member, mark it `"$delete": true`. This is documented to the agent via the Skill recipes.

**Explicit wholesale replace — the `$replace` sentinel.** When an agent genuinely wants to *rebuild* an id-keyed array in one patch (e.g. swap an entire `FilterEffectGroup.Children` chain rather than merge into it — otherwise an id-less replacement array would append and leave the old children behind), it makes the **first** array element the sentinel `{ "$replace": true }`. The remaining elements then fully replace the array in order: each may **omit `Id`** to be minted fresh, or **reuse an existing `Id`** to keep that child (updated in place) while every other prior member is removed; `[{ "$replace": true }]` alone **clears** the array. Replacement elements may not also carry `$delete`/`$index`/`$after`/`$before`, and the sentinel must be standalone and first (otherwise `validation_rejected`). The merge yields the desired array; the reconciler then emits the minimal `remove`/`insert`/`update` operations (data-model §Change Set), preserving `Id` identity wherever a replacement reuses one. Keeping the parent object's own `Id` in the patch means only its children change, not the container.

**Ordering.** Id-keyed merge **preserves existing members' relative order**. A new (omitted-`Id`) member is **appended** unless it carries a position directive — `"$index": N`, or `"$after"`/`"$before"` set to a sibling `Id`. To reorder an existing member, include it with such a directive → the reconciler emits a `move-child` operation (and the Change Set carries the resulting `index`). **Keyframes are the exception**: their order is always derived from `KeyTime` (the model re-sorts via `KeyFrames.Add(out index)`), so position directives on keyframes are ignored. The directives `$index`/`$after`/`$before` are **mutually exclusive** — supplying more than one on a single member is `validation_rejected`; an `$after`/`$before` that names a non-existent sibling `Id` is `stale_handle`.

**Deletion idempotence.** `{ "Id": "…", "$delete": true }` on a member that does **not** exist is an **idempotent no-op** (the desired absent state is already met) — this is deliberately distinct from a *plain* member referencing an unknown `Id`, which is `stale_handle` (a plain reference expects the entity to exist; a delete only asserts the end-state).

## 3. Schema Descriptor (from `get_schema`)

```jsonc
{
  "schemaVersion": "1",
  "types": [
    {
      "type": "Beutl.Graphics.Effects.Brightness",
      "discriminator": "[Beutl.Engine]Beutl.Graphics.Effects:Brightness",
      "category": "FilterEffect",
      "baseFields": [
        { "name": "Id", "valueType": "System.Guid" },
        { "name": "IsEnabled", "valueType": "System.Boolean", "default": true }
      ],
      "properties": [
        {
          "name": "Amount",
          "valueType": "System.Single",
          "elementType": null,
          "display": { "name": "Amount", "description": "…" },
          "range": { "min": 0, "max": 100 },     // from [Range]
          "step": 1,                              // from [NumberStep]
          "default": 100,                         // IProperty.DefaultValue
          "animatable": true,                     // IProperty.IsAnimatable
          "supportsExpression": true
        }
      ]
    }
  ],
  "examples": [
    {
      "name": "create-empty-scene-motion-graphics",
      "patch": {
        "Duration": "00:00:08",
        "Elements": [
          {
            "$type": "[Beutl.ProjectSystem]:Element",
            "Start": "00:00:00",
            "Length": "00:00:08",
            "Objects": [
              { "$type": "[Beutl.Engine]Beutl.Graphics.Shapes:TextBlock", "Text": "Beutl motion", "Animations": { "Opacity": "..." } }
            ]
          }
        ]
      }
    },
    {
      "name": "apply-gradient-fill-and-effect-chain",
      "patch": {
        "Elements": [
          {
            "Id": "<element-id>",
            "Objects": [
              {
                "Id": "<drawable-id>",
                "Fill": { "$type": "[Beutl.Engine]Beutl.Media:LinearGradientBrush", "GradientStops": [ "…" ] },
                "FilterEffect": { "$type": "[Beutl.Engine]Beutl.Graphics.Effects:FilterEffectGroup", "Children": [ "…" ] }
              }
            ]
          }
        ]
      }
    }
  ]
}
```

**Field sources** (research §4): `properties[]` from `EngineObject.Properties` (`IProperty.Name`/`ValueType`/`DefaultValue`/`IsAnimatable`/`SupportsExpression`/`GetAttributes()` plus `IListProperty.ElementType` for list properties), `baseFields[]` from `PropertyRegistry.GetRegistered(type)`, `category` from `LibraryService`/`KnownLibraryItemFormats`, `$type` via `JsonHelper.WriteDiscriminator`. A property with a custom `JsonConverter` records a `converter`/encoding note instead of a structural description. The descriptor enumerates **built-in and installed-extension** drawable, sound, transform, geometry, pen, brush, visual effect, audio effect, easing, graph node, and nested engine-object types (FR-022); the headless host must run type registration so `LibraryService.Current` is populated.

## Conformance notes

- The document shape an agent writes back **must** be deserializable by the same Beutl runtime version (FR-013); cross-version handling is governed by Beutl's serializer (`schemaVersion` surfaces a mismatch rather than preserving unknown fields through an unknown-field cache).
- Values are validated on apply (`[Range]` coercion etc.); the schema's `range`/`step` let the agent stay in-range proactively, and `apply_edit` reports any coercion in the applied change result.
