import contextlib
import io
import os
from pathlib import Path
import sys
import tempfile
import textwrap
import unittest
from unittest import mock

import smoke_test_output as smoke


WORKER_WITHOUT_FFMPEG = """
    import os, sys
    home = os.environ.get("BEUTL_HOME", "")
    first_start = os.path.isdir(home) and not os.listdir(home) and "LD_LIBRARY_PATH" not in os.environ
    sys.exit(2 if first_start and sys.argv[1:2] == ["--pipe"] and len(sys.argv) == 3 else 99)
"""

# Answers like the ModelContextProtocol SDK: a notification may come first, and the end of stdin ends the session.
MCP_HOST = """
    import json, os, sys
    request = json.loads(sys.stdin.readline())
    assert request["jsonrpc"] == "2.0" and request["method"] == "initialize", request
    assert os.path.isdir(os.environ["BEUTL_HOME"])
    print(json.dumps({"jsonrpc": "2.0", "method": "notifications/message", "params": {}}), flush=True)
    result = {"protocolVersion": "2025-06-18", "capabilities": {}, "serverInfo": {"name": "Fake", "version": "1.2"}}
    print(json.dumps({"jsonrpc": "2.0", "id": request["id"], "result": result}), flush=True)
    sys.stdin.read()
"""


def answering(response, after="sys.stdin.read()"):
    return f"""
    import json, sys
    request = json.loads(sys.stdin.readline())
    {response}
    {after}
"""


