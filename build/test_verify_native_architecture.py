import contextlib
import io
from pathlib import Path
import struct
import tempfile
import unittest

import verify_native_architecture as check


def elf(machine):
    return b"\x7fELF\x02\x01\x01".ljust(18, b"\0") + struct.pack("<H", machine) + bytes(44)


def pe(machine, managed=False):
    data = bytearray(0x200)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 0x80)
    data[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<H", data, 0x84, machine)
    optional = 0x80 + 24
    struct.pack_into("<HI", data, optional, 0x20B, 0)
    struct.pack_into("<I", data, optional + 108, 16)
    if managed:
        struct.pack_into("<I", data, optional + 112 + 14 * 8, 0x2008)
    return bytes(data)


def macho(*cpus):
    if len(cpus) == 1:
        return struct.pack("<II", 0xFEEDFACF, cpus[0]) + bytes(24)
    return struct.pack(">II", 0xCAFEBABE, len(cpus)) + b"".join(struct.pack(">IIIII", cpu, 0, 0, 0, 0) for cpu in cpus)


X64, ARM64 = 62, 183


class NativeArchitectureTests(unittest.TestCase):
    def verify(self, rid, files):
        with tempfile.TemporaryDirectory() as directory:
            for name, data in files.items():
                path = Path(directory, name)
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
            return check.verify(Path(directory), rid)

    def test_host_library_in_a_cross_architecture_publish_fails(self):
        checked, problems = self.verify("linux-arm64", {"Beutl": elf(ARM64), "libshaderc_shared.so": elf(X64)})
        self.assertEqual(checked, 2)
        self.assertEqual(problems, ["libshaderc_shared.so: built for x64, not arm64"])

    def test_target_libraries_pass(self):
        self.assertEqual(self.verify("linux-arm64", {"Beutl": elf(ARM64), "libSkiaSharp.so": elf(ARM64)}), (2, []))

    def test_only_folders_the_target_loads_are_checked(self):
        checked, problems = self.verify("linux-arm64", {
            "Beutl": elf(ARM64),
            "runtimes/osx-arm64/native/libBeutlAVF.dylib": macho(0x01000007, 0x0100000C),
            "runtimes/linux-x64/native/libfoo.so": elf(X64),
            "runtimes/linux-arm64/native/libbar.so": elf(X64),
            "runtimes/unix-x64/native/libqux.so": elf(X64),
            "runtimes/unix-arm64/native/libbaz.so": elf(X64),
        })
        self.assertEqual(checked, 3)
        self.assertEqual(problems, ["runtimes/linux-arm64/native/libbar.so: built for x64, not arm64",
                                    "runtimes/unix-arm64/native/libbaz.so: built for x64, not arm64"])

    def test_windows_does_not_load_unix_folders(self):
        self.assertEqual(self.verify("win-x64", {"Beutl.exe": pe(0x8664), "runtimes/unix-x64/native/libfoo.so": elf(X64)}), (1, []))

    def test_managed_assemblies_are_not_native(self):
        checked, problems = self.verify("win-arm64", {"Beutl.exe": pe(0xAA64), "Beutl.dll": pe(0x8664, managed=True)})
        self.assertEqual((checked, problems), (1, []))

    def test_native_dll_for_another_architecture_fails(self):
        _, problems = self.verify("win-arm64", {"Beutl.exe": pe(0xAA64), "vulkan-1.dll": pe(0x8664)})
        self.assertEqual(problems, ["vulkan-1.dll: built for x64, not arm64"])

    def test_universal_library_needs_the_target_slice(self):
        self.assertEqual(self.verify("osx-x64", {"libSkiaSharp.dylib": macho(0x01000007, 0x0100000C)}), (1, []))
        _, problems = self.verify("osx-x64", {"libassimp.6.dylib": macho(0x0100000C)})
        self.assertEqual(problems, ["libassimp.6.dylib: built for arm64, not x64"])

    def test_another_systems_binary_fails(self):
        _, problems = self.verify("win-x64", {"Beutl.exe": pe(0x8664), "libassimp.so.6": elf(X64)})
        self.assertEqual(problems, ["libassimp.so.6: ELF binary in a win-x64 publish"])

    def test_non_native_files_are_ignored(self):
        self.assertEqual(self.verify("linux-x64", {"asset_metadata.json": b"{}", "Main.class": b"\xca\xfe\xba\xbe\0\0\0\x41"}), (0, []))

    def test_an_output_without_native_binaries_is_named(self):
        with tempfile.TemporaryDirectory() as directory:
            for target in (directory, str(Path(directory, "missing"))):
                output = io.StringIO()
                with contextlib.redirect_stdout(output), self.assertRaises(SystemExit) as exit:
                    check.main(["--rid", "linux_arm64", target])
                self.assertEqual(exit.exception.code, 1)
                self.assertEqual(output.getvalue(),
                                 f"::error::{target} has no native binaries; expected the linux-arm64 publish output.\n")


if __name__ == "__main__":
    unittest.main()
