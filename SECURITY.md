# Security Policy

## Supported Versions

Security fixes are applied to the current preview line (`2.0.x`) and the latest
stable line (`1.1.x`). Beutl is currently in a `2.0.0` preview cycle. Releases
older than `1.1` (`< 1.1`) are no longer supported.

| Version          | Supported          |
| ---------------- | ------------------ |
| 2.0.x (preview)  | :white_check_mark: |
| 1.1.x            | :white_check_mark: |
| < 1.1            | :x:                |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues,
pull requests, or the Discord server.**

Instead, use GitHub's private vulnerability reporting:

1. Go to the repository's **Security** tab.
2. Click **Report a vulnerability**.
3. Provide a description, reproduction steps, affected version, and impact.

We aim to acknowledge a report within a few days and will keep you updated on
the fix and disclosure timeline. Once a fix is released, we are happy to credit
you in the advisory unless you prefer to remain anonymous.

## Scope

Beutl ships `Beutl.FFmpegWorker` as a **separate GPL-licensed process** that is
reached only via IPC (`Beutl.FFmpegIpc`). Vulnerabilities in FFmpeg itself
should be reported upstream to the FFmpeg project; report issues in Beutl's IPC
layer or process handling here.

Extensions are distributed through Beutl accounts. If you discover a malicious
or vulnerable published extension, report it through the same private channel
above so we can take it down.
