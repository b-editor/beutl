"""The downloadable application assets, in release-note order."""
import argparse
import fnmatch
import json
import sys
from pathlib import Path
from typing import NamedTuple


class Asset(NamedTuple):
    rid: str
    kind: str
    filename: str
    standalone: bool = True
    registered: bool = True

    @property
    def os(self):
        return self.rid.split("_")[0]

    @property
    def arch(self):
        return self.rid.split("_")[1]

    def name(self, version, deb_version="*"):
        return self.filename.format(version=version, deb_version=deb_version)

    def release_file(self, assets, version):
        pattern = self.name(version)
        matches = [a for a in assets if fnmatch.fnmatchcase(a["name"], pattern)]
        if len(matches) > 1:
            raise ValueError(f"Ambiguous release asset {pattern}")
        return matches[0] if matches else None


ASSETS = (
    Asset("win_x64", "installer", "beutl-standalone-setup.exe"),
    Asset("win_arm64", "installer", "beutl-standalone-arm64-setup.exe"),
    Asset("osx_arm64", "app", "Beutl.osx_arm64.app.zip"),
    Asset("osx_x64", "app", "Beutl.osx_x64.app.zip"),
    Asset("linux_x64", "debian", "beutl_{deb_version}_amd64.deb"),
    Asset("linux_x64", "flatpak", "Beutl-{version}.flatpak"),
    Asset("linux_x64", "zip", "Beutl-linux-x64-standalone-{version}.zip"),
    Asset("linux_arm64", "debian", "beutl_{deb_version}_arm64.deb"),
    Asset("linux_arm64", "flatpak", "Beutl-arm64-{version}.flatpak"),
    Asset("linux_arm64", "zip", "Beutl-linux-arm64-standalone-{version}.zip"),
)


def matrices(version):
    # Windows installers also consume executable zips as intermediate artifacts.
    def rows(kind):
        return [a for a in ASSETS if a.kind == kind]

    return {
        "executables": list(dict.fromkeys(a.rid for a in ASSETS if a.kind in ("installer", "zip"))),
        "installers": [a.arch for a in rows("installer")],
        "macos": [a.rid for a in rows("app")],
        "debian": [{"arch": a.arch, "deb-arch": "amd64" if a.arch == "x64" else a.arch} for a in rows("debian")],
        "flatpak": [{"arch": a.arch, "flatpak-arch": "x86_64" if a.arch == "x64" else "aarch64",
                     "runner": "ubuntu-latest" if a.arch == "x64" else "ubuntu-24.04-arm",
                     "bundle": a.name(version)} for a in rows("flatpak")],
    }


def upload_paths(root, version, deb_version):
    paths = []
    for asset in ASSETS:
        name = asset.name(version, deb_version)
        matches = list(root.glob(f"*/{name}"))
        if len(matches) != 1:
            raise ValueError(f"Expected one downloaded {name}, found {len(matches)}")
        paths.append(matches[0])
    packages = sorted((root / "Packages").glob("*.nupkg"))
    if not packages:
        raise ValueError("Missing NuGet package artifacts")
    return paths + packages


def registrations(assets, version):
    result = []
    for asset in ASSETS:
        if not asset.registered:
            continue
        file = asset.release_file(assets, version)
        if not file or not file.get("browser_download_url"):
            raise ValueError(f"Missing release asset {asset.name(version)}")
        suffix = "-standalone" if asset.standalone and asset.kind in ("zip", "installer") else ""
        result.append(dict(id=f"beutl-{version}-{asset.os}-{asset.arch}-{asset.kind}{suffix}",
                           version=version, os=asset.os, arch=asset.arch, type=asset.kind,
                           standalone=asset.standalone, url=file["browser_download_url"]))
    return {"assets": result}


def links(assets, version, deb_version, downloads):
    groups = {}
    for asset in ASSETS:
        arch = "ARM64" if asset.arch == "arm64" else asset.arch
        group = {"win": f"Windows ({arch})", "osx": "macOS", "linux": f"Linux ({arch})"}[asset.os]
        label = {"installer": "Installer", "app": f"Zip ({asset.arch})", "debian": "Debian package",
                 "flatpak": "Flatpak", "zip": "Zip"}[asset.kind]
        # Older releases used a distribution suffix in their Debian filenames.
        file = asset.release_file(assets, version)
        name = file["name"] if file else asset.name(version, deb_version)
        groups.setdefault(group, []).append(f"  - [{label}]({downloads}/{name})")
    return "\n".join(line for group, items in groups.items() for line in [f"- {group}", *items])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("matrices").add_argument("version")
    uploads = sub.add_parser("uploads")
    uploads.add_argument("root", type=Path)
    uploads.add_argument("version")
    uploads.add_argument("deb_version")
    sub.add_parser("registrations").add_argument("version")
    sub.add_parser("flatpak").add_argument("version")
    notes = sub.add_parser("links")
    notes.add_argument("version")
    notes.add_argument("deb_version")
    notes.add_argument("downloads")
    args = parser.parse_args()
    if args.command == "matrices":
        print(json.dumps(matrices(args.version), separators=(",", ":")))
    elif args.command == "uploads":
        print(",".join(map(str, upload_paths(args.root, args.version, args.deb_version))))
    elif args.command == "flatpak":
        print(next(a for a in ASSETS if a.rid == "linux_x64" and a.kind == "flatpak").name(args.version))
    elif args.command == "registrations":
        print(json.dumps(registrations(json.load(sys.stdin), args.version), separators=(",", ":")))
    else:
        print(links(json.load(sys.stdin)["assets"], args.version, args.deb_version, args.downloads))
