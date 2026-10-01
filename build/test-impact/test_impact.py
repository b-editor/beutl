import copy
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch

import impact


def member(key, name, refs=(), cls="Product", **overrides):
    return dict(key=key, name=name, className=cls, hash="v1", signature="s1", references=list(refs),
                start=1, end=10, opaque=False, lifecycle=False, test=False, **overrides)


def file(*members):
    return dict(hash="file-v1", skeleton="outside", conditional=False, parseError=False,
                methods=list(members), references=[])


class SelectorTests(unittest.TestCase):
    def setUp(self):
        self.project = "tests/Suite/Suite.csproj"
        self.a = impact.test_key(self.project, "Tests", "ReadsValue")
        self.b = impact.test_key(self.project, "Tests", "Other")
        self.tests = {key: dict(key=key, project=self.project, className="Tests", name=name,
                               cases=[dict(fullname="Tests." + name)])
                      for key, name in [(self.a, "ReadsValue"), (self.b, "Other")]}
        self.old = {"src/Value.cs": file(member("value", "Value")),
                    "src/Other.cs": file(member("other", "OtherValue")),
                    "tests/Suite/Tests.cs": file(member("test-a", "ReadsValue", ("Value",), "Tests"),
                                                  member("test-b", "Other", ("OtherValue",), "Tests"))}
        self.current = copy.deepcopy(self.old)
        self.current["src/Value.cs"]["methods"][0]["hash"] = "v2"
        self.baseline = dict(schema=impact.SCHEMA, sha="base", fingerprint={"sdk": "10"}, index=self.old, finalized=True,
                             tests=copy.deepcopy(self.tests), profiles={
                                 self.a: dict(methods=["value", "test-a"], opaque=[]),
                                 self.b: dict(methods=["other", "test-b"], opaque=[])}, collectionSeconds=12.5)

    def choose(self, files=("src/Value.cs",), **kwargs):
        return impact.choose(self.baseline, self.current, self.tests, files, "base", {"sdk": "10"}, **kwargs)

    def assert_all(self, report, reason):
        self.assertEqual(2, report["selectedMethods"])
        self.assertTrue(any(reason in r for r in report["fallbackReasons"]), report["fallbackReasons"])
        self.assertFalse(report["safeToSkip"])

    def test_body_change_selects_one_method_not_project(self):
        report = self.choose()
        self.assertFalse(report["fallbackReasons"])
        self.assertEqual(1, report["selectedMethods"])
        self.assertIn("method-coverage-or-caller", report["reasons"][self.a])
        self.assertEqual([], report["reasons"][self.b])

    def test_static_transitive_callers_cover_previously_unexecuted_branch(self):
        self.old["src/Value.cs"]["methods"].append(member("wrapper", "Wrapper", ("Value",)))
        self.current["src/Value.cs"]["methods"].append(member("wrapper", "Wrapper", ("Value",)))
        for index in [self.old, self.current]:
            index["tests/Suite/Tests.cs"]["methods"][0]["references"] = ["Wrapper"]
        self.baseline["profiles"][self.a]["methods"] = ["test-a"]
        self.assertEqual(1, self.choose()["selectedMethods"])

    def test_new_method_expands_through_new_call_site(self):
        self.current["src/Value.cs"]["methods"] += [member("new", "NewValue")]
        self.current["src/Value.cs"]["methods"][0]["references"] = ["NewValue"]
        report = self.choose()
        self.assertFalse(report["fallbackReasons"])
        self.assertIn("new", report["affectedMethods"])
        self.assertEqual(1, report["selectedMethods"])

    def test_new_unreferenced_method_falls_back_even_with_other_covered_changes(self):
        self.current["src/Value.cs"]["methods"] += [member("new", "UnknownMethod")]
        self.assert_all(self.choose(), "unobserved-change")

    def test_unprofiled_methods_always_selected(self):
        del self.baseline["profiles"][self.b]
        report = self.choose()
        self.assertEqual(2, report["selectedMethods"])
        self.assertIn("unprofiled-test", report["reasons"][self.b])

    def test_parameter_case_inventory_change_is_selected(self):
        self.tests[self.b]["cases"].append(dict(fullname="new case name"))
        self.assertIn("case-inventory-changed", self.choose()["reasons"][self.b])

    def test_new_test_is_selected(self):
        del self.baseline["tests"][self.b]
        del self.baseline["profiles"][self.b]
        self.assertIn("new-test", self.choose()["reasons"][self.b])

    def test_changed_test_is_selected(self):
        self.current["tests/Suite/Tests.cs"]["methods"][1]["hash"] = "changed"
        self.assertIn("changed-test", self.choose(["tests/Suite/Tests.cs"])["reasons"][self.b])

    def test_missing_stale_and_wrong_environment_baseline(self):
        for key, value, reason in [("sha", "stale", "sha-mismatch"),
                                   ("fingerprint", {}, "environment-mismatch"), ("schema", 99, "schema")]:
            with self.subTest(key=key):
                saved = self.baseline[key]
                self.baseline[key] = value
                self.assert_all(self.choose(), reason)
                self.baseline[key] = saved
        self.baseline = None
        self.assert_all(self.choose(), "missing-baseline")

    def test_interrupted_collection_is_not_a_valid_partial_baseline(self):
        self.baseline["finalized"] = False
        self.assert_all(self.choose(), "interrupted")

    def test_corrupt_profile_is_rejected_before_any_skip(self):
        self.baseline["profiles"][self.a] = dict(methods=[], opaque=[])
        with self.assertRaisesRegex(ValueError, "Corrupt baseline"):
            self.choose()

    def test_shared_build_and_resource_changes(self):
        for path in ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                     "global.json", "tests/Shared/data.json", "tests/Font.ttf", "Beutl.slnx",
                     "src/Editor/view.axaml", ".github/workflows/dotnet.yml"]:
            with self.subTest(path=path):
                self.assert_all(self.choose([path]), "unknown-change")

    def test_native_gpu_and_renderer_changes(self):
        for path in ["native/SkiaSharp/lib.so", "external/Submodule", "src/Engine/Graphics/Canvas.cs",
                     "src/Beutl.Extensions.FFmpeg/Loader.cs", "src/Engine/Rendering/Renderer.cs"]:
            with self.subTest(path=path):
                self.assert_all(self.choose([path]), "native-gpu")

    def test_source_embedded_as_resource_falls_back(self):
        self.assert_all(self.choose(resources={"src/Value.cs"}), "resource-change")

    def test_fixture_fields_constructor_property_or_using_changes(self):
        self.current["src/Value.cs"]["skeleton"] = "changed"
        self.assert_all(self.choose(), "non-method-change")

    def test_lifecycle_and_dynamic_changes(self):
        for flag in ("lifecycle", "opaque"):
            with self.subTest(flag=flag):
                self.current["src/Value.cs"]["methods"][0][flag] = True
                self.assert_all(self.choose(), "lifecycle-or-dynamic")
                self.current["src/Value.cs"]["methods"][0][flag] = False

    def test_dynamic_baseline_test_is_always_selected(self):
        self.baseline["profiles"][self.b]["opaque"] = ["dynamic-or-reflection"]
        self.assertIn("opaque:dynamic-or-reflection", self.choose()["reasons"][self.b])

    def test_signature_change_or_removed_method(self):
        self.current["src/Value.cs"]["methods"] = []
        self.assert_all(self.choose(), "signature-changed")

    def test_new_deleted_and_renamed_files(self):
        self.assert_all(self.choose(["src/New.cs"]), "added-deleted")
        del self.current["src/Value.cs"]
        self.assert_all(self.choose(), "added-deleted")

    def test_syntax_error_and_preprocessor_fail_open(self):
        for flag in ("parseError", "conditional"):
            self.current["src/Value.cs"][flag] = True
            self.assert_all(self.choose(), "unsupported-source")

    def test_source_linked_production_coverage_is_not_dropped(self):
        self.current["tests/Suite/Tests.cs"]["methods"][0]["references"] = []
        self.old["tests/Suite/Tests.cs"]["methods"][0]["references"] = []
        self.assertIn("method-coverage-or-caller", self.choose()["reasons"][self.a])

    def test_initializer_or_case_source_reference_falls_back(self):
        self.current["tests/Suite/Tests.cs"]["references"] = ["Value"]
        self.assert_all(self.choose(), "reference-outside-method")

    def test_no_changes_is_advisory_even_when_selection_is_empty(self):
        report = self.choose([])
        self.assertEqual(0, report["selectedMethods"])
        self.assertFalse(report["safeToSkip"])

    def test_static_class_receiver_avoids_unrelated_same_name_chain(self):
        for index in [self.old, self.current]:
            index["src/Value.cs"]["methods"][0]["staticClass"] = "Product"
            index["tests/Suite/Tests.cs"]["methods"][0]["references"] = ["Product|Value"]
            index["tests/Suite/Tests.cs"]["methods"][1]["references"] = ["OtherType|Value"]
        self.current["src/Other.cs"]["references"] = ["instance|Value"]
        report = self.choose()
        self.assertFalse(report["fallbackReasons"])
        self.assertEqual(1, report["selectedMethods"])

    def test_unqualified_static_call_is_conservative(self):
        for index in [self.old, self.current]:
            index["src/Value.cs"]["methods"][0]["staticClass"] = "Product"
        self.assertEqual(1, self.choose()["selectedMethods"])

    def test_instance_receivers_remain_conservative(self):
        for index in [self.old, self.current]:
            index["tests/Suite/Tests.cs"]["methods"][1]["references"] = ["instance|Value"]
        self.assertEqual(2, self.choose()["selectedMethods"])

    def test_comparison_detects_missed_failure_and_missing_cases(self):
        report = self.choose()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "Suite.xml"
            path.write_text('''<test-run><test-suite type="Assembly" name="Suite.dll">
                <test-case classname="Tests" methodname="ReadsValue" fullname="Tests.ReadsValue" result="Passed" duration="1"/>
                <test-case classname="Tests" methodname="Other" fullname="Tests.Other" result="Failed" duration="2"/>
                </test-suite></test-run>''')
            result = impact.compare(report, folder)
            self.assertEqual("complete", result["status"])
            self.assertEqual([self.b], result["missedFailedMethods"])
            self.assertEqual([self.b], result["isolatedPassButFullFailedMethods"])
            self.assertEqual(2, result["omittedTestSeconds"])
            report["tests"][self.a]["cases"].append(dict(fullname="another parameter"))
            self.assertEqual("incomplete", impact.compare(report, folder)["status"])
        with tempfile.TemporaryDirectory() as empty:
            self.assertEqual("incomplete", impact.compare(report, empty)["status"])

    def test_headless_suite_stays_selected_after_isolated_success(self):
        self.tests[self.b]["project"] = "tests/Beutl.HeadlessUITests/Beutl.HeadlessUITests.csproj"
        report = self.choose([])
        self.assertEqual(1, report["selectedMethods"])
        self.assertIn("suite-interaction-policy", report["reasons"][self.b])


