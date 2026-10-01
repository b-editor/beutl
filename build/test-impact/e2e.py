#!/usr/bin/env python3
"""Bounded real-source smoke test. Requires a clean checkout; restores its temporary edit."""
import argparse
from pathlib import Path
from types import SimpleNamespace
import xml.etree.ElementTree as ET

import impact


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=Path("artifacts/test-impact-e2e"))
    args = parser.parse_args()
    root = impact.HERE.parents[1]
    out = (root / args.out).resolve()
    if impact.git(root, "status", "--porcelain", "--untracked-files=all"):
        raise RuntimeError("Run this smoke test in a clean, disposable checkout")
    out.mkdir(parents=True)
    base = impact.git(root, "rev-parse", "HEAD")
    project = "build/test-impact/Smoke/Smoke.csproj"
    common = dict(base=base, project=[project], tfm="net10.0", configuration="Debug")
    collection = out / "collection"
    collection.mkdir()
    baseline = impact.collect(SimpleNamespace(**common, no_build=False, max_methods=8,
        budget_seconds=180, method_timeout=30, method_pattern="*"), root, collection)
    assert len(baseline["profiles"]) == 8, baseline["errors"]
    assert not baseline["errors"], baseline["errors"]

    source = root / "src/Beutl.Core/CultureNameValidation.cs"
    original = source.read_bytes()
    before = b"if (item.Name == name)"
    after = b"if (string.Equals(item.Name, name, StringComparison.Ordinal))"
    assert original.count(before) == 1
    try:
        source.write_bytes(original.replace(before, after))
        impact.run(["dotnet", "build", project, "-m:1", "-v:q", "-p:NuGetAudit=false"], root)
        shadow = out / "shadow"
        shadow.mkdir()
        report = impact.select(SimpleNamespace(**common, baseline=collection / "baseline.json"), root, shadow)
        assert not report["fallbackReasons"], report["fallbackReasons"]
        selected = [t for k, t in report["tests"].items() if report["reasons"][k]]
        assert report["totalMethods"] == 8 and len(selected) == 2, report["reasons"]
        assert report["selectedCases"] == 6 and report["totalCases"] == 32
        assert all(t["className"].endswith("CultureNameValidationTests") for t in selected)

        full = out / "full"
        full.mkdir()
        impact.settings(full / "full.runsettings", full)
        impact.run(impact.test_command(project, "net10.0", "Debug") + ["--settings", full / "full.runsettings"],
                   root, log=full / "test.log")
        report["comparison"] = impact.compare(report, full)
        assert report["comparison"]["status"] == "complete", report["comparison"]
        assert not report["comparison"]["missedFailedMethods"]
        impact.write_json(shadow / "selection.json", report)
        impact.render(report, shadow / "selection.md")

        # A second experimental run proves the candidate identities select all parameter cases.
        # The full run above remains the authority. Production CI never executes this filter.
        subset = out / "subset"
        subset.mkdir()
        where = " or ".join(f"(class == {impact.quote_where(t['className'])} and "
                            f"method == {impact.quote_where(t['name'])})" for t in selected)
        impact.settings(subset / "subset.runsettings", subset, where)
        impact.run(impact.test_command(project, "net10.0", "Debug") + ["--settings", subset / "subset.runsettings"],
                   root, log=subset / "test.log")
        actual = impact.parse_cases(ET.parse(subset / "Smoke.xml").getroot(), project)
        assert set(actual) == {t["key"] for t in selected}
        assert sum(len(t["cases"]) for t in actual.values()) == 6
        assert all(c["result"] == "Passed" for t in actual.values() for c in t["cases"])
        impact.write_json(out / "evidence.json", {
            "baseSha": base, "profiledMethods": len(baseline["profiles"]),
            "collectionSeconds": baseline["collectionSeconds"], "totalMethods": 8, "selectedMethods": 2,
            "fullPassedCases": 32, "subsetPassedCases": 6, "fallbackReasons": [],
            "missedFailedMethods": report["comparison"]["missedFailedMethods"],
        })
        print(f"PASS: 8 methods / 32 cases -> 2 methods / 6 cases; evidence: {out}")
    finally:
        source.write_bytes(original)


if __name__ == "__main__":
    main()
