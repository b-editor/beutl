import contextlib
import hashlib
import io
import json
from pathlib import Path
import shutil
import struct
import tempfile
import unittest

import verify


class NativeBinaryVerificationTests(unittest.TestCase):
    native_root = verify.ROOT / "src" / "Beutl.Engine"

    @staticmethod
    def filename(rid):
        return "libSkiaSharp.so" if rid.startswith("linux-") else "libSkiaSharp.dll"

    def binary(self, rid):
        return (self.native_root / "runtimes" / rid / "native" / self.filename(rid)).read_bytes()

    def assert_rejected_with_updated_hash(self, rid, data, message):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            directory = root / "runtimes" / rid / "native"
            shutil.copytree(self.native_root / "runtimes" / rid / "native", directory)
            (directory / self.filename(rid)).write_bytes(data)
            manifest_path = directory / "build.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            manifest["binarySha256"] = hashlib.sha256(data).hexdigest()
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, message):
                verify.verify(root, [rid])

    def test_committed_runtimes_pass_without_loading_them(self):
        for rid in verify.RIDS:
            with self.subTest(rid=rid), contextlib.redirect_stdout(io.StringIO()):
                verify.verify(self.native_root, [rid])

    def test_linux_runtimes_require_the_fontconfig_patch_hash(self):
        for rid in ("linux-x64", "linux-arm64"):
            for recorded in (None, "0" * 64):
                with self.subTest(rid=rid, recorded=recorded), tempfile.TemporaryDirectory() as temporary:
                    root = Path(temporary)
                    directory = root / "runtimes" / rid / "native"
                    shutil.copytree(self.native_root / "runtimes" / rid / "native", directory)
                    manifest_path = directory / "build.json"
                    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
                    if recorded is None:
                        manifest.pop("fontconfigPatchSha256", None)
                    else:
                        manifest["fontconfigPatchSha256"] = recorded
                    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                    with self.assertRaisesRegex(ValueError, "fontconfigPatchSha256"):
                        verify.verify(root, [rid])

    def test_swapped_architectures_fail_even_with_a_matching_hash(self):
        for rid in verify.RIDS:
            other = rid.replace("x64", "arm64") if rid.endswith("x64") else rid.replace("arm64", "x64")
            with self.subTest(rid=rid):
                self.assert_rejected_with_updated_hash(rid, self.binary(other), "architecture mismatch")

    def test_each_required_export_is_checked(self):
        for rid in verify.RIDS:
            for export in verify.required_exports(rid):
                with self.subTest(rid=rid, export=export):
                    original = self.binary(rid)
                    renamed = original.replace(export + b"\0", b"x" + export[1:] + b"\0")
                    self.assertNotEqual(renamed, original)
                    self.assert_rejected_with_updated_hash(rid, renamed, "missing required exports")

    def test_export_names_without_definitions_do_not_pass(self):
        for rid in verify.RIDS:
            with self.subTest(rid=rid):
                data = bytearray(self.binary(rid))
                if rid.startswith("linux-"):
                    section_offset = struct.unpack_from("<Q", data, 40)[0]
                    section_size, section_count = struct.unpack_from("<HH", data, 58)
                    for index in range(section_count):
                        section = struct.unpack_from("<IIQQQQIIQQ", data, section_offset + index * section_size)
                        if section[1] == 11:  # Leave all names intact, but mark dynamic symbols undefined.
                            for offset in range(section[4], section[4] + section[5], section[9]):
                                struct.pack_into("<H", data, offset + 6, 0)
                else:
                    header = struct.unpack_from("<I", data, 60)[0]
                    # Removing the export directory does not remove the name strings from the DLL.
                    struct.pack_into("<II", data, header + 24 + 112, 0, 0)
                self.assertTrue(all(export + b"\0" in data for export in verify.required_exports(rid)))
                self.assert_rejected_with_updated_hash(rid, data, "missing required exports")

    def test_truncated_binaries_fail_even_with_a_matching_hash(self):
        for rid in verify.RIDS:
            with self.subTest(rid=rid):
                self.assert_rejected_with_updated_hash(rid, self.binary(rid)[:64], "ELF|PE|truncated")


if __name__ == "__main__":
    unittest.main()
