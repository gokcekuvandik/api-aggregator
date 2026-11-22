#!/usr/bin/env bash
# Merges the Cobertura files written by `dotnet test --coverage` and fails when line coverage is below the gate.
set -euo pipefail

results="${1:-TestResults}"
minimum="${COVERAGE_MINIMUM:-80}"
report="$results/coverage-report"

# Generated code is already left out at collection time by coverage.settings.xml.
dotnet tool run reportgenerator \
  "-reports:$results/**/*.cobertura.xml" \
  "-targetdir:$report" \
  "-reporttypes:JsonSummary;MarkdownSummaryGithub;Badges" \
  "-verbosity:Error"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ] && [ -f "$report/SummaryGithub.md" ]; then
  cat "$report/SummaryGithub.md" >> "$GITHUB_STEP_SUMMARY"
fi

# The first match is the summary block, which ReportGenerator writes before the per-assembly figures.
summary_value() {
  grep -m1 -oE "\"$1\": *[0-9.]+" "$report/Summary.json" 2>/dev/null | grep -oE '[0-9.]+$' || true
}

line=$(summary_value linecoverage)
branch=$(summary_value branchcoverage)
if [ -z "$line" ]; then
  echo "::error::No line coverage found: is there a *.cobertura.xml under $results?"
  exit 1
fi
echo "Line coverage: $line% (gate $minimum%), branch coverage: $branch%"

if ! awk -v actual="$line" -v gate="$minimum" 'BEGIN { exit !(actual >= gate) }'; then
  echo "::error::Line coverage $line% is below the $minimum% gate."
  exit 1
fi
