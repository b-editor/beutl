"""List test outputs for tar, keeping only runtime assets compatible with the CI host."""

import argparse
import json
from pathlib import Path
import shutil
import sys

from ci_tests import test_projects


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


def prepare_outputs(root, compatible):
    outputs = [root / ".ci-tools"]
    outputs.extend(project.parent / "bin" / "Debug" / "net10.0" for project in test_projects(root))
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
    parser.add_argument("--runtime-graph", type=Path, required=True)
    parser.add_argument("--rid", required=True)
    arguments = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    graph = json.loads(arguments.runtime_graph.read_text())["runtimes"]
    outputs = prepare_outputs(root, compatible_rids(graph, arguments.rid))
    for output in outputs:
        sys.stdout.buffer.write(str(output.relative_to(root)).encode() + b"\0")
