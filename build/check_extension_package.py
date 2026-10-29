#!/usr/bin/env python3
"""Build and pack a minimal extension against the packed Beutl SDK, as an author would, and check its package.

No other job consumes the packed SDK, so packaging mistakes reached authors only after a release (#1648, #2730).
"""

import argparse
import os
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ElementTree
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape


NAME = "MinimalExtension"

# Packages that only run at build time; Beutl's installer cannot resolve them as dependencies.
BUILD_ONLY = {"beutl.engine.sourcegenerators"}

PROJECT = """<Project Sdk="Beutl.Extensibility.Sdk/{version}">
  <PropertyGroup>
    <Version>1.0.0</Version>
    <Authors>ci</Authors>
  </PropertyGroup>
</Project>
"""

# Resource comes from the source generator that the SDK references.
SOURCE = """using Beutl;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;

namespace MinimalExtension;

[Export]
public sealed class MinimalExtension : Extension;

public sealed partial class Dim : FilterEffect
{
    public Dim()
    {
        ScanProperties<Dim>();
    }

    public IExpressionProperty<float> Amount { get; } = Property.CreateAnimatable(50f);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Saturate(Amount / 100f);
        }
    }
}
"""

NUGET_CONFIG = """<configuration>
  <packageSources>
    <clear />
    <add key="artifacts" value="{artifacts}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"""


def check_package(nupkg, artifacts, version):
    """Returns the problems in the extension package at nupkg."""
    with zipfile.ZipFile(nupkg) as package:
        files = package.namelist()
        nuspec = ElementTree.fromstring(package.read(f"{NAME}.nuspec"))
    dependencies = [
        (element.get("id"), element.get("version"))
        for element in nuspec.iter()
        if element.tag.rsplit("}", 1)[-1] == "dependency"
    ]
    packed = {path.name.lower() for path in Path(artifacts).glob("*.nupkg")}

    problems = []
    if ("Beutl.Extensibility", version) not in dependencies:
        problems.append(f"no dependency on Beutl.Extensibility {version}, from which Beutl reads the target version")
    for package_id, dependency_version in dependencies:
        if package_id.lower() in BUILD_ONLY:
            problems.append(f"a dependency on {package_id}, which only runs at build time")
        elif f"{package_id}.{dependency_version}.nupkg".lower() not in packed:
            problems.append(f"a dependency on {package_id} {dependency_version}, which is not a packed Beutl package")
    libraries = [file for file in files if file.startswith("lib/") and not file.endswith("/")]
    if not any(file.endswith(f"/{NAME}.dll") for file in libraries):
        problems.append(f"no lib/*/{NAME}.dll")
    problems += [f"{file}, a copy of a library that the host provides" for file in libraries if not file.endswith(f"/{NAME}.dll")]
    return problems


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("artifacts", type=Path, help="the folder of the packed Beutl .nupkg files")
    parser.add_argument("--version", required=True, help="the version that the packages were packed with")
    args = parser.parse_args(argv)
    artifacts = args.artifacts.resolve()

    with tempfile.TemporaryDirectory() as work:
        project = Path(work) / NAME
        project.mkdir()
        (project / f"{NAME}.csproj").write_text(PROJECT.format(version=args.version))
        (project / f"{NAME}.cs").write_text(SOURCE)
        (project / "nuget.config").write_text(NUGET_CONFIG.format(artifacts=escape(str(artifacts), {'"': "&quot;"})))
        # A private package folder keeps packages of a reused version from coming out of the user's cache.
        environment = dict(os.environ, NUGET_PACKAGES=str(Path(work) / "packages"))
        output = Path(work) / "out"
        if subprocess.run(["dotnet", "pack", str(project), "-c", "Release", "-o", str(output)], env=environment).returncode != 0:
            print(f"error: dotnet pack failed for {NAME}", file=sys.stderr)
            return 1

        problems = check_package(output / f"{NAME}.1.0.0.nupkg", artifacts, args.version)

    for problem in problems:
        print(f"error: {NAME}.1.0.0.nupkg has {problem}", file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
