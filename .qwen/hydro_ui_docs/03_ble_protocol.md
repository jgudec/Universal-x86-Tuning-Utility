### Transport

- **Profile:** Nordic nRF52 UART GATT profile
- **Service UUID:** `6E400001-B5A3-F393-E0A9-E50E24DCCA9E`
- **TX Characteristic (write):** `6E400002-B5A3-F393-E0A9-E50E24DCCA9E`
- **RX Characteristic (notify):** `6E400003-B5A3-F393-E0A9-E50E24DCCA9E`
- **Write mode:** `WriteWithResponse` (reliable delivery, avoids BLE radio TX buffer overflow)

### Frame Format

**Fixed 8-byte frame, NO checksum:**
```
[0] 0xFE    — Start marker (always)
[1] Command — Command byte
[2] Enable  — 0x01 = enable, 0x00 = disable
[3] Param A — Command-specific
[4] Param B — Command-specific
[5] Param C — Command-specific / padding
[6] 0x00    — Reserved padding
[7] 0xEF    — End marker (always)
```

**CRITICAL:** The device parses fixed byte offsets. Adding a checksum or changing frame length causes silent failures. This was the root cause of the initial "commands sent but no effect" bug.

### Commands

| Command | Byte | On Frame | Off Frame |
|---------|------|----------|-----------|
| **Reset** | `0x19` | `FE 19 00 01 00 00 00 EF` | — |
| **Fan** | `0x1B` | `FE 1B 01 [duty%] 00 00 00 EF` | `FE 1B 00 00 00 00 00 EF` |
| **Pump** | `0x1C` | `FE 1C 01 [duty%] [volt] 00 00 EF` | `FE 1C 00 00 00 00 00 EF` |
| **RGB** | `0x1E` | `FE 1E 01 [R] [G] [B] [state] EF` | `FE 1E 00 00 00 00 00 EF` |
| **Status** | `0x1A` | `FE 1A 01 00 00 00 00 EF` | — |
| **Firmware** | — | `73 77` (no framing) | — |

### Enum Values

**PumpVoltage:** `0x00`=11V, `0x01`=12V, `0x02`=7V, `0x03`=8V
- UXTU enum: `Off=0xFF, V7=0x02, V8=0x03, V11=0x00` (note: V12 is missing from UXTU)

**FanSpeed:** Duty cycle percentage. UXTU presets: `Off=0xFF, 25%, 50%, 75%, 90%, 95%, 100%`

**RgbState:** `Off=0xFF, Static=0x00, Breathe=0x01, Colorful=0x02, BreatheColor=0x03`

**RgbColor:** `Red, Green, Blue, White` (expanded to RGB triplets in WriteRgbModeAsync)