@unittest.skipIf(os.name == "nt", "the fake helpers are shell scripts")
class SmokeTestOutputTests(unittest.TestCase):
    def run_main(self, worker=WORKER_WITHOUT_FFMPEG, mcp=MCP_HOST, timeout=None):
        """Runs main against fake helpers and returns (exit code, stdout, stderr)."""
        with tempfile.TemporaryDirectory() as directory:
            for name, body in ((smoke.WORKER, worker), (smoke.MCP_HOST, mcp)):
                if body is None:
                    continue
                script = Path(directory, f"{name}.py")
                script.write_text(textwrap.dedent(body))
                launcher = Path(directory, name)
                launcher.write_text(f'#!/bin/sh\nexec "{sys.executable}" "{script}" "$@"\n')
                launcher.chmod(0o755)
            arguments = [directory] if timeout is None else ["--timeout", str(timeout), directory]
            stdout, stderr = io.StringIO(), io.StringIO()
            code = 0
            with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                try:
                    smoke.main(arguments)
                except SystemExit as exit:
                    code = exit.code
            return code, stdout.getvalue(), stderr.getvalue()

    def test_helpers_that_come_up_pass(self):
        with mock.patch.dict(os.environ, {"LD_LIBRARY_PATH": "/opt/ffmpeg/lib"}):
            code, stdout, _ = self.run_main()
        self.assertEqual(code, 0, stdout)
        self.assertEqual(stdout.splitlines(), [
            "Beutl.FFmpegWorker exited with 2 (FFmpeg was not found), as expected.",
            "Beutl.AgentToolkit.Mcp answered initialize: Fake 1.2, protocol 2025-06-18.",
        ])

    def test_worker_that_cannot_initialize_fails_with_its_output(self):
        code, stdout, stderr = self.run_main(worker="""
            print("[ffmpeg:Error] Failed to initialize FFmpeg: Could not load file or assembly 'FFmpeg.AutoGen'")
            print("::error::from the worker")
            raise SystemExit(4)
        """)
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.FFmpegWorker exited with 4 (FFmpeg initialization failed); "
                      "expected exit code 2 (FFmpeg was not found).\n", stdout)
        self.assertIn("\n    [ffmpeg:Error] Failed to initialize FFmpeg: Could not load file or assembly", stdout)
        # The helper's lines are indented, so none of them is read as a workflow command.
        self.assertIn("\n    ::error::from the worker\n", stdout)
        self.assertRegex(stderr, r"^1 of 2 helpers did not come up from \S+\.\n$")

    def test_worker_that_finds_ffmpeg_names_the_machine(self):
        code, stdout, _ = self.run_main(worker="raise SystemExit(3)")
        self.assertEqual(code, 1)
        self.assertIn("exited with 3 (it found FFmpeg on this machine", stdout)

    def test_crashed_worker_fails(self):
        code, stdout, _ = self.run_main(worker="import os; os.abort()")
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.FFmpegWorker was killed by SIGABRT; expected exit code 2", stdout)

    def test_hanging_worker_times_out(self):
        code, stdout, _ = self.run_main(worker="import time; time.sleep(30)", timeout=1)
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.FFmpegWorker did not exit within 1 s.", stdout)

    def test_error_response_fails(self):
        code, stdout, _ = self.run_main(mcp=answering(
            'print(json.dumps({"jsonrpc": "2.0", "id": request["id"], '
            '"error": {"code": -32603, "message": "boom"}}), flush=True)'))
        self.assertEqual(code, 1)
        self.assertIn('::error::Beutl.AgentToolkit.Mcp answered initialize with an error: '
                      '{"jsonrpc": "2.0", "id": 1, "error": {"code": -32603, "message": "boom"}}', stdout)

    def test_result_without_server_info_fails(self):
        code, stdout, _ = self.run_main(mcp=answering(
            'print(json.dumps({"jsonrpc": "2.0", "id": request["id"], "result": {}}), flush=True)'))
        self.assertEqual(code, 1)
        self.assertIn("answered initialize without a protocolVersion and serverInfo result", stdout)

    def test_log_line_on_stdout_fails(self):
        for line in ("info: Microsoft.Hosting.Lifetime[0]", '{"Level": "Information", "Message": "Started"}'):
            # The log line comes before a valid answer, which must not hide it.
            code, stdout, _ = self.run_main(mcp=answering(f"print({line!r}, flush=True)", after=f"""
    print(json.dumps({{"jsonrpc": "2.0", "id": request["id"], "result": {{
        "protocolVersion": "2025-06-18", "serverInfo": {{"name": "Fake"}}}}}}), flush=True)
    sys.stdin.read()"""))
            self.assertEqual(code, 1)
            self.assertIn(f"::error::Beutl.AgentToolkit.Mcp wrote a line that is not JSON-RPC to stdout: {line}\n",
                          stdout)

    def test_log_line_after_the_answer_fails(self):
        response = ('print(json.dumps({"jsonrpc": "2.0", "id": request["id"], "result": {'
                    '"protocolVersion": "2025-06-18", "serverInfo": {"name": "Fake"}}}), flush=True)')
        code, stdout, _ = self.run_main(mcp=answering(
            response, after='sys.stdin.read(); print("info: Application is shutting down...", flush=True)'))
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.AgentToolkit.Mcp wrote a line that is not JSON-RPC to stdout: "
                      "info: Application is shutting down...\n", stdout)

    def test_answer_without_the_json_rpc_version_fails(self):
        code, stdout, _ = self.run_main(mcp=answering(
            'print(json.dumps({"id": request["id"], "result": {"protocolVersion": "2025-06-18", '
            '"serverInfo": {"name": "Fake"}}}), flush=True)'))
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.AgentToolkit.Mcp wrote a line that is not JSON-RPC to stdout: "
                      '{"id": 1, "result":', stdout)

    def test_host_that_crashes_before_answering_fails_with_its_stderr(self):
        code, stdout, _ = self.run_main(mcp="""
            import os, sys
            sys.stderr.write("Unhandled exception. System.IO.FileNotFoundException: ModelContextProtocol.Core\\n")
            sys.stderr.flush()
            os.abort()
        """)
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.AgentToolkit.Mcp was killed by SIGABRT before it answered initialize.\n"
                      "    Unhandled exception. System.IO.FileNotFoundException: ModelContextProtocol.Core\n", stdout)

    def test_host_that_never_answers_times_out(self):
        code, stdout, _ = self.run_main(mcp="import time; time.sleep(30)", timeout=1)
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.AgentToolkit.Mcp did not answer initialize within 1 s.", stdout)

        # Notifications do not extend the deadline.
        code, stdout, _ = self.run_main(mcp=answering("""while True:
        print(json.dumps({"jsonrpc": "2.0", "method": "notifications/message", "params": {}}), flush=True)
        __import__("time").sleep(0.01)
        """), timeout=1)
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.AgentToolkit.Mcp did not answer initialize within 1 s.", stdout)

    def test_host_must_exit_cleanly_at_the_end_of_stdin(self):
        response = ('print(json.dumps({"jsonrpc": "2.0", "id": request["id"], "result": {'
                    '"protocolVersion": "2025-06-18", "serverInfo": {"name": "Fake"}}}), flush=True)')
        code, stdout, _ = self.run_main(mcp=answering(response, after="sys.stdin.read(); sys.exit(3)"))
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.AgentToolkit.Mcp answered initialize but exited with 3 "
                      "after its stdin closed.", stdout)

        code, stdout, _ = self.run_main(
            mcp=answering(response, after="sys.stdin.read(); import time; time.sleep(30)"), timeout=1)
        self.assertEqual(code, 1)
        self.assertIn("::error::Beutl.AgentToolkit.Mcp answered initialize but did not exit within 1 s "
                      "after its stdin closed.", stdout)

    def test_missing_helpers_are_named(self):
        code, stdout, _ = self.run_main(worker=None, mcp=None)
        self.assertEqual(code, 1)
        self.assertRegex(stdout, r"^::error::\S+ has no Beutl\.FFmpegWorker or Beutl\.AgentToolkit\.Mcp; "
                                 r"expected the publish output\.\n$")
        code, stdout, _ = self.run_main(mcp=None)
        self.assertEqual(code, 1)
        self.assertRegex(stdout, r"^::error::\S+ has no Beutl\.AgentToolkit\.Mcp; expected the publish output\.\n$")

    def test_timeout_must_be_positive(self):
        code, _, stderr = self.run_main(timeout=0)
        self.assertEqual(code, 2)
        self.assertIn("--timeout must be positive", stderr)


if __name__ == "__main__":
    unittest.main()
