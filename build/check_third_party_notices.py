"""Check the runtime NuGet dependency graph of each distributed executable against its notices."""
import fnmatch
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = ("Beutl", "Beutl.FFmpegWorker", "Beutl.PackageTools.UI", "Beutl.ExceptionHandler", "Beutl.AgentToolkit.Mcp")


def runtime_packages(assets):
    packages = set()
    groups = assets["projectFileDependencyGroups"]
    rid_frameworks = {target.split("/")[0] for target in assets["targets"] if "/" in target}
    for target, libraries in assets["targets"].items():
        # A portable target can list native assets for Android and other platforms we never publish.
        # Prefer NuGet's RID-specific selections whenever the restore provides them.
        if target in rid_frameworks:
            continue
        framework = target.partition("/")[0]
        by_id = {key.split("/")[0].lower(): (key.split("/")[0], value) for key, value in libraries.items()}
        pending = [dep.split()[0].lower() for dep in groups[framework]]
        seen = set()
        while pending:
            package = pending.pop()
            if package in seen or package not in by_id:
                continue
            seen.add(package)
            # The CI solution restore includes this Debug-only tool in several app projects.
            if package == "avaloniaui.diagnosticssupport":
                continue
            name, library = by_id[package]
            pending.extend(dep.lower() for dep in library.get("dependencies", {}))
            if library["type"] == "package" and any(
                path.rsplit("/", 1)[-1] != "_._"
                for kind in ("runtime", "native", "runtimeTargets")
                for path in library.get(kind, {})
            ):
                packages.add(name)
    return packages


def notice_patterns(text):
    patterns = set()
    for line in text.splitlines():
        heading = re.fullmatch(r"## ([\w.\-]+)(?: \(.*\))?", line)
        if heading:
            name = heading[1].lower()
            patterns.update((name, name + ".*"))
        if line.startswith("Packages:"):
            patterns.update(p.lower() for p in re.findall(r"`([^`]+)`", line))
    return patterns


def main():
    patterns = notice_patterns((ROOT / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8-sig"))
    missing = {}
    packages = set()
    for project in PROJECTS:
        path = ROOT / "src" / project / "obj/project.assets.json"
        if not path.is_file():
            sys.exit(f"Missing {path}. Restore Beutl.slnx before checking notices.")
        shipped = runtime_packages(json.loads(path.read_bytes()))
        packages.update(shipped)
        for name in shipped:
            if not any(fnmatch.fnmatchcase(name.lower(), pattern) for pattern in patterns):
                missing.setdefault(name, []).append(project)
    if missing:
        for name, projects in sorted(missing.items()):
            print(f"Missing third-party notice for {name} ({', '.join(projects)})", file=sys.stderr)
        return 1
    print(f"Notices cover {len(packages)} runtime packages across {len(PROJECTS)} distributed projects.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
