import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { appendFileSync } from "node:fs";
import { createInterface } from "node:readline";
import { randomUUID } from "node:crypto";
import { resolve } from "node:path";

const [provider, ...args] = process.argv.slice(2);
const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });
const send = value => process.stdout.write(JSON.stringify(value) + "\n");
const mark = value => appendFileSync("mock-agent.log", value + "\n");

function captureMentionPrompt(prompt) {
    if (typeof prompt !== "string" || !prompt.startsWith("Review @Opening @Title @clip.mp4\n")) return false;
    mark("mention-prompt=" + JSON.stringify(prompt));
    return true;
}
const message = (method, params, id) => send({ ...(provider === "acp" ? { jsonrpc: "2.0" } : {}), id, method, params });
const reply = (id, result) => send({ ...(provider === "acp" ? { jsonrpc: "2.0" } : {}), id, result });
const sessionId = provider + "-saved-session";
const discovering = process.env.BEUTL_MOCK_MODEL_DISCOVERY === "1";
let active;
let turnNumber = 0;
let denyClaudeTool = false;
const acpConfig = [
    { id: "model-id", name: "Model", category: "model", type: "select", currentValue: "draft", options: [
        { group: "edit", name: "Editing", options: [{ value: "draft", name: "Draft" }, { value: "cinema", name: "Cinema" }] },
    ] },
    { id: "effort-id", name: "Effort", category: "thought_level", type: "select", currentValue: "fast", options: [
        { value: "fast", name: "Fast" }, { value: "deep", name: "Deep" },
    ] },
];

function assertMcp(servers) {
    assert.deepEqual(servers.find(server => server.name === "beutl"), {
        name: "beutl", command: "tool-host", args: ["--stdio"], env: [{ name: "PROFILE", value: "test" }],
    });
    assert.deepEqual(servers.find(server => server.name === "external"), {
        name: "external", type: "http", url: "https://example.test/mcp", headers: [{ name: "Authorization", value: "test-token" }],
    });
}

