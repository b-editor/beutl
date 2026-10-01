# Method test impact: shadow experiment

This tool reports which NUnit **methods** would be selected for a change. It does
not skip tests. `.NET` still runs the full solution, requires the GPU canary, and
runs both existing Vulkan validation suites without a filter. `safeToSkip` is
always `false`; neither the JSON nor an empty candidate list is permission to
remove a test gate.

Requirements: the repository's .NET 10 SDK, Python 3.10+, Git, and built test
projects for the requested TFM/configuration. GitHub artifact download also uses
the runner's `gh`. No external service or new NuGet package is required by the
selector. Roslyn is loaded from the installed SDK.

## Collect a bounded baseline

Use a clean checkout of the exact main commit that will be the PR's base. Put all
outputs under the ignored `artifacts/` directory. No stale output directory may be
reused. A partial collection is useful: every method without a usable profile
remains selected, including methods in projects that were not profiled.

```sh
python3 build/test-impact/impact.py collect \
  --base "$(git rev-parse HEAD)" \
  --project tests/Beutl.UnitTests/Beutl.UnitTests.csproj \
  --method-pattern '*CultureNameValidation*' \
  --max-methods 8 --budget-seconds 180 --method-timeout 30 \
  --out artifacts/impact-baseline
```

By default the project is built once. Use `--no-build` only after building the
same clean sources, SDK, TFM and configuration. Omit `--project` to discover all
test projects under `tests/`; repeat it for a smaller explicit scope. Discovery
must produce fresh NUnit XML with real `classname`/`methodname` identities.
Constructed/inherited identities that cannot be mapped uniquely remain selected.

Each method starts a fresh `dotnet test --no-build` process using NUnit's `Where`
filter for exact class and method. All parameter cases, even custom display names,
are grouped together. The collector accepts a profile only when the execution XML
contains exactly that method, every discovered case, and only passed results.
Skipped, failed, timed-out, changed-discovery and missing-coverage runs are left
unprofiled, with the reason saved in `baseline.json`.

Coverlet's OpenCover output is collected **separately for each method** with
`IncludeTestAssembly=true` and no source/attribute exclusion. This includes
production files brought into a test assembly by `Compile Include`. An aggregate
coverage report from the normal suite is never treated as per-test coverage.
Sequence points locate executed methods in the source index; a hit anywhere in
a CLR method retains its whole method, including previously unhit branches.

Cost is one build/discovery plus **N testhost/collector starts**, not N ordinary
test case durations. Defaults are at most 25 methods, a 600-second profiling
budget and 60 seconds per isolated run. Build/discovery are additional bounded
setup work. `collectionSeconds`, `setupSeconds`, per-method wall/test durations,
the error map and `collection.md` make this overhead visible. Collection is serial;
timeouts kill the process tree. No long-lived collector is installed.

## Select and compare

In the PR checkout, build the tested tree (including new tests) first. The baseline
must match the **exact base SHA**, resolved SDK, TFM, OS/version/architecture,
runtime installation, configuration, relevant GPU/globalization environment,
collector settings and selector implementation. An older main baseline is not
silently accepted. Use the same tool version on both checkouts.

```sh
python3 build/test-impact/impact.py select \
  --base "$BASE_SHA" --baseline /path/to/baseline.json \
  --out artifacts/impact-shadow

# Keep the normal full run. NUnit XML supplements the existing TRX diagnostics.
dotnet test Beutl.slnx --no-build -f net10.0 --logger trx \
  -- NUnit.TestOutputXml="$PWD/artifacts/impact-full"

python3 build/test-impact/impact.py compare \
  --report artifacts/impact-shadow/selection.json --results artifacts/impact-full
```

The report gives total/selected methods and cases, per-method reasons, affected
methods, fallback reasons and baseline collection time. Comparison reports
omitted methods that **actually failed**, missing methods/cases, unknown results,
outcomes and summed selected/omitted test case durations. Missing results produce
`incomplete`, never a reassuring zero-miss claim. Summed case duration is not
wall-clock savings: setup, parallelism, discovery and instrumentation differ.
Passing omitted tests cannot establish that selection is sound on future changes.

## Selection boundary

Roslyn detects ordinary method body changes and stable method identities, including
overloads. The dynamic test-to-method map is unioned with a conservative static
reverse-reference graph from **both** base and current source. Identifier names
are matched across overloads/receiver types; this is intentionally a superset, not
a semantic proof. It also handles method groups and literal reflection names.
New methods expand through current callers; changes with no execution evidence or
static path to a test fall back. New/changed tests and changed parameter inventories
are always selected.

Full fallback covers missing/stale/incompatible/corrupt baselines, unknown diffs,
syntax failures/preprocessor directives in changed files, new/deleted/renamed
source files, removed/signature-changed methods, fields, constructors, accessors,
fixture attributes, SetUp/TearDown, references outside ordinary methods (including
TestCaseSource properties), shared props/targets/packages/project files, resources
(including source embedded as data), native/GPU/renderer and submodule changes.
Tests whose profiles contain unresolved/generated, conditional, dynamic/reflection
or native/GPU code remain selected. Nonliteral reflection, runtime generation,
cross-test state, platform behavior and native execution are not proven by a
managed coverage map. This is why this version is **shadow only** and deliberately
over-selects; keep reviewing reports before designing any enforcement mode.

## GitHub Actions

Manually run `.NET` on **main**, enable `collect_test_impact`, and choose a project
and a maximum method count. The run preserves the normal test gates and uploads
`TestImpactBaseline-<sha>` for seven days. Collection is optional and bounded to
ten minutes of profiling. Unprofiled methods remain selected.

PR runs read only artifacts from a successful `workflow_dispatch` of `dotnet.yml`
on main at the exact base SHA. Fork PRs use the ordinary read-only `pull_request`
workflow, not `pull_request_target`. No cache of executable PR content is used as
a baseline. Missing artifacts or unavailable permissions cause full fallback.
The summary and `TestDiagnostics` artifact contain the advisory comparison.
Action versions/references are unchanged; the existing `actions.lock` dependency
set and transitive coverage check still apply.

## Regression and real-source smoke tests

```sh
python3 -m unittest discover -s build/test-impact -p 'test_*.py' -v
python3 build/test-impact/e2e.py --out artifacts/test-impact-e2e
```

Run the smoke test in a **clean disposable checkout**. It links the real
`CultureNameValidation` and `BuiltinThemeIds` sources and their existing NUnit tests
into a small project outside the solution. It collects eight method profiles,
makes a behavior-preserving edit inside `CultureNameValidation.IsValid`, expects
two of eight methods (six of 32 cases), runs all 32 cases and then the six selected
cases, and writes `evidence.json`. The source edit is restored in `finally`;
rebuild before using that checkout's smoke binary for another purpose. This tests
the real collector/adapter, parameter grouping, source linking, selector and
comparison without building the desktop application or loading GPU/native code.
