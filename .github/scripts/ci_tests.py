"""Run exhaustive, disjoint CI shards against the shared Debug build."""

import argparse
from pathlib import Path
import re
import shlex
import subprocess
import xml.etree.ElementTree as ET


def partition(groups, suffix=""):
    """Each group excludes earlier groups; the final group catches every new test."""
    filters = {}
    previous = []
    for name, prefixes in groups:
        patterns = ["^" + re.escape(prefix) + suffix for prefix in prefixes]
        include = " or ".join(f"class =~ '{pattern}'" for pattern in patterns)
        exclude = " and ".join(f"class !~ '{pattern}'" for pattern in previous)
        filters[name] = " and ".join(part for part in (f"({include})" if include else "", exclude) if part)
        previous.extend(patterns)
    return filters


UNIT_FILTERS = partition([
    ("unit-particles", ["Beutl.UnitTests.Engine.Graphics.Particles."]),
    ("unit-rendering", ["Beutl.UnitTests.Engine.Graphics.Rendering."]),
    ("unit-audio", ["Beutl.UnitTests.Engine.Audio."]),
    ("unit-engine", ["Beutl.UnitTests.Engine."]),
    ("unit-git", ["Beutl.UnitTests.Editor.VersionControl."]),
    ("unit-other", []),
])
# A prefix matching AssemblySetUp selects its entire subtree in NUnit. Matching
# test fixture names keeps that namespace-wide SetUpFixture out of the partition.
UI_FILTERS = partition([
    ("ui-a-g", [f"Beutl.HeadlessUITests.{letter}" for letter in "ABCDEFG"]),
    ("ui-h-p", [f"Beutl.HeadlessUITests.{letter}" for letter in "HIJKLMNOP"]),
    ("ui-other", []),
], suffix=".*Tests$")
FILTERS = UNIT_FILTERS | UI_FILTERS
SHARDED_PROJECTS = {"Beutl.UnitTests", "Beutl.HeadlessUITests", "Beutl.Graphics3DTests"}


def test_projects(root):
    # Follow the solution, including newly added test projects, and omit benchmarks/helpers.
    for item in ET.parse(root / "Beutl.slnx").iter("Project"):
        project = root / item.attrib["Path"].replace("\\", "/")
        if any((prop.text or "").strip().lower() == "true"
               for prop in ET.parse(project).iter("IsTestProject")):
            yield project


def assemblies(root, suite):
    if suite in UNIT_FILTERS:
        names = ["Beutl.UnitTests"]
    elif suite in UI_FILTERS:
        names = ["Beutl.HeadlessUITests"]
    elif suite == "graphics3d":
        names = ["Beutl.Graphics3DTests"]
    else:
        names = [project.stem for project in test_projects(root)
                 if project.stem not in SHARDED_PROJECTS]
    return [root / "tests" / name / "bin" / "Debug" / "net10.0" / f"{name}.dll"
            for name in names]


def test_command(root, assembly, suite, validation):
    adapter_path = str(assembly.parent)
    if not validation:
        # Project-based runs add this NuGet path through Coverlet's MSBuild target.
        # DLL-based runs must supply the bundled collector explicitly.
        adapter_path += ";" + str(root / ".ci-tools" / "coverlet")
    command = [
        "dotnet", "test", str(assembly), "--test-adapter-path", adapter_path,
        "--logger", "trx", "--results-directory", str(root / "TestResults" / assembly.stem),
        "--blame-hang", "--blame-hang-timeout", "5m", "--blame-hang-dump-type", "mini",
        "--blame-crash", "--blame-crash-dump-type", "mini",
    ]
    if not validation:
        command.extend(["--collect", "XPlat Code Coverage", "--settings", str(root / "coverlet.runsettings")])
    if suite in FILTERS:
        # DLL execution can pass discovery's selected tests back as a VSTest list.
        # Disable the adapter's 2,000-case cutoff on that list as well, so large
        # shards stay filtered. Explicit/manual capture fixtures stay excluded.
        command.extend([
            "--", "NUnit.Where=" + FILTERS[suite], "NUnit.ExplicitMode=None",
            "NUnit.AssemblySelectLimit=2147483647",
        ])
    return command


def has_test_results(directory):
    # VSTest can return success for a filter matching no tests. A shard must actually discover tests.
    for path in directory.glob("*.trx"):
        counters = ET.parse(path).find(".//{*}Counters")
        if counters is not None and int(counters.attrib["total"]) > 0:
            return True
    return False


def run(root, suite, validation):
    failed = False
    selected = assemblies(root, suite)
    if not selected:
        raise RuntimeError(f"No test assemblies for {suite}")
    for assembly in selected:
        if not assembly.is_file():
            raise FileNotFoundError(f"Shared build is missing {assembly}")
        command = test_command(root, assembly, suite, validation)
        print(shlex.join(command), flush=True)
        result = subprocess.run(command, cwd=root, check=False)
        if result.returncode != 0:
            failed = True
        elif not has_test_results(root / "TestResults" / assembly.stem):
            print(f"::error::No tests ran for {suite}: {assembly.stem}", flush=True)
            failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("suite", choices=[*FILTERS, "graphics3d", "other"])
    parser.add_argument("--validation", action="store_true")
    arguments = parser.parse_args()
    if arguments.validation and arguments.suite not in {*UNIT_FILTERS, "graphics3d"}:
        parser.error("Vulkan validation only applies to the unit and Graphics3D suites")
    raise SystemExit(run(Path(__file__).resolve().parents[2], arguments.suite, arguments.validation))
