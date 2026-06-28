---
description: |
  Generate a code-coverage HTML report for Beutl, matching the CI workflow. Use when the user
  asks about test coverage, wants a coverage report, or wonders if a module is well-tested.
  Triggers on "カバレッジ", "coverage report", "how much of X is covered?". Always confirm
  the measurement scope before running (full vs project).
allowed-tools: Bash(dotnet test:*) Bash(dotnet tool:*) Bash(reportgenerator:*) Bash(find:*)
argument-hint: "[project-path]"
---

# Generate Beutl coverage report

Coverage collection runs the whole test suite, which is **slow (minutes)**. Always confirm scope with the user unless `$ARGUMENTS` is provided. Use AskUserQuestion with these defaults:

- **Scope**: full solution (`Beutl.slnx`, matches CI) / a single test project (faster, narrower)
- **Output**: HTML report at `coveragereport/index.html` (default)

Once scope is decided, run:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool 2>/dev/null || true

# Full solution (matches CI)
dotnet test Beutl.slnx --no-build --verbosity normal -f net10.0 \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings

# Or single project
# dotnet test <path/to/Tests.csproj> --no-build --verbosity normal -f net10.0 \
#   --collect:"XPlat Code Coverage" --settings coverlet.runsettings

# Every test project under tests/*/TestResults/<guid>/ writes its own coverage XML.
# Pass all of them to reportgenerator as a semicolon-joined list so the merged
# report covers the full solution (matching CI).
COBERTURA=$(find tests -type f -path '*/TestResults/*/coverage.cobertura.xml' 2>/dev/null | paste -sd ';' -)
if [ -z "$COBERTURA" ]; then
  echo "No coverage XML found. Run a build first."
  exit 1
fi

reportgenerator -reports:"$COBERTURA" -targetdir:coveragereport -reporttypes:Html
echo "Open coveragereport/index.html"
```

## Notes

- Mirrors `.github/workflows/dotnet.yml` so local and CI dashboards agree.
- Coverage threshold lives in [`.github/workflows/dotnet.yml`](../../../.github/workflows/dotnet.yml) — do not lower it locally without changing CI.
- Project layout: tests sit in `tests/`, source under `src/`.

## After running

If coverage dropped versus the previous PR or branch, point to the specific module under `src/` that lost coverage. Do NOT propose adding tests autonomously — surface the gap and ask the user how to proceed.
