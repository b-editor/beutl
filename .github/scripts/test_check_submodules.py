import contextlib
import io
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import check_submodules

# Keep the developer's Git configuration, such as commit signing, out of these repositories.
GIT_ENVIRONMENT = {
    "GIT_CONFIG_GLOBAL": os.devnull, "GIT_CONFIG_NOSYSTEM": "1",
    "GIT_AUTHOR_NAME": "CI", "GIT_AUTHOR_EMAIL": "ci@example.invalid",
    "GIT_COMMITTER_NAME": "CI", "GIT_COMMITTER_EMAIL": "ci@example.invalid",
}


def git(cwd, *args):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True, text=True).stdout.strip()


def commit(repository, message):
    git(repository, "commit", "--quiet", "--allow-empty", "-m", message)
    return git(repository, "rev-parse", "HEAD")


class CheckSubmodulesTests(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(os.environ, GIT_ENVIRONMENT)
        environment.start()
        self.addCleanup(environment.stop)
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        # Like the terminal fork in #2842: a feature branch is ahead of the declared "beutl" branch.
        self.remote = self.root / "remote"
        self.remote.mkdir()
        git(self.remote, "init", "--quiet", "--initial-branch=main")
        self.on_main = commit(self.remote, "upstream")
        git(self.remote, "switch", "--quiet", "--create", "beutl")
        self.on_beutl = commit(self.remote, "beutl")
        git(self.remote, "switch", "--quiet", "--create", "feature")
        self.feature_only = commit(self.remote, "feature")
        git(self.remote, "switch", "--quiet", "main")

    def check(self, pinned, branch="beutl"):
        project = Path(tempfile.mkdtemp(dir=self.root))
        git(project, "init", "--quiet")
        lines = ['[submodule "external/Dependency"]', "\tpath = external/Dependency", f"\turl = {self.remote}"]
        if branch:
            lines.append(f"\tbranch = {branch}")
        (project / ".gitmodules").write_text("\n".join(lines) + "\n")
        git(project, "add", ".gitmodules")
        git(project, "update-index", "--add", "--cacheinfo", f"160000,{pinned},external/Dependency")
        git(project, "commit", "--quiet", "-m", "Pin the dependency")
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            status = check_submodules.main(project)
        return status, output.getvalue()

    def test_a_pin_on_the_declared_branch_passes(self):
        for pinned in [self.on_beutl, self.on_main]:
            with self.subTest(pinned=pinned):
                self.assertEqual(self.check(pinned), (0, f"external/Dependency: {pinned} is on beutl of {self.remote}\n"))

    def test_a_pin_outside_the_declared_branch_fails(self):
        status, output = self.check(self.feature_only)
        self.assertEqual(status, 1)
        self.assertIn(f"::error file=.gitmodules::external/Dependency: the pinned commit {self.feature_only} "
                      "is not on refs/heads/beutl", output)

    def test_a_missing_branch_fails(self):
        status, output = self.check(self.on_beutl, branch="removed")
        self.assertEqual(status, 1)
        self.assertIn("has no refs/heads/removed", output)

    def test_without_a_declared_branch_the_remote_head_is_used(self):
        self.assertEqual(self.check(self.on_main, branch=None)[0], 0)
        self.assertEqual(self.check(self.on_beutl, branch=None)[0], 1)

    def test_a_repository_without_submodules_passes(self):
        self.assertEqual(check_submodules.main(self.remote), 0)

    def test_an_unreachable_remote_fails_with_the_git_error(self):
        self.remote.rename(self.root / "moved")
        status, output = self.check(self.on_beutl)
        self.assertEqual(status, 1)
        self.assertIn("could not check the pin: git ls-remote", output)


if __name__ == "__main__":
    unittest.main()
