#!/usr/bin/env python3
"""Experimental method impact reports. This program never skips the CI test gate."""
import argparse
import collections
from concurrent.futures import ThreadPoolExecutor
import fnmatch
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import signal
import statistics
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
import zipfile

HERE = Path(__file__).resolve().parent
SCHEMA = 1
# Isolated success cannot certify process-wide UI state or timing under suite load.
# See issues/2553 (Win32 HWND state) and pull/2551 (ad-block deadline sensitivity).
SUITE_SENSITIVE = {"Beutl.HeadlessUITests", "Beutl.E2ETests", "Beutl.Graphics3DTests"}
RISK_PATH = re.compile(
    r"(^native/|^external/|/runtimes/|/Assets/|/Fixtures/|SourceGenerators/|"
    r"/Graphics/|/Graphics3D/|/Rendering/|Renderer|/Shaders/|/GPU/|/Gpu|/Vulkan|/Skia|"
    r"Beutl\.Extensions\.(FFmpeg|AVFoundation|MediaFoundation)|Beutl\.FFmpegWorker/)", re.I)


def run(args, root, timeout=120, log=None):
    """Bound the whole child process tree, including testhost, on the supported Unix hosts."""
    process = subprocess.Popen([str(a) for a in args], cwd=root, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, start_new_session=True)
    try:
        output, _ = process.communicate(timeout=timeout)
    except (subprocess.TimeoutExpired, KeyboardInterrupt):
        if os.name == "posix":
            os.killpg(process.pid, signal.SIGKILL)
        else:
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True)
        output, _ = process.communicate()
        if log:
            Path(log).write_text(output)
        raise RuntimeError(f"Command timed out/interrupted: {args[0:3]}")
    if log:
        Path(log).write_text(output)
    if process.returncode:
        raise RuntimeError(f"Command failed ({process.returncode}): {args[0:3]}\n{output[-3000:]}")
    return output.strip()


def git(root, *args):
    return run(["git", *args], root)


def read_json(path):
    return json.loads(Path(path).read_text())


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n")


