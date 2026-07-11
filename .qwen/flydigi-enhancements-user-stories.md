# Flydigi Cooler Enhancement — User Stories

> Derived from THRM project analysis. Each story includes research notes on how to port specific functionality from THRM (Go/Wails) to UXTU (C#/WPF).

---

## Story 1: Thermal Prediction (Feed-Forward Control)

### User Story
As a user with a thermally demanding workload (gaming, compiling, rendering), I want the fan to ramp up *before* my temperature spikes, not after, so that temperature stays smooth without sudden fan noise.

### Acceptance Criteria
- [ ] The control loop predicts temperature 6 seconds ahead using recent temperature slope + power surge detection
- [ ] Predicted temperature replaces measured temperature for RPM curve lookup
- [ ] Cooling is never predicted (rise clamped to ≥ 0) — ramp-down still uses measured temperature
- [ ] Trend Gain slider (1–12, default 5) lets users tune prediction sensitivity
- [ ] "Predictive Boost" toggle in Smart Control settings to enable/disable the feature
- [ ] Prediction uses measured temperature for learning (not predicted), to avoid contaminating learned offsets

### Research: THRM Implementation Details

**File:** `THRM/internal/smartcontrol/prediction.go`

The predictor maintains a ring buffer of 6 samples (timestamp, CPU temp, GPU temp, CPU power, GPU power). On each sample:

1. **Temperature Slope** — Least-squares linear regression on the last 6 temperature samples. Slope clamped to [0, 2.0°C/s]. Contribution = `slope * 6.0s * gain`.

2. **Power Surge Lead** — Compares current power to the average of previous samples. If surge > 5W: `surge * 0.018 * gain`, capped at 3.0°C. At default gain, a 100W step = ~1.8°C advance.

3. **Total Rise** = slope contribution + power contribution, clamped to [0, 6.0°C].

4. **Trend Gain** = `0.45 + trendGain * 0.09`, range [0.54, 1.53]. Default trendGain=5 → gain=0.90.

**C# Port Notes:**
- Replace `ThermalPredictor` struct with a C# class. The ring buffer is a `CircularBuffer<ThermalSample>` of size 6.
- Least-squares regression is a straightforward 5-line implementation.
- **Critical dependency: power readings.** UXTU's `FlydigiTemperatureProvider` currently reads only temperature. THRM reads CPU package power and GPU board power via LibreHardwareMonitor's `SensorType.Power`. UXTU already references `LibreHardwareMonitorLib` — we need to extend the provider to also read power sensors.
- The `TemperatureData` struct needs `CPUPower` and `GPUPower` fields (float, watts, 0 when unavailable).

**LibreHardwareMonitor Power Sensor Access (from THRM's TempBridge):**
```csharp
// THRM bridge collects power sensors the same way as temp sensors:
foreach (var sensor in hardware.Sensors)
{
    if (sensor.SensorType == SensorType.Power && sensor.Value > 0 && sensor.Value <= 2000)
        powerSensors.Add(new PowerSensor { Key = key, Name = sensor.Name, Value = (float)sensor.Value });
}
```

CPU package power preferred keywords: `"Package"`, `"CPU Package"`, `"PPT"`, `"Power"`.
GPU board power preferred keywords: `"GPU Power"`, `"Board Power"`, `"Total Graphics Power"`, `"Power"`.

---

## Story 2: Multi-Sensor CPU Temperature Averaging

### User Story
As a user with a multi-core CPU, I want the cooler to respond to the average temperature across selected cores (e.g., all cores, or just Package + Average), rather than a single sensor that might not represent overall thermal load.

### Acceptance Criteria
- [ ] The app discovers all available CPU temperature sensors from LibreHardwareMonitor
- [ ] A multi-select control lets users pick which CPU sensors to average (e.g., "Core 0", "Core 1", "Average", "Package")
- [ ] When multiple sensors are selected, their temperatures are averaged arithmetically
- [ ] When no sensors are selected ("Auto"), the app picks the best single sensor (same logic as today)
- [ ] Sensor list is populated on page load and shows current values

### Research: THRM Implementation Details

**File:** `THRM/internal/temperature/temperature.go` (Go reader) + `THRM/bridge/TempBridge/Program.cs` (C# bridge)

THRM's TempBridge already uses LibreHardwareMonitor to discover sensors. The discovery walks:
```csharp
foreach (var hardware in computer.Hardware)
{
    if (hardware.HardwareType == HardwareType.Cpu)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
            {
                var key = $"cpu/{hardware.Name}/{sensor.Name}";
                sensors.Add(new TemperatureSensor { Key = key, Name = sensor.Name, Value = (int)sensor.Value });
            }
        }
        // Also walk SubHardware for some CPU models
        foreach (var sub in hardware.SubHardware)
        {
            sub.Update();
            // collect sensors from sub-hardware too
        }
    }
}
```

**Multi-sensor averaging** (Go process, after receiving data from bridge):
```go
func averageSelectedCpuTemp(temp *types.TemperatureData, selection types.TemperatureSelection) {
    if len(selection.CpuSensors) == 0 {
        return // auto mode, no averaging
    }
    var sum, count int
    for _, sensor := range temp.CpuSensors {
        // case-insensitive key match
        if slices.ContainsFunc(selection.CpuSensors, func(s string) bool {
            return strings.EqualFold(s, sensor.Key)
        }) {
            sum += sensor.Value
            count++
        }
    }
    if count > 0 {
        temp.CPUTemp = (sum + count/2) / count // rounded arithmetic mean
    }
}
```

**C# Port Notes:**
- Extend `FlydigiTemperatureProvider` with:
  - `GetCpuSensors()` → returns `List<TemperatureSensor>` (key, name, value)
  - `GetCpuTemperature(IEnumerable<string> selectedKeys)` → averages selected sensors
- Add `CpuSensors` (multi-select) to the UI. WPF has no built-in multi-select ComboBox, so use a `ListBox` with `SelectionMode="Multiple"` or a tagged multi-select control.
- The sensor discovery is a one-time operation on page load (sensors don't change at runtime).

---

## Story 3: Multi-GPU Device & Sensor Selection

### User Story
As a user with multiple GPUs (e.g., NVIDIA + Intel integrated, or dual NVIDIA), I want to choose which GPU's temperature the cooler responds to, and optionally which sensor on that GPU (Core, Hot Spot, etc.).

### Acceptance Criteria
- [ ] The app discovers all GPU devices (NVIDIA, AMD, Intel) and their temperature sensors
- [ ] A dropdown lets users select which GPU device to monitor
- [ ] A second dropdown lets users select which temperature sensor on the selected GPU
- [ ] "Auto" mode picks the best GPU (NVIDIA > AMD > Intel) and best sensor (Average > GPU Core > Core > Edge)
- [ ] GPU power sensor is also selectable for thermal prediction

### Research: THRM Implementation Details

**File:** `THRM/bridge/TempBridge/Program.cs`

THRM discovers GPUs:
```csharp
// For each GPU hardware type:
foreach (var hwType in new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel })
{
    foreach (var hardware in computer.Hardware.Where(h => h.HardwareType == hwType))
    {
        var vendor = hwType switch { HardwareType.GpuNvidia => "nvidia", HardwareType.GpuAmd => "amd", _ => "intel" };
        var key = $"{vendor}:{gpuIndex}:{hardware.Name}";
        // Collect temp sensors and power sensors for this GPU
    }
}
```

**GPU Selection Priority** (auto mode):
1. If user selected a device key → exact match
2. If user selected a sensor key → find GPU containing that sensor
3. Auto: score by vendor priority (NVIDIA=300, AMD=200, Intel=100), tiebreak by sensor count

**Sensor Keywords** (per-priority): `"Average"`, `"GPU Core"`, `"Core"`, `"Edge"`, `"Junction"`, `"Hot Spot"`, `"Temperature"`

**C# Port Notes:**
- Extend `FlydigiTemperatureProvider` with:
  - `GetGpuDevices()` → returns `List<GpuDevice>` (key, name, vendor, sensors[], powerSensors[])
  - `GetGpuTemperature(string? deviceKey, string? sensorKey)` → reads temp from selected device/sensor
- Add two ComboBoxes to the UI: GPU Device selection + GPU Sensor selection
- This is a relatively small change to the existing provider — it already iterates GPUs, just needs to expose the full list instead of picking the first one.

---

## Story 4: Advanced Learning Engine (Per-Curve-Point Offsets)

### User Story
As a user who wants the fan to learn the optimal curve for my specific laptop's cooling performance, I want the system to adjust learned offsets per curve point (not just per gear), account for thermal inertia, and apply noise-aware downshifts.

### Acceptance Criteria
- [ ] Learning tracks per-curve-point offsets instead of per-gear offsets
- [ ] Steady-state detection: learning only triggers when RPM has been stable and temperature variance is < 2°C for the configured learn window
- [ ] Learn Delay setting (1–8 steps, default 3) to account for thermal inertia before recording equilibrium points
- [ ] Learn Window setting (3–24 samples, default 8) controls how many samples are needed for steady-state detection
- [ ] Efficiency estimation via least-squares regression on equilibrium history (RPM vs temperature)
- [ ] Overheat penalty: when temp exceeds target, RPM increases proportional to delta/efficiency
- [ ] Noise-aware downshift: when temp is below target, downshift aggressiveness is weighted by local noise profile slope
- [ ] Learned offsets are smoothed over ±2 neighboring curve points (2 passes, pull limit 30 RPM)
- [ ] Max Learn Offset cap (100–2000 RPM, default 300) prevents runaway offsets
- [ ] Hard offset cap of 600 RPM prevents any single offset from being too aggressive

### Research: THRM Implementation Details

**File:** `THRM/internal/smartcontrol/learning.go`

This is the most complex feature to port. The key components:

**StableObserver** — per-curve-point bucket tracking:
- Each curve point has a bucket that accumulates temperature + RPM samples
- `pickBucketIndex(temp, curve)` assigns a temperature to the nearest curve point using midpoint splitting
- Steady-state requires: `learnWindow` samples with temp variance ≤ 2°C and RPM variance ≤ max(120, MinRPMChange)
- After steady-state: records an equilibrium point (meanRPM, meanTemp)

**Efficiency Estimation** — least-squares on equilibrium history:
- Needs ≥ 2 equilibrium points with ≥ 80 RPM spread
- `eff = -slope` where slope = regression of temp vs RPM (negative because higher RPM = lower temp)
- Clamped to [0.0008, 0.05] °C/RPM. Default fallback: 0.008 °C/RPM

**Offset Solver** — the learning decision:
```
alpha = 0.025 + (learnRate - 1) * 0.0125  // [0.025, 0.125]

if temp > targetTemp:
    step = alpha * (temp - target) / efficiency
    step = max(step, 20)  // min safety step

elif temp < targetTemp - comfortBand:
    gain = noiseDownGain(rpm, config)  // noise-aware weighting
    step = -alpha * (target - temp) / efficiency * gain

step = clamp(step, -80, +80)
```

**Comfort Band** = `max(hysteresis + 3, 3)` degrees below target. Default target=68, hysteresis=2 → band=5 → lowTarget=63.

**Noise-Aware Downshift** (`noiseDownGain`):
- If user has a noise profile, computes local noise slope vs average slope
- Steeper local slope (noisier range) → more aggressive downshift
- Gain clamped to [0.4, 1.8]

**C# Port Notes:**
- Replace `FlydigiLearningEngine` (currently per-gear) with a new `FlydigiLearningEngineV2` that works per-curve-point
- The `StableObserver` needs to track one bucket per curve point. Each bucket stores:
  - `List<double> samples` (ring buffer of `learnWindow` size)
  - `List<ushort> rpmSamples`
  - `List<(ushort rpm, double temp)> history` (equilibrium points, max 6)
  - `int settleCounter` (delay before learning starts)
- The efficiency regression is a simple least-squares on equilibrium history
- This is a ~400 line C# class. The math is all straightforward linear algebra.

---

## Story 5: User-Configurable Smart Control Parameters

### User Story
As a user who wants fine-grained control over how the fan responds to temperature, I want to configure parameters like target temperature, aggressiveness, hysteresis, ramp limits, and learning speed, so I can tune the cooling behavior to my preferences.

### Acceptance Criteria
- [ ] New "Smart Control" card in the Flydigi page with these configurable parameters:
  - Target Temperature (45–90°C, default 68)
  - Aggressiveness (1–10, default 5) — how aggressively to respond to temperature deviations
  - Hysteresis (0–8°C, default 2) — deadband to prevent oscillation
  - Min RPM Change (20–400, default 50) — minimum RPM change threshold
  - Ramp Up Limit (50–1200 RPM, default 220) — max RPM increase per cycle
  - Ramp Down Limit (50–1200 RPM, default 160) — max RPM decrease per cycle
  - Learn Rate (1–10, default 3) — how fast learning converges
  - Learn Window (3–24 samples, default 8) — samples needed for steady-state detection
  - Learn Delay (1–8 steps, default 3) — thermal inertia delay before learning
  - Trend Gain (1–12, default 5) — prediction feed-forward sensitivity
  - Max Learn Offset (100–2000 RPM, default 300) — cap on learned RPM offsets
  - Learning Bias: Balanced / Cooling / Quiet (default Balanced)
  - Filter Transient Spike (toggle, default On)
  - Predictive Boost (toggle, default On)
- [ ] All parameters are validated on input and clamped to valid ranges
- [ ] Parameters are persisted to settings JSON
- [ ] Separating symmetric ramp limit into asymmetric Ramp Up / Ramp Down limits

### Research: THRM Implementation Details

**File:** `THRM/internal/smartcontrol/config.go`

THRM normalizes every parameter on config load:
```go
func NormalizeConfig(cfg types.SmartControlConfig, curve []types.FanCurvePoint) (types.SmartControlConfig, bool) {
    defaults := types.GetDefaultSmartControlConfig(curve)
    // Each field validated against range, reset to default if out of bounds
    if cfg.TargetTemp < 45 || cfg.TargetTemp > 90 {
        cfg.TargetTemp = defaults.TargetTemp
        changed = true
    }
    // ... (repeated for every field)
}
```

**C# Port Notes:**
- Create a `SmartControlConfig` class (matching THRM's `SmartControlConfig` struct) with all parameters and `[JsonProperty]` attributes
- Add a `Normalize()` static method that validates ranges and resets to defaults
- Add a new "Smart Control" card to `FlydigiCooler.xaml` with sliders/number boxes for each parameter
- The existing `FlydigiSmartControl` class properties (`HysteresisDeadzone`, `MaxRpmChangePerCycle`) become driven by the config object
- Split `MaxRpmChangePerCycle` into `RampUpLimit` and `RampDownLimit` for asymmetric ramping

---

## Story 6: Per-Profile Learning Offsets

### User Story
As a user with multiple fan curve profiles (Silent, Balanced, Performance, Custom), I want each profile to maintain its own learned offsets, so that switching profiles doesn't lose the learning I've built up for a specific curve.

### Acceptance Criteria
- [ ] Learned offsets are stored per curve profile ID (not globally)
- [ ] Per-profile target temperature setting
- [ ] Per-profile learning bias setting
- [ ] Switching profiles restores the learned offsets for that profile
- [ ] Deleting a profile also deletes its learned offsets

### Research: THRM Implementation Details

**File:** `THRM/internal/types/types.go` (SmartControlConfig)

```go
type SmartControlConfig struct {
    LearnedOffsets          []int             // current active profile offsets
    LearnedOffsetsByProfile map[string][]int  // per-profile offsets (keyed by profile ID)
    TargetTempByProfile     map[string]int    // per-profile target temp
    LearningBiasByProfile   map[string]string // per-profile learning bias
}
```

When a profile is activated, THRM copies `LearnedOffsetsByProfile[profileId]` into `LearnedOffsets`. When learning produces new offsets, they're saved back to both the active slot and the per-profile map.

**C# Port Notes:**
- Add to `SmartControlConfig`:
  - `Dictionary<string, int[]> LearnedOffsetsByProfile`
  - `Dictionary<string, int> TargetTempByProfile`
  - `Dictionary<string, string> LearningBiasByProfile`
- When `ActiveProfile` changes in `FlydigiSmartControl`, load the offsets for that profile ID
- When learning updates offsets, save back to both the active array and the per-profile dictionary
- Persist the dictionaries in the settings JSON

---

## Story 7: Configurable Temperature Smoothing (EMA Sample Count)

### User Story
As a user who wants to control how smoothly the temperature reading transitions between readings, I want to configure the EMA sample count so I can choose between fast response (low sample count) and smooth filtering (high sample count).

### Acceptance Criteria
- [ ] EMA Sample Count setting (1–10, default 3) replaces the current fixed asymmetric EWMA
- [ ] Alpha formula: `alpha = 2.0 / (sampleCount + 1)` (standard EMA)
- [ ] Lower sample count → faster response, more noise; higher sample count → smoother, slower response
- [ ] Setting is persisted to settings JSON

### Research: THRM Implementation Details

**File:** `THRM/internal/coreapp/monitoring.go`

```go
// EMA smoothing with configurable sample count
func (a *CoreApp) applyEmaSmoothing(temp int, n int) int {
    alpha := 2.0 / float64(n+1)
    a.tempEMA = alpha*float64(temp) + (1-alpha)*a.tempEMA
    return int(a.tempEMA + 0.5)
}
```

THRM uses a symmetric EMA (same alpha for rising and falling). UXTU currently uses asymmetric EWMA (alpha=0.5 rising, 0.15 falling).

**C# Port Notes:**
- Replace `ApplyEwmaFilter()` in `FlydigiSmartControl` with a configurable EMA:
```csharp
private double ApplyEmaFilter(double rawTemp, int sampleCount)
{
    if (!_filteredTemperature.HasValue)
        return rawTemp;
    
    double alpha = 2.0 / (sampleCount + 1);
    return alpha * rawTemp + (1.0 - alpha) * _filteredTemperature.Value;
}
```
- Add `TempSampleCount` (1-10, default 3) to `Bs2ProSettings`
- Add a slider or number box in the UI
- **Design decision:** THRM uses symmetric EMA. UXTU currently uses asymmetric (faster rise response). The configurable EMA should be symmetric to match THRM, but we could offer an "Asymmetric Response" toggle that keeps the current behavior as an option.

---

## Story 8: Brightness Control for More RGB Modes

### User Story
As a user who uses RGB modes like Static, Rotation, or Breathing, I want to control brightness in all modes, not just Flowing, so I can dim the RGB lighting to my preference regardless of the animation mode.

### Acceptance Criteria
- [ ] Brightness slider is visible for Static, Rotation, Flowing, and Breathing modes
- [ ] Brightness is applied via the same protocol command (0x44 RgbDynamicMode with brightness parameter)
- [ ] Brightness persists per-mode in settings JSON
- [ ] Brightness defaults to 100% for all modes

### Research: THRM Implementation Details

**File:** `THRM/internal/device/device.go` (RGB methods)

THRM applies brightness to all RGB modes through the `SetBrightness()` method, which sends the 0x44 command with the brightness value. The `LightStripConfig` has a single `Brightness` field that applies to all modes.

UXTU currently only shows the brightness slider for Flowing mode (`spRgbSpeedBrightness` visibility is tied to Flowing). The protocol command is the same across modes — this is purely a UI visibility issue.

**C# Port Notes:**
- Change the visibility binding of `spRgbSpeedBrightness` (or just the brightness row) to show for Static, Rotation, Flowing, and Breathing modes
- Store brightness per-mode in settings (e.g., `StaticBrightness`, `RotationBrightness`, `FlowingBrightness`, `BreathingBrightness`) or use a single brightness that applies to all modes
- **Design decision:** THRM uses a single brightness for all modes. UXTU currently has `RotationBrightness` as a separate setting. Options:
  1. Single brightness for all modes (THRM approach) — simpler
  2. Per-mode brightness — more control but more UI clutter
  3. Global brightness + per-mode override — best of both

---

## Implementation Priority & Dependencies

```
Story 7 (EMA) ──→ Story 5 (Config) ──→ Story 4 (Learning) ──→ Story 6 (Per-profile)
                                                    │
Story 2 (Multi-sensor) ──→ Story 1 (Prediction) ───┘
Story 3 (Multi-GPU) ──────────────────────────────┘
                                                    │
Story 8 (Brightness) ──→ (independent, can be done anytime)
```

### Phase 1: Foundation (Stories 2, 3, 7)
- Extend `FlydigiTemperatureProvider` with sensor discovery, power readings, multi-GPU support
- Add EMA sample count configuration
- These are independent infrastructure changes needed by Stories 1, 4, and 5

### Phase 2: Smart Control (Stories 5, 8)
- Add `SmartControlConfig` class and UI card
- Replace fixed parameters with configurable ones
- Fix brightness visibility

### Phase 3: Advanced Features (Stories 1, 4, 6)
- Implement thermal prediction
- Rewrite learning engine for per-curve-point offsets
- Add per-profile offset storage

---

## Files to Modify (Summary)

| File | Stories |
|------|---------|
| `Models/FlydigiTemperatureProvider.cs` | 1, 2, 3 |
| `Models/Bs2ProSettings.cs` | 5, 6, 7 |
| `Models/FlydigiLearningEngine.cs` | 4, 6 |
| `Services/FlydigiSmartControl.cs` | 1, 4, 5, 7 |
| `Views/Pages/FlydigiCooler.xaml` | 2, 3, 5, 8 |
| `Views/Pages/FlydigiCooler.xaml.cs` | 2, 3, 5, 8 |
| `Models/SmartControlConfig.cs` (new) | 5, 6 |
