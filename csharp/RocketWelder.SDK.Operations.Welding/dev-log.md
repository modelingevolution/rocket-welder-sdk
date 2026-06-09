# dev-log — `rw.weldprogram/2` model + canonical serializer (epic-037 build slice #1)

**Author:** .NET engineer · **Date:** 2026-06-06 · **Package:** `RocketWelder.SDK.Operations.Welding`
(namespace `RocketWelder.SDK.Operations`) · **Spec:** `data-model.md` §2/§3/§4 · **Home:** ADR-012.

This is the TESTER's startup guide. You exercise the model **headlessly** via the `weldprogram` CLI.
You should read **only `data-model.md` + this file** — never the C# source.

---

## What shipped

The v1 weld-program model was evolved to the enriched **`rw.weldprogram/2`** schema (data-model.md §2):
a `Segment` now owns an ordered **`Pass[]` (≥1)**; each `Pass` has its own `JobRef` + `ToolFrame` +
`MotionProfile`. Segment-level welding facts (`seamType`, `position` PA–PG, `weldSize`, `gas`,
`polarity`) and `resolver.tracking` / `externalAxis` were added. The canonical serializer, deserializer,
and the **`/1 → /2` migration** (§4) all live in this package.

**What is NOT stored (D-D):** the welder's electrical setpoints (WFS / arc voltage / transfer / pulse).
A pass carries only `jobRef.id` — a reference to the device's qualified job.

**Anti-invention rule (§4):** migrating a `/1` file never fabricates a value. `position` / `weldSize` /
`gas` / `polarity` / `externalAxis` that `/1` never had stay **`null`**, never a fake `0`.

---

## Build the CLI (native `dotnet` — this is a Linux dev-box)

```bash
SDK=/mnt/d/source/modelingevolution/rocket-welder-sdk/csharp
dotnet build "$SDK/RocketWelder.SDK.Automation.WeldProgramCli/RocketWelder.SDK.Automation.WeldProgramCli.csproj" -o /tmp/wpcli
CLI="dotnet /tmp/wpcli/weldprogram.dll"
$CLI --help        # lists: canonicalize, migrate, resolve, sample, sample-topology
```

The committed fixtures live in:
`$SDK/RocketWelder.SDK.Operations.Welding.Tests/Fixtures/`
- `sample-v2.json` — a canonical `/2` program (two segments: s0 multi-pass fillet, s1 single-pass butt)
- `sample-v1.json` — a legacy `/1` program (same part)
- `sample-v1-migrated-to-v2.json` — the golden `/2` output of migrating `sample-v1.json`

You can also generate a `/2` sample with `$CLI sample sample.json`.

---

## Test 1 — byte-identical round-trip (AT-A4, §2 rule 5)

Re-serializing an unchanged program must be **byte-identical**.

```bash
F=$SDK/RocketWelder.SDK.Operations.Welding.Tests/Fixtures
$CLI canonicalize "$F/sample-v2.json" /tmp/roundtrip.json
diff "$F/sample-v2.json" /tmp/roundtrip.json && echo "PASS: byte-identical"
```
Expected: **no diff output**, prints `PASS: byte-identical`.

## Test 2 — change ONE field → exactly one changed line (the diff requirement, §2 rule 1–3)

```bash
F=$SDK/RocketWelder.SDK.Operations.Welding.Tests/Fixtures
sed 's/"legMm": 8/"legMm": 9/' "$F/sample-v2.json" > /tmp/changed_in.json
$CLI canonicalize /tmp/changed_in.json /tmp/changed.json
diff "$F/sample-v2.json" /tmp/changed.json
# expect exactly:  54c54  /  < "legMm": 8  /  > "legMm": 9   (one line changed)
```
Expected: a single `NcN` hunk — **one line `<` removed, one line `>` added**. Any other key changing is
a bug (keys are emitted in fixed §2 order; floats at 6 sig-figs; positional segments + passes).

## Test 3 — positional order (segments AND passes never re-sorted, §2 rule 2)

Reordering is a *real, visible* diff; ids stay stable (a move, not delete+add). Edit
`sample-v2.json` to swap the two `"id": "s0"` / `"id": "s1"` segment objects, re-`canonicalize`, and
confirm: both ids still appear once, `s1` now precedes `s0`, and the **set of lines is unchanged** (a
pure move). Same idea for the two passes `p0`/`p1` inside s0.

## Test 4 — `/1 → /2` migration (§4)

