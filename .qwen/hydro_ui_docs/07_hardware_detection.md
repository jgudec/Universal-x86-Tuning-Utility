The `WaterCoolerHardwareDetector` uses WMI to check:
- `Win32_ComputerSystem` Manufacturer and Model properties
- Matches against: "tongfang", "schenker", "oasis", "specialist", "eluktronics", "tuxedo", "aquaris"
- When detected, the "Hydro UI" navigation item is shown in the sidebar
- Auto-connect only triggers when hardware is detected as supported