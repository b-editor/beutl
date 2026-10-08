"""Repackage the pinned Noto bitmap font as sbix for Skia on all supported OSes.

Requires fontTools 4.60.1. No PNG data, glyph IDs, advances or GSUB rules change.
"""

import argparse
from array import array
import hashlib
from pathlib import Path

from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables._g_l_y_f import Glyph, GlyphCoordinates
from fontTools.ttLib.tables.sbixGlyph import Glyph as BitmapGlyph
from fontTools.ttLib.tables.sbixStrike import Strike
from fontTools.ttLib.tables.ttProgram import Program


SOURCE_SHA256 = "15671215ab769fdc7162a045d56fd7d7e477c51b04e6b3c761d914d8fdd6cc44"


def prepare(source: Path, destination: Path) -> None:
    if hashlib.sha256(source.read_bytes()).hexdigest() != SOURCE_SHA256:
        raise ValueError("Expected Noto Emoji e20cbc2bbec1926686be9f9bee7d1d2cfa1fea0e")

    font = TTFont(source, recalcTimestamp=False)
    glyph_order = font.getGlyphOrder()
    sbix = newTable("sbix")
    sbix.version = 1
    sbix.flags = 1
    sbix.strikes = {}
    outlines = {name: Glyph() for name in glyph_order}

    for locator, bitmaps in zip(font["CBLC"].strikes, font["CBDT"].strikeData, strict=True):
        ppem = locator.bitmapSizeTable.ppemY
        if locator.bitmapSizeTable.ppemX != ppem:
            raise ValueError("An sbix strike must have square pixels")
        strike = Strike(ppem=ppem, resolution=72)
        scale = font["head"].unitsPerEm / ppem
        for name, bitmap in bitmaps.items():
            if bitmap.__class__.__name__ != "cbdt_bitmap_format_17":
                raise ValueError("Expected PNG glyphs with small bitmap metrics")
            metrics = bitmap.metrics
            x = metrics.BearingX
            y = metrics.BearingY - metrics.height
            strike.glyphs[name] = BitmapGlyph(
                glyphName=name,
                originOffsetX=x,
                originOffsetY=y,
                graphicType="png ",
                imageData=bitmap.imageData,
            )

            # CoreText needs outline bounds for bitmap-only glyph metrics. Two single-point
            # contours carry those bounds without adding any visible outline or filled area.
            glyph = Glyph()
            glyph.numberOfContours = 2
            glyph.coordinates = GlyphCoordinates([
                (round(x * scale), round(y * scale)),
                (round((x + metrics.width) * scale), round((y + metrics.height) * scale)),
            ])
            glyph.endPtsOfContours = [0, 1]
            glyph.flags = array("B", [1, 1])
            glyph.program = Program()
            outlines[name] = glyph
        sbix.strikes[ppem] = strike

    font["sbix"] = sbix
    font["glyf"] = newTable("glyf")
    font["glyf"].glyphs = outlines
    font["glyf"].glyphOrder = glyph_order
    font["loca"] = newTable("loca")
    font["maxp"].tableVersion = 0x00010000
    for field in (
        "maxPoints", "maxContours", "maxCompositePoints", "maxCompositeContours",
        "maxTwilightPoints", "maxStorage", "maxFunctionDefs", "maxInstructionDefs",
        "maxStackElements", "maxSizeOfInstructions", "maxComponentElements", "maxComponentDepth",
    ):
        setattr(font["maxp"], field, 0)
    font["maxp"].maxZones = 1
    del font["CBDT"]
    del font["CBLC"]
    destination.parent.mkdir(parents=True, exist_ok=True)
    font.save(destination)
    font.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    prepare(args.source, args.destination)
