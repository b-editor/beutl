import contextlib
import io
import os
import re
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch
from xml.sax.saxutils import escape

import ci_tests
import archive_ci


def matches(expression, name):
    # These filters use only parenthesized OR groups and AND exclusions.
    return all(any((re.search(value, name) is None if operator == "!~" else re.search(value, name) is not None)
                   for operator, value in re.findall(r"class (=~|!~) '([^']+)'", group))
               for group in expression.split(" and "))


class CiTestsTests(unittest.TestCase):
    def setUp(self):
        # The compile job runs these tests. Keep crafted reports out of its annotations and job summary, and
        # keep the capabilities a job declares from changing the results.
        environment = patch.dict(os.environ)
        environment.start()
        self.addCleanup(environment.stop)
        for variable in ("GITHUB_STEP_SUMMARY", *ci_tests.REQUIREMENTS):
            os.environ.pop(variable, None)
        self.output = io.StringIO()
        redirect = contextlib.redirect_stdout(self.output)
        redirect.__enter__()
        self.addCleanup(redirect.__exit__, None, None, None)

    def test_validation_children_overlap_and_a_failure_is_not_hidden(self):
        barrier = threading.Barrier(3)
        started = set()

        def child(root, suite, validation):
            self.assertTrue(validation)
            started.add(suite)
            barrier.wait(timeout=2)
            return 1 if suite == "failing" else 0

        with patch.object(ci_tests, "run", side_effect=child), \
                patch.object(ci_tests.subprocess, "run") as initialize:
            self.assertEqual(ci_tests.run_validation_group(Path("/repo"), ["first", "failing", "third"]), 1)
            initialize.assert_called_once_with(["dotnet", "help"], cwd=Path("/repo"), check=True, stdout=ci_tests.subprocess.DEVNULL)
        self.assertEqual(started, {"first", "failing", "third"})

    def test_same_assembly_has_separate_results_for_concurrent_suites(self):
        root = Path("/repo")
        assembly = root / "Beutl.UnitTests.dll"
        first = ci_tests.test_command(root, assembly, "unit-golden", True)
        second = ci_tests.test_command(root, assembly, "unit-engine", True)
        self.assertNotEqual(first[first.index("--results-directory") + 1], second[second.index("--results-directory") + 1])
        self.assertNotIn("--collect", first)
        self.assertNotIn("--collect", second)

    def test_runtime_graph_keeps_all_compatible_fallbacks(self):
        graph = {
            "linux-x64": {"#import": ["linux", "unix-x64"]},
            "linux": {"#import": ["unix"]},
            "unix-x64": {"#import": ["unix"]},
            "unix": {"#import": ["any"]},
            "any": {},
        }
        self.assertEqual(archive_ci.compatible_rids(graph, "linux-x64"), set(graph))
        with self.assertRaises(KeyError):
            archive_ci.compatible_rids(graph, "unknown-rid")

    def test_archive_keeps_host_assets_and_nested_worker_assets(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / ".ci-tools").mkdir()
            project = root / "tests" / "ExampleTests" / "ExampleTests.csproj"
            output = project.parent / "bin" / "Debug" / "net10.0"
            for subdir in ["runtimes", "FFmpegWorker/runtimes"]:
                for rid in ["linux-x64", "unix", "win-x64", "linux-arm64"]:
                    (output / subdir / rid / "native").mkdir(parents=True)
            with patch.object(archive_ci, "test_projects", return_value=[project]):
                self.assertEqual(archive_ci.prepare_outputs(root, {"linux-x64", "unix"}, "other"),
                                 [root / ".ci-tools", output])
            for subdir in ["runtimes", "FFmpegWorker/runtimes"]:
                self.assertEqual({path.name for path in (output / subdir).iterdir()}, {"linux-x64", "unix"})

    def test_archive_groups_supply_every_suite_without_unrelated_outputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            tools = root / ".ci-tools"
            tools.mkdir()
            names = ["Beutl.UnitTests", "Beutl.HeadlessUITests", "Beutl.Graphics3DTests", "NewTests"]
            for name in names:
                project = root / "tests" / name / f"{name}.csproj"
                output = project.parent / "bin" / "Debug" / "net10.0"
                output.mkdir(parents=True)
                project.write_text("<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>")
                (output / f"{name}.dll").touch()
            projects = "".join(f'<Project Path="tests/{name}/{name}.csproj" />' for name in names)
            (root / "Beutl.slnx").write_text(f"<Solution>{projects}</Solution>")
            groups = {group: set(archive_ci.prepare_outputs(root, {"linux-x64"}, group))
                      for group in ["unit", "ui", "graphics3d", "other"]}
            for suite in [*ci_tests.FILTERS, *ci_tests.VALIDATION_GROUPS, "graphics3d", "other"]:
                with self.subTest(suite=suite):
                    pattern = archive_ci.artifact_pattern(root, suite)
                    selected = pattern.removeprefix("TestBinaries-").removesuffix(".tar.zst").strip("{}").split(",")
                    downloaded = set().union(*(groups[group] for group in selected))
                    needed = {assembly.parent for child in ci_tests.VALIDATION_GROUPS.get(suite, [suite])
                              for assembly in ci_tests.assemblies(root, child)}
                    self.assertEqual(downloaded, needed | {tools})
            self.assertEqual(archive_ci.artifact_pattern(root, "validation-rendering"),
                             "TestBinaries-{graphics3d,unit}.tar.zst")
            self.assertEqual(groups["other"], {tools, root / "tests/NewTests/bin/Debug/net10.0"})

    def test_archive_fails_when_a_selected_output_is_missing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / "tests/Beutl.UnitTests/Beutl.UnitTests.csproj"
            with patch.object(archive_ci, "test_projects", return_value=[project]):
                with self.assertRaises(FileNotFoundError):
                    archive_ci.prepare_outputs(root, {"linux-x64"}, "unit")
                (root / ".ci-tools").mkdir()
                with self.assertRaises(FileNotFoundError):
                    archive_ci.prepare_outputs(root, {"linux-x64"}, "unit")

    def test_artifact_selection_fails_when_the_suite_has_no_assemblies(self):
        with patch.object(archive_ci, "assemblies", return_value=[]):
            with self.assertRaises(ValueError):
                archive_ci.artifact_pattern(Path("/repo"), "other")

    def test_unit_shards_cover_each_test_once_including_new_namespaces(self):
        names = [
            "Beutl.UnitTests.Engine.Graphics.Particles.Fixture.Test",
            "Beutl.UnitTests.Engine.Graphics.Rendering.Golden.Fixture.Test",
            "Beutl.UnitTests.Engine.Audio.Fixture.Test",
            "Beutl.UnitTests.Engine.Graphics.Fixture.Test",
            "Beutl.UnitTests.Engine.NewNamespace.Fixture.Test",
            "Beutl.UnitTests.Editor.VersionControl.Fixture.Test",
            "Beutl.UnitTests.Api.Fixture.Test",
            "NewNamespace.Fixture.Test",
            "Beutl.UnitTests.Api.Fixture.Test(Beutl.UnitTests.Engine.Graphics.Particles.Type)",
        ]
        for name in names:
            with self.subTest(name=name):
                self.assertEqual(sum(matches(filter_, name) for filter_ in ci_tests.UNIT_FILTERS.values()), 1)

    def test_ui_shards_cover_each_initial_once_and_catch_unknown_names(self):
        for initial in "ABCDEFGHIJKLMNOPQRSTUVWXYZ_é":
            name = f"Beutl.HeadlessUITests.{initial}Fixture.Test"
            with self.subTest(name=name):
                self.assertEqual(sum(matches(filter_, name) for filter_ in ci_tests.UI_FILTERS.values()), 1)
        name = "Beutl.HeadlessUITests.PlayerTests"
        self.assertEqual(sum(matches(filter_, name) for filter_ in ci_tests.UI_FILTERS.values()), 1)
        for suite in ci_tests.UI_FILTERS.keys() - {"ui-other"}:
            self.assertFalse(matches(ci_tests.UI_FILTERS[suite], "Beutl.HeadlessUITests.AssemblySetUp"))

    def test_other_suite_follows_solution_and_excludes_helpers_and_sharded_projects(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            names = ["NewTests", "SourceGeneratorTest", "Beutl.UnitTests", "Benchmarks"]
            for name in names:
                project = root / "tests" / name / f"{name}.csproj"
                project.parent.mkdir(parents=True)
                is_test = name != "Benchmarks"
                project.write_text(f"<Project><PropertyGroup><IsTestProject>{str(is_test).lower()}</IsTestProject></PropertyGroup></Project>")
            projects = "".join(f'<Project Path="tests\\{name}\\{name}.csproj" />' for name in names)
            (root / "Beutl.slnx").write_text(f"<Solution>{projects}</Solution>")
            self.assertEqual([path.stem for path in ci_tests.assemblies(root, "other")], ["NewTests", "SourceGeneratorTest"])

    def test_validation_reuses_filters_without_collecting_coverage(self):
        root = Path("/repo")
        assembly = root / "tests/Beutl.UnitTests/bin/Debug/net10.0/Beutl.UnitTests.dll"
        normal = ci_tests.test_command(root, assembly, "unit-engine", False)
        validation = ci_tests.test_command(root, assembly, "unit-engine", True)
        self.assertEqual(normal[-3:], validation[-3:])
        self.assertTrue(normal[-3].startswith("NUnit.Where="))
        self.assertEqual(normal[-2], "NUnit.ExplicitMode=None")
        self.assertEqual(normal[-1], "NUnit.AssemblySelectLimit=2147483647")
        self.assertNotIn("--filter", normal)
        self.assertIn("--collect", normal)
        self.assertNotIn("--collect", validation)
        self.assertIn("/repo/.ci-tools/coverlet", normal[normal.index("--test-adapter-path") + 1])
        self.assertNotIn("coverlet", validation[validation.index("--test-adapter-path") + 1])
        self.assertNotIn("--no-build", normal)  # A DLL runs directly; no project/restore is involved.
        self.assertIn("--blame-hang", validation)

    def test_empty_or_missing_results_do_not_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            self.assertFalse(ci_tests.has_test_results(directory))
            results = directory / "test.trx"
            results.write_text('<TestRun xmlns="urn:trx"><ResultSummary><Counters total="0" /></ResultSummary></TestRun>')
            self.assertFalse(ci_tests.has_test_results(directory))
            results.write_text('<TestRun xmlns="urn:trx"><ResultSummary><Counters total="1" /></ResultSummary></TestRun>')
            self.assertTrue(ci_tests.has_test_results(directory))

    def test_other_assemblies_still_run_after_one_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            paths = [root / "FirstTests.dll", root / "SecondTests.dll"]
            for path in paths:
                path.touch()
            with patch.object(ci_tests, "assemblies", return_value=paths), \
                    patch.object(ci_tests.subprocess, "run") as process, \
                    patch.object(ci_tests, "has_test_results", return_value=True):
                process.side_effect = [subprocess_result(1), subprocess_result(0)]
                self.assertEqual(ci_tests.run(root, "other", False), 1)
                self.assertEqual(process.call_count, 2)

    def test_skips_are_counted_by_reason(self):
        results = [
            ("Gpu", "NotExecuted", "No GPU device: GraphicsContextFactory.GetOrCreateShared returned null."),
            ("GpuFixture", "NotExecuted", "OneTimeSetUp: SetUp : No GPU device: the shared Vulkan context cannot render 3D."),
            ("Parity", "NotExecuted", "Needs a hardware GPU: SwiftShader Device (Subzero) is a Cpu device. InnerShadow: ..."),
            ("Scaled", "NotExecuted", "Needs a hardware GPU: SwiftShader Device (Subzero) is a Cpu device. FlatShadow: ..."),
            ("Worker", "NotExecuted", "SetUp : FFmpeg worker binary not present in the test output; skipping."),
            ("Mac", "NotExecuted", "Only supported on MacOSX"),
            ("MacFixture", "NotExecuted", "OneTimeSetUp: Only supported on MacOSX"),
            ("Lines", "NotExecuted", "First   line\nsecond line"),
            ("Long", "NotExecuted", "x" * 200),
            ("Silent", "NotExecuted", None),
            ("Ran", "Passed", ""),
            ("Broke", "Failed", "No GPU device: a failure is not a skip."),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "test.trx"
            path.write_text(trx(results))
            self.assertTrue(ci_tests.report_skips("unit-golden/Beutl.UnitTests", [path]))
        self.assertEqual(self.output.getvalue().splitlines(), [
            "unit-golden/Beutl.UnitTests: 10 of 12 tests not executed",
            "      2  Needs a hardware GPU",
            "      2  No GPU device",
            "      2  Only supported on MacOSX",
            "      1  (no reason given)",
            "      1  FFmpeg is missing",
            "      1  First line",
            "      1  " + "x" * 159 + "\u2026",
        ])

    def test_the_job_summary_gets_the_counts_but_no_markup_from_a_reason(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            summary = directory / "summary.md"
            summary.write_text("earlier steps\n")
            skipped = directory / "skipped.trx"
            skipped.write_text(trx([("Odd", "NotExecuted", "Needs <b>x</b> | `y` *z* [a](b) & c_d"), ("Ran", "Passed", "")]))
            ran = directory / "ran.trx"
            ran.write_text(trx([("Ran", "Passed", "")]))
            with patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(summary)}):
                ci_tests.report_skips("other/Beutl.AgentToolkit.Tests", [skipped])
                ci_tests.report_skips("other/Beutl.FFmpegIpc.Tests", [ran])
            self.assertEqual(summary.read_text(), (
                "earlier steps\n"
                "**other/Beutl.AgentToolkit.Tests**: 1 of 2 tests not executed\n\n"
                "| Tests | Reason |\n| ---: | --- |\n"
                "| 1 | Needs \\<b\\>x\\</b\\> \\| \\`y\\` \\*z\\* \\[a\\](b) \\& c\\_d |\n\n"
                "**other/Beutl.FFmpegIpc.Tests**: 0 of 1 tests not executed\n\n"))

    def test_only_a_skip_for_the_missing_capability_counts_against_it(self):
        cases = {
            "BEUTL_REQUIRE_FFMPEG": ("FFmpeg is missing", [
                ("Fixture", "NotExecuted", "OneTimeSetUp: FFmpeg native libraries are not available."),
                ("SetUp", "NotExecuted", "SetUp : FFmpeg worker binary not present in the test output; skipping."),
                ("Worker", "NotExecuted", "FFmpeg natives unavailable (FFmpeg worker exited because the FFmpeg "
                                          "libraries could not be found.); skipping the process-level contract test."),
                ("Shared", "NotExecuted", "FFmpeg shared libraries unavailable; skipping the native-dependent assertion."),
                ("Codec", "NotExecuted", "libx264 is not available."),
                ("Platform", "NotExecuted", "Exercises Linux library discovery."),
                ("Present", "NotExecuted", "An FFmpeg worker is available; missing-libraries path is unreachable."),
                ("Failed", "Failed", "FFmpeg native libraries are not available."),
            ], ["Fixture", "SetUp", "Worker", "Shared"]),
            "BEUTL_REQUIRE_GPU": ("no GPU device was available", [
                ("Device", "NotExecuted", "No GPU device: GraphicsContextFactory.GetOrCreateShared returned null."),
                ("Fixture", "NotExecuted", "OneTimeSetUp: No GPU device: initializing the shared graphics context threw."),
                ("Nested", "NotExecuted", "OneTimeSetUp: SetUp : No GPU device: the shared Vulkan context cannot render 3D."),
                ("Hardware", "NotExecuted", "Needs a hardware GPU: SwiftShader Device (Subzero) is a Cpu device. ..."),
                ("Available", "NotExecuted", "A graphics context is available; the Unavailable path is not exercised here."),
                ("Quoted", "NotExecuted", "Validation was not requested, so a later No GPU device: skip is not this one."),
                ("Metal", "NotExecuted", "Skia renders through Metal on the composite backend, which already wraps textures."),
                ("Failed", "Failed", "No GPU device: a failure is reported as a failure."),
            ], ["Device", "Fixture", "Nested"]),
        }
        for variable, (description, results, names) in cases.items():
            with self.subTest(variable), tempfile.TemporaryDirectory() as temporary, \
                    patch.dict(os.environ, {variable: "1"}):
                path = Path(temporary) / "test.trx"
                path.write_text(trx(results))
                self.output.seek(0)
                self.output.truncate()
                self.assertFalse(ci_tests.report_skips("other/Example", [path]))
                self.assertEqual(self.output.getvalue().splitlines()[-1],
                                 f"::error::{variable} is set, but {len(names)} other/Example tests skipped "
                                 f"because {description}: {', '.join(names)}")

    def test_a_skip_fails_only_for_a_capability_the_job_declares(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            assembly = root / "Beutl.FFmpegWorker.Tests.dll"
            assembly.touch()
            results = root / "TestResults" / "other" / assembly.stem
            results.mkdir(parents=True)
            cases = [
                ("FFmpeg native libraries are not available.", "BEUTL_REQUIRE_FFMPEG", [("1", 1), ("true", 1), ("0", 0), ("", 0)]),
                ("No GPU device: GraphicsContextFactory.GetOrCreateShared returned null.", "BEUTL_REQUIRE_GPU",
                 [("1", 1), ("TRUE", 1), ("0", 0), ("", 0)]),
                # CI provides SwiftShader, so a hardware-only expectation is skipped there by design.
                ("Needs a hardware GPU: SwiftShader Device (Subzero) is a Cpu device. ...", "BEUTL_REQUIRE_GPU", [("1", 0)]),
                # Each declaration covers its own capability only.
                ("No GPU device: GraphicsContextFactory.GetOrCreateShared returned null.", "BEUTL_REQUIRE_FFMPEG", [("1", 0)]),
                ("FFmpeg native libraries are not available.", "BEUTL_REQUIRE_GPU", [("1", 0)]),
            ]
            for message, variable, values in cases:
                (results / "test.trx").write_text(trx([("Skipped", "NotExecuted", message), ("Ran", "Passed", "")]))
                for value, expected in values:
                    with self.subTest(message=message, **{variable: value}), \
                            patch.dict(os.environ, {variable: value}), \
                            patch.object(ci_tests, "assemblies", return_value=[assembly]), \
                            patch.object(ci_tests.subprocess, "run", return_value=subprocess_result(0)):
                        self.assertEqual(ci_tests.run(root, "other", False), expected)

    def test_results_of_a_run_without_the_script_are_checked_per_assembly(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            missing = directory / "missing"
            missing.mkdir()
            (directory / "a.trx").write_text(trx(
                [("Render", "NotExecuted", "No GPU device: GraphicsContextFactory.GetOrCreateShared returned null."),
                 ("Ran", "Passed", "")], assembly="Beutl.UnitTests"))
            (directory / "b.trx").write_text(trx([("Ran", "Passed", "")], assembly="Beutl.Graphics3DTests"))
            self.assertEqual(ci_tests.check_results(directory), 0)
            self.assertEqual(self.output.getvalue().splitlines(), [
                "Beutl.UnitTests: 1 of 2 tests not executed",
                "      1  No GPU device",
                "Beutl.Graphics3DTests: 0 of 1 tests not executed",
            ])
            with patch.dict(os.environ, {"BEUTL_REQUIRE_GPU": "1"}):
                self.assertEqual(ci_tests.check_results(directory), 1)
                # The same check through the command line that the Linux ARM64 job runs.
                command = [sys.executable, ci_tests.__file__, "--check-results", str(directory)]
                result = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertEqual(result.returncode, 1)
            self.assertIn("::error::BEUTL_REQUIRE_GPU is set, but 1 Beutl.UnitTests tests skipped", result.stdout)
            self.assertEqual(ci_tests.check_results(missing), 1)
            self.assertIn(f"::error::No test results in {missing}", self.output.getvalue())
            (missing / "truncated.trx").write_text("<TestRun><Results>")
            self.assertEqual(ci_tests.check_results(missing), 1)
            self.assertIn("::error::Could not read the test results of truncated", self.output.getvalue())

    def test_a_run_without_the_script_that_found_no_tests_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            (directory / "a.trx").write_text(trx([("Ran", "Passed", "")], assembly="Beutl.UnitTests"))
            # dotnet test exits with 0 for a run that finds no tests; its TRX has no test definitions.
            empty = directory / "_runner_2026-10-12_03_10_08_net10.0.trx"
            empty.write_text(trx([]))
            self.assertEqual(ci_tests.check_results(directory), 1)
            self.assertIn(f"::error::No tests ran for {empty.stem}", self.output.getvalue())
            self.assertNotIn("No tests ran for Beutl.UnitTests", self.output.getvalue())
            empty.unlink()
            self.assertEqual(ci_tests.check_results(directory), 0)


def trx(results, assembly=None):
    rows = "".join(
        f'<UnitTestResult testName="{name}" outcome="{outcome}" />' if message is None else
        f'<UnitTestResult testName="{name}" outcome="{outcome}"><Output><StdOut>{escape(message)}</StdOut>'
        f'<ErrorInfo><Message>{escape(message)}</Message></ErrorInfo></Output></UnitTestResult>'
        for name, outcome, message in results)
    # VSTest lowercases the storage path; the code base keeps the assembly's spelling.
    definitions = "" if assembly is None else (
        f'<TestDefinitions><UnitTest name="Test" storage="/repo/bin/{assembly.lower()}.dll">'
        f'<TestMethod codeBase="/repo/bin/{assembly}.dll" className="Fixture" name="Test" /></UnitTest></TestDefinitions>')
    return ('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'{definitions}<Results>{rows}</Results><ResultSummary><Counters total="{len(results)}" /></ResultSummary>'
            '</TestRun>')


def subprocess_result(returncode):
    from subprocess import CompletedProcess
    return CompletedProcess([], returncode)
