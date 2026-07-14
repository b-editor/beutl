# 004 strong parity provenance

The eight references in `References/004-parity-strong/` were rendered by the
pre-removal `FilterEffectActivator` at commit `8dad1ba3`. The committed patch
adds a generation-only test to that historical tree; it does not use the
declarative executor from the current tree.

Run `./verify.sh` from this directory on a Vulkan-capable machine. The script
creates a detached temporary worktree at `8dad1ba3`, applies
`legacy-generator.patch`, renders all eight workloads through the legacy
activator, and byte-compares the generated blobs with the committed references.
It also verifies `sha256sums.txt` before starting the historical build.

The first recorded verification was performed on 2026-07-14. All eight
independently generated blobs were byte-identical to the committed references.
Byte identity (and therefore SSIM 1.0) is the expected parity result; provenance
independence comes from the historical generator, not from requiring the two
correct renderers to produce different pixels.
