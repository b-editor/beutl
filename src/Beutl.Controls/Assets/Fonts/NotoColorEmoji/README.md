# Noto Color Emoji

Source: https://github.com/googlefonts/noto-emoji/blob/e20cbc2bbec1926686be9f9bee7d1d2cfa1fea0e/2D/fonts/NotoColorEmoji.ttf

Upstream SHA-256: `15671215ab769fdc7162a045d56fd7d7e477c51b04e6b3c761d914d8fdd6cc44`

Bundled SHA-256: `190f0c0444c15f5985d8bbf7155ece65fa9a3b3f33191aae967ead077f7e06cc`

The PNG strikes are repackaged as `sbix`, with two single-point contours per bitmap
carrying its bounds for CoreText. This uses the same representation as the existing
color emoji test fixture, so macOS can render the font as well as Windows and Linux.
PNG data, glyph IDs, advances and GSUB rules are preserved. The font is licensed
under the adjacent `LICENSE.txt` (SIL Open Font License 1.1).

To reproduce with Python and fontTools 4.60.1, download the pinned upstream font and run:

```shell
python build/fonts/prepare_noto_color_emoji.py NotoColorEmoji-upstream.ttf src/Beutl.Controls/Assets/Fonts/NotoColorEmoji/NotoColorEmoji.ttf
```

The rendering engine uses it for emoji presentation while ordinary digits and text presentation stay in the selected font.
