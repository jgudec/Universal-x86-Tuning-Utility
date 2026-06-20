using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Universal_x86_Tuning_Utility.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage;

namespace Universal_x86_Tuning_Utility.Services
{
    /// <summary>
    /// BLE communication layer for LCT laptop water coolers (LCT21001/LCT22002).
    /// Uses Nordic nRF52 UART GATT profile.
    /// </summary>
    public class WaterCoolerService : IDisposable
    {
        private BluetoothLEDevice _device;
        private GattCharacteristic _txCharacteristic;
        private readonly List<BluetoothLEAdvertisementWatcher> _watchers = new();

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UXTU", "watercooler_settings.json");

        public event EventHandler<WatercoolerConnectionState>? ConnectionStateChanged;
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Raised when a status response is received from the device.
        /// </summary>
        public event EventHandler<(byte PumpStatus, ushort FanRpm, byte Temperature)>? StatusReceived;

        private WaterCoolerSettings _settings = new();

        public bool IsConnected => _device != null && _txCharacteristic != null;

        public WaterCoolerService()
        {
            LoadSettings();
        }

        #region Settings Persistence

        private void LoadSettings()
        {
            try
            {
                var directory = ApplicationData.Current.RoamingFolder;
                var folderTask = directory.CreateFolderAsync("UXTU", CreationCollisionOption.OpenIfExists).AsTask().Result;
                var filePath = Path.Combine(folderTask.Path, "watercooler_settings.json");

                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    _settings = JsonConvert.DeserializeObject<WaterCoolerSettings>(json) ?? new WaterCoolerSettings();
                }
            }
            catch
            {
                _settings = new WaterCoolerSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                var directory = ApplicationData.Current.RoamingFolder;
                var folderTask = directory.CreateFolderAsync("UXTU", CreationCollisionOption.OpenIfExists).AsTask().Result;
                var filePath = Path.Combine(folderTask.Path, "watercooler_settings.json");

                var json = JsonConvert.SerializeObject(_settings, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch { /* non-critical */ }
        }

        public WaterCoolerSettings GetSettings() => _settings;

        public void UpdatePumpVoltage(PumpVoltage voltage)
        {
            _settings.PumpVoltage = voltage.ToString();
            SaveSettings();
        }

        public void UpdateFanSpeed(FanSpeed speed)
        {
            _settings.FanSpeed = speed.ToString();
            SaveSettings();
        }

        public void UpdateRgbMode(RgbState mode)
        {
            _settings.RgbMode = mode.ToString();
            SaveSettings();
        }

        public void UpdateRgbColor(RgbColor color)
        {
            _settings.RgbColor = color.ToString();
            SaveSettings();
        }

        public void UpdateAutoConnect(bool enabled)
        {
            _settings.AutoConnect = enabled;
            SaveSettings();
        }

        #endregion

        /// <summary>
        /// Discovers LCT watercooler devices via BLE advertising.
        /// </summary>
        public async Task<List<WaterCoolerDeviceInfo>> DiscoverDevicesAsync(int timeoutMs = 10000)
        {
            var devices = new List<WaterCoolerDeviceInfo>();

            var watcher = new BluetoothLEAdvertisementWatcher();
            watcher.Received += (s, e) =>
            {
                var name = e.Advertisement.LocalName;
                if (string.IsNullOrEmpty(name)) return;

                // Match known LCT watercooler device names
                if (name.Contains("lct", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("be quiet!", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("oasis", StringComparison.OrdinalIgnoreCase))
                {
                    lock (devices)
                    {
                        devices.Add(new WaterCoolerDeviceInfo
                        {
                            Address = e.BluetoothAddress.ToString(),
                            Name = name,
                            Rssi = e.RawSignalStrengthInDBm
                        });
                    }
                }
            };

            watcher.Start();
            await Task.Delay(timeoutMs);
            watcher.Stop();
            _watchers.Remove(watcher);

            // Deduplicate by address
            return devices.DistinctBy(d => d.Address).ToList();
        }

        /// <summary>
        /// Connects to a BLE device and prepares the TX characteristic.
        /// </summary>
        public async Task<bool> ConnectAsync(string deviceAddress)
        {
            try
            {
                _device = await BluetoothLEDevice.FromBluetoothAddressAsync(ulong.Parse(deviceAddress.Replace(":", "")));
                if (_device == null) return false;

                // GattSession is optional power management, skip for compatibility
                var services = await _device.GetGattServicesForUuidAsync(new Guid(NordicUart.ServiceUuid));

                foreach (var deviceService in services.Services)
                {
                    var characteristics = await deviceService.GetCharacteristicsForUuidAsync(
                        new Guid(NordicUart.CharacteristicTx));

                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        if ((characteristic.CharacteristicProperties & GattCharacteristicProperties.Write) != 0)
                        {
                            _txCharacteristic = characteristic;
                            break;
                        }
                    }
                }

                if (_txCharacteristic != null)
                {
                    // Store last connected device for auto-connect
                    _settings.LastDeviceAddress = deviceAddress;
                    SaveSettings();

                    ConnectionStateChanged?.Invoke(this, WatercoolerConnectionState.Connected);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the current device and sends a reset command.
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                if (IsConnected)
                    await WriteResetCommandAsync();
            }
            catch { /* non-critical during disconnect */ }
            finally
            {
                _txCharacteristic = null;
                _device?.Dispose();
                _device = null;
                ConnectionStateChanged?.Invoke(this, WatercoolerConnectionState.Disconnected);
            }
        }

        /// <summary>
        /// Sends pump voltage command to the device.
        /// </summary>
        public async Task<bool> WritePumpModeAsync(PumpVoltage voltage)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected");

            byte payload;
            bool enabled;

            if (voltage == PumpVoltage.Off)
            {
                payload = 0x00;
                enabled = false;
            }
            else
            {
                payload = (byte)voltage;
                enabled = true;
            }

            var frame = BuildFrame(WaterCoolerCommand.Pump, enabled, new[] { payload });
            return await WriteFrameAsync(frame);
        }

        /// <summary>
        /// Sends fan speed command to the device.
        /// </summary>
        public async Task<bool> WriteFanModeAsync(FanSpeed speed)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected");

            byte payload;
            bool enabled;

            if (speed == FanSpeed.Off)
            {
                payload = 0x00;
                enabled = false;
            }
            else
            {
                payload = (byte)speed;
                enabled = true;
            }

            var frame = BuildFrame(WaterCoolerCommand.Fan, enabled, new[] { payload });
            return await WriteFrameAsync(frame);
        }

        /// <summary>
        /// Sends RGB mode command to the device.
        /// </summary>
        public async Task<bool> WriteRgbModeAsync(RgbState mode, RgbColor color = RgbColor.Red)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected");

            bool enabled = mode != RgbState.Off;

            byte[] payload;
            if (enabled)
            {
                payload = new[] { (byte)mode, (byte)color };
            }
            else
            {
                payload = Array.Empty<byte>();
            }

            var frame = BuildFrame(WaterCoolerCommand.Rgb, enabled, payload);
            return await WriteFrameAsync(frame);
        }

