#!/usr/bin/env python3
"""Start the helper executables of a published Beutl output and check that each one comes up.

Nuke merges the helpers into the app's publish output and skips every file the app already has.
A file the merge leaves out, or a copy the helper cannot load, can pass the build and fail only at runtime.

- Beutl.FFmpegWorker, with an empty BEUTL_HOME, must exit with 2 (FFmpeg not found), which the app
  answers by offering to install FFmpeg. Exit code 4 means the worker started but could not initialize.
- Beutl.AgentToolkit.Mcp must answer a JSON-RPC initialize request on stdio with a result, write
  nothing but JSON-RPC messages to stdout, and exit with 0 when its stdin closes, as it does when an
  agent stops it.
- Beutl.Agents.Worker must load its embedded Node runtime and bridge, answer JSON-RPC ping and
  shutdown requests on stdio, and exit with 0 when its stdin closes.
"""

import argparse
import json
import os
import queue
import signal
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path


WORKER = "Beutl.FFmpegWorker"
MCP_HOST = "Beutl.AgentToolkit.Mcp"
AGENT_WORKER = "Beutl.Agents.Worker"

# The exit codes of src/Beutl.FFmpegWorker/Program.cs.
FFMPEG_NOT_FOUND = 2
WORKER_EXIT_CODES = {
    1: "it rejected its arguments",
    FFMPEG_NOT_FOUND: "FFmpeg was not found",
    3: "it found FFmpeg on this machine and waited for a host; run the check where FFmpeg 8 is not installed",
    4: "FFmpeg initialization failed",
}

INITIALIZE = {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
        "protocolVersion": "2025-06-18",
        "capabilities": {},
        "clientInfo": {"name": "beutl-smoke-test", "version": "1.0.0"},
    },
}
PING = {"jsonrpc": "2.0", "id": 1, "method": "ping"}
SHUTDOWN = {"jsonrpc": "2.0", "id": 2, "method": "shutdown"}

# The longest a helper may take to shut down.
SHUTDOWN_SECONDS = 10


class Failure(Exception):
    """A helper did not come up. Carries the helper's output for the log."""

    def __init__(self, message, output=""):
        super().__init__(message)
        self.output = output


def executable(directory, name):
    return directory / (f"{name}.exe" if os.name == "nt" else name)


def helper_environment(home):
    """The environment of a first start: an empty profile and no library path the worker searches for FFmpeg."""
    environment = dict(os.environ)
    environment["BEUTL_HOME"] = str(home)
    environment.pop("LD_LIBRARY_PATH", None)
    return environment


def describe_exit(code, meanings=None):
    if code < 0:
        try:
            return f"was killed by {signal.Signals(-code).name}"
        except ValueError:
            return f"was killed by signal {-code}"
    meaning = (meanings or {}).get(code)
    return f"exited with {code} ({meaning})" if meaning else f"exited with {code}"


def decode(*streams):
    return "".join(stream.decode("utf-8", "replace") for stream in streams if stream)


