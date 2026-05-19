---
paths:
  - "**/*.axaml"
  - "**/*.xaml"
---

# XAML rules (Avalonia)

- Indentation: 4 spaces (mirrors `.editorconfig`). XAML Styler config: `xamlstyler.json`.
- **Compiled bindings are required**: the root element must declare `x:CompileBindings="True"` together with `x:DataType="..."`. Plain `Binding` and reflection-based bindings are disallowed for new code.
- Use `CompiledBinding` (or the implicit `{Binding}` when compile-bindings are enabled). `ReflectionBinding` is for legacy code only.
- Keep the first property attribute on the same line as the element; subsequent properties align under it, one per line. See `CONTRIBUTING.md` for the canonical example.
- ViewModel namespaces are imported via `xmlns:viewModel="..."`; pair every UserControl with its concrete `x:DataType`.
- Behavior on top of a control? Split into an Avalonia `Behavior` class or a `partial` code-behind file rather than inline event-handler bodies.

Do not add stylistic edits (whitespace, attribute order) by hand — XAML Styler is the source of truth.
