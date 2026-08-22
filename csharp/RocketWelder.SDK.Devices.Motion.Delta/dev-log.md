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

## Test counts

| | Count | Where they run |
|---|---|---|
| Unit | **164** | Anywhere. No sockets: the axis drives an `IModbusChannel` fake that holds registers and nothing else. |
| Live-simulator | **20** | Against a running `delta-positioner-sim` over real Modbus TCP. They **skip with a stated reason** when none is reachable, which on a CI runner is always. |
| **Total** | **184** | |

Of the live-simulator tests, 6 are FR-11 watchdog kill-tests, 4 are advisory-lease tests and 2 are
the NFR-5 stop budget.

*(The first report of this iteration said 104/18; the true split at that commit was 103/19. Corrected
here and in the counts above, which are for the post-review state.)*

## Review round (verdict CHANGES REQUIRED on `8962d9e`)

Three blockers and a minors list, all closed on the same branch. What changed, and what each was:

### Blockers

1. **`HomeAllAsync` turned a cancellation into `InvalidOperationException`.** `RunOperationAsync`
   rethrows `OperationCanceledException`, so a cancelled home completes **Canceled**, not Faulted —
   and `First(t => t.IsFaulted)` then threw "Sequence contains no matching element" in place of the
   cancellation the caller asked for (AC-10). Now a fault outranks a cancellation and is rethrown
   with its stack intact via `ExceptionDispatchInfo`; when nothing faulted, every task was cancelled
   and that is rethrown as-is. **Control run against the pre-fix code confirms the new test fails
   with exactly the reported symptom.**

2. **Sync-over-async in the homing poll.** `LatchFiredNoWait` blocked a threadpool thread on a
   Modbus round-trip every ~150 ms of the creep phase — on a starved pool a deadlock, not merely
   waste. `JogUntilAsync` now takes `Func<bool[], ValueTask<bool>>`; the `||` still short-circuits,
   so the extra read only happens on polls where the cam is not seen.

3. **Untested frozen-contract clauses**, +48 unit tests across four new classes — `SelfCheckTests`
   (FR-7/AC-7), `LimitSwitchTests` (`LimitTripped` and the Min|Max wiring-fault clause),
   `PositionerTests` (the whole device surface) and `ConfigValidationTests`. Blocker 1 is precisely
   the bug the device-level gap let through: every axis-level test passed while it was live.

### Minors, all sweeped

- `InMemoryAxisStateStore` → `ConcurrentDictionary`. Axes home concurrently, so it really is
  written from several threads.
- `JsonAxisStateStore` gained an optional `ILogger` and now **logs the corrupt-file case at Error**
  — this is the "we lost the zero" seam, and an axis that silently forgets zero drives confidently
  to a wrong absolute angle. Its deserialised dictionary is also re-keyed through
  `OrdinalIgnoreCase`: `JsonSerializer` always builds an ordinal one, so a file written as
  `"Turntable"` would have stopped answering to `"turntable"`.
- **The FR-5 hole is closed.** `MoveHz`, `SeekHz` and `NudgeHz` never pass through the
  caller-facing check, and `JogStartAsync` raised anything under the floor with a silent
  `Math.Max` — the one place "rejected, never clamped" did not hold, and the place a persisted
  0 °/s quietly became a real 1 Hz move. Now `DeltaAxisConfig.Validate()` runs at construction, a
  persisted speed outside the range is rejected in favour of the configured default (out loud), and
  the jog's floor check throws instead of raising. Control run confirms the old code commanded
  1.0 Hz where the default is 30.
- `ModbusChannel.DisconnectAsync` replaces the sync `Disconnect`, which blocked on the priority gate
  from inside an async caller.
- `MoveVelocityAsync` no longer leaks its linked CTS: the supervisor owns and disposes it on every
  path, and `StopAsync` clears `_abort`. Writing that surfaced a further bug — **if the caller
  cancelled their own token rather than calling `StopAsync`, nothing ramped the axis down.** The
  supervisor now distinguishes the two by the axis state (`StopAsync` moves to `Stopping` *before*
  cancelling) and performs the stop itself, which is what FR-2's "cancelling it stops the axis"
  actually requires.
- `PriorityGate.Dispose` now fails everything still queued. Refusing only new arrivals left a waiter
  with a non-cancellable token parked forever — a hang on the shutdown path. `Waiter.Priority` was
  dead and is gone.
- `DeltaPositioner` implements `IAsyncDisposable`; the synchronous `Dispose` no longer blocks on
  network I/O, abandoning the beat locally instead (the lease then expires on its own, which is the
  case the watchdog bounds).
