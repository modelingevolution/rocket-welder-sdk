#!/usr/bin/env bash
#
# Verify that every packed package is actually FETCHABLE at a given version, from both feeds.
#
# Why this exists: `dotnet nuget push` returning 0 is not the same fact as "a consumer can
# restore this", and a release status is read as if it were. Both publish workflows used to
# treat the push's exit code as the answer, and the org-feed push additionally ran under
# `continue-on-error: true` — so the job went green whether that feed took the packages or was
# switched off at the wall. This script is what actually decides those jobs.
#
# It lives here rather than inline in a workflow so there is ONE copy for the release path and
# the preview path, and so it can be run by hand against the live feeds — which is the only way
# it has ever been tested.
#
# Usage (run from the directory that contains ./nupkg):
#     VERSION=2.16.0 bash .github/scripts/verify-feeds.sh
#
# Environment:
#   VERSION           required — the version every packed package must serve
#   TIMEOUT_SECONDS   optional, default 900 — total polling budget
#   POLL_SECONDS      optional, default 15  — interval between sweeps
#   PUSH_OUTCOME      optional — what the org-feed push reported, quoted in the failure message
#   NUPKG_DIR         optional, default ./nupkg
#
# Exit 0 = every package serves VERSION from both feeds. Exit 1 = at least one does not, and the
# error names which feed and which package.
#
# What this proves, and what it does not: it proves FETCHABILITY, never IDENTITY — a package with
# the right version and the wrong bits inside passes. Combined with `--skip-duplicate`, a re-run
# can pass having published nothing. See docs/backlog/publish-verification-proves-fetchability-not-identity.md

set -u

VERSION="${VERSION:?VERSION must be set}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-900}"
POLL_SECONDS="${POLL_SECONDS:-15}"
PUSH_OUTCOME="${PUSH_OUTCOME:-not recorded}"
NUPKG_DIR="${NUPKG_DIR:-./nupkg}"

# EVERY packed package is checked, not a representative one: nuget.org validates and indexes them
# independently, so "the first one is up" says nothing about the twenty-second. That blind spot was
# observed for real on 2.16.0 — Abstractions was fetchable while Devices.Motion still 404'd.
ids=""
for f in "$NUPKG_DIR"/*.nupkg; do
  [ -e "$f" ] || { echo "::error::No .nupkg files in $NUPKG_DIR — nothing was packed."; exit 1; }
  b="$(basename "$f")"
  ids="$ids ${b%".${VERSION}.nupkg"}"
done
total="$(echo $ids | wc -w)"
echo "Verifying $total package(s) at version $VERSION on both feeds..."

# The two feeds expose the same {"versions":[...]} body under DIFFERENT paths. Getting this wrong
# is not a hypothetical: a checker that always 404s is a checker that always passes if it inverts
# the test, and always fails if it does not.
#   nuget.org -> /v3-flatcontainer/<id>/index.json
#   org feed  -> /v3/package/<id>/index.json
# Ids are lowercased in both.
deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
while :; do
  missing=""
  for id in $ids; do
    lid="$(echo "$id" | tr '[:upper:]' '[:lower:]')"
    for feed in \
      "nuget.org|https://api.nuget.org/v3-flatcontainer/$lid/index.json" \
      "modelingevolution|https://nuget.modelingevolution.com/v3/package/$lid/index.json"
    do
      name="${feed%%|*}"; url="${feed#*|}"
      # `|| true` so one dropped connection cannot end the whole verification.
      body="$(curl -sS --max-time 20 "$url" 2>/dev/null || true)"
      # Quoted match: "2.15.1" must not be satisfied by "2.15.1-preview.abc1234".
      case "$body" in
        *"\"$VERSION\""*) ;;
        *) missing="$missing $name:$id" ;;
      esac
    done
  done

  # `if`, not `[ ... ] && break`: under `set -e` an AND-OR list whose test fails is a non-zero
  # statement, and that has ended jobs before.
  if [ -z "$missing" ]; then break; fi
  now="$(date +%s)"
  if [ "$now" -ge "$deadline" ]; then break; fi
  echo "  still indexing ($(( deadline - now ))s left) — missing:$missing"
  sleep "$POLL_SECONDS"
done

if [ -n "$missing" ]; then
  echo "::error::Release $VERSION is NOT fetchable from every feed after ${TIMEOUT_SECONDS}s. \
Missing as feed:package —$missing (ModelingEvolution push step reported: ${PUSH_OUTCOME}). \
The tag exists and nuget.org may be serving part of the release: verify before re-running, and \
never re-cut the tag to fix a feed problem."
  exit 1
fi

echo "All $total package(s) serve $VERSION from nuget.org and the ModelingEvolution feed."