def check_worker(path, home, timeout):
    """Starts the worker as the app does on a machine without FFmpeg and expects exit code 2."""
    try:
        result = subprocess.run(
            [str(path), "--pipe", f"beutl-smoke-test-{os.getpid()}"],
            env=helper_environment(home), cwd=home, stdin=subprocess.DEVNULL, capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired as error:
        raise Failure(f"{WORKER} did not exit within {timeout:g} s.", decode(error.stdout, error.stderr))
    except OSError as error:
        raise Failure(f"{WORKER} could not be started: {error}")
    if result.returncode != FFMPEG_NOT_FOUND:
        raise Failure(f"{WORKER} {describe_exit(result.returncode, WORKER_EXIT_CODES)}; "
                      f"expected exit code {FFMPEG_NOT_FOUND} ({WORKER_EXIT_CODES[FFMPEG_NOT_FOUND]}).",
                      decode(result.stdout, result.stderr))
    return f"{WORKER} {describe_exit(FFMPEG_NOT_FOUND, WORKER_EXIT_CODES)}, as expected."


def forward_lines(stream, lines):
    for line in iter(stream.readline, b""):
        lines.put(line)
    lines.put(None)


def is_json_rpc(message):
    """Whether a parsed stdout line is a JSON-RPC 2.0 request, notification or response."""
    if not isinstance(message, dict) or message.get("jsonrpc") != "2.0":
        return False
    if "method" in message:
        return isinstance(message["method"], str)
    return "id" in message and ("result" in message) != ("error" in message)


def reject_json_constant(constant):
    raise ValueError(f"Invalid JSON constant {constant}")


def parse_message(line, helper):
    try:
        message = json.loads(line.decode("utf-8"), parse_constant=reject_json_constant)
    except ValueError:
        message = None
    if not is_json_rpc(message):
        # A log line on stdout breaks protocol parsing.
        text = line.decode("utf-8", "replace").rstrip()
        raise Failure(f"{helper} wrote a line that is not JSON-RPC to stdout: {text[:200]}")
    return message


def request_response(process, lines, helper, request, timeout, grace, *, strict=False):
    """Sends an NDJSON request and returns the matching response within the deadline."""
    method = request["method"]
    try:
        process.stdin.write(json.dumps(request).encode("utf-8") + b"\n")
        process.stdin.flush()
    except OSError:
        pass  # If the process already exited, stdout EOF reports how.

    deadline = time.monotonic() + timeout
    while True:
        try:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise queue.Empty
            line = lines.get(timeout=remaining)
        except queue.Empty:
            raise Failure(f"{helper} did not answer {method} within {timeout:g} s.")
        if line is None:
            try:
                code = process.wait(timeout=grace)
            except subprocess.TimeoutExpired:
                raise Failure(f"{helper} closed its stdout before it answered {method}.")
            raise Failure(f"{helper} {describe_exit(code)} before it answered {method}.")
        message = parse_message(line, helper)
        if type(message.get("id")) is int and message["id"] == request["id"] and "method" not in message:
            if strict and not line.endswith(b"\n"):
                raise Failure(f"{helper} sent a {method} response without a newline.")
            return message
        if strict:
            raise Failure(f"{helper} wrote an unexpected JSON-RPC message while waiting for {method}: "
                          f"{json.dumps(message)[:500]}")


def check_initialize_response(message):
    summary = json.dumps(message)[:500]
    if "error" in message:
        raise Failure(f"{MCP_HOST} answered initialize with an error: {summary}")
    result = message.get("result")
    if not isinstance(result, dict) or not isinstance(result.get("protocolVersion"), str) \
            or not isinstance(result.get("serverInfo"), dict):
        raise Failure(f"{MCP_HOST} answered initialize without a protocolVersion and serverInfo result: {summary}")
    server = result["serverInfo"]
    return (f"{MCP_HOST} answered initialize: {server.get('name')} {server.get('version')}, "
            f"protocol {result['protocolVersion']}.")


def check_ping_response(message):
    result = message.get("result")
    if not isinstance(result, dict) or not isinstance(result.get("nodeVersion"), str) \
            or not result["nodeVersion"].strip() or type(result.get("processId")) is not int or result["processId"] <= 0:
        raise Failure(f"{AGENT_WORKER} answered ping without a valid nodeVersion and processId result: "
                      f"{json.dumps(message)[:500]}")
    return f"{AGENT_WORKER} answered ping: Node.js {result['nodeVersion']}, PID {result['processId']}."


def close_stdin(process):
    try:
        process.stdin.close()
    except OSError:
        pass  # It already exited.


def check_mcp_host(path, home, timeout):
    """Starts the MCP host as an agent does, expects a result for initialize and a clean exit at the end of stdin."""
    return check_rpc_helper(path, home, timeout, MCP_HOST, INITIALIZE, check_initialize_response)


def check_agent_worker(path, home, timeout):
    """Checks that the agent worker loads Node, answers ping, and shuts down cleanly."""
    return check_rpc_helper(path, home, timeout, AGENT_WORKER, PING, check_ping_response, shutdown=SHUTDOWN)


def check_rpc_helper(path, home, timeout, helper, request, check_response, *, shutdown=None):
    grace = min(SHUTDOWN_SECONDS, timeout)
    method = request["method"]
    with tempfile.TemporaryFile() as stderr:
        try:
            process = subprocess.Popen(
                [str(path)], env=helper_environment(home), cwd=home,
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=stderr)
        except OSError as error:
            raise Failure(f"{helper} could not be started: {error}")
        lines = queue.Queue()
        reader = threading.Thread(target=forward_lines, args=(process.stdout, lines), daemon=True)
        reader.start()
        try:
            summary = check_response(request_response(process, lines, helper, request, timeout, grace,
                                                       strict=shutdown is not None))
            if shutdown is not None:
                response = request_response(process, lines, helper, shutdown, grace, grace, strict=True)
                if "error" in response or response["result"] is not None:
                    raise Failure(f"{helper} did not acknowledge shutdown: {json.dumps(response)[:500]}")
            close_stdin(process)
            try:
                code = process.wait(timeout=grace)
            except subprocess.TimeoutExpired:
                raise Failure(f"{helper} answered {method} but did not exit within {grace:g} s "
                              "after its stdin closed.")
            if code != 0:
                raise Failure(f"{helper} answered {method} but {describe_exit(code)} after its stdin closed.")
            # What it wrote after the answer, up to its exit, reaches the client as well.
            reader.join(grace)
            if shutdown is not None and reader.is_alive():
                raise Failure(f"{helper} did not close stdout after shutdown.")
            while True:
                try:
                    line = lines.get_nowait()
                except queue.Empty:
                    break
                if line is None:
                    break
                message = parse_message(line, helper)
                if shutdown is not None:
                    raise Failure(f"{helper} wrote an unexpected JSON-RPC message after shutdown: "
                                  f"{json.dumps(message)[:500]}")
            return summary
        except Failure as failure:
            close_stdin(process)
            try:
                process.wait(timeout=grace)
            except subprocess.TimeoutExpired:
                process.kill()
            stderr.seek(0)
            failure.output = stderr.read().decode("utf-8", "replace")
            raise
        finally:
            if process.poll() is None:
                process.kill()
            process.wait()
            close_stdin(process)
            reader.join(grace)
            if not reader.is_alive():  # A child of the host could still hold stdout open.
                process.stdout.close()


def print_output(output):
    # Indented so that no line of the helper's output reads as a workflow command.
    for line in output.splitlines()[-200:]:
        print(f"    {line}")


def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--timeout", type=float, default=60,
                        help="seconds each helper may take to exit or to answer (default: 60)")
    parser.add_argument("directory", type=Path, help="publish output that holds the helpers, e.g. output/Beutl")
    args = parser.parse_args(arguments)
    if args.timeout <= 0:
        parser.error("--timeout must be positive")

    directory = args.directory.resolve()
    missing = [name for name in (WORKER, MCP_HOST, AGENT_WORKER) if not executable(directory, name).is_file()]
    if missing:
        print(f"::error::{args.directory} has no {' or '.join(missing)}; expected the publish output.")
        parser.exit(1)

    failures = 0
    checks = [(check_worker, executable(directory, WORKER)), (check_mcp_host, executable(directory, MCP_HOST)),
              (check_agent_worker, executable(directory, AGENT_WORKER))]
    for check, path in checks:
        # Each helper gets a profile of its own, empty as on a first start.
        with tempfile.TemporaryDirectory(prefix="beutl-home-") as home:
            try:
                print(check(path, Path(home), args.timeout))
            except Failure as failure:
                failures += 1
                print(f"::error::{failure}")
                print_output(failure.output)
    if failures:
        sys.stdout.flush()  # Keep the summary on stderr after the report above.
        parser.exit(1, f"{failures} of {len(checks)} helpers did not come up from {args.directory}.\n")


if __name__ == "__main__":
    main()
