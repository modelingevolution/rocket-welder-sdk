# `RocketWelder.SDK.Devices.Motion.Delta` — development log

Epic 065 It-2. What was decided, what was deviated from, where every unmeasured constant came from,
and what the reviewer still has to rule on.

**Source of the code:** Daniel's raw port, branch `feature/epic-065-positioner-port-raw`
(base `7a2811f`). That branch is untouched. This is a **lift onto the frozen contract**, not a
rewrite: the approach machinery — staged and continuous approach, the micro-pulse endgame, the
homing sequence, the limit-retreat path — is his, moved across with error mapping, typed speeds and
the state machine wrapped around it.

**Contract:** `requirements.md` and `architecture.md` are frozen. Nothing here changes them. Where a
gap was found, it is recorded below as a question for the reviewer rather than closed in code.

---

## The hard boundary, and where it shows up in the code

The simulator's coast model is the programmed ramp alone and it runs ~7 % fast on long moves. So:

- **No lead distance, braking margin or continuous→pulse handover was tuned or asserted against the
  simulator.** Those constants came across from the port unchanged and carry `BENCH-MEASURED`
  comments at their definitions in `DeltaPositionerDefaults`.
- Simulator tests assert **logic** — state machine, register values and their order, watchdog, lease.
  Never a duration, never a coast distance.
- One place this bit: a move against the simulator will not settle inside the machine's ±0.05°,
  because the simulator's micro-pulse travels about **twice** what the bench-measured pulse
  calibration predicts. The integration tests therefore use a 0.2° tolerance —
  `LiveSimulator.SimulatorTolerance`, labelled at length as not a machine number, matching what the
  simulator repository's own smoke test does for the same reason. **The production defaults are
  untouched**, and `TypedSpeedTests` pins them.

---

## Deviations from the port, with reasons

### 1. The move loop works in **unwrapped** angle

The port recomputed the shortest path on every loop iteration, inside `DeltaTo`. This version
resolves the wrap and the `RotationSense` **once, at command time**, into an absolute target in
unwrapped angle space, and the loop then just closes on a fixed number.

Why: the contract requires `MoveRelativeAsync` to be *unbounded* on a wrapping axis — a +720° delta
must really turn twice. Re-deciding the shortest path each iteration normalises that to 0° and the
axis stands still. It also makes `RotationSense` mean what it says: the path is chosen once, not
re-chosen mid-move by whichever side of 180° the axis happens to be on.

Consequence worth knowing: an overshoot past the target is now corrected by reversing rather than by
going the long way round again. That is what the non-wrapping path already did.

### 2. `MoveVelocityAsync` is available on the **limited** axis too, with a supervisor

The port refused `RotateAsync` on a non-continuous axis. The contract does not forbid it, and jogging
the tilt axis at a velocity is a real need. But the port's velocity jog is open-loop and does **not**
watch the limits, which on an axis that has them is how you drive into a switch.

So velocity motion starts a supervisor task that polls the limit inputs at the approach cadence and
faults with `LimitTripped`. The supervisor is also where "the token remains observed after the task
completes" lives — cancelling the token passed to `MoveVelocityAsync` stops the axis, as FR-2
requires.

### 3. `AxisCapabilities.Homing` is declared on **both** axes, and `HomeAllAsync` homes both

The port's `HomeAllAsync` homed only axes with `RequiresHoming`, which is false on the turntable. The
contract says `HomeAllAsync` homes every axis declaring `Homing`, and both axes physically have a
home cam and a working latch, so declaring otherwise would be a false capability.

**Behaviour change to flag to Daniel:** `HomeAllAsync` now also homes the turntable, which can take
up to a full revolution. It is arguably the correct behaviour — the turntable's wrap domain
[0°, 360°) is defined *relative to its zero*, and without homing that zero is wherever the encoder
happened to be at power-up. `RequiresHoming` still governs the `NotHomed` rejection on absolute
moves, so nothing else changes.

### 4. Speed bounds are one pair, from `MinJogHz`/`MaxMoveHz`

The port used a different ceiling for positioning (`MaxMoveHz`) than for jogging (`MaxJogHz` = 60).
The contract has a single `MinSpeed`/`MaxSpeed` pair per axis, so both resolve from
`MinJogHz`/`MaxMoveHz`. `MaxJogHz` survives only as a hard guard inside `JogStartAsync` — nothing
reaches it through the public API any more.

### 5. `IAxisStateStore` lives in the **adapter**, not the contract

`log/p1-revision.md` §5 lists it under "Keep", and It-1 did not put it in `Devices.Motion`. Leaving
it here is deliberate: a derived-position axis is a Delta speciality (the drive runs in speed mode
because the encoders sit behind the gearboxes), so the captured zero is a number *this adapter*
computes and owns. An axis whose drive knows its own absolute position has nothing to store, and
putting the lifecycle on `IMotionAxis` would make every implementation carry it. OQ-4 — where the
file lives on a device deployment and who backs it up — remains open.

