#!/usr/bin/env python3
"""Bounded, disposable-checkout experiment on an existing test project, after CI gates."""
import argparse
import collections
import json
from pathlib import Path
import statistics
import time
from types import SimpleNamespace
import xml.etree.ElementTree as ET

import impact

PROJECT = "tests/Beutl.FFmpegIpc.Tests/Beutl.FFmpegIpc.Tests.csproj"
SCENARIOS = [
    ("production-method", "src/Beutl.FFmpegIpc/FFmpegErrorMessageMapper.cs",
     b"message.Contains(InvalidDataText, StringComparison.Ordinal)",
     b"message.IndexOf(InvalidDataText, StringComparison.Ordinal) >= 0"),
    ("test-method", "tests/Beutl.FFmpegIpc.Tests/FFmpegErrorMessageMapperTests.cs",
     b"public void TryClassify_KnownCodes_MapToStableKinds()\n    {",
     b"public void TryClassify_KnownCodes_MapToStableKinds()\n    {\n"
     b"        Assert.That(FFmpegErrorMessageMapper.InvalidDataCode, Is.Not.Zero);"),
]


def inventory(tests):
    return collections.Counter((key, c["fullname"]) for key, t in tests.items() for c in t["cases"])


def inspect_execution(folder, tests):
    paths = list(folder.glob("*.xml"))
    if len(paths) != 1:
        raise ValueError("Expected exactly one NUnit result document")
    xml = ET.parse(paths[0]).getroot()
    actual = impact.parse_cases(xml, PROJECT)
    if inventory(actual) != inventory(tests):
        raise ValueError("Measured execution did not match the complete requested case inventory")
    outcomes = collections.Counter(c["result"] for t in actual.values() for c in t["cases"])
    if set(outcomes) - {"Passed", "Skipped", "Inconclusive"}:
        raise ValueError(f"Measured execution failed: {dict(outcomes)}")
    assembly = next(e for e in xml.iter("test-suite") if e.get("type") == "Assembly")
    return {"methods": len(actual), "cases": sum(outcomes.values()), "caseOutcomes": dict(outcomes),
            "engineSeconds": float(assembly.attrib["duration"])}


def execute(root, folder, tests, selected, coverage=True):
    if not tests:
        raise ValueError("A zero-method candidate is not a useful timing experiment")
    folder.mkdir()
    where = " or ".join(f"(class == {impact.quote_where(t['className'])} and "
                        f"method == {impact.quote_where(t['name'])})" for t in tests.values()) if selected else None
    impact.settings(folder / "test.runsettings", folder, where, coverage=coverage)
    start = time.monotonic()
    impact.run(impact.test_command(PROJECT, "net10.0", "Debug") + ["--settings", folder / "test.runsettings",
        "--results-directory", folder] + (["--collect:XPlat Code Coverage"] if coverage else []), root, timeout=180, log=folder / "test.log")
    wall = time.monotonic() - start
    result = inspect_execution(folder, tests)
    return dict(result, wallSeconds=wall, outsideEngineSeconds=wall - result["engineSeconds"])


def summarize_runs(runs, analysis_seconds):
    full = statistics.mean(r["wallSeconds"] for r in runs if r["kind"] == "full")
    selected = statistics.mean(r["wallSeconds"] for r in runs if r["kind"] == "selected")
    return {"fullMeanWallSeconds": full, "selectedMeanWallSeconds": selected,
            "executionSavingSeconds": full - selected,
            "savingAfterAnalysisSeconds": full - selected - analysis_seconds}