function acp(frame) {
    assert.equal(frame.jsonrpc, "2.0");
    const { method, id, params, result } = frame;
    const update = value => message("session/update", { sessionId, update: value });
    const text = (text, messageId) =>
        update({ sessionUpdate: "agent_message_chunk", content: { type: "text", text }, messageId });
    if (id === "acp-permission" && active.waitForPermission) {
        assert.deepEqual(result, { outcome: { outcome: "cancelled" } });
        return;
    }
    if (id === "acp-permission") {
        assert.deepEqual(result, { outcome: { outcome: "selected", optionId: "approved-once" } });
        update({
            sessionUpdate: "tool_call_update", toolCallId: "tool-1", status: "completed",
            content: [{ type: "content", content: { type: "image", mimeType: "image/png", data: "aW1hZ2U=" } }],
        });
        reply(active.id, { stopReason: "end_turn" });
        return;
    }
    switch (method) {
        case "initialize":
            assert.equal(params.protocolVersion, 1);
            assert.equal(params.clientInfo.name, "beutl");
            process.stderr.write("mock ACP initialized\n");
            mark("acp initialize");
            reply(id, { protocolVersion: 1, agentCapabilities: { loadSession: true, mcpCapabilities: { http: true } } });
            break;
        case "session/new":
        case "session/load": {
            if (discovering) assert.deepEqual(params.mcpServers, []);
            else assertMcp(params.mcpServers);
            mark(method);
            if (method === "session/load") {
                assert.equal(params.sessionId, sessionId);
                text("replayed history");
            }
            update({ sessionUpdate: "available_commands_update", availableCommands: [
                { name: "build-plan", description: "Draft an edit plan", input: { hint: "<scene>" } },
            ] });
            reply(id, {
                ...(method === "session/new" ? { sessionId } : {}),
                modes: { currentModeId: "default", availableModes: [{ id: "default", name: "Default" }, { id: "ask", name: "Ask" }] },
                configOptions: acpConfig,
            });
            break;
        }
        case "session/set_mode":
            assert.equal(params.modeId, "ask");
            mark(method);
            reply(id, {});
            break;
        case "session/set_config_option": {
            const option = acpConfig.find(option => option.id === params.configId);
            assert.ok(option);
            option.currentValue = params.value;
            mark(option.id + "=" + params.value);
            reply(id, { configOptions: acpConfig });
            break;
        }
        case "session/prompt": {
            assert.equal(discovering, false);
            assert.equal(params.sessionId, sessionId);
            const prompt = params.prompt[0].text;
            active = { id, waitForPermission: prompt === "permission pending" };
            if (captureMentionPrompt(prompt)) {
                reply(id, { stopReason: "end_turn" });
                break;
            }
            if (prompt === "metadata") {
                update({ sessionUpdate: "tool_call", toolCallId: "coordinator", title: "Render previews", kind: "other" });
                update({ sessionUpdate: "tool_call", toolCallId: "background", title: "Render frames", kind: "execute", status: "in_progress",
                    rawInput: { command: "render-preview" }, _meta: { parentToolCallId: "coordinator", isBackground: true } });
                update({ sessionUpdate: "tool_call_update", toolCallId: "background", status: "completed", rawOutput: "Finished",
                    _meta: { unrelated: "value" } });
                update({ sessionUpdate: "available_commands_update", availableCommands: [{ name: "render", description: "Render previews" }] });
                reply(id, { stopReason: "end_turn" });
                break;
            }
            if (prompt === "wait" || prompt === "crash") {
                if (prompt === "crash") {
                    const descendant = spawn(process.execPath, ["-e", "setInterval(() => {}, 1000)"], { stdio: "ignore", windowsHide: true });
                    mark("descendant=" + descendant.pid);
                }
                mark("provider=" + process.pid);
                text("waiting");
                break;
            }
            text("hello ", "acp-message");
            text("from ACP", "acp-message");
            update({ sessionUpdate: "agent_thought_chunk", content: { type: "text", text: "thinking" } });
            update({ sessionUpdate: "plan", entries: [{ content: "Run a tool", priority: "medium", status: "in_progress" }] });
            update({ sessionUpdate: "tool_call", toolCallId: "tool-1", title: "Edit", name: "mcp__beutl__edit", kind: "edit", status: "in_progress" });
            message("session/request_permission", {
                sessionId,
                toolCall: { toolCallId: "tool-1" },
                options: [{ optionId: "approved-once", name: "Allow", kind: "allow_once" }, { optionId: "denied", name: "Reject", kind: "reject_once" }],
            }, "acp-permission");
            break;
        }
        case "session/cancel":
            assert.equal(params.sessionId, sessionId);
            mark(method);
            reply(active.id, { stopReason: "cancelled" });
            break;
        default:
            throw new Error("Unexpected ACP frame " + JSON.stringify(frame));
    }
}

