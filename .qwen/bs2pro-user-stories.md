# Flydigi BS2 Pro Cooling Pad — User Stories for UXTU Integration

> **Reference project:** THRM (https://github.com/Espresso34/bs2pro-controller)  
> **Target hardware:** Flydigi BS2 Pro (VID 0x37D7, PID 0x1002)  
> **HID library:** HidLibrary 3.3.40 (already in UXTU's dependencies)  
> **Protocol:** `5A A5 <CMD> <LEN> <PAYLOAD> <CHECKSUM>` over HID Report ID 0x02  
> **Full protocol reference:** `docs/bs2pro-ota-ble-commands.md` in THRM repo

---

## Architecture Overview

```
Services/
  FlydigiCoolerService.cs       HID communication, frame building, device lifecycle
  Bs2ProSmartControl.cs         Temperature-driven fan logic, learning, avoidance zones

Models/
  Bs2ProProtocol.cs             Frame builder, command constants, enums, status parsing
  Bs2ProSettings.cs             JSON-serializable settings (device toggles, manual gear)
  Bs2ProFanCurveProfile.cs      Named curve profiles with points + smart control config
  FlydigiCoolerDeviceInfo.cs    Discovered device info (path, serial, product name)

Views/Pages/
  FlydigiCooler.xaml / .xaml.cs Main control page (fan, RGB, device settings, curves)
```

> **Naming convention:** Classes that apply to all Flydigi BS series devices use the generic "FlydigiCooler" prefix. Protocol-level classes that are specific to the BS2 Pro frame format retain "Bs2Pro" (the protocol is identical across BS2/BS3, but was reverse-engineered from the BS2 Pro).

### Design Principles

- **Singleton service** (`FlydigiCoolerService`) following `WaterCoolerService` pattern — auto-connect on startup, JSON persistence to `%APPDATA%\UXTU\`
- **HidLibrary** for HID communication — already a project dependency, proven working in `XgMobileConnectionService.cs`
- **LibreHardwareMonitorLib** for temperature — already used in `FanControl.xaml.cs`
- **Navigation item** conditionally inserted in `MainWindowViewModel` when device is detected (same pattern as Hydro UI)
- **Per-game profiles** integrated into existing `AdaptivePreset` system (same pattern as watercooler per-game settings)
- **All settings persisted to `%APPDATA%\UXTU\bs2pro_settings.json`**
- **Curve profiles persisted to `%APPDATA%\UXTU\bs2pro_curves.json`**

---

## Story 1 — Protocol Layer: Frame Builder and Parser ✅ COMPLETE

**Goal:** Implement the `5A A5` frame protocol for building outgoing commands and parsing incoming status notifications.

**Acceptance Criteria:**
- [x] `Bs2ProProtocol.cs` created in `Models/` with:
  - `Bs2ProFrame.Build(byte cmd, params byte[] payload)` → `byte[]` — constructs `5A A5 <CMD> <LEN> <PAYLOAD> <CHECKSUM>`
  - `Bs2ProFrame.Parse(byte[] data)` → `ParsedFrame?` — validates `5A A5` header + checksum, returns parsed contents
  - `Bs2ProFrame.BuildReport(byte[] frame, int reportLength)` → `byte[]` — prepends Report ID 0x02, pads to report length
- [x] Command constants in `Bs2ProCommand` static class
- [x] `Bs2ProStatusParser.Parse(byte[] payload)` → `Bs2ProStatusNotification?` — parses `0xEF` notification frames
- [x] `Bs2ProGearTableParser.Parse(byte[] payload)` → `ushort[]?` — parses gear RPM table response
- [x] Unit tests for frame building: 12 tests verifying THRM-documented command examples
- [x] Unit tests for parsing: 8 tests including status notification, bad checksum, invalid magic, null/empty
- [x] Unit tests for status parser: 10 tests (parse, gear name decoding, smart start/stop decoding)
- [x] Unit tests for gear table parser: 3 tests
- [x] Unit tests for notification struct: 4 tests
- [x] Unit tests for constants: 3 tests verifying default RPMs and product IDs
- [x] Checksum calculation: `sum(CMD, LEN, PAYLOAD...) & 0xFF`
- [x] **Total: 48 tests, all passing**

**References:**
- THRM: `internal/deviceproto/protocol.go` (BuildFrame, ParseFrame)
- THRM: `internal/deviceproto/commands.go` (command constants)
- THRM: `docs/bs2pro-ota-ble-commands.md` (frame format, examples)

---

## Story 2 — Device Discovery and Connection Management ✅ COMPLETE

**Goal:** Enumerate Flydigi HID devices, open/close connections, and manage the device lifecycle.

**Acceptance Criteria:**
- [x] `FlydigiCoolerService.cs` created in `Services/` (generic name for all BS series support)
- [x] `FlydigiCoolerDeviceInfo.cs` model in `Models/` — device path, product name, manufacturer, serial, product ID, computed model name
- [x] `Bs2ProSettings.cs` model in `Models/` — JSON-serializable settings with auto-connect, device toggles, RGB config
- [x] `DiscoverDevices()` → `List<FlydigiCoolerDeviceInfo>` — enumerates all BS series devices via `HidDevices.Enumerate(0x37D7, [...])`, opens each to read product/manufacturer/serial strings
- [x] `ConnectAsync(devicePath)` → `Task<bool>` — opens device, reads capabilities, starts background read loop
- [x] `DisconnectAsync()` — stops read loop, closes device handle, raises `ConnectionStateChanged`
- [x] `IsConnected` property — reflects current connection state
- [x] Background read loop: `device.Read(200ms timeout)` on dedicated thread, dispatches to `OnInputReport()`
- [x] Read loop uses `CancellationTokenSource` for clean shutdown
- [x] `ConnectionStateChanged` event fires on connect/disconnect
- [x] `StatusReceived` event fires with parsed `Bs2ProStatusNotification` on 0xEF frames
- [x] `StatusChanged` event fires with human-readable status messages
- [x] Auto-connect on startup via `TryAutoConnectAsync()` — 3 retries with 1s/2s exponential backoff
- [x] Settings persistence to `%APPDATA%\UXTU\bs2pro_settings.json`
- [x] HID write throttling: 80ms minimum gap between writes (prevents HID buffer overflow)
- [x] `IDisposable` pattern for cleanup
- [x] All 48 protocol tests still pass

**References:**
- THRM: `internal/device/device.go` (Connect, Disconnect, HidDevice)
- UXTU: `Services/WaterCoolerService.cs` (connection lifecycle pattern)
- UXTU: `Scripts/ASUS/XgMobileConnectionService.cs` (HidLibrary usage pattern)

---

## Story 3 — Fan Speed Control: Gear Presets and Realtime RPM ✅ COMPLETE

**Goal:** Control fan speed via both preset gear modes and direct RPM targeting.

**Acceptance Criteria:**
- [x] `WriteGearAsync(byte gear)` → `Task<bool>` — sends `0x08` command (1=Quiet, 2=Standard, 3=Strong, 4=Overclock)
- [x] `WriteRealtimeRpmAsync(ushort rpm)` → `Task<bool>` — sends `0x23` (enter realtime mode) then `0x21` (target RPM) with 50ms delay between
- [x] `WriteGearRpmAsync(byte gearIndex, ushort rpm)` → `Task<bool>` — sends `0x26` to set per-gear custom RPM
- [x] RPM range validated: 1300-4000 RPM (hardware limits), 0 = fan off
- [x] Default gear RPM table matches THRM defaults:
  - Gear 1 (Quiet): 1300 / 1700 / 1900 RPM (low/med/high)
  - Gear 2 (Standard): 2100 / 2400 / 2700 RPM
  - Gear 3 (Strong): 2800 / 3000 / 3300 RPM
  - Gear 4 (Overclock): 3500 / 3700 / 4000 RPM
- [x] `ExitRealtimeModeAsync()` → `Task<bool>` — sends `0x24` to exit realtime mode
- [x] Fan off: enters realtime mode → sets RPM 0 → exits realtime mode (3-command sequence with 50ms delays, matches THRM)
- [x] Last-applied RPM/gear tracked to avoid redundant writes (same-value skip when already in realtime mode)
- [x] Status notifications parsed: when device sends `0xEF`, extract `currentRpm` and `targetRpm`, raise `FanDataReceived` event
- [x] `FanRpmData` struct: `TargetRpm`, `CurrentRpm`, `Mode` ("Off" / "Gear" / "Realtime")
- [x] Realtime mode tracking: `_realtimeMode` flag avoids re-sending 0x23 on subsequent RPM changes
- [x] Mode reset on disconnect and when device reports leaving realtime mode (physical button press)
- [x] Write failure recovery: on failed 0x21 write, reset realtime state to force fresh handshake on retry
- [x] **Total: 81 tests (48 original + 33 new), all passing**

**References:**
- THRM: `internal/device/device.go` (SetFanSpeed, SetGear, setRealtimeFanSpeedLocked)
- THRM: `internal/types/types.go` (GearCommands map, default RPMs)
- THRM: `docs/bs2pro-ota-ble-commands.md` (realtime RPM sequence, gear presets)

---

## Story 4 — RGB LED Control ✅ COMPLETE

**Goal:** Control the RGB LED strip with all supported animation modes.

**Acceptance Criteria:**
- [x] `WriteRgbOffAsync()` → `Task<bool>` — sends `0x46 00` to turn off LEDs
- [x] `WriteRgbSmartTempAsync()` → `Task<bool>` — sends smart-temp mode sequence (`0x46/0x45/0x44/0x43` handshake, no frame upload)
- [x] `WriteRgbStaticAsync(byte r, byte g, byte b, byte brightness)` → `Task<bool>` — uploads 30-frame static color sequence
- [x] `WriteRgbRotationAsync(byte r, byte g, byte b, string speed, byte brightness)` → `Task<bool>` — uploads rotation animation
- [x] `WriteRgbFlowingAsync(string speed, byte brightness)` → `Task<bool>` — uploads flowing animation (fixed green base)
- [x] `WriteRgbBreathingAsync(byte r, byte g, byte b, byte brightness)` → `Task<bool>` — uploads breathing animation
- [x] Full 30-frame upload sequence follows THRM's documented protocol:
  1. `0x46 00` (off/clear) → 100ms delay
  2. `0x46 01` ×2 (enter dynamic mode, 5ms apart)
  3. `0x45 02` (heartbeat/query)
  4. `0x45 03 01` (heartbeat with param)
  5. `0x41 02` (init transfer)
  6. `0x41 03 01` (init confirm)
  7. `0x47` header frame with f0 (mode/speed/brightness/color)
  8. `0x47` ×30 animation frames (1ms between)
  9. `0x43 01` (apply/commit)
- [x] Speed control: fast (0x05), medium (0x0A), slow (0x0F)
- [x] Brightness: 0-100% with per-frame RGB scaling
- [x] Frame generation for each mode:
  - **Static:** RGB at frames 2,5,8,11,14, all others zero
  - **Rotation:** 304-byte color stream from 6 chunks × 10 positions, distributed to f0[6..9] + frames
  - **Flowing:** 9-frame template repeated across 30 frames, brightness-scaled
  - **Breathing:** 30-byte pattern (R,G,B,0,0,0 per color) repeated to fill 304-byte stream
- [x] `Bs2ProRgbFrameGenerator` static class in `Models/` with `Generate()` method
- [x] `LightUploadData` struct with 10-byte Header and 30×10 byte[,] Frames
- [x] 65-byte light report for RGB frame writes (vs 25-byte control report)
- [x] `WriteLightReport()` private method with same write throttling as control reports
- [x] **Total: 113 tests (81 previous + 32 new), all passing**

**References:**
- THRM: `internal/device/rgb.go` (full upload sequence, frame generation)
- THRM: `docs/bs2pro-ota-ble-commands.md` (RGB command table, upload sequence)

---

## Story 5 — Device Settings: Power-On Start, Smart Start/Stop, Brightness, Gear Light ✅ COMPLETE

**Goal:** Control device configuration toggles and indicator settings.

**Acceptance Criteria:**
- [x] `WritePowerOnStartAsync(bool enabled)` → `Task<bool>` — sends `0x0C` (01=on, 02=off)
- [x] `WriteSmartStartStopAsync(byte mode)` → `Task<bool>` — sends `0x0D` (00=off, 01=immediate, 02=delayed)
- [x] `WriteGearLightAsync(bool enabled)` → `Task<bool>` — sends `0x48` (00=off, 01=on)
- [x] `QueryDeviceSettingsAsync()` → `Task<Bs2ProDeviceSettings>` — sends `0x25` (query work mode) and `0x27` (query gear table) to read current device state
- [x] `Bs2ProDeviceSettings` model: `PowerOnStart`, `SmartStartStopMode`, `GearLightEnabled`, `GearRpmTable`, `WorkMode`
- [x] Settings queried on connect and exposed via `DeviceSettings` property
- [x] Settings restored on auto-connect from saved state
- [x] Query response mechanism: `TaskCompletionSource`-based wait with 300ms timeout, keyed by command byte
- [x] `DeviceSettingsUpdated` event fires after query completes
- [x] Non-0xEF frames routed through `TryDeliverQueryResponse()` in `OnInputReport`
- [x] **Total: 135 tests (113 previous + 22 new), all passing**

**References:**
- THRM: `internal/device/device.go` (SetPowerOnStart, SetSmartStartStop)
- THRM: `internal/device/query.go` (QueryGearTable, QueryWorkMode)
- THRM: `docs/bs2pro-ota-ble-commands.md` (power-on start, smart start/stop, gear light commands)

---

## Story 6 — Settings Persistence and Auto-Connect ✅ COMPLETE

**Goal:** Persist all BS2 Pro settings to disk and restore on app restart.

**Acceptance Criteria:**
- [x] `Bs2ProSettings.cs` model in `Models/`:
  - `bool AutoConnect`, `string LastDevicePath`
  - `int ManualGear`, `int ManualGearSubLevel` (1-4 gear, 0-2 sub-level)
  - `bool PowerOnStart`, `int SmartStartStopMode`, `bool GearLightEnabled`
  - `string RgbMode`, `byte R, G, B`, `string RgbSpeed`, `byte Brightness`
  - `ushort ManualRpm`, `bool SuspendFanOff`, `string TempSource`
  - `bool AvoidanceEnabled`, `ushort AvoidanceStartRpm`, `ushort AvoidanceEndRpm`
- [x] `SaveSettings()` / `LoadSettings()` in `FlydigiCoolerService` — JSON to `%APPDATA%\UXTU\bs2pro_settings.json`
- [x] Auto-save on every settings change (SaveSettings called after each write command)
- [x] Auto-connect on startup: if `AutoConnect=true` and `LastDevicePath` set, attempt reconnect
- [x] Settings restored after successful auto-connect: apply saved gear/RGB/device settings

**References:**
- UXTU: `Services/WaterCoolerService.cs` (LoadSettings, SaveSettings, TryAutoConnectAsync pattern)
- THRM: config persistence in `%USERPROFILE%\.thrm\config.json`

---

## Story 7 — Temperature Reading Integration ✅ COMPLETE

**Goal:** Read CPU and GPU temperatures using LibreHardwareMonitorLib for smart fan control.

**Acceptance Criteria:**
- [x] `FlydigiTemperatureProvider` class in `Models/`:
  - `GetCpuTemperature()` → `double?` — reads CPU temperature via LibreHardwareMonitorLib
  - `GetGpuTemperature()` → `double?` — reads GPU temperature via LibreHardwareMonitorLib
  - `GetMaxTemperature()` → `double?` — returns max(CPU, GPU), handles nulls gracefully
- [x] Computer instance managed as singleton (open once, reuse for all reads)
- [x] Graceful fallback: returns `null` if sensor not available (no crash)
- [x] Temperature source configurable in `Bs2ProSettings` (same as THRM's `tempSource`: max/cpu/gpu)
- [x] `IDisposable` for clean shutdown (calls `computer.Close()`)

**References:**
- UXTU: `Views/Pages/FanControl.xaml.cs` (existing LibreHardwareMonitorLib usage pattern)
- THRM: `internal/temperature/temperature.go` (temperature reading + history)

---

## Story 8 — Fan Curve Model and Editor ✅ COMPLETE (model + built-in profiles; visual editor deferred)

**Goal:** Define temperature-to-RPM fan curves with an editable UI.

**Acceptance Criteria:**
- [x] `FlydigiFanCurveProfile.cs` model in `Models/`:
  - `string Name`, `string Id` (GUID)
  - `List<FanCurvePoint> Points` — `{ Temperature: int, Rpm: ushort }`
  - Points span 0-100°C, RPM 1300-4000
  - `GetRpmForTemperature(double temp)` → `ushort` — linear interpolation, clamped to range
- [x] Default built-in profiles (same as THRM):
  - **Silent:** starts rising at 65°C, max 3000 RPM
  - **Balanced:** starts rising at 50°C, max 3500 RPM
  - **Performance:** starts rising at 40°C, max 4000 RPM
- [x] `ToJSON()` / `FromJSON()` serialization
- [x] `Clone()` deep copy
- [x] Unit tests: 10 tests (interpolation, clamping, profiles, clone, JSON round-trip)
- [ ] `FlydigiFanCurveEditor.xaml` — WPF curve editor control (deferred to post-MVP)
- [ ] Profile CRUD: create, rename, delete, duplicate, import/export (deferred to post-MVP)

**References:**
- THRM: `internal/curveprofiles/` (profile CRUD, export/import)
- THRM: `internal/types/types.go` (FanData struct with curve points)
- UCC: `FanCurveEditorWidget.cpp` (17-point editor with monotonicity enforcement)

---

## Story 9 — Smart Auto-Control with Temperature-Driven Fan Curves ✅ COMPLETE

**Goal:** Automatic fan speed control based on temperature, using fan curves.

**Acceptance Criteria:**
- [x] `FlydigiSmartControl.cs` service in `Services/`:
  - `Start()` / `Stop()` — starts/stops the monitoring loop
  - `IsActive` property
  - Configurable polling interval (default 1000ms)
- [x] Monitoring loop:
  1. Read temperature from `FlydigiTemperatureProvider` (cpu/gpu/max)
  2. Look up target RPM from active fan curve (`GetRpmForTemperature`)
  3. Apply ramp limiting (max RPM change per cycle, default ±200 RPM)
  4. Apply speed avoidance zone if configured (skip RPM range, emergency temp bypass at 85°C)
  5. Send RPM to device via `WriteRealtimeRpmAsync()`
- [x] Critical temperature override: ≥90°C forces 4000 RPM, ≥85°C forces at least 3500 RPM
- [x] Asymmetric EWMA temperature filtering:
  - Rising: alpha = 0.5 (fast response to heating)
  - Falling: alpha = 0.15 (slow decay to prevent premature fan drops)
- [x] `TemperatureUpdated` event with (temp, rpm) tuple
- [x] Smart control toggle in UI with immediate start/stop
- [x] Current temp and target RPM displayed in real-time on the page
- [ ] Hysteresis deadzone (deferred to post-MVP)
- [ ] Transient spike filtering (deferred to post-MVP)

**References:**
- THRM: `internal/coreapp/monitoring.go` (monitoring loop, EMA smoothing)
- THRM: `internal/smartcontrol/target.go` (CalculateTargetRPM, ramp limiting)
- UCC: `FanControlWorker.hpp` (EWMA filtering, hysteresis, critical temp management)

---

## Story 10 — Learning-Based Offset Adjustment ✅ COMPLETE (engine + UI toggles; persistence deferred)

**Goal:** Automatically adjust fan curve points based on observed steady-state temperatures.

**Acceptance Criteria:**
- [x] `FlydigiLearningEngine.cs` in `Models/`:
  - `FeedObservation(gearIndex, observedTemp, targetTemp, currentRpm)` — feeds observations
  - `GetEffectiveOffset(gearIndex)` → `double` — returns effective offset
  - Separate heat-track and cool-track offsets (learning direction matters)
  - Configurable learn rate (0-1, default 0.1)
  - Configurable learning bias: balanced/cooling/quiet mode
  - RPM stability gating (no learning when RPM changes, default 30s threshold)
  - Exponential moving average offset updates
  - `Reset()` to clear all learned offsets
- [x] 16 unit tests covering disabled state, convergence, bias modes, reset
- [x] UI toggle and bias selector on Advanced section
- [ ] Learned offsets persisted in curve profile settings (deferred to post-MVP)
- [ ] UI indicator showing learning activity and applied offsets (deferred to post-MVP)

**References:**
- THRM: `internal/smartcontrol/learning.go` (StableObserver, LearnSteadyOffset)
- THRM: `internal/smartcontrol/helpers.go` (offset smoothing, transient filtering)

---

## Story 11 — Speed Avoidance Zones ✅ COMPLETE

**Goal:** Allow users to define RPM ranges to avoid (resonant frequencies that cause vibration/noise).

**Acceptance Criteria:**
- [x] Speed avoidance properties in `Bs2ProSettings`:
  - `bool AvoidanceEnabled`, `ushort AvoidanceStartRpm`, `ushort AvoidanceEndRpm`
  - Default: disabled, range 2000-2500 RPM
- [x] `FlydigiSpeedAvoidance.Apply()` static helper:
  - If calculated RPM falls within avoidance range, jumps to `EndRpm + 100` (clamped to 4000)
  - Emergency bypass: if temperature ≥ 85°C, ignores avoidance zone
- [x] Integrated into `FlydigiSmartControl` monitoring loop
- [x] UI control on main page (Advanced section): enable/disable + start/end RPM number boxes
- [x] Persisted in settings
- [x] 8 unit tests

**References:**
- THRM: `internal/types/fan_features.go` (SpeedAvoidanceConfig)
- THRM: `internal/smartcontrol/target.go` (avoidance zone logic in CalculateTargetRpm)

---

## Story 12 — Main UI Page ✅ COMPLETE

**Goal:** Create the Flydigi BS2 Pro control page with all device controls.

**Acceptance Criteria:**
- [x] `FlydigiCooler.xaml` page created in `Views/Pages/`:
  - Follows existing UXTU page patterns (CardControl sections, deferred init in Loaded event)
- [x] **Device Status section:** connection status, device info, current/target RPM, temperature
- [x] **Fan Control section:**
  - Mode toggle: Manual / Gear Presets / Auto (Curve)
  - Manual: RPM slider (1300-4000) + NumberBox + apply button
  - Gear Presets: 4 gear buttons (Quiet/Standard/Strong/Overclock) with 3 sub-levels
  - Auto: toggle switch + curve profile dropdown + "Edit Curve" placeholder
- [x] **RGB Control section:**
  - Mode selector: Off / Smart-Temp / Static / Rotation / Flowing / Breathing
  - Color picker (R/G/B sliders, shown for color modes)
  - Speed selector: Fast / Medium / Slow
  - Brightness slider: 0-100%
- [x] **Device Settings section:** Power-On Start toggle, Smart Start/Stop dropdown, Gear Light toggle
- [x] **Advanced section:** Speed Avoidance Zone, Temperature Source, Learning toggle + bias selector
- [x] Deferred initialization pattern: `_isInitialized` guard + `Loaded` event handler
- [x] Service access via `App.GetService<FlydigiCoolerService>()`
- [x] Event handlers wired for `ConnectionStateChanged`, `StatusChanged`, `FanDataReceived`
- [x] UI updates marshaled to dispatcher thread
- [x] Smart control lifecycle: creates `FlydigiTemperatureProvider` + `FlydigiSmartControl` on toggle, disposes on unload

**References:**
- UXTU: `Views/Pages/Watercooler.xaml` (UI layout pattern, card sections)
- UXTU: `Views/Pages/Adaptive.xaml` (sliders, toggles, dropdowns pattern)

---

## Story 13 — Navigation Integration and Hardware Detection ✅ COMPLETE

**Goal:** Conditionally show the BS2 Pro page in navigation when the device is detected.

**Acceptance Criteria:**
- [x] `FlydigiHardwareDetector.cs` in `Scripts/`:
  - `IsDeviceAvailable()` → `bool` — calls `HidDevices.Enumerate(0x37D7, ...)` and checks if any device is connected
  - Cached result with `InvalidateCache()` for re-check on plug/unplug
- [x] Navigation item conditionally inserted in `MainWindowViewModel.InitializeViewModel()` (both Intel and AMD branches):
  - Inserted before "Info" page (same position pattern as Hydro UI)
  - `Content = "BS2 Pro"`, `PageTag = "bs2pro"`
  - `Icon = SymbolRegular.WeatherSnow20`
  - `PageType = typeof(Views.Pages.FlydigiCooler)`
- [x] Page registered as `AddScoped` in `App.xaml.cs`
- [x] Service registered as `AddSingleton` in `App.xaml.cs`
- [x] Auto-discover on startup: if device detected, attempt auto-connect

**References:**
- UXTU: `ViewModels/MainWindowViewModel.cs` (navigation item insertion pattern)
- UXTU: `App.xaml.cs` (DI registration pattern)
- UXTU: `Scripts/WaterCoolerHardwareDetector.cs` (hardware detection pattern)

---

## Story 14 — Per-Game Profile Integration

**Goal:** Integrate BS2 Pro fan control into the existing Adaptive per-game profile system.

**Acceptance Criteria:**
- [ ] Add BS2 Pro properties to `AdaptivePreset` class (same pattern as existing `WcPumpVoltage`, `WcFanSpeed`):
  - `string Bs2ProFanMode` — "Off", "Gear", "Rpm", "Curve"
  - `int Bs2ProGear` — 1-4 (gear level)
  - `ushort Bs2ProRpm` — 1300-4000 (manual RPM)
  - `string Bs2ProCurveProfileId` — GUID of active curve profile for this game
  - `bool Bs2ProAutoControl` — whether to enable smart control for this game
- [ ] Add BS2 Pro card to `Adaptive.xaml` (collapsible section, same pattern as "Hydro UI (Watercooler)" card):
  - Visible only when BS2 Pro device is detected
  - Fan mode selector, gear/RPM controls, curve profile selector
  - Auto-control toggle
- [ ] On game launch (in `adaptive_Tick`):
  - Apply BS2 Pro settings from the game's `AdaptivePreset`
  - If auto-control enabled, start `Bs2ProSmartControl` with the selected curve
  - If manual mode, set the specified gear or RPM
- [ ] On game exit / revert:
  - Stop smart control if it was game-specific
  - Revert to default BS2 Pro settings or previous game's settings
- [ ] Per-game BS2 Pro settings saved in `adaptivePresets.json` (existing file, extended schema)

**References:**
- UXTU: `Services/AdaptivePresetManager.cs` (AdaptivePreset class, per-game settings)
- UXTU: `Views/Pages/Adaptive.xaml` (per-game UI, watercooler card pattern)
- UXTU: `Views/Pages/Adaptive.xaml.cs` (`adaptive_Tick`, profile apply logic)

---

## Story 15 — System Suspend/Resume Handling

**Goal:** Properly handle system sleep and wake events to prevent device issues.

**Acceptance Criteria:**
- [ ] Register for Windows power notifications via `PowerRegisterSuspendResumeNotification`:
  - On suspend: disconnect from device (send fan-off command if `SuspendFanOff` setting enabled), close HID handle
  - On resume: wait 2 seconds, then attempt reconnection if auto-connect is enabled
- [ ] `SuspendFanOff` setting in `Bs2ProSettings` (default: true — turn off fan on sleep to prevent dry running)
- [ ] Same pattern as watercooler suspend/resume handling

**References:**
- THRM: `internal/powernotify/` (system suspend/resume notifications)
- THRM: `internal/coreapp/lifecycle.go` (suspend/resume handling in lifecycle)

---

## Story 16 — Device Plug/Unplug Handling

**Goal:** Detect device being physically plugged or unplugged at runtime.

**Acceptance Criteria:**
- [ ] Background device monitoring:
  - Periodic poll (every 5s) of `HidDevices.Enumerate()` to detect device appearance/disappearance
  - Alternative: use HidLibrary's `Inserted`/`Removed` events if reliable
- [ ] On device unplugged while connected:
  - Gracefully stop read loop
  - Raise `ConnectionStateChanged(false)`
  - Show UI notification (disconnected)
  - Attempt reconnect with exponential backoff (1s, 2s, 4s, 8s, max 30s)
- [ ] On device plugged while disconnected:
  - If auto-connect enabled, attempt connection
  - Update device list in UI

**References:**
- THRM: `internal/coreapp/system_device.go` (reconnect loop)
- THRM: `internal/coreapp/lifecycle.go` (device lifecycle management)

---

## Story 17 — Time-Based Curve Scheduling

**Goal:** Automatically switch fan curve profiles based on time of day.

**Acceptance Criteria:**
- [ ] `TimeCurveSchedule` in `Bs2ProSettings`:
  - `bool Enabled`
  - `List<TimeCurveEntry>` — `{ StartTime: TimeSpan, EndTime: TimeSpan, ProfileId: string }`
- [ ] Background timer checks current time against active schedule:
  - Matches current time to active time window
  - Switches to the scheduled curve profile
  - Falls back to default profile if no schedule matches
- [ ] UI control: add/remove time windows, assign curve profiles to each window
- [ ] Persisted in settings

**References:**
- THRM: `internal/types/fan_features.go` (TimeCurveScheduleConfig)
- THRM: `internal/curveprofiles/` (profile switching logic)

---

## Story 18 — Code Quality and Error Handling

**Goal:** Ensure production-quality error handling, resource management, and code organization.

**Acceptance Criteria:**
- [ ] All HidLibrary device handles properly disposed (using statements or `IDisposable` pattern)
- [ ] Background read loop uses `CancellationTokenSource` for clean shutdown
- [ ] Write operations have timeout (1000ms) and return success/failure
- [ ] Failed writes logged (not silently swallowed)
- [ ] HID write throttling: minimum 80ms gap between writes (same as UCC, prevents HID buffer overflow)
- [ ] IO progress guard: prevents concurrent HID operations (disable UI controls during discovery/connect)
- [ ] Null-safe temperature reads: gracefully handles missing sensors
- [ ] Smart control stops cleanly on page navigation away (no orphaned timers)
- [ ] Settings save failures handled gracefully (file lock, permission denied)
- [ ] Consistent naming: follows UXTU conventions (`Universal_x86_Tuning_Utility` namespace, PascalCase classes)
- [ ] No magic numbers: all protocol bytes named as constants
- [ ] Frame building/parsing code has XML documentation comments
- [ ] Service implements `IDisposable` for cleanup on app shutdown

**References:**
- UXTU: `Services/WaterCoolerService.cs` (error handling, disposal pattern)
- THRM: `internal/device/device.go` (IO guard, concurrent operation prevention)

---

## Implementation Order

The recommended implementation order (dependencies first):

```
Story 1  (Protocol layer)
  └── Story 2  (Device connection)
        └── Story 3  (Fan speed control)
        └── Story 4  (RGB control)
        └── Story 5  (Device settings)
        └── Story 6  (Persistence + auto-connect)
        └── Story 7  (Temperature reading)
              └── Story 8  (Fan curve model + editor)
                    └── Story 9  (Smart auto-control)
                          └── Story 10  (Learning-based offsets)
        └── Story 11  (Speed avoidance zones)
        └── Story 12  (Main UI page)
        └── Story 13  (Navigation + detection)
        └── Story 14  (Per-game profiles)
        └── Story 15  (Suspend/resume)
        └── Story 16  (Plug/unplug handling)
        └── Story 17  (Time-based scheduling)
        └── Story 18  (Code quality pass — after all stories)
```

Stories 3, 4, 5, 6 can be implemented in parallel after Story 2.
Stories 8, 11 can be implemented in parallel after Story 7.
Story 18 is a final pass after all functional stories are complete.
