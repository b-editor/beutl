import re
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import ci_tests


def matches(expression, name):
    # These filters use only parenthesized OR groups and AND exclusions.
    return all(any((value not in name if operator == "!~" else value in name)
                   for operator, value in re.findall(r"FullyQualifiedName(!~|~)([^|&()]+)", group))
               for group in expression.split("&"))


class CiTestsTests(unittest.TestCase):
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
        name = "Beutl.HeadlessUITests.PlayerTests.Test(Beutl.HeadlessUITests.AgentType)"
        self.assertEqual(sum(matches(filter_, name) for filter_ in ci_tests.UI_FILTERS.values()), 1)

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
        self.assertEqual(normal[normal.index("--filter") + 1], validation[validation.index("--filter") + 1])
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


def subprocess_result(returncode):
    from subprocess import CompletedProcess
    return CompletedProcess([], returncode)
