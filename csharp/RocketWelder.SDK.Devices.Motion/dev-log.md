# dev-log — motion contract (epic-065, iteration 1)

**Author:** .NET engineer · **Date:** 2026-08-20 · **Package:** `RocketWelder.SDK.Devices.Motion`
(namespace `RocketWelder.SDK.Devices.Motion`) · **Spec:** `docs/epics/epic-065-external-axis-motion/`
— `architecture.md` §"The contract (final)" is normative; `requirements.md` FR-2/3/4/6/8/9, AC-21,
AC-26.

Iteration 1 is **contract only**: no adapter, no transport, no hardware. The Delta VFD-C2000
adapter refactor is It-2 and is not in this repository yet.

---

## What shipped

| Deliverable | Where |
|---|---|
| The contract — `IMotionAxis` (+ derived `Kind`), `IRotaryAxis`, `ILinearAxis`, `IMotionDevice` (+ derived `Kind`), `IPositioner`, `ILinearTrack`, `MotionDeviceExtensions`, `AxisState`, `AxisCapabilities`, `RotationSense`, `MotionDeviceKind`, `AxisStatus`, `LimitSwitchState`, `MotionError`, `MotionException` | `RocketWelder.SDK.Devices.Motion` |
| `AxisKind` | `RocketWelder.SDK.Abstractions` — see the layering decision below |
| `AxisDeclaration` + `DeviceTypeInfo.Axes` | `RocketWelder.SDK.Automation.Abstractions` |
| `ExternalAxis(string Device, string Axis, double AngleDeg)` | `RocketWelder.SDK.Operations.Welding` |
| AC-21 negative-compilation suite, AC-26 transport check, derived-`Kind` and surface tests | `RocketWelder.SDK.Devices.Motion.Tests` |
| Roster tests | `RocketWelder.SDK.Automation.Tests/AxisRosterTests.cs` |

The contract is a transcription of `architecture.md` §"The contract (final)". Where the spec's code
block and this code differ it is only in XML documentation, never in a signature.

---

## The layering decision: where `AxisKind` lives, and why

**Decision:** `AxisKind` is declared in the **`RocketWelder.SDK.Abstractions`** package, in the
**`RocketWelder.SDK.Devices.Motion` namespace**. `AxisDeclaration` stays in
`RocketWelder.SDK.Automation.Abstractions` next to `DeviceTypeInfo`.

### The constraint

Two types need `AxisKind`, and they live in packages that are **siblings**, not neighbours:

```
                    RocketWelder.SDK.Abstractions          (IDevice, DeviceId, …)
                      ▲                          ▲
                      │                          │
  RocketWelder.SDK.Devices.*                RocketWelder.SDK.Automation.Abstractions
  (Camera, Robot, Welding, DistanceSensor,  (IPlugin, DeviceTypeInfo, ConfigPropertySchema)
   and now Motion)
```

Every `Devices.*` package references `Abstractions` (plus `ModelingEvolution.Drawing` / `Signals`)
and nothing else in the family. `Automation.Abstractions` references `Abstractions` and carries an
explicit comment in its csproj: *"NO MicroPlumberd, NO ModelingEvolution.Observable, **NO
SDK.Devices.\***"*. There is no edge between the two columns today, in either direction.

`AxisDeclaration` is pinned to `Automation.Abstractions` because it is typed by
`ConfigPropertySchema` — FR-8 is explicit that the roster reuses the existing schema mechanism
rather than inventing one — and `DeviceTypeInfo.Axes` has to be typed by it.

### The options, and why they lose

| Option | Outcome |
|---|---|
| `Automation.Abstractions` → references `Devices.Motion` (for `AxisKind`) | Breaks the package's stated rule and makes **every** plugin — camera, welder, distance sensor — carry the motion contract. It also inverts the sibling direction: `Devices.*` are consumers of `Abstractions`, not providers to the plugin contract. |
| Move `AxisDeclaration` into `Devices.Motion` | It needs `ConfigPropertySchema`, so `Devices.Motion` → `Automation.Abstractions`; and `DeviceTypeInfo.Axes` needs `AxisDeclaration`, so `Automation.Abstractions` → `Devices.Motion`. **Reference cycle.** Dead on arrival. Also gives the contract a dependency on DI, logging and signals packages it has no use for. |
| **`AxisKind` in `Abstractions`** | The common ancestor both columns already reference. **Adds no dependency edge to any package**, creates no cycle, and leaves `Devices.Motion`'s reference list at exactly `Abstractions` + `ModelingEvolution.Drawing` — which is what AC-26 pins. |

### Why the namespace does not follow the package

`AxisKind` sits in namespace `RocketWelder.SDK.Devices.Motion` so the published surface reads
exactly as `architecture.md` specifies: a consumer writes one `using` and sees the whole contract.
Namespace ≠ package id is already the norm here (`Automation.Abstractions` publishes the
`RocketWelder.SDK.Automation` namespace). The practical payoff: if `Automation.Abstractions` ever
grows a legitimate reason to reference `Devices.Motion`, moving the type is a
`[TypeForwardedTo]` — **zero source change for every consumer**, because no `using` moves.

