import { build } from "esbuild";

await build({
    entryPoints: ["src/bridge.ts"],
    outfile: "dist/bridge.mjs",
    bundle: true,
    platform: "node",
    target: "node26",
    format: "esm",
    banner: {
        js: 'import { createRequire as createNodeRequire } from "node:module"; const require = createNodeRequire(import.meta.url);',
    },
    legalComments: "eof",
});