def render(evidence, path):
    lines = ["# Bounded test impact measurement", "", f"Status: **{evidence['status']}**; "
             f"baseline/tested checkout: `{evidence['baseSha']}`.", "",
             f"Scope: `{PROJECT}`. Full solution and Vulkan gates are unchanged.", "",
             "All paired runs use the same edited build, SDK, environment, OpenCover settings and zero NUnit "
             "workers within each coverage mode. Order is full/selected/selected/full. This is not the normal parallel CI workload. "
             "First/subsequent executions are retained individually; OS/filesystem caches are not flushed. "
             "Cold/warm analysis refers only to the process-local syntax cache, not a cold machine.", ""]
    baseline = evidence.get("baseline", {})
    if baseline:
        lines += [f"Baseline: {baseline['profiledMethods']}/{baseline['totalMethods']} methods; "
                  f"{baseline['collectionSeconds']:.2f}s total, {baseline['setupSeconds']:.2f}s setup.", "",
                  "Collection phase timings (seconds): `" + str(baseline["timings"]) + "`.", "",
                  "Extrapolated full-project collection cost: `" + str(baseline["fullProfileEstimateSeconds"]) +
                  "` seconds (sample median × method count + setup; methods were ranked by full-run duration, "
                  "so this is not a reliable forecast for slow/native methods or the whole solution).", ""]
    lines += ["| Change | Methods | Cases | Full mean | Selected mean | Execution saving | After analysis |",
              "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
    for item in evidence["scenarios"]:
        if "summary" not in item:
            continue
        summary = item["summary"]
        lines.append(f"| {item['name']} | {item['selectedMethods']}/{item['totalMethods']} | "
                     f"{item['selectedCases']}/{item['totalCases']} | {summary['fullMeanWallSeconds']:.2f}s | "
                     f"{summary['selectedMeanWallSeconds']:.2f}s | {summary['executionSavingSeconds']:.2f}s | "
                     f"{summary['savingAfterAnalysisSeconds']:.2f}s |")
    for item in evidence["scenarios"]:
        lines += ["", f"## {item['name']}", "", f"Build: {item.get('buildSeconds', 0):.2f}s; "
                  f"analysis: {item.get('analysisSeconds', 0):.2f}s; "
                  f"restore build: {item.get('restoreBuildSeconds', 0):.2f}s.", "",
                  f"Fallbacks: `{item.get('fallbackReasons', [])}`.", "",
                  "| Run | Methods / cases | Wall | NUnit engine | Outside engine |",
                  "| --- | ---: | ---: | ---: | ---: |"]
        for run in item.get("runs", []):
            lines.append(f"| {run['kind']} | {run['methods']} / {run['cases']} | {run['wallSeconds']:.2f}s | "
                         f"{run['engineSeconds']:.2f}s | {run['outsideEngineSeconds']:.2f}s |")
    lines += ["", "Outside-engine time includes process startup, discovery, coverage instrumentation and cleanup; "
              "it is not a direct startup-only measurement. Saving after analysis excludes baseline collection "
              "and build, listed separately; a fresh baseline makes this bounded experiment net additional work. "
              "Two samples per variant cannot establish a stable performance improvement. No whole-CI saving is inferred.", ""]
    for item in evidence["scenarios"]:
        for mode, data in item.get("modes", {}).items():
            lines += ["", f"### {item['name']} / {mode}", "",
                      f"Warm analysis: {item['warmAnalysisSeconds']:.2f}s; timings: `{item['warmTimings']}`.", "",
                      "Summary (seconds; break-even is executions): `" + str(data["summary"]) + "`.", "",
                      "Raw paired runs: `" + str(data["runs"]) + "`."]
    lines += ["", f"Total experiment cost: {evidence.get('totalExperimentSeconds', 0):.2f}s.", "",
              "No persistent analysis cache or executable cache is used. Dependency/SDK/config changes still invalidate "
              "the baseline. Cold/warm results are checked for identical selection. Baseline break-even excludes edit/restore "
              "builds, assumes the exact valid baseline remains usable, and is undefined for nonpositive net savings.", ""]
    if evidence.get("error"):
        lines += ["Error: " + evidence["error"], ""]
    path.write_text("\n".join(lines))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=Path("artifacts/test-impact/measurement"))
    args = parser.parse_args()
    root = impact.HERE.parents[1]
    out = (root / args.out).resolve()
    out.mkdir(parents=True)
    base = impact.git(root, "rev-parse", "HEAD")
    evidence = {"status": "incomplete", "baseSha": base, "project": PROJECT, "scenarios": []}
    common = dict(base=base, project=[PROJECT], tfm="net10.0", configuration="Debug", index_cache={})
    started = time.monotonic()

    def build(folder, label):
        start = time.monotonic()
        impact.run(["dotnet", "build", PROJECT, "--no-restore", "-f", "net10.0", "-c", "Debug", "-m:1", "-v:q",
                    "--disable-build-servers"], root, timeout=180, log=folder / (label + ".log"))
        return time.monotonic() - start

    try:
        collection = out / "collection"
        collection.mkdir()
        baseline = impact.collect(SimpleNamespace(**common, no_build=True, max_methods=16,
            budget_seconds=300, method_timeout=60,
            method_pattern="*", duration_results=root / "artifacts/test-impact/full"),
            root, collection)
        evidence["fingerprint"] = baseline["fingerprint"]
        evidence["baseline"] = dict(profiledMethods=len(baseline["profiles"]), totalMethods=len(baseline["tests"]),
            **{k: baseline[k] for k in ("collectionSeconds", "setupSeconds", "timings", "fullProfileEstimateSeconds", "errors", "profileOrder")})
        if not baseline["profiles"] or not baseline["finalized"]:
            raise ValueError("No usable finalized baseline was collected")
        for name, path, before, after in SCENARIOS:
            folder = out / name
            folder.mkdir()
            item = {"name": name, "runs": []}
            evidence["scenarios"].append(item)
            source = root / path
            original = source.read_bytes()
            if original.count(before) != 1:
                raise ValueError(f"The explicit experimental edit no longer matches {path}")
            try:
                source.write_bytes(original.replace(before, after))
                (folder / "experiment.patch").write_text(impact.git(root, "diff", "--", path) + "\n")
                item["buildSeconds"] = build(folder, "build")
                shadow = folder / "shadow"
                shadow.mkdir()
                report = impact.select(SimpleNamespace(**common, baseline=collection / "baseline.json"), root, shadow)
                item.update({k: report[k] for k in ("totalMethods", "selectedMethods", "totalCases", "selectedCases",
                                                  "fallbackReasons", "analysisSeconds", "timings")})
                if not report["inventoryComplete"] or not report["affectedMethods"]:
                    raise ValueError("Experiment did not analyze a nonzero ordinary-method change")
                warm = folder / "shadow-warm"
                warm.mkdir()
                warm_report = impact.select(SimpleNamespace(**common, baseline=collection / "baseline.json"), root, warm)
                if any(report[k] != warm_report[k] for k in ("tests", "reasons", "fallbackReasons", "affectedMethods")):
                    raise ValueError("Cold/warm analysis changed the selection")
                item["warmAnalysisSeconds"] = warm_report["analysisSeconds"]
                item["warmTimings"] = warm_report["timings"]
                selected = {k: t for k, t in report["tests"].items() if report["reasons"][k]}
                item["modes"] = {}
                for mode, coverage in (("coverage", True), ("no-coverage", False)):
                    runs = []
                    for i, kind in enumerate(("full", "selected", "selected", "full")):
                        results = folder / f"{mode}-{i}-{kind}"
                        run = execute(root, results, selected if kind == "selected" else report["tests"],
                                      kind == "selected", coverage=coverage)
                        runs.append(dict(run, kind=kind, coverage=coverage,
                                         order=i, temperature="first" if i == 0 else "subsequent"))
                        if kind == "full":
                            comparison = impact.compare(report, results)
                            item.setdefault("comparisons", []).append(comparison)
                            if comparison["status"] != "complete" or comparison["missedFailedMethods"]:
                                raise ValueError("Full execution did not validate the comparison")
                        print(f"{name} {mode} {kind}: {run['cases']} cases in {run['wallSeconds']:.2f}s", flush=True)
                    summary = summarize_runs(runs, report["analysisSeconds"])
                    summary["savingAfterWarmAnalysisSeconds"] = summary["executionSavingSeconds"] - item["warmAnalysisSeconds"]
                    summary["savingWithFreshBaselineSeconds"] = summary["savingAfterAnalysisSeconds"] - baseline["collectionSeconds"]
                    summary["baselineBreakEvenRuns"] = (baseline["collectionSeconds"] / summary["savingAfterAnalysisSeconds"]
                                                         if summary["savingAfterAnalysisSeconds"] > 0 else None)
                    item["modes"][mode] = dict(runs=runs, summary=summary)
                    if coverage:
                        item["runs"], item["summary"] = runs, summary
                    impact.write_json(out / "measurement.json", evidence)

            finally:
                source.write_bytes(original)
                item["restoreBuildSeconds"] = build(folder, "restore-build")
        evidence["status"] = "complete"
    except Exception as error:
        evidence["error"] = str(error)
        raise
    finally:
        evidence["totalExperimentSeconds"] = time.monotonic() - started
        impact.write_json(out / "measurement.json", evidence)
        render(evidence, out / "measurement.md")
        print("MEASUREMENT_JSON=" + json.dumps(evidence), flush=True)


if __name__ == "__main__":
    main()

