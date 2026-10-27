# Input processing pipeline

Implemented in `src/OpenController.Core/Processing` and covered by `tests/…/StickProcessorTests.cs`
and `CurveAndTriggerTests.cs`. Processing runs on the sampling thread for every sample.

## 1. Normalization

XInput stick values are signed 16-bit integers. To make both extremes map exactly to ±1:

```
n(v) = v / 32767   for v ≥ 0
n(v) = v / 32768   for v < 0
```

The inverse rounds half away from zero and clamps to [-32768, 32767]. Normalize → denormalize is
**bit-exact** for all 65 536 inputs (tested exhaustively). Triggers use `t = v / 255`.
+Y means *up* (XInput convention); DirectInput Y is inverted on input.

## 2. Stick order of operations

For a raw stick sample **r** = (x, y):

| Step | Operation |
|---|---|
| 1 | read raw values |
| 2 | normalize to [-1, 1] |
| 3 | offset correction (optional): **v** = **r** − **o**, where **o** is the measured resting offset |
| 4 | input range: x ← x / R<sub>x</sub>, y ← y / R<sub>y</sub> (R ∈ [0.5, 1] = raw value that counts as full deflection) |
| 5 | inversion: x ← −x and/or y ← −y |
| 6 | deadzone (radial or axial, below) → normalized magnitude t ∈ [0, 1] |
| 7 | response curve: t ← f(t) |
| 8 | anti-deadzone: t ← a + (1 − a)·t (only outside the inner deadzone) |
| 9 | sensitivity: t ← s·t |
| 10 | clamp: t ← min(t, S) with output saturation S ∈ [0.05, 1]; each axis is clamped to [-1, 1] |
| 11 | denormalize to XInput units for the virtual controller |

**Identity shortcut:** when every parameter is neutral (d = 0, outer = 0, a = 0, s = 1, linear curve,
no inversion, R = 1, S = 1, no offset) the raw vector is returned unchanged. Without this, the circular
clamp in step 10 would shrink the corners of square-gate sticks. Passthrough is therefore bit-exact.

### Radial deadzone (default)

With r = √(x² + y²), inner deadzone d and outer deadzone o:

```
r ≤ d          →  output 0
r > d          →  t = clamp((r − d) / ((1 − o) − d), 0, 1)
output vector  =  (x / r, y / r) · m(t)
```

- The output is exactly zero on and inside the deadzone circle.
- At r → d⁺, t → 0, so without anti-deadzone the output is **continuous** at the edge.
- The direction (x/r, y/r) is preserved, so diagonals are not distorted.
- r ≥ 1 − o already yields full output (outer deadzone).
- Valid settings require (1 − o) − d ≥ 0.05.

### Axial deadzone (optional)

The same magnitude mapping is applied to |x| and |y| separately and the signs are restored.
This snaps toward the axes near the center (useful for some emulators) but does not preserve direction.

### Response curves (monotonic on [0, 1], f(0) = 0)

| Curve | Definition | Valid parameters |
|---|---|---|
| Linear | f(t) = t | — |
| Power | f(t) = t^k | 0.2 ≤ k ≤ 5 |
| S-curve | f(t) = t^k / (t^k + (1 − t)^k) | 0.2 ≤ k ≤ 5 |
| Custom | piecewise-linear through points | first point (0, 0), last x = 1, x strictly increasing, y non-decreasing, y ∈ [0, 1] |

Every valid curve is non-decreasing, so the complete magnitude mapping m(r) is monotonic for every valid
configuration (tested by sampling 2 001 radii for several configurations).

### Anti-deadzone — the deliberate discontinuity

Many games apply their own deadzone. Anti-deadzone a raises the smallest non-zero output to a, so the first
movement beyond *our* inner deadzone immediately exceeds *the game's* deadzone:

```
r ≤ d  →  0
r > d  →  a + (1 − a)·f(t)      (≥ a)
```

The output therefore **jumps from 0 to a** at r = d. This is intended, explained in the GUI, and should
only be used to cancel a known in-game deadzone.

### Invalid configurations

`StickSettings.Validate()` / `TriggerSettings.Validate()` reject out-of-range, NaN or inconsistent values.
The GUI keeps the last valid settings active and shows the error; invalid profiles are never saved, and invalid
profile files are reported instead of being loaded.

## 3. Triggers

```
t_in ≤ d                →  0
otherwise  t = clamp((t_in − d) / (M − d), 0, 1)       M = "full press at" (outer deadzone)
           t ← f(t);  t ← a + (1 − a)·t;  t ← s·t;  t ← clamp(t, 0, 1)
output byte = round(255·t)                              always within 0..255
pressed     = t > 0 and t ≥ activation threshold
```

## 4. Remapping (after processing)

See `ButtonRemapper`:
- Unmapped controls drive themselves; a trigger left on *Default* keeps its analog value.
- A trigger remapped elsewhere no longer drives its own analog output.
- A control mapped onto a trigger presses it fully (255).
- Several sources on one target are OR-combined.
- Keyboard targets only produce key presses when keyboard output is explicitly enabled.

## 5. Diagnostics formulas

**Drift test** (`DriftAnalyzer`), samples pᵢ (raw, normalized) while the stick rests for 4 s:

| Metric | Formula |
|---|---|
| mean offset | **m** = mean(pᵢ) |
| max offset | max ‖pᵢ‖ |
| RMS noise | √mean(‖pᵢ − **m**‖²) |
| radial deviation | mean / standard deviation / 99th percentile of ‖pᵢ‖ |
| recommended deadzone | ceil₀.₀₀₅(1.15 · max(max‖pᵢ‖, mean‖pᵢ‖ + 4σ) + 0.01), clamped to [0.02, 0.5] |
| … with offset correction | same formula on ‖pᵢ − **m**‖ |

The measurement is **rejected** if any sample exceeds radius 0.40, if the 99th-percentile distance from the
median exceeds 0.08 (stick touched), with fewer than 100 samples, or with non-finite values.

**Circularity test** (`CircularityAnalyzer`): samples with radius ≥ 0.5 are binned into 72 sectors (5°); the
maximum radius per sector approximates the gate. Reported: angular coverage, max/min/mean edge radius,
mean absolute and RMS error versus the ideal radius 1.0, roundness = min/max. Raw and processed outputs are
analyzed separately.
