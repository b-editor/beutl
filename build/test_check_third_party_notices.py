import fnmatch
import unittest

from check_third_party_notices import notice_patterns, runtime_packages


class ThirdPartyNoticesTests(unittest.TestCase):
    def test_follows_project_and_transitive_edges_to_shipped_files(self):
        packages = {
            "App.Library/1": {"type": "project", "dependencies": {"Facade": "1"}},
            "Facade/1": {"type": "package", "runtime": {"lib/_._": {}},
                         "dependencies": {"Native": "1", "Managed": "1", "RidAsset": "1"}},
            "Native/1": {"type": "package", "native": {"runtimes/linux-x64/native/libnative.so": {}}},
            "Managed/1": {"type": "package", "runtime": {"lib/Managed.dll": {}}},
            "RidAsset/1": {"type": "package", "runtimeTargets": {"runtimes/win-x64/native/n.dll": {}}},
            "AvaloniaUI.DiagnosticsSupport/1": {"type": "package", "dependencies": {"Tool": "1"}},
            "Tool/1": {"type": "package", "runtime": {"lib/tool.dll": {}}},
        }
        portable = dict(packages, **{"Native/1": {"type": "package", "dependencies": {"AndroidOnly": "1"}},
                                     "AndroidOnly/1": {"type": "package", "native": {"android.so": {}}}})
        assets = {"projectFileDependencyGroups": {"net11.0": ["App.Library >= 1", "AvaloniaUI.DiagnosticsSupport >= 1"]},
                  "targets": {"net11.0": portable, "net11.0/linux-x64": packages}}
        self.assertEqual(runtime_packages(assets), {"Managed", "Native", "RidAsset"})

    def test_notice_family_and_explicit_patterns_respect_package_boundaries(self):
        patterns = notice_patterns("## Example (worker only)\nPackages: `Other.Native.*`, `Single`\n")
        for package in ("Example", "Example.Child", "Other.Native.linux", "Single"):
            self.assertTrue(any(fnmatch.fnmatchcase(package.lower(), pattern) for pattern in patterns))
        self.assertFalse(any(fnmatch.fnmatchcase("exampleextra", pattern) for pattern in patterns))
