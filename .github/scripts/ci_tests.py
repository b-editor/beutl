"""Run exhaustive, disjoint CI shards against the shared Debug build."""

import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import re
import shlex
import subprocess
import threading
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
    ("unit-golden", ["Beutl.UnitTests.Engine.Graphics.Rendering.Golden."]),
    ("unit-rendering", ["Beutl.UnitTests.Engine.Graphics.Rendering."]),
    ("unit-audio", ["Beutl.UnitTests.Engine.Audio."]),
    ("unit-engine", ["Beutl.UnitTests.Engine."]),
    ("unit-git", ["Beutl.UnitTests.Editor.VersionControl."]),
    ("unit-other", []),
])
# A prefix matching AssemblySetUp selects its entire subtree in NUnit. Matching
# test fixture names keeps that namespace-wide SetUpFixture out of the partition.
UI_FILTERS = partition([
    ("ui-a-d", [f"Beutl.HeadlessUITests.{letter}" for letter in "ABCD"]),
    ("ui-e-i", [f"Beutl.HeadlessUITests.{letter}" for letter in "EFGHI"]),
    ("ui-j-m", [f"Beutl.HeadlessUITests.{letter}" for letter in "JKLM"]),
    ("ui-n-r", [f"Beutl.HeadlessUITests.{letter}" for letter in "NOPQR"]),
    ("ui-other", []),
], suffix=".*Tests$")
FILTERS = UNIT_FILTERS | UI_FILTERS
# These short validation-only groups share a runner to leave room for the
# longer coverage shards, while still executing every test in both modes.
QUICK_VALIDATION = "unit-quick-validation"
FILTERS[QUICK_VALIDATION] = f"({UNIT_FILTERS['unit-particles']}) or ({UNIT_FILTERS['unit-audio']})"
VALIDATION_GROUPS = {
    "validation-core": ["unit-golden", "unit-engine", "unit-other"],
    "validation-rendering": ["unit-rendering", QUICK_VALIDATION, "unit-git", "graphics3d"],
}
SHARDED_PROJECTS = {"Beutl.UnitTests", "Beutl.HeadlessUITests", "Beutl.Graphics3DTests"}
# The skip reasons FFmpeg-backed tests give when the FFmpeg 8 libraries or the worker are missing,
# for example "FFmpeg native libraries are not available." or "FFmpeg natives unavailable (...)".
FFMPEG_MISSING = re.compile(
    r"\bFFmpeg (?:native|natives|shared librar|worker)\w*\b.*\b(?:unavailable|not available|not present)\b",
    re.IGNORECASE)
# NUnit puts the kind of fixture method before a reason raised in one, as in "OneTimeSetUp: " or "SetUp : ",
# and a setup that a one-time setup calls gets both: "OneTimeSetUp: SetUp : ".
FIXTURE_METHOD = r"^(?:(?:OneTime)?(?:SetUp|TearDown)\s*:\s*)*"
# tests/TestGpu.cs starts the reason of every GPU skip with one of these. A test that needs any Vulkan
# device, SwiftShader included, skips with the first when none can be created; a test that holds only on
# a hardware GPU skips with the second on SwiftShader or another CPU implementation.
GPU_MISSING = re.compile(FIXTURE_METHOD + r"No GPU device:")
HARDWARE_GPU_ONLY = re.compile(FIXTURE_METHOD + r"Needs a hardware GPU:")
# A job declares what it provides by setting one of these to 1 or true. There, a test that skipped
# because it is missing means broken provisioning, and the shard fails. Other skips are only counted.
REQUIREMENTS = {
    "BEUTL_REQUIRE_GPU": ("no GPU device was available", GPU_MISSING),
    "BEUTL_REQUIRE_FFMPEG": ("FFmpeg is missing", FFMPEG_MISSING),
}
# Skips counted under one reason, however each test words the details.
REASON_GROUPS = [
    ("No GPU device", GPU_MISSING),
    ("Needs a hardware GPU", HARDWARE_GPU_ONLY),
    ("FFmpeg is missing", FFMPEG_MISSING),
]
REASON_LENGTH = 160
# Validation groups run suites in parallel threads; each suite's report is written in one piece.
REPORT_LOCK = threading.Lock()


def test_projects(root):
    # Follow the solution, including newly added test projects, and omit benchmarks/helpers.
    for item in ET.parse(root / "Beutl.slnx").iter("Project"):
        project = root / item.attrib["Path"].replace("\\", "/")
        if any((prop.text or "").strip().lower() == "true"
               for prop in ET.parse(project).iter("IsTestProject")):
            yield project


def assemblies(root, suite):
    if suite in UNIT_FILTERS or suite == QUICK_VALIDATION:
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
        "--logger", "trx", "--results-directory", str(root / "TestResults" / suite / assembly.stem),
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


def not_executed(paths):
    """Returns how many results the TRX files hold, and the name and skip message of each one not executed."""
    total, skipped = 0, []
    for path in paths:
        for result in ET.parse(path).iterfind(".//{*}UnitTestResult"):
            total += 1
            if result.attrib.get("outcome") == "NotExecuted":
                message = result.find("{*}Output/{*}ErrorInfo/{*}Message")
                skipped.append((result.attrib["testName"], "" if message is None else message.text or ""))
    return total, skipped