### 6. `ModbusChannel` sits behind an `IModbusChannel` interface

So the axis's own logic — state machine, register write order, homing latch sequence, speed
conversion — is testable without a socket. The test double holds registers and **nothing else**; it
applies exactly one wiring rule (the output-frequency status word follows the commanded frequency
while M4 is on) and the position never moves. Anything that has to move goes to the live simulator.
This was a deliberate guard against the double quietly growing into a second simulator with its own
physics, which would be a model of a model.

---

## New in this iteration (not in the port)

### `PriorityGate` — the channel's two exceptions to FIFO

FR-11 and NFR-5 both need the channel to be more than a lock:

- **Stop lane** preempts queued move traffic (AC-23). Without it a 26 s homing hold queues ahead of
  the stop and 200 ms is not reachable by any amount of luck.
- **Heartbeat deferral is bounded at 200 ms**, not merely deprioritised (AC-24). An unbounded yield
  lets a long move starve the beat and self-trip the watchdog.

Preemption is of the **queue**, not of the frame in flight — one Modbus transaction is a few
milliseconds and the move loop releases the gate across its own polling delays.

### `DeltaHeartbeat` — FR-11's client half

Connection-lifetime, not motion-lifetime (a beat that ran only during moves would trip after every
successful one). **Never writes 0**: the drive's network arms on the first *change* of D130 and D130
powers up at 0, so a first beat of literally 0 leaves the watchdog silently disarmed. The counter
starts at 1 and skips 0 on wrap.

The beat's paired read covers D132/D133 in one transaction — that is where a trip is noticed, and its
cadence is what gives an idle axis a `StatusChanged` rhythm (narrowing OQ-2).

### `AdvisoryLease` — the epic's reference implementation

FR-11 names the adapter's as the reference and the simulator's as the mirrored oracle. The 8-row
vector table from `delta-positioner-sim/docs/register-map.md` is copied **verbatim** into
`AdvisoryLeaseTests`: rows 1–7 as one `[Theory]`, row 8 (two owners, both granted — advisory, no CAS
on Modbus) as its own `[Fact]`, exactly as the simulator's mirror does. Changing behaviour means
changing that table first and telling the other side.

Lease acquisition costs the sampling wait **only for a foreign owner**. Rows 1, 6 and 7 do not depend
on the heartbeat's age at all, so an unowned or already-ours register is decided from one read.

---

## Defects found while building this

Four, all fixed; two were in the code and two in my own tests.

1. **`AxisStatus` was cached beside the state**, so a reset axis went on reporting the `DriveFault`
   it had just been reset out of — precisely the stale-boolean shape AC-1 exists to forbid. The
   status is now composed from the live state over the last measurement, and a test pins that the
   two can never disagree. A failed status read no longer faults the axis either: a dropped frame is
   not a failed move.

2. **`ContinueWith(…, OnlyOnRanToCompletion)`**, inherited from the port's `ReadPositionAsync` shape,
   turns a **faulted** read into a **cancelled** task. A drive that had gone away therefore surfaced
   as `TaskCanceledException` — "somebody cancelled" rather than "the transport is dead" — sailing
   past every `catch (MotionException)` written to handle it. Replaced with plain awaits.

3. **A disposed `ModbusChannel` silently reopened its socket** on the next transaction, because
   `EnsureConnected` did not consult `_disposed`. Beyond being wrong, it made the watchdog kill-tests
   unable to emulate a kill at all: the "dead" commander came back for one write.

4. **My first four kill-tests killed a watchdog that had never armed.** The network arms on the first
   *change* of D130, and the tests disposed the channel before the first beat had gone out — so they
   were passing or failing for the wrong reason. They now wait for a real change in the register
   first. Evidence that this mattered: the tilt drive had not tripped once across an entire earlier
   run, and does now.

Point 4 is why the simulator's own log is checked after every integration run rather than just the
green bar.

---

## Provenance of every constant that is not measured

Following the convention the simulator repository established. Anything not listed here is measured
on the machine on 2026-08-19 and recorded in `current-state.md`.

| Value | Where | Why it is not measured |
|---|---|---|
| Tilt speed fit `0.2214·hz − 0.081` | `DeltaPositionerDefaults.TiltSpeed` | **Derived.** The one recorded sweep is the turntable's — its slope is 97.4 % of the turntable's theoretical ratio and 239 % of tilt's, so it cannot be tilt's. Carried as the same measured 2.6 % slip on tilt's own gearing with the same measured 0.366 Hz dead band. Identical derivation to the simulator's, deliberately. **Risk R-4 — bench-measure before any tilt speed number is trusted.** |
| Tilt pulse calibration `Slope 0.7, DeadTime 0.0` | `DeltaPositionerDefaults.Tilt` | **Inherited, unverified**, from the port. The turntable's equivalent turned out to be wrong by 2× plus a dead time. It currently meets the 0.10° tolerance regardless — on inherited numbers. **Risk R-4.** |
| `MaxJogHz = 60` | `DeltaAxisConfig.MaxJogHz` | **Inherited.** A bare constant in the port; nothing measured the drive's real ceiling and the recorded sweep stops at 50 Hz. Named as configuration here rather than hidden so a bench session can pin it. The simulator's `MaxOutputHz` carries the same 60 for the same reason. |
| D130–D133 addresses | `DeltaRegisters` | **Proposed**, chosen in the simulator repository in the same free D block the existing program already uses. FR-11 says these addresses join the documented ladder register map but names none, and the port has none to copy. **Pending ratification at the AC-25 ladder edit** — if the vendor's ISPSoft project puts them elsewhere, one constant block changes and nothing else does. |
| `SimulatorTolerance = 0.2°` | `LiveSimulator` (tests only) | **Not a machine number and must never travel back into the defaults.** The simulator's endgame is not the machine's. Matches the simulator repo's own smoke test. |

