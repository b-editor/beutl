# Default font API migration

`FontFamily.Default` is now a read-only static property instead of a static
field. It returns `FontManager.Instance.DefaultTypeface.FontFamily`, so both
APIs use the same resolved font and initialization no longer starts a second
`fc-match` process.

Source code that reads `FontFamily.Default` needs no changes, but extensions
compiled against the previous field must be rebuilt. Reflection-based code
must use `GetProperty("Default")` instead of `GetField("Default")`.

On Linux, `fc-match` is resolved through `PATH`. If it cannot start, exits with
an error, returns an unusable font, or exceeds the three-second query timeout,
the manager uses Skia's default typeface.
