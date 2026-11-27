import { CodexEvents, type ActiveCodexTurn } from "./codexEvents.js";
import { CodexRpc, CodexRpcError, type CodexInput, type CodexRequest, type CodexSkill, type CodexTurn } from "./codexProtocol.js";
import { ProviderProcess } from "./process.js";
import { type Adapter, type AdapterHost, type CompactionResult, type ComposerSuggestion, type Decision, type McpSpec, type ModelDescriptor, type Permission, type PermissionMode, type Turn, type TurnRequest, type TurnResult, CLIENT_INFO, formatPrompt, isAllowed, withTimeout } from "./protocol.js";

function tomlString(value: string): string {
    return JSON.stringify(value).replace(/\x7f/g, "\\u007f");
}

function tomlKey(value: string): string {
    return /^[A-Za-z0-9_-]+$/.test(value) ? value : tomlString(value);
}

function tomlTable(values: Record<string, string>): string {
    return "{ " + Object.entries(values).map(([key, value]) => tomlKey(key) + " = " + tomlString(value)).join(", ") + " }";
}

function buildMcpServerConfig(
    server: McpSpec,
    index: number,
    approvalMode: string,
    env: Record<string, string | undefined>,
): string {
    const fields = ["default_tools_approval_mode = " + tomlString(approvalMode)];
    if (server.type === "stdio") {
        fields.push("command = " + tomlString(server.command), "args = [" + (server.args ?? []).map(tomlString).join(", ") + "]");
        if (server.env) fields.push("env = " + tomlTable(server.env));
    } else {
        fields.push("url = " + tomlString(server.url));
        const headers = Object.fromEntries(Object.entries(server.headers ?? {}).map(([name, value], header) => {
            const variable = "BEUTL_AGENT_MCP_HEADER_" + index + "_" + header;
            env[variable] = value;
            return [name, variable];
        }));
        if (Object.keys(headers).length) fields.push("env_http_headers = " + tomlTable(headers));
    }
    return "mcp_servers." + tomlKey(server.name) + "={ " + fields.join(", ") + " }";
}

function approval(decision: Decision, choices?: unknown[] | null): "accept" | "acceptForSession" | "decline" | "cancel" {
    let answer: ReturnType<typeof approval> = decision === "allowAlways" ? "acceptForSession" : decision === "allowOnce" ? "accept" : "decline";
    if (choices && !choices.includes(answer))
        answer = answer === "acceptForSession" && choices.includes("accept") ? "accept" : "cancel";
    return answer;
}

function approvalOptions(choices?: unknown[] | null): Permission["options"] {
    if (!choices) return undefined;
    const options: NonNullable<Permission["options"]> = [];
    if (choices.includes("accept")) options.push({ decision: "allowOnce", label: "Accept" });
    if (choices.includes("acceptForSession")) options.push({ decision: "allowAlways", label: "Accept for session" });
    options.push({ decision: "rejectOnce", label: "Decline" });
    return options;
}

export class CodexAdapter implements Adapter {
    private process?: ProviderProcess;
    private rpc?: CodexRpc;
    private threadId?: string;
    private model?: string;
    private active?: ActiveCodexTurn;
    private skills?: Promise<CodexSkill[]>;
    private mode: PermissionMode;
    private readonly events = new CodexEvents();

    constructor(private readonly host: AdapterHost) {
        this.mode = host.options.permissionMode;
    }

    async open(): Promise<void> {
        const options = this.host.options;
        const rpc = await this.startServer();
        const policy = this.policy();
        const params = { cwd: options.workingDirectory, model: options.model, sandbox: policy.sandbox, approvalPolicy: policy.approvalPolicy };
        const response = options.externalSessionId
            ? await rpc.request("thread/resume", { ...params, threadId: options.externalSessionId, excludeTurns: true })
            : await rpc.request("thread/start", params);
        this.threadId = response.thread.id;
        this.model = response.model ?? options.model;
        this.host.saveSessionId(this.threadId);
    }

