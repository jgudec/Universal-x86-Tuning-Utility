### Connection Lifecycle

1. **Discover** — `BluetoothLEAdvertisementWatcher` scans for names containing "lct", "be quiet!", "oasis"
2. **Connect** — `BluetoothLEDevice.FromBluetoothAddressAsync()`, creates `GattSession`, discovers Nordic UART service, subscribes to RX notifications
3. **Auto-connect** — On startup, if enabled, connects to last-known device then restores saved pump/fan/RGB settings
4. **Disconnect** — (1) Send reset, (2) Unsubscribe RX, (3) Close ALL GATT services, (4) Dispose GattSession + wait for close, (5) Dispose device handle
5. **Reconnect** — Forces cleanup + `GC.Collect()`, up to 3 retries with 1-second delays

### GATT Session Tracking

Windows BLE is reference-counted — disposing your GattSession doesn't guarantee immediate teardown. The service tracks `_sessionClosedTcs` via `SessionStatusChanged` events to detect when the OS-level link truly closes. This prevents "zombie" connections.

### Settings Persistence

- **Location:** `%APPDATA%\UXTU\watercooler_settings.json`
- **Saved:** Pump voltage, fan speed, RGB mode, RGB color, RGB enabled flag, auto-connect flag, last device address
- **Restored:** On connect, saved settings are re-sent to the device