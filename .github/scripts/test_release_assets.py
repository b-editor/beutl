import tempfile
import unittest
from pathlib import Path

from release_assets import ASSETS, links, matrices, registrations, upload_paths


class ReleaseAssetTests(unittest.TestCase):
    def test_builds_uploads_links_and_registrations_agree(self):
        version, deb_version = "2.0.0-preview.1", "2.0.0-12"
        plan = matrices(version)
        self.assertEqual(set(plan["executables"]), {"win_" + a for a in plan["installers"]} |
                         {"linux_" + p["arch"] for p in plan["debian"]})
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            package = root / "Packages" / "sdk.nupkg"
            package.parent.mkdir()
            package.touch()
            files = []
            for asset in ASSETS:
                path = root / asset.rid / asset.name(version, deb_version)
                path.parent.mkdir(exist_ok=True)
                path.touch()
                files.append({"name": path.name, "browser_download_url": "https://example.org/" + path.name})
            intermediate = root / "win_x64" / "Beutl-win-x64-standalone-test.zip"
            intermediate.touch()
            uploads = upload_paths(root, version, deb_version)
            self.assertEqual({p.name for p in uploads}, {f["name"] for f in files} | {package.name})
            urls = {a["url"] for a in registrations(files, version)["assets"]}
            self.assertEqual(len(urls), len([a for a in ASSETS if a.registered]))
            notes = links(files, version, deb_version, "https://example.org")
            for f in files:
                self.assertIn(f"({f['browser_download_url']})", notes)
            uploads[0].unlink()
            with self.assertRaises(ValueError):
                upload_paths(root, version, deb_version)
            with self.assertRaises(ValueError):
                registrations(files[1:], version)

    def test_legacy_debian_link_uses_the_published_name(self):
        name = "beutl_2.0.0-12_ubuntu22.04_amd64.deb"
        notes = links([{"name": name}], "2.0.0", "2.0.0-12", "https://example.org")
        self.assertIn(f"[Debian package](https://example.org/{name})", notes)
