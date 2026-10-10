#!/usr/bin/env python3
"""Fail when a RID-specific publish carries a native binary the target cannot load.

A library chosen for the build host instead of the target, as a RID-neutral project reference
does, keeps working on the host. Checking the published files is what catches it before a
cross-architecture release (e.g. linux-arm64 built on x64) ships it.
"""

import argparse
import struct
from pathlib import Path


ELF_MACHINES = {62: "x64", 183: "arm64"}
PE_MACHINES = {0x8664: "x64", 0xAA64: "arm64", 0x014C: "x86"}
MACHO_CPUS = {0x01000007: "x64", 0x0100000C: "arm64"}
FORMATS = {"linux": "ELF", "win": "PE", "osx": "Mach-O"}


def read(data, layout, offset):
    if offset < 0 or offset + struct.calcsize(layout) > len(data):
        raise ValueError("truncated header")
    return struct.unpack_from(layout, data, offset)


def identify(data):
    """Returns (format, architectures) of a native binary, or None for any other file."""
    if data[:4] == b"\x7fELF":
        order = "<" if data[5] == 1 else ">"
        machine = read(data, order + "H", 18)[0]
        return "ELF", {ELF_MACHINES.get(machine, f"e_machine {machine}")}
    if data[:2] == b"MZ":
        header = read(data, "<I", 0x3C)[0]
        if data[header:header + 4] != b"PE\0\0":
            return None
        machine = read(data, "<H", header + 4)[0]
        optional = header + 24
        wide = read(data, "<H", optional)[0] == 0x20B
        count = read(data, "<I", optional + (108 if wide else 92))[0]
        directories = optional + (112 if wide else 96)
        # A CLI header marks a managed assembly, whose machine field need not name the target.
        if count > 14 and read(data, "<I", directories + 14 * 8)[0] != 0:
            return None
        return "PE", {PE_MACHINES.get(machine, f"machine {machine:#x}")}
    magic = read(data, ">I", 0)[0] if len(data) >= 4 else 0
    if magic in (0xCFFAEDFE, 0xCEFAEDFE):  # thin Mach-O, little-endian
        cpu = read(data, "<I", 4)[0]
        return "Mach-O", {MACHO_CPUS.get(cpu, f"cputype {cpu:#x}")}
    if magic in (0xCAFEBABE, 0xCAFEBABF):
        count = read(data, ">I", 4)[0]
        if not 0 < count < 20:  # Java class files share the magic number
            return None
        size = 32 if magic == 0xCAFEBABF else 20
        cpus = [read(data, ">I", 8 + index * size)[0] for index in range(count)]
        return "Mach-O", {MACHO_CPUS.get(cpu, f"cputype {cpu:#x}") for cpu in cpus}
    return None


def loaded_on(relative, rid):
    """Whether the runtime may load a file at this path for the target RID."""
    parts = relative.parts
    if "runtimes" not in parts[:-1]:
        return True
    folder = parts[parts.index("runtimes") + 1]
    system = rid.split("-")[0]
    return folder in (rid, system, "any") or (folder == "unix" and system != "win")


def verify(directory, rid):
    system, arch = rid.split("-")
    problems = []
    checked = 0
    for path in sorted(p for p in directory.rglob("*") if p.is_file() and not p.is_symlink()):
        relative = path.relative_to(directory)
        if not loaded_on(relative, rid):
            continue
        with path.open("rb") as stream:
            data = stream.read(64 * 1024)
        try:
            native = identify(data)
        except ValueError as error:
            problems.append(f"{relative}: {error}")
            continue
        if native is None:
            continue
        checked += 1
        kind, architectures = native
        if kind != FORMATS[system]:
            problems.append(f"{relative}: {kind} binary in a {rid} publish")
        elif arch not in architectures:
            problems.append(f"{relative}: built for {', '.join(sorted(architectures))}, not {arch}")
    return checked, problems


def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, help="target runtime identifier, e.g. linux-arm64 or linux_arm64")
    parser.add_argument("directory", type=Path, help="publish output to check")
    args = parser.parse_args(arguments)
    rid = args.rid.replace("_", "-")
    if rid.count("-") != 1 or rid.split("-")[0] not in FORMATS:
        parser.error(f"unsupported runtime identifier: {args.rid}")
    checked, problems = verify(args.directory, rid)
    for problem in problems:
        print(f"::error::{problem}")
    if checked == 0:
        # A missing, empty or wrong directory has nothing to check; name it instead of reporting "0 of 0".
        print(f"::error::{args.directory} has no native binaries; expected the {rid} publish output.")
        parser.exit(1)
    if problems:
        parser.exit(1, f"{len(problems)} of {checked} native binaries cannot load on {rid}.\n")
    print(f"Verified {checked} native binaries for {rid}.")


if __name__ == "__main__":
    main()
