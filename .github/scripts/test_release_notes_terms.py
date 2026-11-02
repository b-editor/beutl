from pathlib import Path
import tempfile
import unittest

import release_notes_terms


def write_resx(path, **strings):
    data = "".join(f'<data name="{name}" xml:space="preserve"><value>{value}</value></data>' for name, value in strings.items())
    path.write_text(f"<root>{data}</root>", encoding="utf-8")


class TermsTest(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.language = Path(directory.name)

    def test_pairs_each_files_labels(self):
        write_resx(self.language / "Strings.resx", TrackMatte="Track Matte", Drawable="Drawable")
        write_resx(self.language / "Strings.ja.resx", TrackMatte="トラックマット", Drawable="描画オブジェクト")
        write_resx(self.language / "AudioStrings.resx", AudioEffect="Audio Effect")
        write_resx(self.language / "AudioStrings.ja.resx", AudioEffect="音声エフェクト")

        self.assertEqual(release_notes_terms.terms(self.language), {
            "Audio Effect": "音声エフェクト", "Drawable": "描画オブジェクト", "Track Matte": "トラックマット",
        })

    def test_skips_what_is_not_a_translated_label(self):
        write_resx(self.language / "Strings.resx", Sentence="Could not load the file.", Format="Display {0}",
                   Same="ACES", Long="x" * 41, Missing="Missing", Empty="Empty")
        write_resx(self.language / "Strings.ja.resx", Sentence="読み込めません。", Format="ディスプレイ {0}",
                   Same="ACES", Long="長い", Empty="")

        self.assertEqual(release_notes_terms.terms(self.language), {})

    def test_prefers_the_most_used_translation(self):
        write_resx(self.language / "Strings.resx", First="Store", Second="Store", Third="Store")
        write_resx(self.language / "Strings.ja.resx", First="保存場所", Second="ストア", Third="ストア")

        self.assertEqual(release_notes_terms.terms(self.language), {"Store": "ストア"})


if __name__ == "__main__":
    unittest.main()