def fingerprint(root, tfm, configuration):
    tool = hashlib.sha256()
    for path in [HERE / "impact.py", HERE / "Indexer/Program.cs", HERE / "Indexer/Indexer.csproj"]:
        tool.update(path.read_bytes())
    os_version = platform.mac_ver()[0] if sys.platform == "darwin" else platform.version()
    if sys.platform.startswith("linux"):
        release = platform.freedesktop_os_release()
        os_version = release.get("ID", "") + ":" + release.get("VERSION_ID", "")
    return {
        "sdk": run(["dotnet", "--version"], root), "tfm": tfm, "configuration": configuration,
        "os": platform.system(), "osVersion": os_version, "architecture": platform.machine(),
        "runtimes": run(["dotnet", "--list-runtimes"], root).splitlines(),
        "toolHash": tool.hexdigest(),
        "environment": {key: os.environ.get(key, "") for key in (
            "BEUTL_REQUIRE_GPU", "BEUTL_VULKAN_VALIDATION", "VK_ICD_FILENAMES",
            "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "DOTNET_ROLL_FORWARD")},
        "collector": "opencover;IncludeTestAssembly=true;no-exclusions;workers=0",
    }


def source_index(root, out, scope, timings=None, cache=None, environment=None):
    paths = git(root, "ls-files", "-z", "--", "*.cs").split("\0")
    # Include newly added files for local selection, even before git add.
    paths += git(root, "ls-files", "--others", "--exclude-standard", "-z", "--", "*.cs").split("\0")
    sources = {p: (root / p).read_text(encoding="utf-8-sig") for p in sorted(set(paths))
               if p and p in scope and (root / p).is_file() and not p.startswith("build/test-impact/")}
    # Only cache immutable syntax results in this process, never discovery, coverage,
    # MSBuild evaluations or executable files. Hash every source together: global
    # aliases can change the references in otherwise unchanged files.
    cache_key = hashlib.sha256(json.dumps([str(root.resolve()), git(root, "rev-parse", "HEAD"),
        environment, sources], sort_keys=True).encode()).hexdigest()
    if cache is not None and environment is not None and cache_key in cache:
        result = json.loads(cache[cache_key])
        write_json(out / "index.json", result)
        if timings is not None:
            timings.update(indexerBuildSeconds=0, sourceIndexSeconds=0, sourceIndexCacheHit=True)
        return result
    project = HERE / "Indexer/Indexer.csproj"
    began = time.monotonic()
    run(["dotnet", "build", project, "--nologo", "-v:q", "-m:1", "-p:NuGetAudit=false",
         "--disable-build-servers"], root,
        log=out / "index-build.log")
    if timings is not None:
        timings["indexerBuildSeconds"] = time.monotonic() - began
    began = time.monotonic()
    write_json(out / "sources.json", sources)
    run(["dotnet", HERE / "Indexer/bin/Debug/net10.0/Indexer.dll", out / "sources.json",
         out / "index.json"], root)
    (out / "sources.json").unlink()
    if timings is not None:
        timings["sourceIndexSeconds"] = time.monotonic() - began
    result = read_json(out / "index.json")
    if cache is not None and environment is not None:
        if len(cache) >= 2:
            cache.pop(next(iter(cache)))
        cache[cache_key] = json.dumps(result)
    if timings is not None:
        timings["sourceIndexCacheHit"] = False
    return result


def project_paths(root, requested):
    if requested:
        projects = sorted(set(requested))
    else:
        projects = [p.relative_to(root).as_posix() for p in sorted((root / "tests").glob("*/*.csproj"))
                    if any((e.text or "").strip() == "true" for e in ET.parse(p).iter("IsTestProject"))]
    for p in projects:
        if not (root / p).resolve().is_relative_to(root) or not (root / p).is_file():
            raise ValueError(f"Invalid project: {p}")
    if not projects:
        raise ValueError("No test projects found")
    return projects


def compilation_scope(root, projects, tfm, configuration):
    """Evaluate the requested tests' Compile/ProjectReference closure, including linked files."""
    pending = {root / p for p in projects}
    visited, sources = set(), set()

    def evaluate(project):
        return json.loads(run(["dotnet", "msbuild", project, "-nologo",
            "-getItem:Compile,ProjectReference", f"-p:TargetFramework={tfm}",
            f"-p:Configuration={configuration}"], root))["Items"]

    # Independent evaluations at each graph level share no output files: no targets
    # are executed. Bound concurrency so this advisory step cannot saturate a runner.
    with ThreadPoolExecutor(max_workers=2) as pool:
        while pending:
            batch = sorted({p.resolve() for p in pending} - visited)
            pending = set()
            if any(not p.is_relative_to(root) for p in batch):
                raise ValueError("ProjectReference outside the repository cannot be analyzed")
            visited.update(batch)
            for evaluated in pool.map(evaluate, batch):
                for item in evaluated["Compile"]:
                    path = Path(item["FullPath"]).resolve()
                    if path.is_relative_to(root):
                        sources.add(path.relative_to(root).as_posix())
                pending.update(Path(item["FullPath"]) for item in evaluated["ProjectReference"])
    return sources


def source_resources(root):
    """Source-looking files can actually be embedded generator inputs/shared resources."""
    resources = set()
    sources = set(filter(None, git(root, "ls-files", "-z", "--", "*.cs").split("\0")))
    for project in filter(None, git(root, "ls-files", "-z", "--", "*.csproj").split("\0")):
        for item in ET.parse(root / project).iter("EmbeddedResource"):
            for pattern in item.get("Include", "").split(";"):
                if not pattern:
                    continue
                pattern = pattern.replace("\\", "/")
                if "$(" in pattern:
                    # An unevaluated resource expression must not certify a C# change safe.
                    resources.update(sources)
                else:
                    absolute = os.path.normpath(str((root / project).parent / pattern))
                    resources.update(p for p in sources if fnmatch.fnmatchcase(str(root / p), absolute))
    return resources


def settings(path, result_dir, where=None, coverage=False, discovery=False):
    root = ET.Element("RunSettings")
    config = ET.SubElement(root, "RunConfiguration")
    ET.SubElement(config, "MaxCpuCount").text = "1"
    nunit = ET.SubElement(root, "NUnit")
    for key, value in {"NumberOfTestWorkers": "0", "AssemblySelectLimit": "100000",
                       "PreFilter": "false", "TestOutputXml": str(result_dir.resolve()),
                       "DumpXmlTestDiscovery": str(discovery).lower()}.items():
        ET.SubElement(nunit, key).text = value
    if where is not None:
        ET.SubElement(nunit, "Where").text = where
    if coverage:
        collectors = ET.SubElement(ET.SubElement(root, "DataCollectionRunSettings"), "DataCollectors")
        collector = ET.SubElement(collectors, "DataCollector", friendlyName="XPlat code coverage")
        config = ET.SubElement(collector, "Configuration")
        ET.SubElement(config, "Format").text = "opencover"
        ET.SubElement(config, "IncludeTestAssembly").text = "true"
        ET.SubElement(config, "Exclude").text = ""
        ET.SubElement(config, "ExcludeByAttribute").text = ""
        ET.SubElement(config, "UseSourceLink").text = "false"
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


def test_command(project, tfm, configuration):
    return ["dotnet", "test", project, "--no-build", "--no-restore", "-f", tfm, "-c", configuration]


def test_key(project, class_name, method):
    return f"{project}::{class_name}.{method}"


def parse_cases(xml, project):
    groups = {}
    for case in xml.iter("test-case"):
        cls, method = case.get("classname"), case.get("methodname")
        if not cls or not method:
            raise ValueError("NUnit case has no classname/methodname; cannot group safely")
        key = test_key(project, cls, method)
        entry = groups.setdefault(key, {"key": key, "project": project, "className": cls,
                                        "name": method, "cases": []})
        entry["cases"].append({"fullname": case.get("fullname"), "result": case.get("result"),
                               "duration": float(case.get("duration", "0"))})
    return groups


def discover(root, projects, tfm, configuration, out):
    tests = {}
    for i, project in enumerate(projects):
        folder = out / f"discovery-{i}"
        folder.mkdir(parents=True)
        target = Path(run(["dotnet", "msbuild", project, "-nologo", "-getProperty:TargetPath",
                           f"-p:TargetFramework={tfm}", f"-p:Configuration={configuration}"], root))
        if not target.is_file():
            raise ValueError(f"Build the test project first: {project} ({target})")
        dump = target.parent / "Dump"
        before = {p: (p.stat().st_mtime_ns, p.stat().st_size) for p in dump.glob("D_*.dump")}
        settings(folder / "discovery.runsettings", folder, discovery=True)
        run(test_command(project, tfm, configuration) + ["--list-tests", "--settings",
            folder / "discovery.runsettings"], root, log=folder / "discovery.log")
        candidates = [p for p in dump.glob("D_*.dump")
                      if before.get(p) != (p.stat().st_mtime_ns, p.stat().st_size)]
        if len(candidates) != 1:
            raise ValueError(f"Expected one fresh NUnit discovery XML for {project}, got {candidates}")
        groups = parse_cases(ET.parse(candidates[0]).getroot(), project)
        if not groups:
            raise ValueError(f"No discoverable NUnit methods in {project}")
        tests.update(groups)
    return tests


def methods(index):
    return {m["key"]: dict(m, file=path) for path, f in index.items() for m in f["methods"]}


def test_source_lookup(index):
    lookup = collections.defaultdict(set)
    for key, method in methods(index).items():
        lookup[method["className"], method["name"]].add(key)
    return lookup


def source_methods(test, lookup):
    # NUnit's constructed generic/inherited identities may not resolve uniquely; those are opaque.
    return lookup.get((test["className"], test["name"]), set())


def references_method(references, method):
    name = method["name"]
    if method.get("staticClass"):
        return name in references or method["staticClass"] + "|" + name in references
    return any(reference.rsplit("|", 1)[-1] == name for reference in references)


def coverage_methods(paths, root, index):
    """If a CLR method ran at all, retain all its points, not only its previously hit lines."""
    covered, opaque = set(), set()
    for path in paths:
        xml = ET.parse(path).getroot()
        for module in xml.iter("Module"):
            files = {}
            for file in module.findall("./Files/File"):
                location = Path(file.attrib["fullPath"])
                if location.is_absolute() and location.is_relative_to(root):
                    files[file.attrib["uid"]] = location.relative_to(root).as_posix()
            for method in module.findall("./Classes/Class/Methods/Method"):
                points = method.findall("./SequencePoints/SequencePoint")
                if not any(int(p.get("vc", "0")) > 0 for p in points):
                    continue
                for point in points:
                    file = files.get(point.get("fileid"))
                    if not file:
                        opaque.add("unmapped-document")
                        continue
                    if RISK_PATH.search(file):
                        opaque.add("native/gpu/resource-path")
                    if file not in index:
                        opaque.add("generated-or-unindexed-source")
                        continue
                    if index[file]["conditional"] or index[file]["parseError"]:
                        opaque.add("conditional-or-unparsed-source")
                    if index[file].get("opaque"):
                        opaque.add("dynamic-or-reflection-file")
                    line = int(point.attrib["sl"])
                    for member in index[file]["methods"]:
                        if member["start"] <= line <= member["end"]:
                            covered.add(member["key"])
                            if member["opaque"]:
                                opaque.add("dynamic-or-reflection")
    if not covered:
        opaque.add("no-mapped-method-coverage")
    return sorted(covered), sorted(opaque)


def quote_where(value):
    # NUnit TSL quoted strings support backslash escaping, but unusual runtime identities are
    # left unprofiled instead of risking an accidentally broader filter.
    if not re.fullmatch(r"[\w.`+<>\[\], ]+", value):
        raise ValueError(f"Unsupported NUnit filter identity: {value}")
    return "'" + value + "'"


def validate_isolated(test, executed):
    if set(executed) != {test["key"]}:
        raise ValueError("Isolation failed: NUnit executed different methods")
    actual = executed[test["key"]]["cases"]
    expected = collections.Counter(c["fullname"] for c in test["cases"])
    if collections.Counter(c["fullname"] for c in actual) != expected:
        raise ValueError("Isolation failed: not every parameterized case ran")
    if any(c["result"] != "Passed" for c in actual):
        raise ValueError("Skipped/failed cases cannot establish a baseline")
    return actual


def duration_priority(candidates, results):
    """Historical aggregate durations are scheduling hints, never coverage evidence."""
    durations = collections.defaultdict(float)
    if results and Path(results).is_dir():
        for path in sorted(Path(results).glob("*.xml")):
            try:
                xml = ET.parse(path).getroot()
                for case in xml.iter("test-case"):
                    identity = (case.get("classname"), case.get("methodname"))
                    duration = float(case.get("duration", "0"))
                    if 0 <= duration < float("inf"):
                        durations[identity] += duration
            except (ET.ParseError, ValueError, OSError):
                continue
    return sorted(candidates, key=lambda t: (
        -durations[(t["className"], t["name"])], t["key"]))


def collect(args, root, out):
    start = time.monotonic()
    timings = {}
    if git(root, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Baseline collection requires a clean checkout at --base (put output under artifacts/)")
    if (out / "baseline.json").exists():
        raise ValueError("Use a fresh output directory; an older baseline must not survive a failed collection")
    sha = git(root, "rev-parse", "HEAD")
    if sha != git(root, "rev-parse", args.base):
        raise ValueError("Baseline --base must equal the checked-out HEAD")
    projects = project_paths(root, args.project)
    began = time.monotonic()
    if not args.no_build:
        for i, project in enumerate(projects):
            run(["dotnet", "build", project, "-f", args.tfm, "-c", args.configuration,
                 "-m:1", "-v:q", "-p:NuGetAudit=false", "--disable-build-servers"],
                root, timeout=600, log=out / f"build-{i}.log")
    timings["testProjectBuildSeconds"] = time.monotonic() - began if not args.no_build else 0
    began = time.monotonic()
    scope = compilation_scope(root, projects, args.tfm, args.configuration)
    timings["compileScopeSeconds"] = time.monotonic() - began
    environment = fingerprint(root, args.tfm, args.configuration)
    index = source_index(root, out, scope, timings, getattr(args, "index_cache", None), environment)
    began = time.monotonic()
    tests = discover(root, projects, args.tfm, args.configuration, out)
    timings["discoverySeconds"] = time.monotonic() - began
    lookup = test_source_lookup(index)
    baseline = {"schema": SCHEMA, "sha": sha, "fingerprint": environment,
                "projects": projects, "index": index, "tests": tests, "profiles": {}, "errors": {},
                "collectionSeconds": 0, "complete": False, "finalized": False, "timings": timings}
    candidates = [t for t in tests.values() if any(fnmatch.fnmatchcase(t["key"], pattern)
                  for pattern in args.method_pattern.split(";"))]
    candidates = duration_priority(candidates, getattr(args, "duration_results", None))
    baseline["profileOrder"] = [t["key"] for t in candidates[:args.max_methods]]
    profile_start = time.monotonic()
    baseline["setupSeconds"] = profile_start - start
    for i, test in enumerate(candidates[:args.max_methods]):
        remaining = args.budget_seconds - (time.monotonic() - profile_start)
        if remaining <= 1:
            break
        folder = out / f"method-{i:05}"
        folder.mkdir()
        began = time.monotonic()
        try:
            where = f"class == {quote_where(test['className'])} and method == {quote_where(test['name'])}"
            settings(folder / "coverage.runsettings", folder, where, coverage=True)
            run(test_command(test["project"], args.tfm, args.configuration) + ["--settings",
                folder / "coverage.runsettings", "--collect:XPlat Code Coverage", "--results-directory", folder],
                root, timeout=min(args.method_timeout, remaining), log=folder / "test.log")
            result_files = list(folder.glob("*.xml"))
            if len(result_files) != 1:
                raise ValueError("Missing/ambiguous NUnit results for isolated method")
            executed = parse_cases(ET.parse(result_files[0]).getroot(), test["project"])
            actual = validate_isolated(test, executed)
            coverage = list(folder.rglob("coverage.opencover.xml"))
            if len(coverage) != 1:
                raise ValueError("Missing/ambiguous isolated OpenCover report")
            covered, opaque = coverage_methods(coverage, root, index)
            if len(source_methods(test, lookup)) != 1:
                opaque.append("unresolved-test-source")
            baseline["profiles"][test["key"]] = {"methods": covered, "opaque": opaque,
                "wallSeconds": time.monotonic() - began,
                "testSeconds": sum(c["duration"] for c in actual)}
        except (RuntimeError, ValueError, OSError, ET.ParseError) as error:
            baseline["errors"][test["key"]] = str(error)
        print(f"Profile {i + 1}/{min(len(candidates), args.max_methods)}: {test['className']}.{test['name']}", flush=True)
        baseline["collectionSeconds"] = time.monotonic() - start
        write_json(out / "baseline.json", baseline)
    baseline["collectionSeconds"] = time.monotonic() - start
    timings["profilingSeconds"] = time.monotonic() - profile_start
    walls = [p["wallSeconds"] for p in baseline["profiles"].values()]
    baseline["fullProfileEstimateSeconds"] = (baseline["setupSeconds"] + statistics.median(walls) * len(tests)
                                              if walls else None)
    baseline["finalized"] = git(root, "rev-parse", "HEAD") == sha and not git(
        root, "status", "--porcelain", "--untracked-files=all")
    baseline["complete"] = baseline["finalized"] and len(baseline["profiles"]) == len(tests) and not baseline["errors"]
    write_json(out / "baseline.json", baseline)
    (out / "collection.md").write_text(
        f"# Test impact baseline\n\nSHA: `{sha}`\n\n"
        f"Methods: {len(tests)}; profiled: {len(baseline['profiles'])}; errors: {len(baseline['errors'])}.\n\n"
        f"Collection wall time: {baseline['collectionSeconds']:.2f}s. "
        "Every method starts a fresh testhost and collector; this is not normal suite runtime.\n\n"
        "Unprofiled, skipped or opaque methods remain selected. Scope: " + ", ".join(projects) + "\n")
    return baseline


def choose(baseline, current, tests, changed_files, expected_sha, environment, resources=()):
    """Pure selector, also exercised by regression tests. All outputs are advisory."""
    reasons = {key: [] for key in tests}
    fallback = []
    affected = set()
    if not baseline:
        fallback.append("missing-baseline")
    elif baseline.get("schema") != SCHEMA:
        fallback.append("unsupported-baseline-schema")
    elif baseline.get("sha") != expected_sha:
        fallback.append("baseline-sha-mismatch")
    elif baseline.get("fingerprint") != environment:
        fallback.append("baseline-environment-mismatch")
    elif not baseline.get("finalized"):
        fallback.append("baseline-interrupted-or-checkout-changed")
    else:
        old = baseline["index"]
        old_methods, new_methods = methods(old), methods(current)
        for key, profile in baseline["profiles"].items():
            if key not in baseline["tests"] or not (profile["methods"] or profile["opaque"]) \
                    or not set(profile["methods"]).issubset(old_methods):
                raise ValueError("Corrupt baseline profile: " + key)
        lookup = test_source_lookup(current)
        all_methods = dict(old_methods, **new_methods)
        changed = set()
        for path in changed_files:
            if path in resources or RISK_PATH.search(path):
                fallback.append(f"native-gpu-or-resource-change:{path}")
            elif not path.endswith(".cs"):
                fallback.append(f"shared-config-resource-or-unknown-change:{path}")
            elif path not in old or path not in current:
                # New/deleted source may affect assembly discovery, generators, or registrations.
                fallback.append(f"added-deleted-or-unindexed-source:{path}")
            elif any(old[path][k] or current[path][k] for k in ("parseError", "conditional")):
                fallback.append(f"unsupported-source:{path}")
            elif old[path]["skeleton"] != current[path]["skeleton"]:
                fallback.append(f"non-method-change:{path}")
            else:
                before = {m["key"]: m for m in old[path]["methods"]}
                after = {m["key"]: m for m in current[path]["methods"]}
                if before.keys() - after.keys():
                    fallback.append(f"deleted-or-signature-changed-method:{path}")
                for key, method in after.items():
                    if key not in before or before[key]["hash"] != method["hash"]:
                        changed.add(key)
                        if method["lifecycle"] or method["opaque"] or before.get(key, {}).get("opaque"):
                            fallback.append(f"lifecycle-or-dynamic-change:{key}")
        affected = set(changed)
        # Union old and new references: replacing/removing a call must not erase its old edge.
        reverse = collections.defaultdict(set)
        static_reverse = collections.defaultdict(set)
        for method in [*old_methods.values(), *new_methods.values()]:
            for reference in method["references"]:
                reverse[reference.rsplit("|", 1)[-1]].add(method["key"])
                static_reverse[reference].add(method["key"])

        def callers(key):
            method = all_methods[key]
            name = method["name"]
            if method.get("staticClass"):
                return static_reverse[name] | static_reverse[method["staticClass"] + "|" + name]
            return reverse[name]

        queue = list(changed)
        while queue:
            key = queue.pop()
            for caller in callers(key) - affected:
                affected.add(caller)
                queue.append(caller)
        # References in field/property initializers, constructors, fixture attributes and
        # TestCaseSource properties cannot be represented by ordinary method edges.
        for path, file in current.items():
            if any(references_method(file["references"], all_methods[k]) for k in affected):
                fallback.append(f"reference-outside-method:{path}")
        for key, test in tests.items():
            profile = baseline["profiles"].get(key)
            src = source_methods(test, lookup)
            if Path(test["project"]).stem in SUITE_SENSITIVE:
                reasons[key].append("suite-interaction-policy")
            if key not in baseline["tests"]:
                reasons[key].append("new-test")
            elif collections.Counter(c["fullname"] for c in test["cases"]) != collections.Counter(
                    c["fullname"] for c in baseline["tests"][key]["cases"]):
                reasons[key].append("case-inventory-changed")
            if len(src) != 1:
                reasons[key].append("unresolved-test-source")
            if src & changed:
                reasons[key].append("changed-test")
            if src & affected:
                reasons[key].append("static-reference")
            if not profile:
                reasons[key].append("unprofiled-test")
            else:
                reasons[key].extend("opaque:" + reason for reason in profile["opaque"])
                if set(profile["methods"]) & affected:
                    reasons[key].append("method-coverage-or-caller")
            if any(all_methods[k]["opaque"] for k in src):
                reasons[key].append("dynamic-test")
        if changed and not any(reasons.values()):
            fallback.append("changed-method-without-test-evidence")
        # Changed methods with no old coverage AND no reachable test need conservative expansion
        # even when some unrelated unprofiled tests already make the selection nonempty.
        observed = {m for p in baseline["profiles"].values() for m in p["methods"]}
        test_sources = {m for test in tests.values() for m in source_methods(test, lookup)}
        for seed in changed:
            reachable, pending = {seed}, [seed]
            while pending:
                key = pending.pop()
                for caller in callers(key) - reachable:
                    reachable.add(caller)
                    pending.append(caller)
            if not (reachable & (observed | test_sources)):
                fallback.append("unobserved-change-without-static-test-path:" + seed)
    if fallback:
        for key in reasons:
            reasons[key].append("full-fallback")
    return {"schema": SCHEMA, "mode": "shadow", "safeToSkip": False, "baseSha": expected_sha,
            "totalMethods": len(tests), "selectedMethods": sum(bool(r) for r in reasons.values()),
            "totalCases": sum(len(t["cases"]) for t in tests.values()),
            "selectedCases": sum(len(tests[k]["cases"]) for k, r in reasons.items() if r),
            "fallbackReasons": sorted(set(fallback)), "affectedMethods": sorted(affected),
            "tests": tests, "reasons": reasons,
            "collectionSeconds": baseline.get("collectionSeconds") if baseline else None,
            "profiledTestMethods": sorted(baseline.get("profiles", {})) if baseline else [],
            "profiledMethods": len(baseline.get("profiles", {})) if baseline else 0}


def render(report, path):
    comparison = report.get("comparison")
    elapsed = report.get("collectionSeconds")
    elapsed = f"{elapsed:.2f}s" if isinstance(elapsed, (int, float)) else "unavailable"
    lines = ["# Test impact (shadow only)", "", "The full suite and Vulkan validation remain required.", "",
             f"Methods: **{report['selectedMethods']} / {report['totalMethods']} selected**; "
             f"cases: **{report['selectedCases']} / {report['totalCases']}**.", "",
             f"Baseline collection: {elapsed}; "
             f"profiled methods: {report['profiledMethods']}.", "",
             "## Full fallback reasons", ""]
    lines += ["- " + r.replace("\n", " ") for r in report["fallbackReasons"][:25]] or ["None."]
    if len(report["fallbackReasons"]) > 25:
        lines.append(f"- {len(report['fallbackReasons']) - 25} more reasons in selection.json.")
    if not report.get("inventoryComplete", True):
        lines += ["", "**Inventory is incomplete: totals above are only known methods. All tests must run.**"]
    if comparison:
        lines += ["", "## Comparison with full execution", "", "```json",
                  json.dumps(comparison, indent=2), "```", "",
                  "Passing omitted tests do not prove future selection is sound. Duration is the sum "
                  "of test case times, not a prediction of wall-clock savings."]
    lines += ["", "## Selection reasons", "", "| Reason | Methods |", "| --- | ---: |"]
    counts = collections.Counter(reason for reasons in report["reasons"].values() for reason in reasons)
    lines += [f"| {reason} | {count} |" for reason, count in sorted(counts.items())]
    lines += ["", "Per-method decisions and the complete candidate list are in `selection.json`. "
              "A zero candidate count must never be passed to dotnet test as an empty filter.", ""]
    Path(path).write_text("\n".join(lines))


def select(args, root, out):
    start = time.monotonic()
    timings = {}
    tests, baseline = {}, None
    try:
        projects = project_paths(root, args.project)
        began = time.monotonic()
        tests = discover(root, projects, args.tfm, args.configuration, out)
        timings["discoverySeconds"] = time.monotonic() - began
        sha = git(root, "rev-parse", args.base)
        changed = set(filter(None, git(root, "diff", "--name-only", "--no-renames", "-z", sha, "--").split("\0")))
        changed.update(filter(None, git(root, "ls-files", "--others", "--exclude-standard", "-z").split("\0")))
        if args.baseline and Path(args.baseline).is_file():
            baseline = read_json(args.baseline)
        environment = fingerprint(root, args.tfm, args.configuration)
        # Metadata rejection needs no static graph. Still discover the actual test inventory.
        current, resources = {}, set()
        if baseline and baseline.get("schema") == SCHEMA and baseline.get("sha") == sha \
                and baseline.get("fingerprint") == environment and baseline.get("finalized"):
            began = time.monotonic()
            scope = compilation_scope(root, projects, args.tfm, args.configuration)
            timings["compileScopeSeconds"] = time.monotonic() - began
            current = source_index(root, out, scope, timings, getattr(args, "index_cache", None), environment)
            resources = source_resources(root)
        report = choose(baseline, current, tests, changed, sha, environment, resources)
        report["projects"] = projects
        report["changedFiles"] = sorted(changed)
    except Exception as error:
        report = choose(None, {}, tests, [], args.base, {})
        report["fallbackReasons"] = ["analysis-failure:" + str(error)]
        report["inventoryComplete"] = False
    else:
        report["inventoryComplete"] = True
    report["headSha"] = git(root, "rev-parse", "HEAD")
    report["analysisSeconds"] = time.monotonic() - start
    report["timings"] = timings
    write_json(out / "selection.json", report)
    render(report, out / "selection.md")
    print(f"Shadow selection: {report['selectedMethods']}/{report['totalMethods']} methods; "
          f"fallbacks: {len(report['fallbackReasons'])}. Full test execution remains required.")
    return report


def compare(report, results):
    tests = report["tests"]
    by_assembly = {Path(t["project"]).stem: t["project"] for t in tests.values()}
    seen, failed, unknown = set(), set(), set()
    observed_cases = collections.defaultdict(collections.Counter)
    durations = collections.Counter()
    outcomes = collections.Counter()
    for path in sorted(Path(results).glob("*.xml")):
        xml = ET.parse(path).getroot()
        for assembly in xml.iter("test-suite"):
            if assembly.get("type") != "Assembly":
                continue
            project = by_assembly.get(Path(assembly.get("name", "")).stem)
            if not project:
                unknown.add(assembly.get("name", str(path)))
                continue
            for key, group in parse_cases(assembly, project).items():
                if key not in tests:
                    unknown.add(key)
                    continue
                seen.add(key)
                for case in group["cases"]:
                    observed_cases[key][case["fullname"]] += 1
                    outcomes[case["result"]] += 1
                    durations["selectedTestSeconds" if report["reasons"][key] else "omittedTestSeconds"] += case["duration"]
                    if case["result"] == "Failed":
                        failed.add(key)
    missing = sorted(set(tests) - seen)
    missing_cases = {k: list((collections.Counter(c["fullname"] for c in t["cases"]) - observed_cases[k]).elements())
                     for k, t in tests.items()}
    missing_cases = {k: v for k, v in missing_cases.items() if v}
    return {"status": "complete" if seen and not missing_cases and not unknown else "incomplete",
            "observedMethods": len(seen), "missingMethods": missing, "unknownMethodsOrAssemblies": sorted(unknown),
            "missingCases": missing_cases,
            "missedFailedMethods": sorted(k for k in failed if not report["reasons"][k]),
            "isolatedPassButFullFailedMethods": sorted(failed & set(report.get("profiledTestMethods", []))),
            "failedMethods": len(failed), "caseOutcomes": dict(outcomes), **dict(durations)}


def download_baseline(args, root, out):
    """Only read artifacts from successful manual main runs at the exact requested SHA."""
    try:
        (out / "baseline.json").unlink(missing_ok=True)
        if not re.fullmatch(r"[0-9a-f]{40}", args.base):
            raise ValueError("Expected a full SHA")
        repo = os.environ["GITHUB_REPOSITORY"]
        artifacts = json.loads(run(["gh", "api", f"repos/{repo}/actions/artifacts?name=TestImpactBaseline-{args.base}&per_page=100"], root))
        for artifact in artifacts["artifacts"]:
            if artifact["expired"] or artifact.get("workflow_run", {}).get("head_sha") != args.base:
                continue
            run_id = artifact["workflow_run"]["id"]
            workflow = json.loads(run(["gh", "api", f"repos/{repo}/actions/runs/{run_id}"], root))
            if (workflow["event"], workflow["head_branch"], workflow["conclusion"], workflow["path"]) != (
                    "workflow_dispatch", "main", "success", ".github/workflows/dotnet.yml"):
                continue
            # gh writes binary zip data directly to a file; never interpolate artifact data in a shell.
            with tempfile.TemporaryFile() as zipped:
                subprocess.run(["gh", "api", f"repos/{repo}/actions/artifacts/{artifact['id']}/zip"],
                               cwd=root, stdout=zipped, check=True, timeout=60)
                zipped.seek(0)
                with zipfile.ZipFile(zipped) as archive:
                    info = archive.getinfo("baseline.json")
                    if info.file_size > 100 * 1024 * 1024:
                        raise ValueError("Baseline exceeds size limit")
                    write_json(out / "baseline.json", json.loads(archive.read(info)))
            print(f"Downloaded exact-SHA main baseline from run {run_id}")
            return
        print("No matching baseline: the selector will report a full fallback.")
    except Exception as error:
        print(f"Baseline unavailable: {error}. The selector will report a full fallback.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("collect", "select", "download-baseline"):
        p = sub.add_parser(name)
        p.add_argument("--root", type=Path, default=HERE.parents[1])
        p.add_argument("--base", required=True)
        p.add_argument("--out", type=Path, required=True)
        if name != "download-baseline":
            p.add_argument("--project", action="append")
            p.add_argument("--tfm", default="net10.0")
            p.add_argument("--configuration", default="Debug")
        if name == "select":
            p.add_argument("--baseline", type=Path)
        if name == "collect":
            p.add_argument("--no-build", action="store_true")
            p.add_argument("--max-methods", type=int, default=25)
            p.add_argument("--budget-seconds", type=int, default=600)
            p.add_argument("--method-timeout", type=int, default=60)
            p.add_argument("--method-pattern", default="*")
            p.add_argument("--duration-results", type=Path, help="Full-run NUnit XML directory; ranking only")
    p = sub.add_parser("compare")
    p.add_argument("--report", type=Path, required=True)
    p.add_argument("--results", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "compare":
        report = read_json(args.report)
        try:
            report["comparison"] = compare(report, args.results)
        except Exception as error:
            report["comparison"] = {"status": "incomplete", "error": str(error)}
        write_json(args.report, report)
        render(report, args.report.with_suffix(".md"))
        return
    root, out = args.root.resolve(), args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    if args.command == "collect" and (args.max_methods < 1 or args.budget_seconds < 1 or args.method_timeout < 1):
        parser.error("Collection limits must be positive")
    {"collect": collect, "select": select, "download-baseline": download_baseline}[args.command](args, root, out)


if __name__ == "__main__":
    main()