The reasoning is repeated in the XML doc on `AxisKind` itself, where the next person to wonder
"why is this file here?" will actually be standing.

---

## Notes on individual decisions

**`DeviceTypeInfo.Axes` is an init-only property, not a positional parameter.** `DeviceTypeInfo` is
a positional record whose last three parameters are optional; a new positional parameter would have
to go last and would still churn every call site that uses positional syntax past that point. An
init-only member with `= []` is purely additive: every existing plugin compiles unchanged, and a
motion plugin writes `new DeviceTypeInfo(…) { Axes = [ … ] }`. Default empty (never `null`) means no
consumer needs a null check.

**`Kind` is a default interface member on both `IMotionAxis` and `IMotionDevice`.** That is what
makes "one classification mechanism" structural rather than a convention: an implementation *cannot*
declare a kind that contradicts its own interface, because it does not declare one at all. The
`NotSupportedException` arm is the closed set defending itself — an implementer outside it gets a
named failure, not a silent default. Note the consequence for callers: `Kind` resolves through the
interface, so `((IMotionAxis)axis).Kind`, not `concreteAxis.Kind`.

**`MotionDeviceExtensions` throws `MotionException`, not `InvalidCastException`.** These accessors
are the runtime residue of an error the generated facade catches at compile time (FR-10), so when
they do fail the failure must be machine-readable: `UnknownAxis` from the indexer,
`WrongAxisKind` from the cast.

---

## `ExternalAxis` — the schema edit (FR-9)

`ExternalAxis(int JointId, double AngleDeg)` → `ExternalAxis(string Device, string Axis, double AngleDeg)`.
Serialised as `{ "device": …, "axis": …, "angleDeg": … }`; `jointId` is gone from the writer, the
reader and the tests.

**AC-20 is an ops release gate, not this iteration's work.** The code edit is safe on its own
because the field is *serialised but never executed* — nothing in this repository or in rw2 reads
`JointId`; the `externalAxis` executor is deferred by choice (FR-9). What AC-20 requires is the
**production persisted-data check** (It-0.3): confirm whether any stored weld program carries a
non-null `externalAxis`, and if any do, add the upcaster mapping the historical `JointId` through
the standard cell's declared axis order. Per the iteration plan this edit **may merge before that
check completes, but must not deploy past a persisted write until the result is recorded.**

Data point, not a substitute for the check: every `externalAxis` in every weld program committed
anywhere in `docs/` is `null` — the two `/2` fixtures in this repo, the golden migrated `/1` file,
and both saved programs captured during the epic-037 e2e run. The migration path can only ever emit
`null` (data-model.md §4's anti-invention rule), so `/1`-derived records are structurally incapable
of carrying one.

---

## How AC-21 is verified (and how the harness is kept honest)

`SnippetCompiler` compiles small C# snippets **in-process with Roslyn against the real assemblies** —
the same `RocketWelder.SDK.Devices.Motion.dll` and `ModelingEvolution.Drawing.dll` the test project
references, loaded from the runtime's `TRUSTED_PLATFORM_ASSEMBLIES` list. Nothing is stubbed, and it
runs under a plain `dotnet test`; no separate CI step, no expected-to-fail project.

The three required cases, each asserting the compiler's own error id:

| Case | Result |
|---|---|
| `AngularSpeed` handed to a linear axis move | `CS1503` |
| `Length<double, Millimetre<double>>` target on a rotary axis move | `CS1503` |
| `AngularSpeed + Speed` | `CS0019` |

Plus the mirrors (linear speed on a rotary axis; a `Degree` target on a linear axis) and P-2's
`MoveVelocityAsync(Percentage)`.

**Every negative case has a positive twin compiled through the same harness**, because a
negative-compilation suite whose harness is quietly broken passes trivially — a missing reference
would make *everything* "fail to compile" and every test green. The twins are the control:
correct-typed calls compile clean, `MoveAbsoluteAsync(45)` compiles (so typing costs the caller
nothing), `AngularSpeed + AngularSpeed` compiles, and two harness-floor tests assert that an empty
snippet compiles and that an ordinary type error is still *reported*.

`TransportDependencyTests` covers AC-26 twice over: a blocklist walk of the reference closure (a
blocklist only catches transports someone thought of) **and** a pin on the direct reference set —
`ModelingEvolution.Drawing` + `RocketWelder.SDK.Abstractions`, nothing else — which fails on any new
dependency, transport or not.

---

## Build and environment notes

- `ModelingEvolution.Drawing` **1.13.0.71** (the angular-speed dimension) is a `PackageReference`;
  it restored from the org feed during this work. Never a `ProjectReference` across repositories.
- No `<Version>` is committed in any csproj — CI injects it from the tag (see
  `Directory.Build.props`).
- Build with `dotnet.exe` from WSL.
