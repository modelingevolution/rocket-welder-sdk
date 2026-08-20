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
| The contract — `IMotionAxis` (+ derived `Kind`), `IRotaryAxis`, `ILinearAxis`, `IMotionDevice` (+ derived `Kind`), `IPositioner`, `ILinearTrack`, `MotionDeviceExtensions`, `AxisState`, `AxisKind`, `AxisCapabilities`, `RotationSense`, `MotionDeviceKind`, `AxisStatus`, `LimitSwitchState`, `MotionError`, `MotionException` | `RocketWelder.SDK.Devices.Motion` |
| `AxisDeclaration` + `MotionDeviceTypeInfo : DeviceTypeInfo` (the roster) | `RocketWelder.SDK.Devices.Motion` |
| `RocketWelder.SDK.Automation.Abstractions` | **untouched — zero diff against `master`** |
| `ExternalAxis(string Device, string Axis, double AngleDeg)` | `RocketWelder.SDK.Operations.Welding` |
| AC-21 negative-compilation suite, AC-26 transport check, derived-`Kind`, surface and roster tests | `RocketWelder.SDK.Devices.Motion.Tests` |

The contract is a transcription of `architecture.md` §"The contract (final)". Where the spec's code
block and this code differ it is only in XML documentation, never in a signature.

---

## The layering decision: the roster rides on a subclass

**Decision (team-lead ruling, 2026-08-20 — the PRIMARY branch of that ruling was taken):**
`RocketWelder.SDK.Automation.Abstractions` is left **completely untouched**. `AxisKind`,
`AxisDeclaration` and `MotionDeviceTypeInfo : DeviceTypeInfo` all live in
`RocketWelder.SDK.Devices.Motion`, which references `Automation.Abstractions` — one way, never back.
Motion plugins register a `MotionDeviceTypeInfo`; the registry keeps storing `DeviceTypeInfo`;
consumers pattern-match `is MotionDeviceTypeInfo m`.

### The constraint the ruling resolves

The epic's own documents pull in two directions: `architecture.md` puts `AxisDeclaration` in the
motion contract (where it needs `ConfigPropertySchema` from `Automation.Abstractions`), while FR-8
puts `Axes` on `DeviceTypeInfo` (which would force `Automation.Abstractions` →
`Devices.Motion`). Taken together that is a **reference cycle**. It has to be broken somewhere.

Extending the record breaks it at the cheapest point. `Automation.Abstractions` carries an explicit
`NO MicroPlumberd, NO ModelingEvolution.Observable, NO SDK.Devices.*` in its csproj, and every
`Devices.*` package already depends on it-or-below; the subclass respects both. Nothing about the
plugin contract changes, so no camera, welder or distance-sensor plugin acquires a motion
dependency, and no existing `new DeviceTypeInfo(…)` call site is touched.

### Why the primary branch was viable (checked, not assumed)

The ruling's fallback applies only if subclassing breaks something concrete. It does not:

- `DeviceTypeInfo` is `public record`, **not sealed**.
- `DeviceTypeRegistry` stores it in `Dictionary`/`List` **by reference** and returns it as
  `DeviceTypeInfo`; there is no exact-type check anywhere.
- **No serialization of `DeviceTypeInfo` exists** in this repository — a grep for
  `DeviceTypeInfo` next to `serial|json|deserial` returns nothing. Registration is in-process.
- Record equality is unaffected in the direction that matters: a `MotionDeviceTypeInfo` never
  equals a plain `DeviceTypeInfo` with the same values (the synthesised `EqualityContract` differs),
  which is the correct answer, and nothing in the codebase compares registry entries by value.
- The factory plumbing is `Func<ConfigSet, DeviceId, object>` and is inherited unchanged.

`AxisKind` remains usable by `IMotionAxis`'s derived-`Kind` default interface member, since it is now
in the same package as the interface.

### One measured cost, accepted

Deriving from `DeviceTypeInfo` makes **`ModelingEvolution.Signals` a direct reference of the contract
package** — `DeviceTypeInfo.GetSignals` is typed `ISignal<float>`. This is measured, not inferred:
`TransportDependencyTests.Contract_DirectReferences_AreExactlyTheDeclaredOnes` failed the moment the
subclass landed, which is exactly what that pin exists for. Removing the `GetSignals` parameter from
`MotionDeviceTypeInfo`'s own signature (letting plugins set it through the object initializer, since
it is an `init` property on the base) was tried and **did not remove the reference** — the compiler
emits it for the base type regardless — so the full parameter forwarding was restored rather than
paying an ergonomic cost for nothing.

`ModelingEvolution.Signals` is not a transport, so **NFR-4 / AC-26 are unaffected**; the direct
reference set is now pinned at Drawing + Signals + Abstractions + Automation.Abstractions, and any
further growth fails a test.

Full reference set of `RocketWelder.SDK.Devices.Motion`:

```
ModelingEvolution.Drawing 1.13.0.71     typed units (Degree, Length, Speed, AngularSpeed, Percentage)
RocketWelder.SDK.Abstractions           IDevice, DeviceId
RocketWelder.SDK.Automation.Abstractions  ConfigPropertySchema, DeviceTypeInfo, ConfigSet
ModelingEvolution.Signals               inherited only — the contract itself uses no signal type
```

## Notes on individual decisions

**`MotionDeviceTypeInfo.Axes` is an init-only property, not a positional parameter.**
`DeviceTypeInfo`'s last three parameters are optional, so a new positional parameter would have to
go last and would still churn any call site using positional syntax past that point. An init-only
member with `= []` reads better at the call site anyway —
`new MotionDeviceTypeInfo(…) { Axes = [ … ] }` — and default-empty (never `null`) means no consumer
needs a null check. Note the asymmetry that is deliberate: a **non**-motion device type has no
`Axes` member at all, so "does this device type have axes?" is a type test, not an empty-array
check.

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
- Tests run in CI via `.github/workflows/test.yml` (added with this iteration — every other
  workflow was restore/build/pack/push, so nothing ran `dotnet test` and AC-21/AC-26 had no CI to
  be green in).

### NuGet feed outage during this work (2026-08-20)

**Failure mode.** `https://nuget.modelingevolution.com` answered `HTTP 200` from WSL and refused
the connection from Windows (`curl` exit 7, 5/5 probes) from roughly 16:30 onward — having served
`ModelingEvolution.Drawing 1.13.0.71` to the same `dotnet.exe` at 16:22. Only Windows was affected;
the hosted runner restores the whole solution cleanly, so this never reached CI.

**Who it breaks.** Only projects with **floating** package versions, because a floating range forces
a live feed query where a pinned version is satisfied from the global cache:
`RocketWelder.SDK.Robotics.Core` (`System.Reactive 6.*`), `Robotics.Core.Tests`,
`AdaptivePoints.Tests` and `Devices.Robot.Tests` (`NSubstitute 5.*`) — all `NU1301`, none related
to this branch.

**Workaround.** Restore those projects from nuget.org alone
(`dotnet.exe restore <proj> -s https://api.nuget.org/v3/index.json`), then build the solution with
`--no-restore`. Side effect worth knowing: it leaves those three test projects in a state where
`dotnet test` exits without producing a summary, which is why `.github/workflows/test.yml` does not
yet list them.
