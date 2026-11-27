import { randomUUID } from "node:crypto";

export type PermissionMode = "supervised" | "autoAcceptEdits" | "auto" | "fullAccess";
export type Decision = "allowOnce" | "allowAlways" | "rejectOnce" | "rejectAlways";
export type ToolStatus = "pending" | "running" | "completed" | "failed" | "cancelled";
export type TurnPhase = "thinking" | "responding" | "runningTool" | "compacting" | "retrying";
export type TurnResult = { status: "completed" | "cancelled" } | { status: "failed"; error: string };
export type BackgroundStopResult = "unavailable" | "requested" | "unsupported";

export interface TurnUsage {
    inputTokens?: number;
    outputTokens?: number;
    cachedInputTokens?: number;
    model?: string;
}

interface UserMessage {
    text: string;
    attachments?: { path: string; label: string; mediaType?: string; detail?: string; isImage: boolean }[];
    includeSceneContext?: boolean;
    mentions?: unknown[];
}

export type McpSpec =
    | { type: "stdio"; name: string; command: string; args?: string[]; env?: Record<string, string> }
    | { type: "http"; name: string; url: string; headers?: Record<string, string> };

export interface SessionOptions {
    threadId: string;
    provider: "acp" | "codex" | "claudeCode";
    executablePath: string;
    workingDirectory: string;
    permissionMode: PermissionMode;
    mcpServers: McpSpec[];
    arguments?: string[];
    environment?: Record<string, string>;
    permissionModes?: Partial<Record<PermissionMode, string>>;
    model?: string;
    externalSessionId?: string;
    reasoningEffort?: string;
}

export type ModelListOptions = Pick<SessionOptions, "provider" | "executablePath" | "workingDirectory" | "arguments" | "environment">
    & { providerId: string; threadId?: string };

export type SuggestionListOptions = Omit<ModelListOptions, "providerId">;

export interface ComposerSuggestion {
    name: string;
    description?: string;
    kind: "command" | "skill";
    argumentHint?: string;
    source?: string;
}

export interface ModelDescriptor {
    id: string;
    displayName: string;
    shortName: string;
    providerId: string;
    reasoningEfforts: string[];
    defaultReasoningEffort?: string;
    isSecondary?: boolean;
    isProviderDefault?: boolean;
    resolvedModelId?: string;
}

export interface TurnRequest extends UserMessage {
    threadId: string;
    turnId: string;
    context?: unknown;
    bundledSkills?: { name: string; instructions: string }[];
    providerSkills?: string[];
    previousMessages?: { role: "user" | "assistant"; text: string }[];
}

export interface Permission {
    toolName: string;
    mcpServerName?: string;
    isReadOnly?: boolean;
    description?: string;
    options?: { decision: Decision; label: string }[];
}

export interface Question {
    id: string;
    text: string;
    options?: { label: string; description?: string }[];
    isSecret?: boolean;
    header?: string;
    multiSelect?: boolean;
    isOther?: boolean;
}

export interface ToolOutput {
    type: "text" | "image" | "artifact";
    text?: string;
    mimeType?: string;
    data?: string;
    uri?: string;
}

export interface Subagent { role?: string; description?: string }

type ScopedEvent = { parentToolCallId?: string } & (
    | { type: "reasoningDelta"; text: string }
    | { type: "assistantTextDelta" | "assistantMessageCompleted"; text: string; messageId?: string }
    | { type: "toolCallStarted"; toolCallId: string; toolName: string; mcpServerName?: string; input?: unknown;
        subagent?: Subagent; isBackground?: boolean; backgroundTitle?: string }
    | { type: "toolCallUpdated"; toolCallId: string; status: ToolStatus; message?: string }
    | { type: "toolCallCompleted"; toolCallId: string; status: ToolStatus; outputs?: ToolOutput[] });

