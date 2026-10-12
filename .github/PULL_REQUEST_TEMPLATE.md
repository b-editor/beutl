## Description
<!-- What does this PR do and why? Bullet points encouraged. -->

## Affected areas
<!-- Check the modules this PR touches. These map to the `area-*` labels. -->
- [ ] `Beutl.Engine` (rendering / scene / track)
- [ ] `Beutl.ProjectSystem` (project / document persistence)
- [ ] UI (`Beutl.Editor`, `Beutl.Editor.Components`, `Beutl.Controls`)
- [ ] `Beutl.Extensibility` (plugin abstractions)
- [ ] `Beutl.NodeGraph` (node editor)
- [ ] `Beutl.FFmpegIpc` / `Beutl.FFmpegWorker` (media IPC boundary)
- [ ] `Beutl.Api` (server API client)
- [ ] Build / CI / docs only

## Breaking changes
<!-- Public API / behavior changes that downstream code must adapt to.
     Write "None" if there are none. Breaking PRs use a `feat!:` / `refactor!:`
     title and a `BREAKING CHANGE:` commit footer. -->

## Raises the format floor?
<!-- Does this PR change the meaning of a persisted member, change the structure of persisted data,
     or write a type or member under a new name? Then older versions would misread what this build
     saves: raise `FileFormat.Floor` (`src/Beutl.Core/Serialization/FileFormat.cs`) in this PR,
     together with the migration that reads the old form, and give the new value here.
     New members and new types do not raise it. Write "No" otherwise. -->

## Test plan
<!-- - Unit/integration tests added or updated (path + brief intent)
     - Manual repro steps for behavior changes
     - For UI changes: screenshots or short screen recording (before/after if applicable) -->

## Fixed issues / References
<!-- - Closes #1234
     - Project board: b-editor/projects/9 ("item title")
     - Leave blank if none -->

---
<!--
Reminders (see CONTRIBUTING.md):
- New logic ships with a NUnit test under `tests/`.
- New XAML uses compiled bindings (`x:CompileBindings="True"` + `x:DataType`).
- Do not cross the GPL/MIT boundary: MIT projects must not reference `Beutl.FFmpegWorker`.
-->
