using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Universal_x86_Tuning_Utility.Services
{
    /// <summary>
    /// Controls keyboard backlight via HID feature reports sent to the ITE lighting controller.
    /// 
    /// The keyboard backlight on XMG/TUXEDO laptops is NOT controlled through EC registers.
    /// Instead, an ITE HID controller (vid_048d) handles all keyboard lighting via 9-byte
    /// feature reports. The EC registers 0x078C (KBD_STATUS) and 0x0767 (TRIGGER) are
    /// logo-only and do not affect the keyboard backlight.
    /// 
    /// This implementation is based on MechControl (decompiled from MechControl.dll).
    /// </summary>
    public sealed class KeyboardHidService : IDisposable
    {
        #region Constants

        // ITE vendor ID (0x048d) — the lighting controller vendor
        private const ushort ITE_VID = 0x048D;
        private const string ITE_VID_STRING = "vid_048d";

        // HID device zones
        public const int ZoneKeyboard = 0;
        public const int ZoneLightBar = 2;
        public const int ZoneLogo = 3;

        // Zone masks (used in 0x08 brightness/mode reports)
        private const byte ZoneMaskKeyboard = 0x02;
        private const byte ZoneMaskLightBar = 0x22;
        private const byte ZoneMaskLogo = 0x23;

        // HID feature report command types
        private const byte CMD_KEYBOARD_OFF = 0x09;  // Keyboard-only special off command
        private const byte CMD_ZONE_RESET = 0x12;    // Generic zone reset
        private const byte CMD_MODE_BRIGHTNESS = 0x08; // Zone mode/speed/brightness config
        private const byte CMD_SET_COLOR = 0x14;     // Set RGB color
        private const byte CMD_ZONE_ON_OFF = 0x1A;   // Zone on/off enable

        // HID report size
        private const int FEATURE_REPORT_SIZE = 9;

        // Win32 constants
        private const int HIDP_STATUS_SUCCESS = 0x110000;
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        #endregion

        #region P/Invoke

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(nint preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(nint preparsedData, out HidpCaps caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint SetupDiGetClassDevs(ref Guid classGuid, nint enumerator, nint hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, nint detailData, uint detailDataSize, out uint requiredSize, nint deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

        #endregion

        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int CbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public nint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        #endregion

        #region State

        private readonly object _lock = new object();
        private SafeFileHandle? _keyboardHandle;
        private bool _disposed;

        #endregion

        /// <summary>
        /// Gets the current keyboard brightness level (0-100).
        /// </summary>
        public int Brightness { get; private set; }

        /// <summary>
        /// Gets the current keyboard color.
        /// </summary>
        public (byte R, byte G, byte B) Color { get; private set; }

        /// <summary>
        /// Gets whether the keyboard backlight is currently on.
        /// </summary>
        public bool IsOn { get; private set; }

        /// <summary>
        /// Gets whether the HID keyboard zone is available on this hardware.
        /// </summary>
        public bool IsAvailable { get; private set; }

        #region Initialization

        /// <summary>
        /// Discovers and opens the ITE HID lighting controller for keyboard (zone 0).
        /// Returns true if a keyboard lighting controller was found and opened.
        /// </summary>
        public bool Open()
        {
            lock (_lock)
            {
                if (_keyboardHandle != null && !_keyboardHandle.IsInvalid)
                    return IsAvailable;

                var devicePaths = FindIteDevicePaths();
                DebugLog($"[KBD-HID] Found {devicePaths.Count} ITE HID device paths");

                // Find devices with 9-byte feature reports
                var lightingDevices = new List<(string Path, ushort Usage)>();
                foreach (var path in devicePaths)
                {
                    DebugLog($"[KBD-HID] Probing: {path}");
                    var handle = TryOpenHandle(path);
                    if (handle == null)
                    {
                        DebugLog($"[KBD-HID]   Cannot open (error {Marshal.GetLastWin32Error()})");
                        continue;
                    }

                    try
                    {
                        if (!HidD_GetPreparsedData(handle, out nint preparsedData))
                        {
                            DebugLog("[KBD-HID]   GetPreparsedData failed");
                            continue;
                        }

                        try
                        {
                            int status = HidP_GetCaps(preparsedData, out HidpCaps caps);
                            DebugLog($"[KBD-HID]   GetCaps status=0x{status:X8} feature={caps.FeatureReportByteLength} usage=0x{caps.Usage:X4}");

                            if (status == HIDP_STATUS_SUCCESS && caps.FeatureReportByteLength == FEATURE_REPORT_SIZE)
                            {
                                lightingDevices.Add((path, caps.Usage));
                                DebugLog("[KBD-HID]   -> Lighting controller");
                            }
                        }
                        finally
                        {
                            HidD_FreePreparsedData(preparsedData);
                        }
                    }
                    finally
                    {
                        handle.Dispose();
                    }
                }

                DebugLog($"[KBD-HID] {lightingDevices.Count} lighting devices (9-byte feature reports)");

                // Map by usage: usage 1 = keyboard (zone 0), usage 2 = light bar + logo
                var usageMap = new Dictionary<ushort, string>();
                foreach (var (path, usage) in lightingDevices)
                {
                    usageMap.TryAdd(usage, path);
                }

                bool found = false;

                if (usageMap.ContainsKey(1) || usageMap.ContainsKey(2))
                {
                    found = true;
                    if (usageMap.TryGetValue(1, out string? keyboardPath))
                    {
                        _keyboardHandle = OpenWriteHandle(keyboardPath);
                        DebugLog($"[KBD-HID] Zone 0 (keyboard): {(_keyboardHandle != null && !_keyboardHandle.IsInvalid ? "opened" : "FAILED")}");
                    }
                }

                // Fallback: try first available device
                if (!found && lightingDevices.Count > 0)
                {
                    _keyboardHandle = OpenWriteHandle(lightingDevices[0].Path);
                    DebugLog($"[KBD-HID] Fallback zone 0: {(_keyboardHandle != null && !_keyboardHandle.IsInvalid ? "opened" : "FAILED")}");
                }

                IsAvailable = _keyboardHandle != null && !_keyboardHandle.IsInvalid;
                return IsAvailable;
            }
        }

        private static List<string> FindIteDevicePaths()
        {
            var list = new List<string>();
            HidD_GetHidGuid(out Guid hidGuid);
            nint deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, nint.Zero, nint.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == nint.Zero || deviceInfoSet == new IntPtr(-1))
                return list;

            try
            {
                SpDeviceInterfaceData ifaceData = default;
                ifaceData.CbSize = Marshal.SizeOf(ifaceData);

                for (uint i = 0; SetupDiEnumDeviceInterfaces(deviceInfoSet, nint.Zero, ref hidGuid, i, ref ifaceData); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref ifaceData, nint.Zero, 0, out uint requiredSize, nint.Zero);
                    nint buffer = Marshal.AllocHGlobal((int)requiredSize);

                    try
                    {
                        // Write the CSP_DEVICE_INTERFACE_DETAIL_DATA_A/W header size
                        Marshal.WriteInt32(buffer, Environment.Is64BitProcess ? 8 : 6);

                        if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref ifaceData, buffer, requiredSize, out _, nint.Zero))
                        {
                            string? path = Marshal.PtrToStringUni(buffer + 4);
                            if (!string.IsNullOrEmpty(path) && path.Contains(ITE_VID_STRING, StringComparison.OrdinalIgnoreCase))
                            {
                                list.Add(path);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return list;
        }

        private static SafeFileHandle? TryOpenHandle(string path)
        {
            // Try different access modes: no access, read, write
            foreach (uint access in new[] { 0u, GENERIC_READ, GENERIC_WRITE })
            {
                var handle = CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, nint.Zero, OPEN_EXISTING, 0, nint.Zero);
                if (!handle.IsInvalid)
                    return handle;
                handle.Dispose();
            }
            return null;
        }

        private static SafeFileHandle OpenWriteHandle(string path)
        {
            var handle = CreateFile(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nint.Zero, OPEN_EXISTING, 0, nint.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nint.Zero, OPEN_EXISTING, 0, nint.Zero);
            }
            return handle;
        }

        #endregion

        #region Keyboard Control

        /// <summary>
        /// Turns on the keyboard backlight with the specified color and brightness.
        /// </summary>
        public void TurnOn(byte r, byte g, byte b, int brightness)
        {
            EnsureAvailable();
            brightness = Math.Clamp(brightness, 0, 100);

            lock (_lock)
            {
                // Report 1: Zone enable
                SendReport(new byte[] { 0x00, CMD_ZONE_ON_OFF, (byte)ZoneKeyboard, 0x01, 0x04, 0x00, 0x00, 0x00, 0x01 });

                // Report 2: Set RGB color
                SendReport(new byte[] { 0x00, CMD_SET_COLOR, (byte)ZoneKeyboard, 0x01, r, g, b, 0x00, 0x00 });

                // Report 3: Set brightness and speed (speed=8 for keyboard)
                SendReport(new byte[] { 0x00, CMD_MODE_BRIGHTNESS, 0x00, 0x01, 0x01, (byte)brightness, 0x00, 0x00, 0x00 });

                // Update Report 3 byte[2] to zone mask and byte[6] to speed
                SendReport(new byte[] { 0x00, CMD_MODE_BRIGHTNESS, ZoneMaskKeyboard, 0x01, 0x01, (byte)brightness, 0x08, 0x00, 0x00 });

                _color = (r, g, b);
                _brightness = brightness;
                IsOn = true;

                DebugLog($"[KBD-HID] Keyboard ON — color=({r},{g},{b}), brightness={brightness}");
            }
        }

        /// <summary>
        /// Turns off the keyboard backlight.
        /// </summary>
        public void TurnOff()
        {
            EnsureAvailable();

            lock (_lock)
            {
                // Keyboard (zone 0) has a special off command
                SendReport(new byte[] { 0x00, CMD_KEYBOARD_OFF, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

                // Generic zone reset
                SendReport(new byte[] { 0x00, CMD_ZONE_RESET, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00 });

                // Mode/brightness clear
                SendReport(new byte[] { 0x00, CMD_MODE_BRIGHTNESS, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

                // Zone-specific clear
                SendReport(new byte[] { 0x00, CMD_MODE_BRIGHTNESS, 0x01, (byte)ZoneKeyboard, 0x00, 0x00, 0x00, 0x00, 0x00 });

                // Zone off
                SendReport(new byte[] { 0x00, CMD_ZONE_ON_OFF, (byte)ZoneKeyboard, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 });

                IsOn = false;
                DebugLog("[KBD-HID] Keyboard OFF");
            }
        }

        /// <summary>
        /// Updates the keyboard color and brightness without turning it off.
        /// </summary>
        public void SetColor(byte r, byte g, byte b, int brightness)
        {
            EnsureAvailable();
            brightness = Math.Clamp(brightness, 0, 100);

            lock (_lock)
            {
                // Report 1: Set RGB color
                SendReport(new byte[] { 0x00, CMD_SET_COLOR, (byte)ZoneKeyboard, 0x01, r, g, b, 0x00, 0x00 });

                // Report 2: Set brightness and speed
                SendReport(new byte[] { 0x00, CMD_MODE_BRIGHTNESS, ZoneMaskKeyboard, 0x01, 0x01, (byte)brightness, 0x08, 0x00, 0x00 });

                _color = (r, g, b);
                _brightness = brightness;

                DebugLog($"[KBD-HID] Color update — color=({r},{g},{b}), brightness={brightness}");
            }
        }

        /// <summary>
        /// Saves the current keyboard settings to BIOS so they persist across reboots.
        /// </summary>
        public void SaveToBios(bool enabled)
        {
            EnsureAvailable();

            lock (_lock)
            {
                if (enabled)
                {
                    // Persistent color report (byte[3]=9 for BIOS persistence, byte[8]=1)
                    SendReport(new byte[] { 0x00, CMD_SET_COLOR, (byte)ZoneKeyboard, 0x09, _color.R, _color.G, _color.B, 0x00, 0x00 });

                    // Persistent zone enable
                    SendReport(new byte[] { 0x00, CMD_ZONE_ON_OFF, 0x00, 0x01, 0x04, 0x00, 0x00, 0x00, 0x01 });

                    // Persistent color with byte[8]=1
                    SendReport(new byte[] { 0x00, CMD_SET_COLOR, (byte)ZoneKeyboard, 0x01, _color.R, _color.G, _color.B, 0x00, 0x01 });

                    // Persistent brightness
                    SendReport(new byte[] { 0x00, CMD_MODE_BRIGHTNESS, ZoneMaskKeyboard, 0x01, 0x01, (byte)_brightness, 0x08, 0x00, 0x01 });
                }
                else
                {
                    // Save disabled state
                    SendReport(new byte[] { 0x00, CMD_ZONE_ON_OFF, (byte)ZoneKeyboard, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 });
                }

                DebugLog($"[KBD-HID] Saved to BIOS (enabled={enabled})");
            }
        }

        private void SendReport(byte[] report)
        {
            if (report.Length != FEATURE_REPORT_SIZE)
                throw new ArgumentException($"Feature report must be exactly {FEATURE_REPORT_SIZE} bytes");

            if (_keyboardHandle == null || _keyboardHandle.IsInvalid)
                throw new InvalidOperationException("HID keyboard zone is not available");

            bool success = HidD_SetFeature(_keyboardHandle, report, report.Length);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                DebugLog($"[KBD-HID] HidD_SetFeature failed (error {error}): {string.Join(", ", report.Select(b => $"0x{b:X2}"))}");
            }
        }

        #endregion

        #region State Tracking

        private (byte R, byte G, byte B) _color = (0, 255, 255); // Default aqua
        private int _brightness = 50;

        #endregion

        #region Helpers

        private void EnsureAvailable()
        {
            if (!IsAvailable)
                throw new InvalidOperationException("Keyboard HID controller is not available. This feature requires an ITE HID lighting controller (vid_048d).");
            if (_disposed)
                throw new ObjectDisposedException(nameof(KeyboardHidService));
        }

        #endregion

        #region Logging

        private static void DebugLog(string message)
        {
            Debug.WriteLine(message);
            System.Diagnostics.Trace.WriteLine(message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _keyboardHandle?.Dispose();
        }

        #endregion
    }
}
