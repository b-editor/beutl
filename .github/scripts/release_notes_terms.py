"""Print Beutl's interface labels as "English = Japanese" lines, for the models that write the release notes."""

from collections import Counter
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def values(path):
    # These files come from this repository's own checkout, so the standard parser is enough.
    return {data.get("name"): (data.findtext("value") or "").strip() for data in ET.parse(path).getroot().findall("data")}


def is_label(text):
    # A sentence or a format string is not a term to reuse.
    return 2 <= len(text) <= 40 and "{" not in text and not text.endswith((".", "?", "!", "…", ":"))


def terms(language):
    translations = {}
    for path in sorted(language.glob("*.ja.resx")):
        neutral = values(path.with_name(path.name.removesuffix(".ja.resx") + ".resx"))
        for name, japanese in values(path).items():
            english = neutral.get(name, "")
            if is_label(english) and japanese and japanese != english:
                translations.setdefault(english, Counter())[japanese] += 1
    # Where a label has several translations, the most used one is the term.
    return {english: found.most_common(1)[0][0] for english, found in sorted(translations.items())}


def main(argv):
    for english, japanese in terms(Path(argv[1])).items():
        print(f"{english} = {japanese}")


if __name__ == "__main__":
    sys.exit(main(sys.argv))