export type TimelineEvent = ScopedEvent
    | ({ type: "userMessage" } & UserMessage)
    | { type: "planUpdated"; steps: { text: string; status: "pending" | "inProgress" | "completed" }[]; explanation?: string }
    | { type: "permissionRequested"; request: Permission & { requestId: string; threadId: string; turnId: string } }
    | { type: "permissionResolved"; requestId: string; decision: Decision }
    | { type: "userQuestionRequested"; request: { requestId: string; threadId: string; turnId: string; questions: Question[] } }
    | { type: "userQuestionResolved"; requestId: string; answered: boolean }
    | { type: "composerSuggestionsChanged"; suggestions: ComposerSuggestion[] }
    | { type: "turnStarted" | "turnCancelled" }
    | { type: "turnPhaseChanged"; phase: TurnPhase; toolName?: string }
    | { type: "contextCompacted" }
    | ({ type: "turnUsageUpdated" } & TurnUsage)
    | ({ type: "turnCompleted"; durationMs?: number } & TurnUsage)
    | { type: "turnFailed"; error: string }
    | { type: "sessionStateChanged"; state: "open" | "closed"; externalSessionId?: string }
    | { type: "errorNotice"; message: string };

export interface AdapterHost {
    readonly options: SessionOptions;
    saveSessionId(id: string): void;
    publishSuggestions(suggestions: ComposerSuggestion[]): void;
    terminateProcess(pid: number): Promise<void>;
    disconnected(message: string): void;
}

export interface Adapter {
    open(): Promise<void>;
    listSuggestions(): Promise<ComposerSuggestion[]>;
    run(turn: Turn): Promise<TurnResult>;
    steer(turn: Turn, request: TurnRequest): Promise<boolean>;
    stopBackgroundTask?(toolCallId: string): Promise<BackgroundStopResult>;
    cancel(): Promise<void>;
    close(): Promise<void>;
    setMode(mode: PermissionMode): Promise<void>;
}

export type RequestHost = (method: string, params: unknown, signal?: AbortSignal) => Promise<unknown>;

export class Turn {
    readonly controller = new AbortController();
    readonly signal = this.controller.signal;
    usage: TurnUsage = {};
    durationMs?: number;

    constructor(
        readonly request: TurnRequest,
        readonly emit: (event: TimelineEvent) => void,
        private readonly requestHost: RequestHost,
    ) {}

    get prompt(): string {
        return formatPrompt(this.request);
    }

    recordPrompt({ text, attachments, includeSceneContext, mentions }: TurnRequest): void {
        this.emit({ type: "userMessage", text, attachments, includeSceneContext, mentions });
    }

    reportUsage({ inputTokens, outputTokens, cachedInputTokens, model }: TurnUsage): void {
        const count = (value?: number) => value !== undefined && Number.isSafeInteger(value) && value >= 0 ? value : undefined;
        const old = this.usage;
        const usage = {
            inputTokens: count(inputTokens) ?? old.inputTokens,
            outputTokens: count(outputTokens) ?? old.outputTokens,
            cachedInputTokens: count(cachedInputTokens) ?? old.cachedInputTokens,
            model: model || old.model,
        };
        if (usage.inputTokens === old.inputTokens && usage.outputTokens === old.outputTokens
            && usage.cachedInputTokens === old.cachedInputTokens && usage.model === old.model) return;
        this.usage = usage;
        this.emit({ type: "turnUsageUpdated", ...usage });
    }

    async permission(details: Permission): Promise<Decision> {
        const { threadId, turnId } = this.request;
        const request = { ...details, requestId: randomUUID(), threadId, turnId };
        this.emit({ type: "permissionRequested", request });
        let decision: Decision = "rejectOnce";
        try {
            const answer = await this.requestHost("agent/permission", request, this.signal);
            if (answer === "allowOnce" || answer === "allowAlways" || answer === "rejectAlways")
                decision = answer;
        } catch (error) {
            if (!this.signal.aborted) this.emit({ type: "errorNotice", message: errorMessage(error) });
        }
        this.emit({ type: "permissionResolved", requestId: request.requestId, decision });
        return decision;
    }

