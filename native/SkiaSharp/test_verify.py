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
        return verify.library_name(rid)

    @staticmethod
    def macho_slices(data):
        count = struct.unpack_from(">I", data, 4)[0]
        return [(8 + index * 20,) + struct.unpack_from(">iiII", data, 8 + index * 20) for index in range(count)]

    @staticmethod
    def macho_load_command(data, start, kinds):
        command_count = struct.unpack_from("<I", data, start + 16)[0]
        offset = start + 32
        for _ in range(command_count):
            command, command_size = struct.unpack_from("<II", data, offset)
            if command in kinds:
                return offset
            offset += command_size
        raise AssertionError(f"no load command {kinds} in the slice at {start}")

    @classmethod
    def macho_export_edges(cls, data, start):
        """Maps each exported name of one slice to the file offset of the trie edge that ends it."""
        command = cls.macho_load_command(data, start, (0x22, 0x80000022))
        trie = start + struct.unpack_from("<I", data, command + 40)[0]
        edges = {}

        def uleb(offset):
            value, shift = 0, 0
            while True:
                byte = data[offset]
                offset += 1
                value |= (byte & 0x7F) << shift
                shift += 7
                if byte < 0x80:
                    return value, offset

        pending = [(0, b"", None)]
        while pending:
            node, prefix, edge = pending.pop()
            terminal, offset = uleb(trie + node)
            if terminal:
                edges[prefix] = edge
            offset += terminal
            children = data[offset]
            offset += 1
            for _ in range(children):
                end = data.index(b"\0", offset)
                name, edge_offset = data[offset:end], offset
                child, offset = uleb(end + 1)
                pending.append((child, prefix + name, edge_offset))
        return edges

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
            if rid == "osx":
                continue
            other = rid.replace("x64", "arm64") if rid.endswith("x64") else rid.replace("arm64", "x64")
            with self.subTest(rid=rid):
                self.assert_rejected_with_updated_hash(rid, self.binary(other), "architecture mismatch")

    def test_macos_runtime_requires_both_slices(self):
        for architecture, cpu in (("arm64", 0x0100000C), ("x86_64", 0x01000007)):
            with self.subTest(architecture=architecture):
                data = bytearray(self.binary("osx"))
                for header, slice_cpu, *_ in self.macho_slices(data):
                    if slice_cpu == cpu:
                        struct.pack_into(">i", data, header, 7)  # Relabel the slice as i386.
                self.assert_rejected_with_updated_hash("osx", bytes(data), f"architecture mismatch.*{architecture}")

    def test_macos_slice_headers_must_match_their_universal_labels(self):
        for architecture, other in (("arm64", 0x01000007), ("x86_64", 0x0100000C)):
            with self.subTest(architecture=architecture):
                data = bytearray(self.binary("osx"))
                for _, slice_cpu, _, start, _ in self.macho_slices(data):
                    if verify.MACHO_SLICES.get(slice_cpu) == architecture:
                        struct.pack_into("<i", data, start + 4, other)  # Keep the outer label, change the image.
                self.assert_rejected_with_updated_hash("osx", bytes(data), f"architecture mismatch.*{architecture}")

    def test_macos_runtime_requires_the_linker_patch_hash(self):
        for key in ("macosLinkerPatchSha256",):
            with self.subTest(key=key), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                directory = root / "runtimes" / "osx" / "native"
                shutil.copytree(self.native_root / "runtimes" / "osx" / "native", directory)
                manifest_path = directory / "build.json"
                manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
                manifest.pop(key, None)
                manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, key):
                    verify.verify(root, ["osx"])

    def test_each_required_export_is_checked(self):
        for rid in verify.RIDS:
            if rid == "osx":
                continue
            for export in verify.required_exports(rid):
                with self.subTest(rid=rid, export=export):
                    original = self.binary(rid)
                    renamed = original.replace(export + b"\0", b"x" + export[1:] + b"\0")
                    self.assertNotEqual(renamed, original)
                    self.assert_rejected_with_updated_hash(rid, renamed, "missing required exports")

    def test_each_required_export_is_checked_in_every_macos_slice(self):
        for _, slice_cpu, _, start, _ in self.macho_slices(self.binary("osx")):
            for export in verify.REQUIRED_EXPORTS:
                with self.subTest(slice=verify.MACHO_SLICES[slice_cpu], export=export):
                    data = bytearray(self.binary("osx"))
                    edge = self.macho_export_edges(data, start)[b"_" + export]
                    data[edge] = ord("x")  # Rename the export in this slice's trie only; LC_SYMTAB keeps it.
                    self.assertTrue(export + b"\0" in data)
                    self.assert_rejected_with_updated_hash("osx", bytes(data), "missing required exports")

    def test_export_names_without_definitions_do_not_pass(self):
        for rid in verify.RIDS:
            with self.subTest(rid=rid):
                data = bytearray(self.binary(rid))
                if rid == "osx":
                    # Leave the symbol tables intact, but give every slice an empty export trie.
                    for _, _, _, start, _ in self.macho_slices(data):
                        command = self.macho_load_command(data, start, (0x22, 0x80000022))
                        struct.pack_into("<I", data, command + 44, 0)
                elif rid.startswith("linux-"):
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
                self.assert_rejected_with_updated_hash(rid, bytes(data), "missing required exports")

    def test_truncated_binaries_fail_even_with_a_matching_hash(self):
        for rid in verify.RIDS:
            with self.subTest(rid=rid):
                self.assert_rejected_with_updated_hash(rid, self.binary(rid)[:64], "ELF|PE|Mach-O|truncated")


if __name__ == "__main__":
    unittest.main()
