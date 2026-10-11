"""Fail when a submodule is pinned to a commit outside the branch .gitmodules declares for it."""

import os
from pathlib import Path
import subprocess
import sys
import tempfile


def git(*args, cwd, check=True):
    # A remote that asks for credentials must fail instead of waiting for input.
    result = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True,
                            env=os.environ | {"GIT_TERMINAL_PROMPT": "0"})
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def submodules(root):
    if not (root / ".gitmodules").is_file():
        return
    paths = git("config", "--file", ".gitmodules", "--get-regexp", r"^submodule\..*\.path$", cwd=root, check=False)
    for line in paths.splitlines():
        key, path = line.split(" ", 1)
        name = key.removeprefix("submodule.").removesuffix(".path")
        url = git("config", "--file", ".gitmodules", f"submodule.{name}.url", cwd=root)
        branch = git("config", "--file", ".gitmodules", f"submodule.{name}.branch", cwd=root, check=False)
        # The superproject's tree records the pin as a gitlink: "160000 commit <sha>\t<path>".
        entry = git("ls-tree", "HEAD", "--", path, cwd=root).split()
        pinned = entry[2] if entry[:2] == ["160000", "commit"] else None
        yield path, url, branch, pinned


def problem(url, branch, pinned):
    if pinned is None:
        return "HEAD has no submodule commit at this path."
    # Without a declared branch, `git submodule update --remote` and Dependabot follow the remote HEAD.
    ref = f"refs/heads/{branch}" if branch else "HEAD"
    with tempfile.TemporaryDirectory() as repository:
        if not git("ls-remote", url, ref, cwd=repository):
            return f"{url} has no {ref}."
        git("init", "--quiet", "--bare", cwd=repository)
        git("fetch", "--quiet", "--no-tags", url, ref, cwd=repository)
        on_branch = subprocess.run(["git", "merge-base", "--is-ancestor", pinned, "FETCH_HEAD"],
                                   cwd=repository, capture_output=True).returncode == 0
    if not on_branch:
        return (f"the pinned commit {pinned} is not on {ref} of {url}. Merge it into that branch first, "
                "so the pin stays reachable and matches .gitmodules.")
    return None


def main(root):
    failed = False
    for path, url, branch, pinned in submodules(root):
        try:
            message = problem(url, branch, pinned)
        except RuntimeError as error:
            message = f"could not check the pin: {error}"
        if message:
            print(f"::error file=.gitmodules::{path}: {message}", flush=True)
            failed = True
        else:
            print(f"{path}: {pinned} is on {branch or 'HEAD'} of {url}", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(Path(__file__).resolve().parents[2]))