    async listModels(providerId: string): Promise<ModelDescriptor[]> {
        const rpc = await this.startServer();
        const models: ModelDescriptor[] = [];
        const seen = new Set<string>();
        let cursor: string | undefined;
        for (let page = 0; page < 10; page++) {
            const response = await rpc.request("model/list", { cursor, includeHidden: false });
            for (const model of response.data) {
                const id = model.model || model.id;
                if (!id || seen.has(id)) continue;
                seen.add(id);
                const displayName = (model.displayName || id).replace(/^(GPT[- ][\d.]+)-([A-Z])/i, "$1 $2");
                models.push({
                    id, providerId, displayName,
                    shortName: displayName.replace(/^GPT[- ]/i, ""),
                    reasoningEfforts: (model.supportedReasoningEfforts ?? []).map(option => option.reasoningEffort),
                    defaultReasoningEffort: model.defaultReasoningEffort,
                    isSecondary: models.length >= 4,
                    isProviderDefault: model.isDefault,
                    contextWindowTokens: model.contextWindow ?? undefined,
                });
            }
            if (!response.nextCursor || response.nextCursor === cursor || models.length >= 100)
                break;
            cursor = response.nextCursor;
        }
        return models;
    }

    async listSuggestions(): Promise<ComposerSuggestion[]> {
        const rpc = this.rpc ?? await this.startServer();
        return (await this.listSkills(rpc)).map(skill => ({
            name: skill.name, description: skill.shortDescription || skill.description, kind: "skill", source: skill.scope || "codex",
        }));
    }

    private listSkills(rpc: CodexRpc): Promise<CodexSkill[]> {
        return this.skills ??= withTimeout(rpc.request("skills/list", { cwds: [this.host.options.workingDirectory] }),
            3000, "Codex did not report its skills.").then(response =>
            response.data.flatMap(entry => entry.skills).filter(skill => skill.enabled !== false)).catch(() => []);
    }

    private async createInput(turn: Turn, rpc: CodexRpc): Promise<CodexInput[]> {
        const input: CodexInput[] = [{ type: "text", text: turn.prompt, text_elements: [] }];
        const names = new Set(Array.from(turn.request.text.matchAll(/(?:^|\s)[/$]([\p{L}\p{N}_.:-]+)(?=\s|$)/gu),
            match => match[1].toLowerCase()));
        for (const skill of turn.request.bundledSkills ?? []) names.delete(skill.name.toLowerCase());
        if (names.size) {
            for (const skill of await this.listSkills(rpc)) {
                if (skill.path && names.delete(skill.name.toLowerCase()))
                    input.push({ type: "skill", name: skill.name, path: skill.path });
            }
        }
        return input;
    }

    private buildServerArgs(env: Record<string, string | undefined>): string[] {
        const options = this.host.options;
        const args = [...(options.arguments ?? []), "app-server", "--listen", "stdio://"];
        const approvalMode = this.mode === "fullAccess" || this.mode === "auto" ? "approve" : "prompt";
        for (const [index, server] of options.mcpServers.entries()) {
            args.push("-c", buildMcpServerConfig(server, index, approvalMode, env));
        }
        return args;
    }

    private async startServer(): Promise<CodexRpc> {
        const options = this.host.options;
        const env: Record<string, string | undefined> = { ...process.env, ...options.environment };
        const args = this.buildServerArgs(env);
        const child = this.process = new ProviderProcess(this.host, options.executablePath, args, options.workingDirectory, env);
        const rpc = this.rpc = new CodexRpc(child, event => this.events.notify(event, {
            threadId: this.threadId,
            defaultModel: this.host.options.model ?? this.model,
            active: this.active,
        }), request => this.request(request), message => {
            this.active?.finished.reject(new Error(message));
            this.host.disconnected(message);
        });
        await rpc.request("initialize", {
            clientInfo: CLIENT_INFO,
            capabilities: { experimentalApi: true, requestAttestation: false },
        });
        rpc.initialized();
        return rpc;
    }

    async run(turn: Turn): Promise<TurnResult> {
        const { rpc, threadId } = this;
        if (!rpc || !threadId) throw new Error("Codex thread is closed.");
        const { approvalPolicy, sandboxPolicy } = this.policy();
        return await this.runCore(turn, async () => {
            const input = await this.createInput(turn, rpc);
            turn.signal.throwIfAborted();
            return rpc.request("turn/start", {
                threadId,
                cwd: this.host.options.workingDirectory,
                input,
                model: this.host.options.model,
                effort: this.host.options.reasoningEffort,
                approvalPolicy,
                sandboxPolicy,
            });
        });
    }

    async compact(turn: Turn): Promise<CompactionResult> {
        const { rpc, threadId } = this;
        if (!rpc || !threadId) throw new Error("Codex thread is closed.");
        try {
            return await this.runCore(turn, async () => {
                turn.signal.throwIfAborted();
                await rpc.request("thread/compact/start", { threadId });
                // The RPC acknowledges the command; turn notifications identify its execution.
                return undefined;
            });
        } catch (error) {
            if (error instanceof CodexRpcError && (error.code === -32601
                || /\b(?:(?:unknown|unsupported|unrecognized) (?:method|variant)|method not found)\b/i.test(error.message)))
                return { status: "unsupported" };
            throw error;
        }
    }

