#!/usr/bin/env python3
"""Verify the source, hashes, architecture, exports and notices of the bundled runtimes."""

import argparse
import hashlib
import json
import struct
from pathlib import Path

from patches import patch_hashes


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
RIDS = ("linux-x64", "linux-arm64", "win-x64", "win-arm64")
REQUIRED_EXPORTS = {
    b"gr_beutl_backendrendertarget_get_vk_image_layout",
    b"gr_beutl_backendrendertarget_set_vk_image_layout",
}


def unpack(data, layout, offset):
    if offset < 0 or offset + struct.calcsize(layout) > len(data):
        raise ValueError("Native binary contains a truncated header or table.")
    return struct.unpack_from(layout, data, offset)


def c_string(data, offset, end):
    terminator = data.find(b"\0", offset, end)
    if offset < 0 or offset >= end or terminator < 0:
        raise ValueError("Native binary contains an invalid export name.")
    return data[offset:terminator]


def elf_exports(data, rid):
    # The shipped Linux runtimes are little-endian ELF64 shared objects.
    if data[:6] != b"\x7fELF\x02\x01" or unpack(data, "<H", 16)[0] != 3:
        raise ValueError(f"{rid}: expected an ELF64 shared library.")
    machine = unpack(data, "<H", 18)[0]
    if machine != {"linux-x64": 62, "linux-arm64": 183}[rid]:
        raise ValueError(f"{rid}: ELF architecture mismatch (e_machine={machine}).")
    section_offset = unpack(data, "<Q", 40)[0]
    section_size, section_count = unpack(data, "<HH", 58)
    if section_size != 64 or section_count == 0 or section_offset + section_size * section_count > len(data):
        raise ValueError(f"{rid}: invalid ELF section table.")
    sections = [unpack(data, "<IIQQQQIIQQ", section_offset + i * section_size) for i in range(section_count)]
    exports = set()
    for section in sections:
        if section[1] != 11:  # SHT_DYNSYM, not the static/debug symbol table
            continue
        offset, size, string_index, entry_size = section[4], section[5], section[6], section[9]
        if entry_size != 24 or size % entry_size or offset + size > len(data) or string_index >= len(sections):
            raise ValueError(f"{rid}: invalid ELF dynamic symbol table.")
        strings = sections[string_index]
        string_start, string_end = strings[4], strings[4] + strings[5]
        if strings[1] != 3 or string_end > len(data):  # SHT_STRTAB
            raise ValueError(f"{rid}: invalid ELF dynamic string table.")
        for position in range(offset, offset + size, entry_size):
            name, info, visibility, defined_in, _, _ = unpack(data, "<IBBHQQ", position)
            # Only defined, externally visible function symbols can satisfy the C ABI.
            if defined_in != 0 and (info >> 4) in (1, 2) and (info & 15) == 2 and (visibility & 3) in (0, 3):
                exports.add(c_string(data, string_start + name, string_end))
    return exports


def pe_exports(data, rid):
    if data[:2] != b"MZ":
        raise ValueError(f"{rid}: expected a PE DLL.")
    header = unpack(data, "<I", 60)[0]
    if data[header:header + 4] != b"PE\0\0":
        raise ValueError(f"{rid}: invalid PE signature.")
    machine, section_count = unpack(data, "<HH", header + 4)
    if machine != {"win-x64": 0x8664, "win-arm64": 0xAA64}[rid]:
        raise ValueError(f"{rid}: PE architecture mismatch (Machine={machine:#x}).")
    optional_size, characteristics = unpack(data, "<HH", header + 20)
    optional = header + 24
    if ((characteristics & 0x2000) == 0 or optional_size < 120
            or unpack(data, "<H", optional)[0] != 0x20B
            or unpack(data, "<I", optional + 108)[0] == 0):
        raise ValueError(f"{rid}: expected a PE32+ DLL with an export directory.")
    sections = [unpack(data, "<8sIIIIIIHHI", optional + optional_size + i * 40)
                for i in range(section_count)]

    def file_offset(rva, size=1):
        for section in sections:
            address, raw_size, raw_offset = section[2], section[3], section[4]
            if address <= rva and rva + size <= address + raw_size:
                offset = raw_offset + rva - address
                if offset + size <= len(data):
                    return offset
        raise ValueError(f"{rid}: PE export address is outside the file-backed sections.")

    export_rva, export_size = unpack(data, "<II", optional + 112)
    if export_rva == 0 or export_size < 40:
        return set()
    directory = file_offset(export_rva, 40)
    function_count, name_count, functions, names, ordinals = unpack(data, "<IIIII", directory + 20)
    functions = file_offset(functions, function_count * 4)
    names = file_offset(names, name_count * 4)
    ordinals = file_offset(ordinals, name_count * 2)
    exports = set()
    for index in range(name_count):
        ordinal = unpack(data, "<H", ordinals + index * 2)[0]
        if ordinal >= function_count:
            raise ValueError(f"{rid}: invalid PE export ordinal.")
        function = unpack(data, "<I", functions + ordinal * 4)[0]
        if function == 0 or export_rva <= function < export_rva + export_size:
            continue  # Absent definitions and forwarded exports are not our C ABI implementation.
        file_offset(function)
        name = file_offset(unpack(data, "<I", names + index * 4)[0])
        exports.add(c_string(data, name, len(data)))
    return exports


def verify_binary(data, rid):
    exports = elf_exports(data, rid) if rid.startswith("linux-") else pe_exports(data, rid)
    missing = REQUIRED_EXPORTS - exports
    if missing:
        names = ", ".join(name.decode("ascii") for name in sorted(missing))
        raise ValueError(f"{rid}: missing required Vulkan exports: {names}")


def verify(native_root, rids):
    source = json.loads((HERE / "source.json").read_text(encoding="utf-8"))
    for rid in rids:
        directory = native_root / "runtimes" / rid / "native"
        manifest = json.loads((directory / "build.json").read_text(encoding="utf-8"))
        for key, expected in {**source, "rid": rid, **patch_hashes(rid)}.items():
            if manifest.get(key) != expected:
                raise ValueError(f"{rid}: {key} does not match the pinned source and patch; rebuild this runtime.")
        filename = "libSkiaSharp.so" if rid.startswith("linux-") else "libSkiaSharp.dll"
        data = (directory / filename).read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if manifest.get("binarySha256") != digest:
            raise ValueError(f"{rid}: {filename} does not match its recorded SHA-256.")
        verify_binary(data, rid)
        for notice in ("Skia.LICENSE", "Skia.NOTICES"):
            if not (directory / notice).read_bytes():
                raise ValueError(f"{rid}: {notice} is empty.")
        print(f"Verified {rid}: SkiaSharp {source['skiaSharpVersion']}, architecture and Vulkan exports, SHA-256 {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-root", type=Path, default=ROOT / "src" / "Beutl.Engine")
    parser.add_argument("--rid", choices=RIDS, action="append")
    args = parser.parse_args()
    try:
        verify(args.native_root, args.rid or RIDS)
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
