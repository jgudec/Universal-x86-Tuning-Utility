using System.Linq;
using HidLibrary;

namespace Universal_x86_Tuning_Utility.Scripts
{
    /// <summary>
    /// Detects whether a Flydigi BS series cooling pad is currently connected.
    /// Checks for HID devices with VID 0x37D7 and known product IDs.
    /// </summary>
    public static class FlydigiHardwareDetector
    {
        private const int VendorId = 0x37D7;
        private static readonly int[] ProductIds = { 0x1001, 0x1002, 0x1003, 0x1004 };

        private static bool? _hasDevice;
        private static string? _cachedModelName;

        /// <summary>
        /// Clears the cached detection result. Call when device plug/unplug events occur.
        /// </summary>
        public static void InvalidateCache()
        {
            _hasDevice = null;
            _cachedModelName = null;
        }

        /// <summary>
        /// Returns true if a Flydigi BS series cooling pad is currently connected.
        /// Result is cached after first call. Use InvalidateCache() to force re-check.
        /// </summary>
        public static bool IsDeviceAvailable()
        {
            if (_hasDevice.HasValue)
                return _hasDevice.Value;

            try
            {
                var devices = HidDevices.Enumerate(VendorId, ProductIds);
                bool available = devices.Any();
                _hasDevice = available;
                return available;
            }
            catch
            {
                // HidLibrary not available or enumeration failed
                _hasDevice = false;
                return false;
            }
        }

        /// <summary>
        /// Returns the model name of the first detected Flydigi cooling pad (e.g., "BS2 PRO").
        /// Returns "Flydigi Cooler" if no device is detected or model is unknown.
        /// </summary>
        public static string GetDetectedModelName()
        {
            if (_cachedModelName != null)
                return _cachedModelName;

            try
            {
                var devices = HidDevices.Enumerate(VendorId, ProductIds);
                var first = devices.FirstOrDefault();
                if (first != null)
                {
                    ushort productId = (ushort)first.Attributes.ProductId;
                    _cachedModelName = productId switch
                    {
                        0x1001 => "BS2",
                        0x1002 => "BS2 PRO",
                        0x1003 => "BS3",
                        0x1004 => "BS3 PRO",
                        _ => "Flydigi Cooler"
                    };
                }
                else
                {
                    _cachedModelName = "Flydigi Cooler";
                }
            }
            catch
            {
                _cachedModelName = "Flydigi Cooler";
            }

            return _cachedModelName;
        }
    }
}