```bash
F=$SDK/RocketWelder.SDK.Operations.Welding.Tests/Fixtures
$CLI migrate "$F/sample-v1.json" /tmp/v2.json
head -2 /tmp/v2.json                      # -> "schema": "rw.weldprogram/2"
grep -nE '"(position|weldSize|gas|polarity|role)"' /tmp/v2.json
#   position/weldSize/gas/polarity -> null   (NOT invented)
#   role -> "cap"                            (§4.3: a lone single-pass run is its own cap)
diff /tmp/v2.json "$F/sample-v1-migrated-to-v2.json" && echo "PASS: matches golden"
```
Migration checks (§4): `schema` becomes `/2`; the single `/1` run is wrapped as `passes[0]`;
`weldJob.id → jobRef.id` (the old `weldJob.params` blob is **dropped** — D-D); `torchFrame → toolFrame`
verbatim; `travelSpeedMmPerS → motion.travelSpeedMmPerS` with `weave: null`; `binding` / `subRange` /
`resolver` / `datum` / `version` carried unchanged.

Migration is **idempotent + byte-stable**: re-`canonicalize` the migrated `/2` and diff — no change.

> Note: `canonicalize` on a `/1` file does the same thing (the deserializer auto-migrates `/1` on read);
> `migrate` is the explicitly-named command for the tester.

## Test 5 — edge re-binding still works (§3, unchanged by `/2`)

```bash
$CLI sample /tmp/p.json
$CLI sample-topology /tmp/topo.json
$CLI resolve /tmp/p.json /tmp/topo.json     # -> "s0 E0" / "s1 E2"
```

---

## Run the unit tests directly

```bash
dotnet test "$SDK/RocketWelder.SDK.Operations.Welding.Tests/RocketWelder.SDK.Operations.Welding.Tests.csproj"
```
40 tests (xUnit + FluentAssertions): round-trip byte-identical, fixture round-trip, fixed key order
(program/segment/pass), one-field & one-enum diffs, segment+pass reorder positional stability,
6-sig-fig float formatting, full semantic round-trip (weave / tracking / external axis / fillet
leg-vs-throat), `/1→/2` migration (all §4 facts + golden-fixture byte match), `/3` unknown-schema reject,
and the §3 fingerprint/resolve guards.

---

## Engineer notes / spec ambiguities flagged (do not paper over)

1. **`/1→/2` migrated pass `role`: spec says `cap`, the build prompt said `root`.** I followed the
   **spec** — `data-model.md` §4.3 explicitly: *"`role` defaults `"cap"`… a lone single-pass run is its
   own cap"* (`PassRole.Cap`). The build-slice prompt said "length-1 `Pass[]` (root)". These conflict; the
   locked design governs. **If `root` is actually wanted, it's a one-line change** in
   `WeldProgramMigrator.MigrateSegment` (`Role: PassRole.Cap` → `PassRole.Root`) and the golden fixture
   regenerates. Team-lead to confirm.

2. **`position` / `weldSize` / `gas` / `polarity` are nullable on `Segment`.** §2's JSON sample shows them
   with concrete values, but §4.2 says a `/1` migration leaves them **`null` "until an author sets it"**,
   and §4's anti-invention rule forbids inventing them. To satisfy both (authored = value; migrated =
   null) without fabricating, these four are `T?` and serialize as `null` when absent. `seamType` stays
   non-nullable (always present in `/1`).

3. **Enums vs the old string `SeamType`.** §2's "Enums" block lists `SeamType{fillet,…}`, so the v1
   `readonly record struct SeamType(string Code)` was replaced by a real `enum SeamType`. All §2 enums
   (`SeamType`, `WeldPosition` PA–PG, `PassRole`, `Technique`, `Polarity`, `ResolverMode`, `TrackingMode`)
   are C# enums mapped to their canonical wire strings (hyphenated where §2 is, e.g. `adjacent-feature`)
   in the serializer — same pattern as the existing `EdgeKind`.

4. **`EdgeBinding` / §3 fingerprint untouched.** Per the brief, the §3 fingerprint is already implemented
   and was not modified; `EdgeIdHint` stays `string` per ADR-009.

5. **Array-of-vectors rendering quirk** (`"endpoints": [[…],[…]\n    ]`) is **pre-existing v1 behaviour**
   (vectors written as inline raw tokens inside a normal JSON array). It is fully deterministic and
   byte-stable (round-trips identically), so AT-A4 holds; I left it unchanged to avoid perturbing the
   already-shipped binding serialization.
