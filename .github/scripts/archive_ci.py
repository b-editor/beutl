"""Select and archive only the test outputs needed by each CI shard."""

import argparse
import json
from pathlib import Path
import shutil
import sys

from ci_tests import FILTERS, VALIDATION_GROUPS, assemblies, test_projects


PROJECT_GROUPS = {
    "Beutl.UnitTests": "unit",
    "Beutl.HeadlessUITests": "ui",
    "Beutl.Graphics3DTests": "graphics3d",
}


def archive_group(project):
    return PROJECT_GROUPS.get(project.stem, "other")


def artifact_pattern(root, suite):
    # Use the runner's assembly selection, including every child of a validation group.
    groups = sorted({archive_group(assembly)
                     for child in VALIDATION_GROUPS.get(suite, [suite])
                     for assembly in assemblies(root, child)})
    if not groups:
        raise ValueError(f"No test assemblies for {suite}")
    pattern = groups[0] if len(groups) == 1 else "{" + ",".join(groups) + "}"
    return f"TestBinaries-{pattern}.tar.zst"


def compatible_rids(graph, rid):
    compatible = set()

    def visit(current):
        if current in compatible:
            return
        compatible.add(current)
        for parent in graph[current].get("#import", []):
            visit(parent)

    visit(rid)
    return compatible


def prepare_outputs(root, compatible, group):
    outputs = [root / ".ci-tools"]
    outputs.extend(project.parent / "bin" / "Debug" / "net10.0" for project in test_projects(root)
                   if archive_group(project) == group)
    for output in outputs:
        if not output.is_dir():
            raise FileNotFoundError(f"Shared build is missing {output}")
        for runtimes in output.rglob("runtimes"):
            for runtime in runtimes.iterdir():
                if runtime.is_dir() and runtime.name not in compatible:
                    shutil.rmtree(runtime)
    return outputs


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    list_parser = commands.add_parser("list", help="list a group's output directories for tar")
    list_parser.add_argument("--runtime-graph", type=Path, required=True)
    list_parser.add_argument("--rid", required=True)
    list_parser.add_argument("--group", choices=[*PROJECT_GROUPS.values(), "other"], required=True)
    pattern_parser = commands.add_parser("pattern", help="select the artifacts needed by a test shard")
    pattern_parser.add_argument("suite", choices=[*FILTERS, *VALIDATION_GROUPS, "graphics3d", "other"])
    arguments = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    if arguments.command == "pattern":
        print(artifact_pattern(root, arguments.suite))
        raise SystemExit(0)
    graph = json.loads(arguments.runtime_graph.read_text())["runtimes"]
    outputs = prepare_outputs(root, compatible_rids(graph, arguments.rid), arguments.group)
    for output in outputs:
        sys.stdout.buffer.write(str(output.relative_to(root)).encode() + b"\0")
