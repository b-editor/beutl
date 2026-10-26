# 🎬 Beutl

__English__ / [Japanese](README.ja.md)

![](https://img.shields.io/github/issues/b-editor/beutl)
![](https://img.shields.io/github/forks/b-editor/beutl)
![](https://img.shields.io/github/stars/b-editor/beutl)
![](https://img.shields.io/github/license/b-editor/beutl)
![](https://img.shields.io/github/downloads/b-editor/beutl/total)
![](https://img.shields.io/github/v/release/b-editor/beutl)
![](https://img.shields.io/github/repo-size/b-editor/beutl)
[![Daily build](https://github.com/b-editor/beutl/actions/workflows/daily-build.yml/badge.svg)](https://github.com/b-editor/beutl/actions/workflows/daily-build.yml)
[![Discord](https://img.shields.io/discord/868076100511760385.svg?label=&logo=discord&logoColor=ffffff&color=7389D8&labelColor=6A7EC2)](https://discord.gg/Bm3pnVc928)

**Create. Animate. Make it yours.**

Beutl (pronounced /bjútəl/) is free and open source video editing and compositing software for **Windows, macOS, and Linux**. Bring video, shapes, text, and audio together with keyframes, node graphs, effects, and shaders.

[Download](https://beutl.beditor.net/en#download) · [Website](https://beutl.beditor.net/en) · [Documentation](https://docs.beutl.beditor.net) · [Extensions](https://beutl.beditor.net/en/store)

![Beutl editor showing a typographic animation preview and its layered timeline](assets/screenshots/screenshot-light-dark.png)

_A typographic animation in Beutl. [Watch the editing demo](https://beutl.beditor.net/en). The image is from a development version._

## 📖 Features

### Timeline & Compositing

Arrange video, shapes, text, and audio as separate layers on the timeline. Nest scenes to keep complex composites organized, and experiment with non-destructive editing and undo.

### Node Graphs

Connect shapes, effects, random values, and switches to build procedural visuals. Adjust a value and let the connected nodes carry the change through your composition.

### Keyframes & Easing

Animate individual objects, from fades and slides to kinetic typography. Shape the timing with easing functions such as Back, Bounce, and Elastic, or fine-tune motion with spline curves.

### Effects, Shaders & Scripts

Stack built-in effects such as blur, shadows, LUTs, chroma key, pixel sorting, and displacement maps. Write SKSL or GLSL shaders and C# scripts when you need a custom effect.

### Audio & Visualizers

Mix music and sound effects with EQ, compressor, limiter, and delay. Turn audio into visuals with waveform, spectrum, and spectrogram visualizers.

### Text & Typography

Style text with tags for weight, italics, size, color, and letter spacing. Create subtitles and captions, or split text into individual characters and animate them with keyframes.

### GPU Rendering & Export

Use Vulkan-based GPU rendering, with MoltenVK on macOS and a software fallback when no GPU is available. Work with reduced-scale previews, then export with supersampling. Supported output formats include MP4, MOV, MKV, and WebM.

### More Creative Tools

- **Shapes and brushes:** Bézier curves, arcs, gradients, and image, tile, and noise brushes.
- **Particles:** Emitters for effects such as fire, smoke, and sparkles.
- **Color grading:** Tone curves, `.cube` LUTs, and HDR / wide-gamut color management.
- **Expressions:** Link properties and compute parameters from other values.
- **3D scenes (experimental):** Combine 2D compositions with cameras, lighting, meshes, and PBR materials.

### Extensions

Add effects, objects, codecs, and UI through the [extension store](https://beutl.beditor.net/en/store). You can also develop and publish your own extensions.

### Version Control

Track a project's editing history with Git, restore earlier versions, create experimental branches, and back up work to a remote repository from within Beutl.

## 📥 Installation

Choose the download for your operating system on the [website](https://beutl.beditor.net/en#download), then follow the [installation guide](https://docs.beutl.beditor.net/get-started/install). Release notes and previous versions are available on [GitHub Releases](https://github.com/b-editor/beutl/releases).

## Development

On a fresh checkout, initialize the Git submodules first with `git submodule update --init --recursive`.

Run the application from the repository root using the included scripts, which do not require the global Nuke tool.

macOS/Linux:

```sh
./build.sh dev
```

Windows (PowerShell):

```powershell
.\build.ps1 dev
```

This runs `src/Beutl` in Debug configuration, automatically selecting `net10.0-windows` on Windows and `net10.0` on macOS/Linux. If the global Nuke tool is already installed, you can also use `nuke dev`.

[Source code of the old version (BEditor)](https://github.com/b-editor/beutl/tree/old/develop)

## License

This project is licensed under the **MIT License** ([LICENSE](https://github.com/b-editor/beutl/blob/main/LICENSE)).

**Beutl.FFmpegWorker** is a separate executable that links with GPL-licensed FFmpeg and is distributed under the **GNU General Public License v3** ([LICENSE.GPL](https://github.com/b-editor/beutl/blob/main/LICENSE.GPL)). It runs as an independent process and communicates with the main application via IPC, so the GPL does not apply to the rest of the codebase.

## Patrons

- ozero
- [darthaunty1](https://www.patreon.com/user/creators?u=89848438)
- [calloc134](https://github.com/calloc134)
- [Bony_Chops](https://github.com/BonyChops)
- [Hayashi Tomonari](https://www.patreon.com/user/creators?u=62872137)
