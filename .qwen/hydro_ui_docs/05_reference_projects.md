### watercooler-manager (Python Windows tray)
**Path:** `C:\Users\Jeik\Documents\Repos\watercooler-manager`
**Role:** Direct reference for UXTU's initial implementation

- Python + Bleak + pystray tray app
- Uses `WriteWithoutResponse` (no RX, no status queries)
- Simpler: just tray menu with pump/fan/RGB controls
- Key files: `src/watercooler_manager/device.py` (BLE), `enums.py` (protocol)
- Device discovery filters for "LCT21001" / "LCT22002" in advertised name

### tuxedo-control-center (Linux Electron)
**Path:** `C:\Users\Jeik\Documents\Repos\tuxedo-control-center`
**Role:** Original source of the BLE protocol

- TypeScript/Electron app by TUXEDO Computers
- Uses `node-ble` for BLE communication
- **Dual-state tracking:** `aquarisStateCurrent` (actual) vs `aquarisStateExpected` (desired) — only sends commands for changed params
- Sends reset on disconnect
- Demo mode (device UUID "demo" simulates with 600ms delays)
- Key files: `src/e-app/LCT21001.ts` (BLE), `src/e-app/backendAPIs/aquarisAPI.ts` (state)
- Protocol reference: `Files-for-AI/tuxedo-control-center-BLE-rundown.md`

### UCC — Uniwill Control Center (C++ Qt6)
**Path:** `C:\Users\Jeik\Documents\Repos\ucc`
**Role:** Most advanced reference, targeted at XMG Neo 16 A25 (user's laptop)

- C++20/Qt6 daemon + GUI architecture
- Uses Qt Bluetooth (`QLowEnergyController`) on BlueZ/Linux
- **Architecture:** GUI → D-Bus proxy → uccd daemon → LCTWaterCoolerWorker (BLE)
- Key files:
  - `uccd/src/workers/LCTWaterCoolerWorker.cpp` (1335 lines, BLE daemon worker)
  - `ucc-gui/src/LCTWaterCoolerController.cpp` (GUI D-Bus proxy)
  - `PROJECT_OVERVIEW_FOR_PORTING_TO_WINDOWS.md` (porting context)

**Advanced features UCC has that UXTU doesn't:**
- **Watercooler-specific fan curves** — separate 17-point temperature-to-speed curve editor for the watercooler fan
- **Pump voltage curves** — step-wise by temperature (3 thresholds), default 40C→V7, 55C→V8, 70C→V11
- **Pump hysteresis** — 3°C deadband prevents oscillation (step-up immediate, step-down delayed)
- **Temperature-based LED mode** — blue→red gradient mapped to fan speed
- **EWMA temperature filtering** — asymmetric: fast rise (alpha 0.5), slow fall (alpha 0.15)
- **Exponential backoff reconnection** — 5s→120s, with GATT cache purge after 3 failures
- **BLE write throttling** — 80ms minimum gap between commands
- **Suspend/resume handling** — teardown on sleep, reconnect on wake
- **Adapter reset** — power-cycles Bluetooth adapter after 5 consecutive failures
- **MAC address pinning** — stores trusted MAC on first connection
- **Profile integration** — watercooler connection triggers dedicated power state, separate profiles