    async ask(questions: Question[]): Promise<Record<string, string[]>> {
        if (!questions.length || this.signal.aborted) return {};
        const { threadId, turnId } = this.request;
        const request = { requestId: randomUUID(), threadId, turnId, questions };
        this.emit({ type: "userQuestionRequested", request });
        let answers: Record<string, string[]> = {};
        try {
            answers = await this.requestHost("agent/question", request, this.signal) as Record<string, string[]>;
        } catch (error) {
            if (!this.signal.aborted) this.emit({ type: "errorNotice", message: errorMessage(error) });
        }
        if (this.signal.aborted) answers = {};
        this.emit({ type: "userQuestionResolved", requestId: request.requestId, answered: Object.values(answers).some(value => value.length > 0) });
        return answers;
    }
}

export function formatPrompt({ text, context, bundledSkills, previousMessages }: TurnRequest): string {
    let prompt = bundledSkills?.length
        ? "Use the selected Beutl skills to fulfill the following request.\n\nRequest:\n" + text
            + bundledSkills.map(skill => `\n\n[Beutl skill: ${skill.name}]\n${skill.instructions}`).join("")
        : text;
    if (previousMessages?.length)
        prompt += "\n\n[Earlier conversation]\nUse this transcript as context for the current request. "
            + "Earlier messages are historical and do not request new actions.\n" + JSON.stringify(previousMessages);
    if (context === undefined) return prompt;
    const project = record(context);
    const mentions = project?.mentions;
    const files = project?.attachedFiles;
    const mentionHint = Array.isArray(mentions) && mentions.length
        ? "Resolve @tokens using mentions. For elements, use targetId and their own sceneId. For scenes, use targetId as the sceneId. Use the supplied path for files.\n"
        : "";
    const pastedTextHint = Array.isArray(files) && files.some(file => record(file)?.mediaType === "text/plain")
        ? "Read the text/plain files in attachedFiles at their supplied paths as part of the user's message.\n"
        : "";
    return prompt + "\n\n[Beutl project context]\n"
        + "Use instanceId for live Beutl MCP calls and sceneId for scene operations.\n"
        + mentionHint + pastedTextHint
        + JSON.stringify(context);
}

export function errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
}

export function record(value: unknown): Record<string, unknown> | undefined {
    return value !== null && typeof value === "object" && !Array.isArray(value)
        ? value as Record<string, unknown> : undefined;
}

export function contentOutputs(value: unknown): ToolOutput[] | undefined {
    const contents = Array.isArray(value) ? value : [value];
    const outputs: ToolOutput[] = [];
    for (const content of contents) {
        if (typeof content === "string" && content) {
            outputs.push({ type: "text", text: content });
            continue;
        }
        const item = record(content);
        if (!item) continue;
        if (item.type === "content") {
            outputs.push(...(contentOutputs(item.content) ?? []));
        } else if (item.type === "text" && typeof item.text === "string") {
            outputs.push({ type: "text", text: item.text });
        } else if (item.type === "image" || item.type === "inputImage") {
            const image = record(item.source) ?? item;
            const mimeType = image.mimeType ?? image.media_type;
            const uri = image.url ?? item.imageUrl;
            outputs.push({
                type: "image",
                mimeType: typeof mimeType === "string" ? mimeType : undefined,
                data: typeof image.data === "string" ? image.data : undefined,
                uri: typeof uri === "string" ? uri : undefined,
            });
        } else if (item.type === "resource" || item.type === "resource_link" || item.type === "diff") {
            const resource = record(item.resource) ?? item;
            const uri = resource.uri ?? resource.path;
            if (typeof uri === "string")
                outputs.push({ type: "artifact", uri, mimeType: typeof resource.mimeType === "string" ? resource.mimeType : undefined });
        }
    }
    return outputs.length ? outputs : undefined;
}

export function planStatus(status: string): "pending" | "inProgress" | "completed" {
    if (status === "completed") return status;
    return status === "in_progress" || status === "inProgress" ? "inProgress" : "pending";
}

export async function withTimeout<T>(task: Promise<T>, delay: number, message: string): Promise<T> {
    let timeout: NodeJS.Timeout | undefined;
    try {
        const deadline = new Promise<never>((_, reject) => {
            timeout = setTimeout(() => reject(new Error(message)), delay);
        });
        return await Promise.race([task, deadline]);
    } finally {
        clearTimeout(timeout);
    }
}