---

## Open questions for the reviewer

**Q1 — `MotionError` has no member for mechanical non-completion.** A stall, a positioning timeout, a
move that stopped outside tolerance and a home latch that never fired all currently map to
`DriveFault`, with the specific cause in the message (see `DeltaAxis.Mechanical`). None of them is
"the drive reported a fault of its own". The port had distinct codes (`Stalled`, `Timeout`,
`PositionNotReached`, `HomeLatchFailed`); the frozen enum has none of them, and FR-6/AC-19 promise a
caller can branch without matching text. All four share one caller response — reset and re-command,
possibly re-home — so an additive `MotionError.MotionFailed` would say it honestly. **Adding one is a
contract change and was not made here.**

**Q2 — a clean disconnect leaves a spurious watchdog trip behind.** Observed live: after
`DisconnectAsync` stops the beat, the drive's network is still *armed*, so one second later it trips
and latches `D132 = 1` on an axis that is already stopped and was shut down deliberately. The next
attach then reports `WatchdogTripped` and demands a reset — the "fault everyone learns to ignore"
that FR-11's arming semantics were written to prevent at power-up, reappearing at shutdown.

The adapter cannot fix this from its side: the ladder offers no disarm, and clearing `D132` on attach
would swallow a *real* trip a human should see. **Recommendation for the AC-25 ladder edit:** disarm
the network when `D131` (the lease owner) goes to 0. A graceful shutdown already writes that, so it
costs one rung and no new register, and an ungraceful death still leaves `D131` set and the watchdog
armed — which is exactly the case it exists for. Flagging rather than fixing, because it is a
cross-repository behaviour change.

**Q3 — `HomeAllAsync` now homes the turntable** (deviation 3 above). Correct by the contract and
arguably correct by the machine, but it is a behaviour change against the port and it costs up to a
revolution. Worth Daniel's eye.

---

## Not done here, and why

- **Bench measurements** (tilt speed fit, tilt pulse calibration, home-cam position) — hardware only.
  Each gap is marked at its constant and listed above.
- **The plugin, the hub roster and the Add-device dialog** — It-3.
- **`ProgramScaffolder` and the FR-12 block** — It-5.
- **The ladder edit** — It-4, gated on the vendor's ISPSoft export (AC-25).
- **AC-4, AC-5, AC-22** — bench-only by the epic's own boundary; nothing in this iteration claims
  them.

## Ride-alongs from the It-1 review

- `Degree<T>`'s constructor is private — this project uses `Degree<double>.Create(x)` throughout.
  (Note for the docs: the review note says "`Degree.Create(x)`", but `Degree` is the non-generic
  helper class holding only `Sin`/`Cos`; the factory is on `Degree<T>`.)
- **AC-26 transitive walk filtered through `IsFrameworkAssembly`.** The closure *records*
  shared-framework assemblies even though it does not walk through them, so the `sockets` marker
  would match `System.Net.Sockets` the moment anything in the closure named it directly — a false
  accusation about an assembly present in every .NET process. A control test pins that the new filter
  does not also wave through `System.IO.Ports` or `FluentModbus`.
- **AdaptivePoints, Devices.Robot and Robotics.Core promoted into `test.yml`.** They had been held out
  as "not observed producing a result locally"; that was the org NuGet feed being down (NU1301), not
  the tests. Restored from nuget.org and run with `--no-restore` they give 14, 10 and 237 passing.
  This project's own test suite is in the list too — its live-simulator tests skip with a stated
  reason when no simulator is reachable, which on a hosted runner is always.

## Environment notes

- Built and tested with `dotnet.exe` from WSL, per the org standard.
- The org NuGet feed `nuget.modelingevolution.com` was **down throughout** this iteration (NU1301 /
  connection refused). Everything restores cleanly from nuget.org; the NU1900 warnings in every build
  log are the same outage and are not caused by anything here.
- `Microsoft.Extensions.Logging.Abstractions` is pinned to **10.0.7**, matching the rest of the repo.
  9.0.0 (the port's version) is a package downgrade against `Automation.Abstractions` and fails the
  build as NU1605.
- No `<Version>` in the csproj — CI injects from tags.