def skip_reason(message):
    """Returns the reason a skip is counted under, so that tests skipped for one reason are counted together."""
    for label, pattern in REASON_GROUPS:
        if pattern.search(message):
            return label
    lines = re.sub(FIXTURE_METHOD, "", message.strip()).splitlines()
    reason = " ".join(lines[0].split()) if lines else ""
    if len(reason) > REASON_LENGTH:
        reason = reason[:REASON_LENGTH - 1].rstrip() + "\u2026"
    return reason or "(no reason given)"


def markdown(text):
    # Reasons are test-authored text; keep them from adding table cells, markup or HTML to the summary.
    return re.sub(r"([\\`*_\[\]<>|~&])", r"\\\1", text)


def report_skips(label, paths):
    """Prints how many tests did not execute, by reason, and checks the capabilities the job declared.

    A skipped test still counts as a pass. The counts go to the log and, in GitHub Actions, to the job
    summary. Returns False when a test skipped because a capability the job declared was missing.
    """
    try:
        total, skipped = not_executed(paths)
    except ET.ParseError as error:
        # A test host that crashed can leave an incomplete file. Keep going with the other assemblies.
        print(f"::error::Could not read the test results of {label}: {error}", flush=True)
        return False
    counts = Counter(skip_reason(message) for _, message in skipped)
    rows = sorted(counts.items(), key=lambda row: (-row[1], row[0]))
    lines = [f"{label}: {len(skipped)} of {total} tests not executed"]
    lines += [f"{count:7}  {reason}" for reason, count in rows]
    errors = []
    for variable, (description, pattern) in REQUIREMENTS.items():
        if os.environ.get(variable, "").lower() not in {"1", "true"}:
            continue
        if names := [name for name, message in skipped if pattern.search(message)]:
            errors.append(f"::error::{variable} is set, but {len(names)} {label} tests skipped "
                          f"because {description}: {', '.join(names[:5])}")
    summary = [f"**{markdown(label)}**: {len(skipped)} of {total} tests not executed", ""]
    if rows:
        summary += ["| Tests | Reason |", "| ---: | --- |"]
        summary += [f"| {count} | {markdown(reason)} |" for reason, count in rows]
        summary.append("")
    with REPORT_LOCK:
        print("\n".join(lines + errors), flush=True)
        if path := os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(path, "a", encoding="utf-8") as file:
                file.write("\n".join(summary) + "\n")
    return not errors


def trx_assembly(path):
    # The storage attribute is lowercased; the code base keeps the assembly's own spelling.
    try:
        method = ET.parse(path).find(".//{*}TestDefinitions/{*}UnitTest/{*}TestMethod")
    except ET.ParseError:
        method = None  # report_skips reports the unreadable file under the file's name.
    return Path(method.attrib["codeBase"]).stem if method is not None and "codeBase" in method.attrib else path.stem


def check_results(directory):
    """Reports and checks the TRX files that test runs made without this script wrote to directory."""
    paths = sorted(directory.glob("*.trx"))
    if not paths:
        print(f"::error::No test results in {directory}", flush=True)
        return 1
    # Each dotnet test run writes one TRX file, so each file holds one assembly.
    passed = [report_skips(trx_assembly(path), [path]) for path in paths]
    return 0 if all(passed) else 1


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
        results = root / "TestResults" / suite / assembly.stem
        if result.returncode != 0:
            failed = True
        elif not has_test_results(results):
            print(f"::error::No tests ran for {suite}: {assembly.stem}", flush=True)
            failed = True
        if not report_skips(f"{suite}/{assembly.stem}", sorted(results.glob("*.trx"))):
            failed = True
    return 1 if failed else 0


def run_validation_group(root, suites):
    # The first .NET CLI invocation migrates NuGet state under a named mutex.
    # Initialize it once before child processes race to create its shared memory.
    subprocess.run(["dotnet", "help"], cwd=root, check=True, stdout=subprocess.DEVNULL)
    # No coverage instrumentation: each child reads the shared DLLs and keeps
    # its Beutl home, GPU context and test results isolated from the other children.
    with ThreadPoolExecutor(max_workers=3) as executor:
        results = list(executor.map(lambda suite: run(root, suite, True), suites))
    return 1 if any(results) else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("suite", nargs="?", choices=[*FILTERS, *VALIDATION_GROUPS, "graphics3d", "other"])
    parser.add_argument("--validation", action="store_true")
    parser.add_argument("--check-results", type=Path, metavar="DIRECTORY",
                        help="report and check the TRX files of test runs made without this script")
    arguments = parser.parse_args()
    if arguments.check_results is not None:
        if arguments.suite is not None or arguments.validation:
            parser.error("--check-results runs no suite")
        raise SystemExit(check_results(arguments.check_results))
    if arguments.suite is None:
        parser.error("a suite is required")
    if arguments.validation and arguments.suite not in {*UNIT_FILTERS, QUICK_VALIDATION, *VALIDATION_GROUPS, "graphics3d"}:
        parser.error("Vulkan validation only applies to the unit and Graphics3D suites")
    if not arguments.validation and arguments.suite in {QUICK_VALIDATION, *VALIDATION_GROUPS}:
        parser.error("Combined suites are only used for Vulkan validation")
    root = Path(__file__).resolve().parents[2]
    if arguments.suite in VALIDATION_GROUPS:
        raise SystemExit(run_validation_group(root, VALIDATION_GROUPS[arguments.suite]))
    raise SystemExit(run(root, arguments.suite, arguments.validation))