class FormatTests(unittest.TestCase):
    def test_isolation_rejects_missing_extra_failed_and_skipped_cases(self):
        test = dict(key="one", cases=[dict(fullname="parameter 1"), dict(fullname="parameter 2")])
        passed = dict(cases=[dict(fullname="parameter 1", result="Passed"),
                             dict(fullname="parameter 2", result="Passed")])
        self.assertEqual(2, len(impact.validate_isolated(test, {"one": passed})))
        for actual in ({}, {"one": passed, "extra": passed},
                       {"one": dict(cases=passed["cases"][:1])},
                       {"one": dict(cases=[dict(c, result="Skipped") for c in passed["cases"]])},
                       {"one": dict(cases=[dict(c, result="Failed") for c in passed["cases"]])}):
            with self.subTest(actual=actual), self.assertRaises(ValueError):
                impact.validate_isolated(test, actual)

    def test_discovery_groups_custom_named_cases_by_real_method(self):
        xml = ET.fromstring('''<test-run>
            <test-case classname="Tests" methodname="Parameter" fullname="Tests.A custom name" />
            <test-case classname="Tests" methodname="Parameter" fullname="Tests.Another custom name" />
            </test-run>''')
        groups = impact.parse_cases(xml, "suite.csproj")
        self.assertEqual(1, len(groups))
        self.assertEqual(2, len(next(iter(groups.values()))["cases"]))

    def test_no_method_metadata_rejected(self):
        with self.assertRaises(ValueError):
            impact.parse_cases(ET.fromstring('<test-case name="custom" />'), "suite.csproj")

    def test_coverage_retains_entire_executed_method_including_unhit_points(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            coverage = root / "coverage.xml"
            coverage.write_text(f'''<CoverageSession><Module><Files><File uid="1" fullPath="{root}/src/Value.cs"/></Files>
              <Classes><Class><Methods><Method><SequencePoints>
              <SequencePoint vc="1" sl="2" fileid="1"/><SequencePoint vc="0" sl="22" fileid="1"/>
              </SequencePoints></Method></Methods></Class></Classes></Module></CoverageSession>''')
            second = member("second", "Second")
            second.update(start=20, end=30)
            index = {"src/Value.cs": file(member("first", "First"), second)}
            covered, opaque = impact.coverage_methods([coverage], root, index)
            self.assertEqual(["first", "second"], covered)
            self.assertFalse(opaque)

    def test_filter_rejects_ambiguous_identity(self):
        self.assertEqual("'Tests.Method'", impact.quote_where("Tests.Method"))
        with self.assertRaises(ValueError):
            impact.quote_where("Tests' or method == 'Other")


class RoslynTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = impact.HERE.parents[1]
        impact.run(["dotnet", "build", impact.HERE / "Indexer/Indexer.csproj", "-v:q", "-m:1",
                    "-p:NuGetAudit=false", "-p:UseSharedCompilation=false"], cls.root)

    def index(self, source, extra=None):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            impact.write_json(folder / "input.json", dict({"Example.cs": source}, **(extra or {})))
            impact.run(["dotnet", impact.HERE / "Indexer/bin/Debug/net10.0/Indexer.dll",
                        folder / "input.json", folder / "output.json"], self.root)
            return impact.read_json(folder / "output.json")["Example.cs"]

    def test_body_and_new_methods_do_not_change_skeleton(self):
        a = self.index("namespace X; class C { int A() => 1; }")
        b = self.index("namespace X; class C { int A() => B(); int B() => 2; }")
        self.assertEqual(a["skeleton"], b["skeleton"])
        self.assertEqual(a["methods"][0]["key"], b["methods"][0]["key"])
        self.assertNotEqual(a["methods"][0]["hash"], b["methods"][0]["hash"])
        self.assertIn("B", b["methods"][0]["references"])

    def test_overloads_have_distinct_identity_and_nested_type_matches_nunit(self):
        index = self.index("namespace X; class C { class Inner { void A(int a) {} void A(string a) {} } }")
        self.assertEqual("X.C+Inner", index["methods"][0]["className"])
        self.assertNotEqual(index["methods"][0]["key"], index["methods"][1]["key"])

    def test_attributes_initializers_directives_and_bad_syntax_are_visible(self):
        a = self.index("class C { [SetUp] void Init() {} int P => Value(); }")
        self.assertTrue(a["methods"][0]["lifecycle"])
        self.assertIn("Value", a["references"])
        self.assertTrue(self.index("#if DEBUG\nclass C {}\n#endif")["conditional"])
        self.assertTrue(self.index("class C { void A( } ")["parseError"])

    def test_comments_do_not_change_body_identity_but_signature_does(self):
        a = self.index("class C { int A() => 1; }")
        b = self.index("class C { /* note */ int A() => 1; }")
        c = self.index("class C { int A(int x) => 1; }")
        self.assertEqual(a["methods"][0]["hash"], b["methods"][0]["hash"])
        self.assertNotEqual(a["methods"][0]["key"], c["methods"][0]["key"])

    def test_static_receiver_aliases_method_groups_and_type_names(self):
        index = self.index("""using Alias = X.Utility;
            namespace X; static class Utility { public static int Value() => 1; }
            class C { Utility field; void M() { Alias.Value(); Run(Utility.Value); } }""")
        value, caller = index["methods"]
        self.assertEqual("Utility", value["staticClass"])
        self.assertIn("Utility|Value", caller["references"])
        self.assertIn("Alias|Value", caller["references"])
        self.assertNotIn("Utility", index["references"])

    def test_generic_static_and_extension_methods_do_not_use_receiver_shortcut(self):
        index = self.index("""static class C<T> { static void Method() {} }
            static class Extensions { static void Method(this string text) {} }""")
        self.assertTrue(all(m["staticClass"] is None for m in index["methods"]))

    def test_global_alias_and_collection_method_group(self):
        index = self.index("class C { void M() { var callbacks = new[] { Alias.Read }; } }",
                           {"Usings.cs": "global using Alias = X.Utility;"})
        self.assertIn("Utility|Read", index["methods"][0]["references"])

    def test_reflection_is_opaque_inside_methods_and_constructors(self):
        index = self.index('''class C { C() { typeof(C).GetMethod("M"); }
            void M() { var method = typeof(C).GetMethod("M"); method.Invoke(this, null); } }''')
        self.assertTrue(index["opaque"])
        self.assertTrue(index["methods"][0]["opaque"])

    def test_compile_scope_recurses_project_references_and_keeps_linked_paths(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder).resolve()
            test = root / "tests/Test.csproj"
            lib = root / "src/Lib.csproj"
            responses = [dict(Items=dict(Compile=[dict(FullPath=str(root / "shared/Linked.cs"))],
                                           ProjectReference=[dict(FullPath=str(lib))])),
                         dict(Items=dict(Compile=[dict(FullPath=str(root / "src/Lib.cs"))], ProjectReference=[]))]
            with patch.object(impact, "run", side_effect=[json.dumps(r) for r in responses]) as command:
                scope = impact.compilation_scope(root, ["tests/Test.csproj"], "net10.0", "Debug")
            self.assertEqual({"shared/Linked.cs", "src/Lib.cs"}, scope)
            self.assertEqual(lib, command.call_args_list[1].args[0][2])

    def test_real_smoke_scope_contains_linked_sources_not_the_desktop_app(self):
        scope = impact.compilation_scope(self.root, ["build/test-impact/Smoke/Smoke.csproj"], "net10.0", "Debug")
        self.assertIn("src/Beutl.Core/CultureNameValidation.cs", scope)
        self.assertIn("tests/Beutl.UnitTests/Core/CultureNameValidationTests.cs", scope)
        self.assertNotIn("src/Beutl.Api/Services/PluginDependencyResolver.cs", scope)


if __name__ == "__main__":
    unittest.main()
