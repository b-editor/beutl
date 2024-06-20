## Guidelines for Contributing to Beutl

### Pull request
To avoid duplicating work that may already be in progress, it is recommended to open an issue before submitting a PR.

If the changes are minor, you may not need to open an issue.

To keep the history clean, **do not forget to rebase and force push.**

### Code Guidelines

We use the [.NET Coding Style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md).

**UI Implementation**
- If the event handler of a UserControl becomes complex, separate it into a Behavior or split the file using `partial`.

XAML Files
- Use four spaces for indentation.
- When adding properties to a control, place the first property on the same line as the element, and align all subsequent properties on separate lines with the first property.
- When using `Binding`, use [compiled bindings](https://docs.avaloniaui.net/docs/next/basics/data/data-binding/compiled-bindings).
```xaml
<UserControl x:CompileBindings="True"
             x:DataType="viewModel:MyViewModel">
    <TextBox Foreground="White"
             MaxWidth="240"
             Text="{Binding Text.Value}" />
</UserControl>
```