        /// <summary>
        /// Sends status query command to the device.
        /// </summary>
        public async Task<bool> QueryStatusAsync()
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected");

            var frame = BuildFrame(WaterCoolerCommand.Status, true, Array.Empty<byte>());
            return await WriteFrameAsync(frame);
        }

        /// <summary>
        /// Sends reset command to the device.
        /// </summary>
        public async Task<bool> WriteResetAsync()
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected");

            var frame = BuildFrame(WaterCoolerCommand.Reset, false, Array.Empty<byte>());
            return await WriteFrameAsync(frame);
        }

        /// <summary>
        /// Attempts to auto-connect to the last known device on startup.
        /// </summary>
        public async Task<bool> TryAutoConnectAsync()
        {
            if (!_settings.AutoConnect || string.IsNullOrEmpty(_settings.LastDeviceAddress))
                return false;

            StatusChanged?.Invoke(this, "Attempting auto-connect...");
            var connected = await ConnectAsync(_settings.LastDeviceAddress);

            if (connected)
            {
                // Restore saved settings to device
                try
                {
                    var pumpVoltage = _settings.GetPumpVoltage();
                    if (pumpVoltage != PumpVoltage.Off)
                        await WritePumpModeAsync(pumpVoltage);

                    var fanSpeed = _settings.GetFanSpeed();
                    if (fanSpeed != FanSpeed.Off)
                        await WriteFanModeAsync(fanSpeed);

                    var rgbMode = _settings.GetRgbMode();
                    var rgbColor = _settings.GetRgbColor();
                    if (_settings.RgbEnabled && rgbMode != RgbState.Off)
                        await WriteRgbModeAsync(rgbMode, rgbColor);
                }
                catch { /* non-critical during auto-connect */ }

                StatusChanged?.Invoke(this, "Auto-connected successfully");
            }
            else
            {
                StatusChanged?.Invoke(this, "Auto-connect failed (device not found)");
            }

            return connected;
        }

        private async Task<bool> WriteFrameAsync(byte[] frame)
        {
            try
            {
                var status = await _txCharacteristic.WriteValueAsync(frame.AsBuffer());
                return status == GattCommunicationStatus.Success;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Write failed: {ex.Message}");
                return false;
            }
        }

        private byte[] BuildFrame(byte command, bool enabled, byte[] payload)
        {
            var frame = new List<byte>
            {
                WaterCoolerCommand.FrameStart,
                command,
                enabled ? (byte)1 : (byte)0
            };
            frame.AddRange(payload);

            // Checksum: XOR all bytes between start and end markers
            byte checksum = 0;
            for (int i = 1; i < frame.Count; i++)
                checksum ^= frame[i];
            frame.Add(checksum);
            frame.Add(WaterCoolerCommand.FrameEnd);

            return frame.ToArray();
        }

        private async Task WriteResetCommandAsync()
        {
            if (_txCharacteristic == null) return;
            try
            {
                var frame = BuildFrame(WaterCoolerCommand.Reset, false, Array.Empty<byte>());
                await _txCharacteristic.WriteValueAsync(frame.AsBuffer());
            }
            catch { /* non-critical */ }
        }

        public void Dispose()
        {
            DisconnectAsync().Wait(2000);
            foreach (var watcher in _watchers)
                watcher.Stop();
        }

        /// <summary>
        /// Connection state changes raised by the service.
        /// </summary>
        public enum WatercoolerConnectionState
        {
            Connected,
            Disconnected,
            Scanning
        }
    }
}
