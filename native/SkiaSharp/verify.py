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
RIDS = ("linux-x64", "linux-arm64", "win-x64", "win-arm64", "osx")
REQUIRED_EXPORTS = {
    b"gr_beutl_backendrendertarget_get_vk_image_layout",
    b"gr_beutl_backendrendertarget_set_vk_image_layout",
}
WINDOWS_EXPORTS = {b"sk_beutl_fontmgr_create_freetype"}


def required_exports(rid):
    return REQUIRED_EXPORTS | (WINDOWS_EXPORTS if rid.startswith("win-") else set())


# Mach-O CPU types of the slices the universal macOS runtime must carry.
MACHO_SLICES = {0x0100000C: "arm64", 0x01000007: "x86_64"}


def library_name(rid):
    if rid == "osx":
        return "libSkiaSharp.dylib"
    return "libSkiaSharp.so" if rid.startswith("linux-") else "libSkiaSharp.dll"


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


def uleb128(data, offset, end):
    value = shift = 0
    while True:
        if offset >= end:
            raise ValueError("Native binary contains a truncated ULEB128 value.")
        byte = data[offset]
        offset += 1
        value |= (byte & 0x7F) << shift
        shift += 7
        if byte < 0x80:
            return value, offset


def macho_trie_exports(data, start, end, rid, name):
    # dyld resolves symbols through this trie, not LC_SYMTAB; a name only in the symbol table does not load.
    exports = set()
    pending = [(0, b"")]
    visited = set()
    while pending:
        node, prefix = pending.pop()
        if node in visited or not 0 <= node < end - start:
            raise ValueError(f"{rid}: invalid Mach-O export trie in the {name} slice.")
        visited.add(node)
        terminal_size, offset = uleb128(data, start + node, end)
        if terminal_size:
            flags, _ = uleb128(data, offset, min(end, offset + terminal_size))
            # Regular definitions only: re-exports are not our C ABI implementation.
            if flags & 0x03 == 0 and not flags & 0x08:
                exports.add(prefix[1:] if prefix.startswith(b"_") else prefix)
        offset += terminal_size
        child_count = unpack(data, "B", offset)[0]
        offset += 1
        for _ in range(child_count):
            edge = c_string(data, offset, end)
            offset += len(edge) + 1
            child, offset = uleb128(data, offset, end)
            pending.append((child, prefix + edge))
    return exports


def macho_slice_exports(data, start, end, rid, name, cpu):
    if unpack(data, "<I", start)[0] != 0xFEEDFACF:
        raise ValueError(f"{rid}: {name} slice is not a 64-bit Mach-O image.")
    inner_cpu = unpack(data, "<i", start + 4)[0]
    if inner_cpu != cpu:
        raise ValueError(f"{rid}: Mach-O architecture mismatch ({name} slice header has CPU type {inner_cpu:#x}).")
    command_count, commands_size = unpack(data, "<II", start + 16)
    if unpack(data, "<I", start + 12)[0] != 6 or start + 32 + commands_size > end:  # MH_DYLIB
        raise ValueError(f"{rid}: {name} slice is not a Mach-O dynamic library.")
    trie = None
    offset = start + 32
    for _ in range(command_count):
        command, command_size = unpack(data, "<II", offset)
        if command_size < 8 or offset + command_size > start + 32 + commands_size:
            raise ValueError(f"{rid}: invalid Mach-O load command in the {name} slice.")
        if command in (0x22, 0x80000022):  # LC_DYLD_INFO, LC_DYLD_INFO_ONLY
            trie = unpack(data, "<II", offset + 40)
        elif command == 0x80000033:  # LC_DYLD_EXPORTS_TRIE
            trie = unpack(data, "<II", offset + 8)
        offset += command_size
    if trie is None or trie[1] == 0:
        return set()
    trie_start, trie_end = start + trie[0], start + trie[0] + trie[1]
    if trie_end > end:
        raise ValueError(f"{rid}: invalid Mach-O export trie in the {name} slice.")
    return macho_trie_exports(data, trie_start, trie_end, rid, name)


def macho_exports(data, rid):
    # The macOS runtime is a universal library, as the upstream SkiaSharp asset is.
    if unpack(data, ">I", 0)[0] not in (0xCAFEBABE, 0xCAFEBABF):
        raise ValueError(f"{rid}: expected a universal Mach-O library.")
    wide = unpack(data, ">I", 0)[0] == 0xCAFEBABF
    slice_count = unpack(data, ">I", 4)[0]
    slices = {}
    for index in range(slice_count):
        if wide:
            cpu, _, start, size = unpack(data, ">iiQQ", 8 + index * 32)
        else:
            cpu, _, start, size = unpack(data, ">iiII", 8 + index * 20)
        if cpu in MACHO_SLICES:
            if start + size > len(data):
                raise ValueError(f"{rid}: truncated Mach-O {MACHO_SLICES[cpu]} slice.")
            slices[MACHO_SLICES[cpu]] = macho_slice_exports(data, start, start + size, rid, MACHO_SLICES[cpu], cpu)
    missing = sorted(set(MACHO_SLICES.values()) - slices.keys())
    if missing:
        raise ValueError(f"{rid}: Mach-O architecture mismatch (missing {', '.join(missing)} slice).")
    # An export counts only when every slice provides it.
    return set.intersection(*slices.values())


def verify_binary(data, rid):
    if rid == "osx":
        exports = macho_exports(data, rid)
    else:
        exports = elf_exports(data, rid) if rid.startswith("linux-") else pe_exports(data, rid)
    missing = required_exports(rid) - exports
    if missing:
        names = ", ".join(name.decode("ascii") for name in sorted(missing))
        raise ValueError(f"{rid}: missing required exports: {names}")


def verify(native_root, rids):
    source = json.loads((HERE / "source.json").read_text(encoding="utf-8"))
    for rid in rids:
        directory = native_root / "runtimes" / rid / "native"
        manifest = json.loads((directory / "build.json").read_text(encoding="utf-8"))
        for key, expected in {**source, "rid": rid, **patch_hashes(rid)}.items():
            if manifest.get(key) != expected:
                raise ValueError(f"{rid}: {key} does not match the pinned source and patch; rebuild this runtime.")
        filename = library_name(rid)
        data = (directory / filename).read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if manifest.get("binarySha256") != digest:
            raise ValueError(f"{rid}: {filename} does not match its recorded SHA-256.")
        verify_binary(data, rid)
        for notice in ("Skia.LICENSE", "Skia.NOTICES"):
            if not (directory / notice).read_bytes():
                raise ValueError(f"{rid}: {notice} is empty.")
        print(f"Verified {rid}: SkiaSharp {source['skiaSharpVersion']}, architecture and Beutl exports, SHA-256 {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-root", type=Path, default=ROOT / "src" / "Beutl.Engine")
    parser.add_argument("--rid", choices=RIDS, action="append")
    args = parser.parse_args()
    try:
        verify(args.native_root, args.rid or RIDS)
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
