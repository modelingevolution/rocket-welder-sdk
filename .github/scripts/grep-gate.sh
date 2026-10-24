#!/usr/bin/env bash
# Grep gate (epic-129 test-scenarios TS-129-48). Each row is: id <TAB> extended regex <TAB> pathspecs.
# A row passes when `git grep -nE` finds 0 lines. Add a row per slice as the G-rows are switched on.
# Same shape as drawing's .github/scripts/grep-gate.sh.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

ROWS=(
  # G-18 (S3): the Pose3 maths members are deleted in Drawing 2.0; Frame3.Interpolate/Distance/FromSurface replace them.
  $'G-18\t\\bPose3(d|<[^>]*>)?\\.(Lerp|Distance|FromSurface)\\b\tcsharp/'
  # G-18a (S3, epic-129 test-scenarios numbering): no Euler rotation algebra on Rotation3 and no direction → Euler shortcut
  # (Rotation3.Combine/Slerp and Vector3.RotationTo are deleted; use Quaternion.Slerp / Quaternion.FromTo).
  $'G-18a\t\\bRotation3(d|<[^>]*>)?\\.(Combine|Slerp|Inverse|Rotate|ToMatrix3x3)\\b|\\bRotationTo\\s*\\(\tcsharp/'
)

failed=0
for row in "${ROWS[@]}"; do
  IFS=$'\t' read -r id pattern paths <<<"$row"
  # shellcheck disable=SC2086  # pathspecs are space-separated on purpose
  hits=$(git grep -nE "$pattern" -- $paths)
  count=$(printf '%s' "$hits" | grep -c . || true)
  echo "$id: $count line(s)  [git grep -nE '$pattern' -- $paths]"
  if [ "$count" -ne 0 ]; then
    printf '%s\n' "$hits"
    echo "::error::grep gate $id found $count line(s)"
    failed=1
  fi
done
exit $failed