    private async runCore(turn: Turn, start: () => Promise<{ turn: CodexTurn } | undefined>): Promise<TurnResult> {
        const active: ActiveCodexTurn = this.active = { turn, started: Promise.resolve().then(start), finished: Promise.withResolvers<TurnResult>(),
            childThreads: new Map(), collabParents: new Map(), startedCollab: new Set() };
        try {
            const identify = active.started.then(response => { active.externalId ??= response?.turn.id; });
            const [, result] = await Promise.all([identify, active.finished.promise]);
            return result;
        } finally {
            this.active = undefined;
        }
    }

    async steer(turn: Turn, request: TurnRequest): Promise<boolean> {
        const { rpc, threadId, active } = this;
        if (!rpc || !threadId || active?.turn !== turn) return false;
        try {
            const expectedTurnId = active.externalId ?? (await active.started)?.turn.id;
            if (!expectedTurnId || this.active !== active || turn.signal.aborted) return false;
            await withTimeout(rpc.request("turn/steer", {
                threadId, expectedTurnId, input: [{ type: "text", text: formatPrompt(request), text_elements: [] }],
            }), 4000, "Codex did not acknowledge the steer request.");
            return true;
        } catch (error) {
            if (this.active !== active || turn.signal.aborted) return false;
            throw error;
        }
    }

    async cancel(): Promise<void> {
        const active = this.active;
        if (!this.rpc || !this.threadId || !active) return;
        const turnId = active.externalId ?? (await active.started)?.turn.id ?? active.externalId;
        if (turnId) await this.rpc.request("turn/interrupt", { threadId: this.threadId, turnId });
    }

    async close(): Promise<void> {
        await this.process?.stop();
    }

    async setMode(mode: PermissionMode): Promise<void> {
        this.mode = mode;
    }

    private policy() {
        if (this.mode === "fullAccess")
            return { approvalPolicy: "never", sandbox: "danger-full-access", sandboxPolicy: { type: "dangerFullAccess" } } as const;
        return {
            approvalPolicy: "on-request",
            sandbox: "workspace-write",
            sandboxPolicy: {
                type: "workspaceWrite",
                writableRoots: [],
                networkAccess: false,
                excludeTmpdirEnvVar: true,
                excludeSlashTmp: true,
            },
        } as const;
    }

    private async request({ method, params }: CodexRequest): Promise<unknown> {
        const active = this.active;
        const turn = active && params?.threadId === this.threadId
            && (!active.externalId || params.turnId === active.externalId) ? active.turn : undefined;
        switch (method) {
            case "item/commandExecution/requestApproval": {
                const decision = turn ? await turn.permission({
                    toolName: "Codex command", description: params.command ?? params.reason ?? undefined,
                    options: approvalOptions(params.availableDecisions),
                }) : "rejectOnce";
                return { decision: turn?.signal.aborted ? "cancel" : approval(decision, params.availableDecisions) };
            }
            case "item/fileChange/requestApproval": {
                if (this.mode === "autoAcceptEdits")
                    return { decision: "accept" };
                const decision = turn ? await turn.permission({ toolName: "Codex file change", description: params.reason ?? undefined }) : "rejectOnce";
                return { decision: turn?.signal.aborted ? "cancel" : approval(decision) };
            }
            case "item/permissions/requestApproval": {
                const decision = turn ? await turn.permission({ toolName: "Codex permissions", description: params.reason ?? undefined }) : "rejectOnce";
                const granted = !turn?.signal.aborted && isAllowed(decision);
                return { permissions: granted ? params.permissions : {}, scope: granted && decision === "allowAlways" ? "session" : "turn" };
            }
            case "item/tool/requestUserInput": {
                const answers = turn ? await turn.ask(params.questions.map(question => ({
                    id: question.id, text: question.question, header: question.header, isSecret: question.isSecret,
                    isOther: question.isOther, options: question.options ?? undefined,
                }))) : {};
                return { answers: Object.fromEntries(Object.entries(answers).map(([id, answers]) => [id, { answers }])) };
            }
            default:
                throw new CodexRpcError(-32601, "Unsupported Codex app-server request: " + method);
        }
    }
}
