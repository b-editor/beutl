"""Exercise the Linux C ABI in a subprocess so a font enumeration hang is bounded."""

import argparse
import ctypes
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]
NATIVE_ROOT = ROOT / "src" / "Beutl.Engine"


def probe(library):
    # Inspect the same isolated configuration Skia will load. A timeout must only
    # count as a regression if the fixture really contains a missing property.
    fontconfig = ctypes.CDLL("libfontconfig.so.1")

    class FontSet(ctypes.Structure):
        _fields_ = [("nfont", ctypes.c_int), ("sfont", ctypes.c_int),
                    ("fonts", ctypes.POINTER(ctypes.c_void_p))]

    fontconfig.FcInitLoadConfigAndFonts.restype = ctypes.c_void_p
    fontconfig.FcConfigGetFonts.argtypes = [ctypes.c_void_p, ctypes.c_int]
    fontconfig.FcConfigGetFonts.restype = ctypes.POINTER(FontSet)
    fontconfig.FcPatternGetString.argtypes = [ctypes.c_void_p, ctypes.c_char_p,
                                            ctypes.c_int, ctypes.POINTER(ctypes.c_char_p)]
    fontconfig.FcConfigDestroy.argtypes = [ctypes.c_void_p]
    config = fontconfig.FcInitLoadConfigAndFonts()
    if not config:
        raise RuntimeError("Fontconfig could not load the fixture")
    try:
        fonts = fontconfig.FcConfigGetFonts(config, 0).contents  # FcSetSystem
        missing = 0
        for index in range(fonts.nfont):
            family = ctypes.c_char_p()
            result = fontconfig.FcPatternGetString(fonts.fonts[index], b"family", 0,
                                                 ctypes.byref(family))
            if result == 1:  # FcResultNoMatch: the property itself is absent.
                missing += 1
            elif result != 0:  # FcResultMatch
                raise RuntimeError(f"Unexpected Fontconfig result: {result}")
        print(json.dumps({"fonts": fonts.nfont, "missing": missing}), flush=True)
    finally:
        fontconfig.FcConfigDestroy(config)

    native = ctypes.CDLL(str(library))
    native.sk_fontmgr_create_default.restype = ctypes.c_void_p
    native.sk_fontmgr_count_families.argtypes = [ctypes.c_void_p]
    native.sk_fontmgr_get_family_name.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p]
    native.sk_fontmgr_unref.argtypes = [ctypes.c_void_p]
    native.sk_string_new_empty.restype = ctypes.c_void_p
    native.sk_string_get_c_str.argtypes = [ctypes.c_void_p]
    native.sk_string_get_c_str.restype = ctypes.c_char_p
    native.sk_string_destructor.argtypes = [ctypes.c_void_p]
    native.sk_fontmgr_match_family.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
    native.sk_fontmgr_match_family.restype = ctypes.c_void_p
    native.sk_fontstyleset_get_count.argtypes = [ctypes.c_void_p]
    native.sk_fontstyleset_create_typeface.argtypes = [ctypes.c_void_p, ctypes.c_int]
    native.sk_fontstyleset_create_typeface.restype = ctypes.c_void_p
    native.sk_fontstyleset_unref.argtypes = [ctypes.c_void_p]
    native.sk_typeface_unref.argtypes = [ctypes.c_void_p]

    manager = native.sk_fontmgr_create_default()
    if not manager:
        raise RuntimeError("Skia returned a null font manager")
    try:
        families = []
        for index in range(native.sk_fontmgr_count_families(manager)):
            name = native.sk_string_new_empty()
            try:
                native.sk_fontmgr_get_family_name(manager, index, name)
                family = native.sk_string_get_c_str(name)
                families.append(family.decode("utf-8"))
                styles = native.sk_fontmgr_match_family(manager, family)
                if not styles:
                    raise RuntimeError(f"No styles for {family!r}")
                try:
                    if native.sk_fontstyleset_get_count(styles) < 1:
                        raise RuntimeError(f"Empty style set for {family!r}")
                    typeface = native.sk_fontstyleset_create_typeface(styles, 0)
                    if not typeface:
                        raise RuntimeError(f"Cannot load a typeface for {family!r}")
                    native.sk_typeface_unref(typeface)
                finally:
                    native.sk_fontstyleset_unref(styles)
            finally:
                native.sk_string_destructor(name)
        print(json.dumps(sorted(families)), flush=True)
    finally:
        native.sk_fontmgr_unref(manager)


@unittest.skipUnless(sys.platform == "linux", "Fontconfig is Linux-only")
class FontconfigInitializationTests(unittest.TestCase):
    def check_fonts(self, *, valid, missing):
        arch = {"x86_64": "x64", "aarch64": "arm64"}.get(platform.machine().lower())
        if arch is None:
            self.skipTest("No bundled Linux runtime for this architecture")
        library = NATIVE_ROOT / "runtimes" / f"linux-{arch}" / "native" / "libSkiaSharp.so"
        with tempfile.TemporaryDirectory(prefix="beutl-fontconfig-") as temporary:
            root = Path(temporary)
            fonts = root / "fonts"
            fonts.mkdir()
            assets = ROOT / "tests" / "Beutl.UnitTests" / "Assets" / "Font"
            if valid:
                # Two styles share the same family and have additional family
                # names. Check both alias enumeration and deduplication.
                for name in ("Roboto-Regular.ttf", "Roboto-Medium.ttf"):
                    shutil.copy2(assets / name, fonts / name)
            if missing:
                shutil.copy2(assets / "Roboto-Regular.ttf", fonts / "MissingFamily.ttf")
            config = root / "fonts.conf"
            config.write_text(f"""<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
<fontconfig>
  <dir>{escape(str(fonts))}</dir>
  <cachedir>{escape(str(root / 'cache'))}</cachedir>
  <match target="scan">
    <edit name="family" mode="append"><string>Beutl Regression Alias</string></edit>
  </match>
  <match target="scan">
    <test name="file" compare="eq"><string>{escape(str(fonts / 'MissingFamily.ttf'))}</string></test>
    <edit name="family" mode="delete_all"/>
  </match>
</fontconfig>
""", encoding="utf-8")
            try:
                result = subprocess.run(
                    [sys.executable, str(Path(__file__).resolve()), "--probe", str(library)],
                    env={**os.environ, "FONTCONFIG_FILE": str(config), "FONTCONFIG_PATH": str(root)},
                    capture_output=True, text=True, timeout=20,
                )
            except subprocess.TimeoutExpired as error:
                self.fail(f"Font manager initialization hung; fixture: {error.stdout!r}")
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            fixture, families = map(json.loads, result.stdout.splitlines())
            self.assertEqual(fixture, {"fonts": (2 if valid else 0) + int(missing),
                                       "missing": int(missing)})
            self.assertEqual(families, ["Beutl Regression Alias", "Roboto", "Roboto Medium"] if valid else [])

    def test_normal_families_and_aliases_remain_available(self):
        self.check_fonts(valid=True, missing=False)

    def test_missing_family_is_skipped_and_valid_fonts_remain_usable(self):
        self.check_fonts(valid=True, missing=True)

    def test_only_missing_families_produces_an_empty_manager(self):
        self.check_fonts(valid=False, missing=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--probe", type=Path)
    parser.add_argument("--native-root", type=Path, default=NATIVE_ROOT)
    args, remaining = parser.parse_known_args()
    if args.probe:
        probe(args.probe)
    else:
        NATIVE_ROOT = args.native_root.resolve()
        unittest.main(argv=[sys.argv[0], *remaining])
