import json
import tempfile
import unittest
from pathlib import Path

import avf_build_record as record


class VerifyTests(unittest.TestCase):
    def setUp(self):
        self._directory = tempfile.TemporaryDirectory()
        self.root = Path(self._directory.name)
        self.package = self.root / record.PACKAGE
        self.write(self.package / "Package.swift", "// package\n")
        self.write(self.package / "Sources/BeutlAVF/Reader.swift", "// reader\n")
        self.write(self.package / "Sources/CBeutlAVFTypes/include/BeutlAVFTypes.h", "// header\n")
        self.write(self.package / "Sources/CBeutlAVFTypes/include/module.modulemap", "// map\n")
        self.write(self.package / "Sources/CBeutlAVFTypes/BeutlAVFTypes.c", "// shim\n")
        self.write(self.package / "Tests/BeutlAVFTests/ReaderTests.swift", "// test\n")
        for rid in record.RIDS:
            self.write(record.dylib_path(self.root, rid), "binary")
        record.write_record(self.root, "Apple Swift version 6.4")

    def tearDown(self):
        self._directory.cleanup()

    @staticmethod
    def write(path, text):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text.encode())

    def test_a_fresh_record_holds_and_says_what_it_recorded(self):
        written = json.loads((self.root / record.RECORD).read_text())

        self.assertEqual(record.verify(self.root), [])
        self.assertEqual(written["toolchain"], "Apple Swift version 6.4")
        self.assertEqual(sorted(written["dylibs"]), sorted(record.RIDS))

    def test_every_kind_of_source_counts(self):
        for source in (
            "Package.swift",
            "Sources/BeutlAVF/Reader.swift",
            "Sources/CBeutlAVFTypes/include/BeutlAVFTypes.h",
            "Sources/CBeutlAVFTypes/include/module.modulemap",
            "Sources/CBeutlAVFTypes/BeutlAVFTypes.c",
        ):
            with self.subTest(source):
                path = self.package / source
                original = path.read_bytes()
                path.write_bytes(original + b"// changed\n")
                problems = record.verify(self.root)
                path.write_bytes(original)
                self.assertEqual(len(problems), 1)
                self.assertIn("sources", problems[0])

    def test_a_new_or_renamed_source_counts(self):
        path = self.package / "Sources/BeutlAVF/Writer.swift"
        self.write(path, "// writer\n")
        self.assertEqual(len(record.verify(self.root)), 1)

        path.unlink()
        renamed = self.package / "Sources/BeutlAVF/Reader.swift"
        renamed.rename(renamed.with_name("Reader2.swift"))
        self.assertEqual(len(record.verify(self.root)), 1)

    def test_what_the_dylib_is_not_built_from_does_not_count(self):
        self.write(self.package / "Tests/BeutlAVFTests/ReaderTests.swift", "// another test\n")
        self.write(self.package / "Sources/BeutlAVF/notes.txt", "// notes\n")

        self.assertEqual(record.verify(self.root), [])

    def test_a_checkout_with_crlf_line_endings_is_the_same_source(self):
        path = self.package / "Sources/BeutlAVF/Reader.swift"
        path.write_bytes(path.read_bytes().replace(b"\n", b"\r\n"))

        self.assertEqual(record.verify(self.root), [])

    def test_a_dylib_that_is_not_the_recorded_one_is_named(self):
        self.write(record.dylib_path(self.root, "osx-x64"), "another binary")

        problems = record.verify(self.root)

        self.assertEqual(len(problems), 1)
        self.assertIn("osx-x64", problems[0])

    def test_a_missing_dylib_or_record_is_reported(self):
        record.dylib_path(self.root, "osx-arm64").unlink()
        self.assertIn("osx-arm64", record.verify(self.root)[0])

        (self.root / record.RECORD).unlink()
        self.assertIn("missing", record.verify(self.root)[0])

    def test_a_record_that_does_not_name_a_toolchain_is_reported(self):
        record.write_record(self.root, "")

        self.assertIn("toolchain", record.verify(self.root)[0])


class CommittedRecordTests(unittest.TestCase):
    def test_the_committed_dylibs_match_the_committed_sources(self):
        self.assertEqual(record.verify(record.ROOT), [])


if __name__ == "__main__":
    unittest.main()
