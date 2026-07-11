All Hydro UI code lives in `Universal x86 Tuning Utility/` (the main WPF project). The V2 directory is empty (deployment project only).

### Core Files

| Layer | File | Path | Purpose |
|-------|------|------|---------|
| Models | `WaterCoolerProtocol.cs` | `Models/WaterCoolerProtocol.cs` | Enums (RgbState, PumpVoltage, FanSpeed, RgbColor), command bytes, Nordic UART UUIDs |
| Models | `WaterCoolerSettings.cs` | `Models/WaterCoolerSettings.cs` | JSON-serializable settings class with enum string helpers |
| Models | `WaterCoolerDeviceInfo.cs` | `Models/WaterCoolerDeviceInfo.cs` | BLE device info POCO (Address, Name, Rssi, Model) |
| Service | `WaterCoolerService.cs` | `Services/WaterCoolerService.cs` | **BLE communication layer** — connect, disconnect, discover, command framing, RX parsing (728 lines) |
| Scripts | `WaterCoolerHardwareDetector.cs` | `Scripts/WaterCoolerHardwareDetector.cs` | WMI-based hardware detection for supported Tongfang chassis |
| Views | `Watercooler.xaml` | `Views/Pages/Watercooler.xaml` | Main WPF Page UI — device discovery, connection status, pump, fan, RGB, settings cards |
| Views | `Watercooler.xaml.cs` | `Views/Pages/Watercooler.xaml.cs` | Code-behind — event handlers, UI state management, delegates to WaterCoolerService |
| Views | `Adaptive.xaml` (549-628) | `Views/Pages/Adaptive.xaml` | Embedded "Hydro UI (Watercooler)" collapsible card in per-game profile page |
| Views | `Adaptive.xaml.cs` (751+) | `Views/Pages/Adaptive.xaml.cs` | Per-game watercooler settings (load/save/apply from game profiles) |

### Wiring

| File | Path | Role |
|------|------|------|
| `App.xaml.cs` | `App.xaml.cs` | DI registration (Watercooler page as Scoped, WaterCoolerService as Singleton), auto-connect on startup |
| `MainWindowViewModel.cs` | `ViewModels/MainWindowViewModel.cs` | Conditional navigation item insertion ("Hydro UI" with water/drop icon) when hardware is detected |

### Fan Control (Legacy, Separate System)

| File | Path | Status |
|------|------|--------|
| `FanControl.xaml` + `.cs` | `Views/Pages/FanControl.xaml` | Empty button handlers, legacy UI |
| `Fan_Control.cs` | `Scripts/Fan Control/Fan_Control.cs` | **Entirely commented out** (dead code) — uses WinRingEC direct EC RAM writes |
| `FanConfigManager.cs` | `Services/FanConfigManager.cs` | JSON-based fan config loader |
| `Fan Configs/*.json` | `Fan Configs/` | 9 device-specific fan config JSON files |