function codex(frame) {
    assert.equal(frame.jsonrpc, undefined);
    const { method, id, params, result } = frame;
    const notify = (method, params) => message(method, { threadId: sessionId, turnId: active.turn.id, ...params });
    if (id === "codex-permission") {
        assert.deepEqual(result, { decision: "acceptForSession" });
        message("item/tool/requestUserInput", {
            threadId: sessionId, turnId: active.turn.id, itemId: "question-item",
            questions: [
                { id: "choice", header: "Value", question: "Choose a value", isOther: false,
                    options: [{ label: "A", description: "First" }, { label: "B", description: "Second" }] },
                { id: "secret", header: "Access", question: "Enter the code", isSecret: true, isOther: true, options: null },
            ],
        }, "codex-question");
        return;
    }
    if (id === "codex-question") {
        assert.deepEqual(result, { answers: { choice: { answers: ["A"] }, secret: { answers: ["mock-code"] } } });
        notify("item/commandExecution/outputDelta", { itemId: "command-item", delta: "out" });
        notify("item/completed", { item: { type: "commandExecution", id: "command-item", status: "completed", aggregatedOutput: "out" } });
        notify("item/completed", { item: { type: "agentMessage", id: "message-item", text: "hello from Codex" } });
        notify("turn/completed", { turn: { ...active.turn, status: "completed" } });
        return;
    }
    switch (method) {
        case "initialize":
            assert.equal(args[0], "app-server");
            assert.ok(args.includes("stdio://"));
            if (!discovering) {
                assert.ok(args.some(arg => arg.startsWith("mcp_servers.beutl=") && arg.includes('command = "tool-host"')));
                assert.ok(args.some(arg => arg.startsWith("mcp_servers.external=") && arg.includes("env_http_headers")));
                assert.ok(!args.some(arg => arg.includes("test-token")));
                assert.equal(process.env.BEUTL_AGENT_MCP_HEADER_1_0, "test-token");
            }
            mark("codex initialize");
            reply(id, {});
            break;
        case "initialized":
            mark(method);
            break;
        case "thread/start":
        case "thread/resume":
            assert.equal(discovering, false);
            assert.equal(params.approvalPolicy, "on-request");
            assert.equal(params.sandbox, "workspace-write");
            if (method === "thread/resume") {
                assert.equal(params.threadId, sessionId);
                assert.equal(params.excludeTurns, true);
            }
            mark(method);
            reply(id, { thread: { id: sessionId } });
            break;
        case "model/list": {
            assert.equal(params.includeHidden, false);
            const secondPage = params.cursor === "next";
            mark(method);
            const model = secondPage ? "gpt-6.2-astra" : "gpt-6.2-sol";
            reply(id, { nextCursor: secondPage ? null : "next", data: [{
                id: model, model, displayName: secondPage ? "GPT-6.2-Astra" : "GPT-6.2-Sol",
                supportedReasoningEfforts: [{ reasoningEffort: "low" }, { reasoningEffort: "max" }],
                defaultReasoningEffort: "low", isDefault: !secondPage,
            }] });
            break;
        }
        case "skills/list":
            assert.deepEqual(params.cwds, [process.cwd()]);
            mark(method);
            if (process.env.BEUTL_MOCK_UNSUPPORTED_SKILLS === "1") {
                send({ id, error: { code: -32601, message: "Method not found" } });
            } else {
                reply(id, { data: [{ cwd: process.cwd(), skills: [
                    { name: "editing-notes", description: "Follow the scene editing guide", path: resolve("mock-skill/SKILL.md"), scope: "repo", enabled: true },
                    { name: "disabled-skill", description: "Disabled", path: resolve("disabled-skill/SKILL.md"), enabled: false },
                ] }] });
            }
            break;
        case "turn/start": {
            assert.equal(params.threadId, sessionId);
            assert.equal(params.approvalPolicy, "on-request");
            assert.deepEqual(params.sandboxPolicy, {
                type: "workspaceWrite", writableRoots: [], networkAccess: false,
                excludeTmpdirEnvVar: true, excludeSlashTmp: true,
            });
            assert.equal(params.input[0].type, "text");
            assert.deepEqual(params.input[0].text_elements, []);
            if (params.effort) mark("codex effort=" + params.effort);
            active = { turn: { id: "external-turn-" + ++turnNumber, status: "inProgress" } };
            notify("turn/started", { turn: active.turn });
            reply(id, { turn: active.turn });
            if (captureMentionPrompt(params.input[0].text)) {
                notify("turn/completed", { turn: { ...active.turn, status: "completed" } });
                break;
            }
            if (params.input[0].text === "wait") {
                notify("item/agentMessage/delta", { itemId: "waiting", delta: "waiting" });
                break;
            }
            if (params.input[0].text === "$editing-notes inspect the scene") {
                if (process.env.BEUTL_MOCK_UNSUPPORTED_SKILLS === "1") assert.equal(params.input.length, 1);
                else assert.deepEqual(params.input[1], { type: "skill", name: "editing-notes", path: resolve("mock-skill/SKILL.md") });
                const started = { type: "collabAgentToolCall", id: "collab-spawn", tool: "spawnAgent", senderThreadId: sessionId,
                    receiverThreadIds: ["child-thread"], status: "inProgress", prompt: "Review the video", agentType: "reviewer",
                    agentsStates: { "child-thread": { status: "running" } } };
                notify("item/started", { item: started });
                notify("item/completed", { item: { ...started, status: "completed" } });
                const child = { threadId: "child-thread", turnId: "child-turn" };
                notify("item/reasoning/summaryTextDelta", { ...child, itemId: "worker-thinking", delta: "Checking the opening frame" });
                notify("item/started", { ...child, item: { type: "commandExecution", id: "child-command", command: "render-preview",
                    status: "inProgress", isBackground: true } });
                notify("item/commandExecution/outputDelta", { ...child, itemId: "child-command", delta: "Rendering" });
                notify("item/completed", { ...child, item: { type: "commandExecution", id: "child-command", command: "render-preview",
                    status: "completed", aggregatedOutput: "Rendered", isBackground: true } });
                notify("item/agentMessage/delta", { ...child, itemId: "shared", delta: "Subagent output" });
                notify("item/completed", { ...child, item: { id: "shared", type: "agentMessage", text: "Subagent output" } });
                notify("item/completed", { item: { ...started, id: "collab-wait", tool: "wait", status: "completed",
                    agentsStates: { "child-thread": { status: "completed", message: "Review finished" } } } });
                notify("item/completed", { item: { id: "shared", type: "agentMessage", text: "Main output" } });
                notify("turn/completed", { turn: { ...active.turn, status: "completed" } });
                break;
            }
            notify("item/agentMessage/delta", { itemId: "message-item", delta: "hello from Codex" });
            notify("item/started", { item: { type: "commandExecution", id: "command-item", command: "sample", status: "inProgress" } });
            message("item/commandExecution/requestApproval", {
                threadId: sessionId, turnId: active.turn.id, itemId: "command-item", command: "sample",
                availableDecisions: ["accept", "acceptForSession", "decline", "cancel"],
            }, "codex-permission");
            break;
        }
        case "turn/steer":
            assert.equal(params.threadId, sessionId);
            assert.equal(params.expectedTurnId, active.turn.id);
            assert.deepEqual(params.input, [{ type: "text", text: "Focus on the opening", text_elements: [] }]);
            mark(method);
            reply(id, { turnId: active.turn.id });
            notify("item/agentMessage/delta", { itemId: "steered", delta: "Following the revised prompt" });
            notify("turn/completed", { turn: { ...active.turn, status: "completed" } });
            break;
        case "turn/interrupt":
            assert.equal(params.threadId, sessionId);
            assert.equal(params.turnId, active.turn.id);
            mark(method);
            reply(id, {});
            notify("turn/completed", { turn: { ...active.turn, status: "interrupted" } });
            break;
        default:
            throw new Error("Unexpected Codex frame " + JSON.stringify(frame));
    }
}

