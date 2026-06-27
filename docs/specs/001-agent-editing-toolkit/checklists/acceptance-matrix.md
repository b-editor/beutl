# Acceptance Matrix

This matrix records aggregate acceptance checks for the Agent Editing Toolkit.

## SC-001: Briefs Open Cleanly

Pass threshold: at least 90% of authored brief fixtures save and reopen in Beutl.

| Fixture | Expected | Result | Notes |
|---|---|---|---|
| title-card | 10 s scene with title and shape background | pending | |
| media-overlay | image/video source plus overlay text | pending | Requires local media fixture |
| multi-scene | two scenes saved in one project | pending | |
| audio-bed | audio element and visual scene | pending | Requires local audio fixture |

## SC-004: GUI Operation Parity

Pass threshold: at least 90% of in-scope GUI operations have an equivalent toolkit operation.

| Operation | Toolkit Path | Result | Notes |
|---|---|---|---|
| Add element | `add_element` / `apply_edit` | covered | |
| Move element | `move_element` / merge-patch ordering | covered | |
| Remove element | `remove_element` with `confirmDelete` | covered | |
| Duplicate element | `duplicate_element` | covered | |
| Split element | `split_element` | covered | |
| Group/ungroup | `group_elements` / `ungroup_elements` | covered | |
| Property edit | `apply_edit` full document or patch | covered | |
| Effect attach/reorder/remove | id-keyed merge-patch | covered | |

## SC-007: Render/Export Success

Pass threshold: at least 95% of render/export attempts for supported content succeed; unsupported GPU/codec cases return typed errors.

| Fixture | Still Render | Video Export | Expected Typed Errors |
|---|---|---|---|
| CPU-safe shape/text | pending | pending | none |
| SKSL/particle CPU-safe | pending | pending | none |
| 3D without GPU | pending | n/a | `rendering_unavailable` |
| Missing FFmpeg libs | n/a | pending | `codec_unavailable` |
| Unsupported output extension | n/a | pending | `codec_unavailable` |

## Run Record

- Date:
- Commit:
- Platform:
- `dotnet build Beutl.slnx`: pass / fail
- `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings`: pass / fail
- Manual quickstart result: pass / fail
