import type { ChildProcessByStdio } from "node:child_process";
import type { Readable, Writable } from "node:stream";
import spawn from "cross-spawn";
import { type AdapterHost, errorMessage, withTimeout } from "./protocol.js";

export class ProviderProcess {
    readonly child: ChildProcessByStdio<Writable, Readable, Readable>;
    readonly closed: Promise<string>;
    private stopping?: Promise<void>;

    constructor(
        private readonly host: AdapterHost,
        command: string,
        args: string[],
        cwd = host.options.workingDirectory,
        env = { ...process.env, ...host.options.environment },
    ) {
        const child = this.child = spawn.spawn(command, args, { cwd, env, stdio: "pipe", windowsHide: true, shell: false });
        child.stderr.on("data", (data: Buffer) => process.stderr.write(data));
        child.stdin.on("error", () => { /* a closed child is reported through closed */ });
        this.closed = new Promise(resolve => {
            child.once("error", error => resolve(error.message));
            child.once("exit", (code, signal) => resolve("Agent process exited (" + (signal ?? code ?? "unknown") + ")."));
        });
    }

    stop(): Promise<void> {
        return this.stopping ??= this.stopCore();
    }

    private async stopCore(): Promise<void> {
        const child = this.child;
        try {
            if (child.pid !== undefined && child.exitCode === null && child.signalCode === null) {
                try {
                    await this.host.terminateProcess(child.pid);
                } finally {
                    if (child.exitCode === null && child.signalCode === null) child.kill("SIGKILL");
                }
            }
            await withTimeout(this.closed, 1000, "Agent process did not exit.");
        } catch (error) {
            console.error(errorMessage(error));
        } finally {
            child.stdin.destroy();
            child.stdout.destroy();
            child.stderr.destroy();
        }
    }
}