const claudeSession = "be530115-3fe5-4006-a233-8631ece46abc";
const claudeMessage = (type, value) => send({ type, uuid: randomUUID(), session_id: claudeSession, ...value });
const claudeCommands = [
    { name: "compact", description: "Summarize the conversation", argumentHint: "<focus>", builtin: true },
    { name: "shot-review", description: "Review the timeline shots", argumentHint: "<scene>" },
];
const claudeQuestions = [
    { question: "Which sections should be included?", header: "Sections", multiSelect: true,
        options: [{ label: "Intro", description: "Open with a title" }, { label: "Credits", description: "Show the credits" }] },
    { question: "What should the title be?", header: "Title", multiSelect: false,
        options: [{ label: "Untitled", description: "Use a working title" }] },
];
let claudePreToolHook;
let awaitingClaudeSteer;
function claude(frame) {
    if (frame.type === "control_request") {
        const { request_id, request } = frame;
        assert.equal(request.subtype, "initialize");
        if (!discovering) {
            assert.ok(args.includes("--include-partial-messages"));
            assert.equal(request.mcpServers.beutl.command, "tool-host");
            assert.equal(request.mcpServers.external.headers.Authorization, "test-token");
        }
        assert.ok(!args.some(arg => arg.includes("test-token")));
        assert.ok(args.includes("--strict-mcp-config"));
        assert.match(args.join(" "), /--setting-sources[ =]user,project/);
        assert.ok(process.env.CLAUDE_CONFIG_DIR);
        claudePreToolHook = request.hooks?.PreToolUse?.[0]?.hookCallbackIds?.[0];
        const resume = args.find(arg => arg.startsWith("--resume="));
        mark(resume ? "claude " + resume.slice(2) : "claude initialize");
        const effort = args.find(arg => arg.startsWith("--effort="));
        if (effort) mark("claude " + effort.slice(2));
        claudeMessage("control_response", {
            response: {
                subtype: "success", request_id,
                response: {
                    commands: claudeCommands, output_style: "default", available_output_styles: [], account: {}, mcp_servers_applied: true,
                    models: [{ value: "opus", resolvedModel: "claude-opus-test", displayName: "Opus Test",
                        description: "Test model", supportedEffortLevels: ["low", "high"], supportsEffort: true }],
                },
            },
        });
        return;
    }
    const block = { type: "tool_use", name: "mcp__beutl__edit", id: "toolu_claude", input: { name: "Clip" } };
    if (frame.type === "user") {
        mark("claude prompt");
        assert.equal(discovering, false);
        const content = frame.message.content;
        if (awaitingClaudeSteer) {
            assert.equal(content, "Keep only the title");
            assert.equal(frame.priority, "now");
            assert.deepEqual(frame.origin, { kind: "human" });
            assert.notEqual(frame.uuid, awaitingClaudeSteer);
            // The steered input may start as the original response finishes.
            claudeMessage("result", { subtype: "success", is_error: false, result: "original result", user_message_uuid: awaitingClaudeSteer });
            mark("claude steer");
            claudeThinkingReply(frame.uuid);
            awaitingClaudeSteer = undefined;
            return;
        }
        denyClaudeTool = content === "reject" || (Array.isArray(content) && content.some(item => item.text === "reject"));
        claudeMessage("system", { subtype: "init", slash_commands: ["compact", "help"], skills: ["shot-review", "look-chain"] });
        if (captureMentionPrompt(content)) {
            claudeThinkingReply(frame.uuid);
            return;
        }
        if (content === "wait to steer") {
            awaitingClaudeSteer = frame.uuid;
            claudeMessage("stream_event", {
                event: { type: "content_block_delta", index: 0, delta: { type: "thinking_delta", thinking: "Waiting for a steer" } },
                parent_tool_use_id: null,
            });
            return;
        }
        if (content === "rich interaction" || (Array.isArray(content) && content.some(item => item.text === "rich interaction"))) {
            claudeRichInteraction();
            return;
        }
        if (content === "thinking before text" || (Array.isArray(content) && content.some(item => item.text === "thinking before text"))) {
            claudeThinkingReply(frame.uuid);
            return;
        }
        claudeMessage("stream_event", { event: { type: "message_start", message: { id: "message-claude" } }, parent_tool_use_id: null });
        claudeMessage("stream_event", {
            event: { type: "content_block_delta", index: 0, delta: { type: "text_delta", text: "hello from Claude" } },
            parent_tool_use_id: null,
        });
        claudeMessage("stream_event", {
            event: { type: "content_block_delta", index: 2, delta: { type: "text_delta", text: "more from Claude" } },
            parent_tool_use_id: null,
        });
        claudeMessage("assistant", {
            message: { id: "message-claude", role: "assistant", content: [
                { type: "text", text: "hello from Claude" }, block, { type: "text", text: "more from Claude" },
            ] },
            parent_tool_use_id: null,
        });
        claudeMessage("control_request", {
            request_id: "mock-permission",
            request: { subtype: "can_use_tool", tool_name: block.name, input: block.input, tool_use_id: block.id },
        });
    } else if (frame.type === "control_response") {
        if (frame.response.request_id === "mock-question-hook") {
            assert.equal(frame.response.response.hookSpecificOutput.permissionDecision, "ask");
            claudeMessage("control_request", { request_id: "mock-question", request: {
                subtype: "can_use_tool", tool_name: "AskUserQuestion", input: { questions: claudeQuestions }, tool_use_id: "ask",
            } });
            return;
        }
        if (frame.response.request_id === "mock-question") {
            const response = frame.response.response;
            assert.equal(response.behavior, "allow");
            assert.deepEqual(response.updatedInput, { questions: claudeQuestions,
                answers: { [claudeQuestions[0].question]: "Intro, Credits, Interview", [claudeQuestions[1].question]: "City at night" } });
            claudeMessage("result", { subtype: "success", is_error: false, result: "done" });
            return;
        }
        assert.equal(frame.response.request_id, "mock-permission");
        assert.equal(frame.response.response.behavior, denyClaudeTool ? "deny" : "allow");
        claudeMessage("user", {
            message: {
                role: "user", content: [{
                    type: "tool_result", tool_use_id: block.id, is_error: denyClaudeTool,
                    content: [{ type: "text", text: denyClaudeTool ? "denied" : "created" }],
                }],
            },
            parent_tool_use_id: null,
        });
        claudeMessage("result", { subtype: "success", is_error: false, result: "done" });
    } else {
        throw new Error("Unexpected Claude frame " + JSON.stringify(frame));
    }
}

