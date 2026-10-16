import tempfile
import unittest
import zipfile
from pathlib import Path

import check_extension_package as check


VERSION = "0.0.0-ci"
DLL = f"lib/net10.0/{check.NAME}.dll"


def nuspec(*dependencies):
    elements = "".join(f'<dependency id="{package_id}" version="{version}" />' for package_id, version in dependencies)
    return (
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><dependencies>'
        f'<group targetFramework="net10.0">{elements}</group></dependencies></metadata></package>'
    )


class CheckPackageTests(unittest.TestCase):
    def setUp(self):
        self._directory = tempfile.TemporaryDirectory()
        self.directory = Path(self._directory.name)
        self.artifacts = self.directory / "artifacts"
        self.artifacts.mkdir()
        for name in ("Beutl.Extensibility", "Beutl.Api", "Beutl.Engine.SourceGenerators"):
            (self.artifacts / f"{name}.{VERSION}.nupkg").touch()

    def tearDown(self):
        self._directory.cleanup()

    def check(self, dependencies, files=(DLL,)):
        path = self.directory / f"{check.NAME}.1.0.0.nupkg"
        with zipfile.ZipFile(path, "w") as package:
            package.writestr(f"{check.NAME}.nuspec", nuspec(*dependencies))
            for file in files:
                package.writestr(file, b"")
        return check.check_package(path, self.artifacts, VERSION)

    def test_passes_a_package_of_the_extension_that_depends_on_packed_libraries(self):
        self.assertEqual(self.check([("Beutl.Extensibility", VERSION), ("beutl.api", VERSION)]), [])

    def test_reports_a_missing_or_other_version_of_the_extensibility_dependency(self):
        for dependencies in ([("Beutl.Api", VERSION)], [("Beutl.Extensibility", "1.0.0")]):
            with self.subTest(dependencies=dependencies):
                self.assertIn(
                    f"no dependency on Beutl.Extensibility {VERSION}, from which Beutl reads the target version",
                    self.check(dependencies))

    def test_reports_a_build_only_or_unpacked_dependency(self):
        problems = self.check([("Beutl.Extensibility", VERSION), ("Beutl.Engine.SourceGenerators", VERSION), ("Other", "1.0.0")])

        self.assertEqual(problems, [
            "a dependency on Beutl.Engine.SourceGenerators, which only runs at build time",
            "a dependency on Other 1.0.0, which is not a packed Beutl package",
        ])

    def test_reports_a_missing_extension_assembly_and_copies_of_host_libraries(self):
        problems = self.check([("Beutl.Extensibility", VERSION)], files=("lib/net10.0/Beutl.Engine.dll",))

        self.assertEqual(problems, [
            f"no lib/*/{check.NAME}.dll",
            "lib/net10.0/Beutl.Engine.dll, a copy of a library that the host provides",
        ])


if __name__ == "__main__":
    unittest.main()