- The beat-rate test is driven by the injected `TimeProvider`
  (`Microsoft.Extensions.TimeProvider.Testing`) rather than by wall clock.
- The live sense test now measures the **distance actually travelled** through the raw encoder
  count, with a Shortest-sense control on the same target — it previously only re-checked arithmetic
  a unit test already pinned.
- `DisconnectAsync` calls `StopAllAsync` first (reviewer's suggestion): a graceful disconnect of a
  moving positioner brings the axes to rest before dropping the beat, instead of handing a turning
  machine to whatever attaches next and leaving the watchdog to do a shutdown's job.

Not acted on, by instruction: **Q1** (`MotionError.MotionFailed` — a contract change the user
decides), **Q3** (the unhomed-turntable / `RequiresHoming` question — Daniel's), and any ladder or
epic-doc edit.

### Second round: five follow-ups (verdict APPROVE with follow-ups, `10e075e`)

1. **A vacuous assertion of my own.** `DisconnectingStopsTheAxesBeforeReleasingTheLease` never
   called `ConnectAsync`, so no lease was ever held, the release index was always −1, and an
   `if (leaseRelease >= 0)` guard skipped the assertion the test is named for. It now connects,
   checks D131 really holds our owner id first, and asserts both indices found. **Control: removing
   the stop-before-release step fails the test, where the guarded version passed.** A second test
   pins the other half of the ordering — the lease is claimed before any write that could move the
   machine.
2. `Abandon()` had been inserted between `ReadRegisterAsync`'s `<remarks>` and its declaration, so
   the `ContinueWith` explanation documented the wrong method. Moved back.
3. **The supervisor no longer reports `Standstill` when the ramp-down did not reach the drive.** It
   faults with `CommunicationLost` instead: claiming "at rest" about a machine nobody managed to stop
   is the worst available answer, and the watchdog catching it a second later does not make the state
   true while it is being read.
4. `JogStartAsync`'s bounds carry a **four-ULP representation tolerance**. The °/s → Hz conversion is
   four floating-point operations — exact for today's calibration, but one lost bit in a re-measured
   one would make `MoveVelocityAsync(axis.MinSpeed)` throw `UnreachableSpeed`, an axis refusing its
   own advertised minimum. Four ULP near 50 Hz is ~3·10⁻¹⁴ Hz, twelve orders under the drive's
   0.01 Hz register resolution. Guarded both ways: both axes accept their advertised Min and Max, and
   a control pins that a full 0.01 Hz below the floor is still rejected, so the slack cannot drift
   into a physical allowance.
5. `Validate()` bounds-checks `HomeSensorInput` and `LimitInputs` against `DeltaRegisters.InputCount`
   — input 12 used to surface as an `IndexOutOfRangeException` from the middle of a jog rather than
   the named startup failure. It also rejects both limits naming one input (the axis could never tell
   which end it rests on) and a home sensor colliding with a limit, with a control that the real
   machine's X7 / X5–X6 are accepted.

---

## Defects found while building this

Four during the build, all fixed; two were in the code and two in my own tests. Three more came out
of the review round and are listed above.

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
| Direction-check threshold `20` raw counts | `DeltaAxis.VerifyDirectionCoreAsync` | **Inherited** from the port, where it was a bare literal. It is two encoder quanta (the quantum is 10 counts = 0.036°), so it reads as "more than noise" — but nothing measured how far the axis actually creeps during the check's 1.5 s jog, so the margin above the noise floor is unquantified. Pinned from both sides by `SelfCheckTests` so it cannot drift silently; worth confirming on the bench alongside the tilt pulse calibration. |
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
- **The "exits without a summary" symptom is stale local NuGet assets, not the tests.** The three
  promoted suites intermittently produce *no output at all* with exit code 0 when run with
  `--no-restore` against assets the feed outage left half-written; an explicit
  `dotnet restore --source https://api.nuget.org/v3/index.json` cures it and they then report 14, 10
  and 237 passing every time. This is why `test.yml`'s explicit `restore` step matters, and why the
  promotion is safe there even though the symptom is reproducible on this box. If a future run sees
  an empty result locally, restore before concluding anything about the suite.
- `Microsoft.Extensions.Logging.Abstractions` is pinned to **10.0.7**, matching the rest of the repo.
  9.0.0 (the port's version) is a package downgrade against `Automation.Abstractions` and fails the
  build as NU1605.
- No `<Version>` in the csproj — CI injects from tags.