function claudeThinkingReply(inputId) {
    claudeMessage("stream_event", { event: { type: "message_start", message: { id: "message-thinking" } }, parent_tool_use_id: null });
    claudeMessage("stream_event", {
        event: { type: "content_block_delta", index: 0, delta: { type: "thinking_delta", thinking: "Plan the reply" } },
        parent_tool_use_id: null,
    });
    claudeMessage("assistant", {
        message: { id: "message-thinking", role: "assistant", content: [{ type: "thinking", thinking: "Plan the reply" }] },
        parent_tool_use_id: null,
    });
    claudeMessage("stream_event", {
        event: { type: "content_block_delta", index: 1, delta: { type: "text_delta", text: "Single reply after thinking" } },
        parent_tool_use_id: null,
    });
    claudeMessage("assistant", {
        message: { id: "message-thinking", role: "assistant", content: [{ type: "text", text: "Single reply after thinking" }] },
        parent_tool_use_id: null,
    });
    claudeMessage("result", { subtype: "success", is_error: false, result: "done", user_message_uuid: inputId });
}

function claudeRichInteraction() {
    const assistant = (id, content, parent_tool_use_id = null) => claudeMessage("assistant", {
        message: { id, role: "assistant", content }, parent_tool_use_id,
    });
    assistant("claude-root", [
        { type: "tool_use", name: "Agent", id: "claude-agent", input: { description: "Review shots", subagent_type: "scene-reviewer" } },
        { type: "tool_use", name: "Bash", id: "claude-bash", input: { command: "render-preview", run_in_background: true } },
    ]);
    const scoped = { parent_tool_use_id: "claude-agent" };
    claudeMessage("stream_event", { ...scoped, event: { type: "message_start", message: { id: "claude-child" } } });
    claudeMessage("stream_event", { ...scoped, event: { type: "content_block_delta", index: 0, delta: { type: "text_delta", text: "Checking shots" } } });
    claudeMessage("stream_event", { ...scoped, event: { type: "content_block_delta", index: 1, delta: { type: "thinking_delta", thinking: "Inspect scene" } } });
    assistant("claude-child", [
        { type: "text", text: "Checking shots" },
        { type: "tool_use", name: "Read", id: "claude-read", input: { file_path: "scene.beutl" } },
    ], "claude-agent");
    claudeMessage("tool_progress", { ...scoped, tool_use_id: "claude-read", tool_name: "Read", elapsed_time_seconds: 1 });
    claudeMessage("user", { ...scoped, message: {
        role: "user", content: [{ type: "tool_result", tool_use_id: "claude-read", content: "Read the scene" }],
    } });
    claudeMessage("system", { subtype: "task_started", task_id: "render-task", tool_use_id: "claude-bash", task_type: "local_bash",
        is_backgrounded: true, description: "Render previews" });
    claudeMessage("system", { subtype: "task_progress", task_id: "render-task", description: "Render previews", summary: "Rendering frames" });
    claudeMessage("system", { subtype: "task_notification", task_id: "render-task", status: "completed", summary: "Rendered 12 frames" });
    assert.ok(claudePreToolHook);
    claudeMessage("control_request", { request_id: "mock-question-hook", request: {
        subtype: "hook_callback", callback_id: claudePreToolHook, tool_use_id: "ask", input: {
            hook_event_name: "PreToolUse", tool_name: "AskUserQuestion", tool_input: { questions: claudeQuestions },
            tool_use_id: "ask", cwd: process.cwd(), session_id: claudeSession, transcript_path: "mock-transcript.jsonl",
        },
    } });
}

lines.on("line", line => {
    const frame = JSON.parse(line);
    if (provider === "acp") acp(frame);
    else if (provider === "codex") codex(frame);
    else if (provider === "claude") claude(frame);
    else throw new Error("Unknown mock agent " + provider);
});
