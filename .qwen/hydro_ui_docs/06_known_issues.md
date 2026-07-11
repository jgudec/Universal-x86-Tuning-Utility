### Protocol Format Bug (FIXED)
The WPF implementation initially sent variable-length frames with XOR checksum. The device expects fixed 8-byte frames with NO checksum. This was the root cause of "commands sent but no effect on hardware."

### RGB LEDs Stay Off After Connect
Despite saved mode being "Breathe", LEDs remain off unless the user manually changes mode. Suspected cause: reset command on connect clears device state, and the RGB restore command may arrive before the LED controller re-initializes. Possible fix: add delay between connect and RGB command, or send RGB command twice.

### Disconnect Recovery
After disconnect, the cooler may enter a weird state (lights stay on, can't reconnect). The disconnect sequence was improved to close ALL GATT services before disposing, but may need further tuning.

### Debug Status (June 24, 2026)
- Page loads without crashing (fixed XAML issues)
- Constructor deferred to `Loaded` event to avoid DI scope conflicts
- Device discovery works — finds the cooler
- Connection establishes successfully
- Controls work after protocol fix
- RX notifications / status grid still empty