import os
import re
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch

import ci_tests
import archive_ci


def matches(expression, name):
    # These filters use only parenthesized OR groups and AND exclusions.
    return all(any((re.search(value, name) is None if operator == "!~" else re.search(value, name) is not None)
                   for operator, value in re.findall(r"class (=~|!~) '([^']+)'", group))
               for group in expression.split(" and "))


class CiTestsTests(unittest.TestCase):
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
                self.assertEqual(archive_ci.prepare_outputs(root, {"linux-x64", "unix"}), [root / ".ci-tools", output])
            for subdir in ["runtimes", "FFmpegWorker/runtimes"]:
                self.assertEqual({path.name for path in (output / subdir).iterdir()}, {"linux-x64", "unix"})

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

    def test_only_skips_for_missing_ffmpeg_are_reported(self):
        results = [
            ("Fixture", "NotExecuted", "OneTimeSetUp: FFmpeg native libraries are not available."),
            ("SetUp", "NotExecuted", "SetUp : FFmpeg worker binary not present in the test output; skipping."),
            ("Worker", "NotExecuted", "FFmpeg natives unavailable (FFmpeg worker exited because the FFmpeg "
                                      "libraries could not be found.); skipping the process-level contract test."),
            ("Shared", "NotExecuted", "FFmpeg shared libraries unavailable; skipping the native-dependent assertion."),
            ("Codec", "NotExecuted", "libx264 is not available."),
            ("Platform", "NotExecuted", "Exercises Linux library discovery."),
            ("Present", "NotExecuted", "An FFmpeg worker is available; missing-libraries path is unreachable."),
            ("Failed", "Failed", "FFmpeg native libraries are not available."),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            (directory / "test.trx").write_text(trx(results))
            self.assertEqual(ci_tests.ffmpeg_skips(directory), ["Fixture", "SetUp", "Worker", "Shared"])

    def test_a_skip_for_missing_ffmpeg_fails_only_when_ffmpeg_is_required(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            assembly = root / "Beutl.FFmpegWorker.Tests.dll"
            assembly.touch()
            results = root / "TestResults" / "other" / assembly.stem
            results.mkdir(parents=True)
            (results / "test.trx").write_text(trx([("Encode", "NotExecuted", "FFmpeg native libraries are not available.")]))
            for value, expected in [("1", 1), ("true", 1), ("0", 0), ("", 0)]:
                with self.subTest(BEUTL_REQUIRE_FFMPEG=value), \
                        patch.dict(os.environ, {"BEUTL_REQUIRE_FFMPEG": value}), \
                        patch.object(ci_tests, "assemblies", return_value=[assembly]), \
                        patch.object(ci_tests.subprocess, "run", return_value=subprocess_result(0)):
                    self.assertEqual(ci_tests.run(root, "other", False), expected)


def trx(results):
    rows = "".join(
        f'<UnitTestResult testName="{name}" outcome="{outcome}"><Output><StdOut>{message}</StdOut>'
        f'<ErrorInfo><Message>{message}</Message></ErrorInfo></Output></UnitTestResult>'
        for name, outcome, message in results)
    return ('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
            f'{rows}</Results><ResultSummary><Counters total="{len(results)}" /></ResultSummary></TestRun>')


def subprocess_result(returncode):
    from subprocess import CompletedProcess
    return CompletedProcess([], returncode)
