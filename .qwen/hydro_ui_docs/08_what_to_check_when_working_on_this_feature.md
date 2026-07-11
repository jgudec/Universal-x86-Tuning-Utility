1. **Frame format** — Always verify frames are exactly 8 bytes: `FE CMD EN P1 P2 P3 00 EF`
2. **WriteWithResponse** — Don't switch to WriteWithoutResponse (causes BLE buffer overflow)
3. **GATT session lifecycle** — Windows BLE keeps zombie connections; always wait for session close
4. **Disconnect sequence** — Reset → unsubscribe → close services → dispose session → dispose device
5. **Settings persistence** — Settings are saved as enum string names, not values
6. **Adaptive page integration** — Per-game profiles also send watercooler commands when activated