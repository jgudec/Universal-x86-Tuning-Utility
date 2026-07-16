using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Universal_x86_Tuning_Utility.Models;
using Universal_x86_Tuning_Utility.Services;

namespace Universal_x86_Tuning_Utility.Views.Pages
{
    /// <summary>
    /// Interaction logic for Watercooler page.
    /// Provides pump voltage, fan speed, and RGB lighting controls for LCT water coolers.
    /// </summary>
    public partial class Watercooler : Page
    {
        private WaterCoolerService? _waterCoolerService;
        private string? _selectedDeviceAddress;
        private bool _isInitialized;

        public Watercooler()
        {
            InitializeComponent();
            Loaded += Watercooler_Loaded;
        }

        private void Watercooler_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            Loaded -= Watercooler_Loaded;

            try
            {
                _waterCoolerService = App.GetService<WaterCoolerService>();
                if (_waterCoolerService == null)
                {
                    MessageBox.Show(
                        "Watercooler service is not available.\nPlease restart the application.",
                        "Service Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                _waterCoolerService.ConnectionStateChanged += OnConnectionStateChanged;
                _waterCoolerService.StatusChanged += OnStatusChanged;

                LoadSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to initialize watercooler page: {ex.Message}",
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadSettings()
        {
            if (_waterCoolerService == null) return;

            var settings = _waterCoolerService.GetSettings();

            // Restore pump voltage selection
            var pumpVoltage = settings.GetPumpVoltage();
            cbxPumpVoltage.SelectedIndex = GetPumpVoltageIndex(pumpVoltage);

            // Restore fan speed selection
            var fanSpeed = settings.GetFanSpeed();
            cbxFanSpeed.SelectedIndex = GetFanSpeedIndex(fanSpeed);

            // Restore RGB mode selection
            var rgbMode = settings.GetRgbMode();
            cbxRgbMode.SelectedIndex = GetRgbModeIndex(rgbMode);

            // Restore RGB color selection
            var rgbColor = settings.GetRgbColor();
            cbxRgbColor.SelectedIndex = GetRgbColorIndex(rgbColor);

            // Restore auto-connect toggle
            tsAutoConnect.IsChecked = settings.AutoConnect;

            // Check if the service is already connected (e.g., via auto-connect on startup)
            if (_waterCoolerService.IsConnected)
            {
                _selectedDeviceAddress = settings.LastDeviceAddress;
                OnConnectionStateChanged(null, WaterCoolerService.WatercoolerConnectionState.Connected);
            }
            else
            {
                // Pre-populate from last known device so Connect/Disconnect works without re-scanning
                if (!string.IsNullOrEmpty(settings.LastDeviceAddress))
                    _selectedDeviceAddress = settings.LastDeviceAddress;

                SetControlsEnabled(false);
            }
        }

        private int GetPumpVoltageIndex(PumpVoltage voltage) => voltage switch
        {
            PumpVoltage.Off => 0,
            PumpVoltage.V7 => 1,
            PumpVoltage.V8 => 2,
            PumpVoltage.V11 => 3,
            _ => 0
        };

        private int GetFanSpeedIndex(FanSpeed speed) => speed switch
        {
            FanSpeed.Off => 0,
            FanSpeed.Percent25 => 1,
            FanSpeed.Percent50 => 2,
            FanSpeed.Percent75 => 3,
            FanSpeed.Percent90 => 4,
            FanSpeed.Percent95 => 5,
            FanSpeed.Percent100 => 6,
            _ => 0
        };

        private int GetRgbModeIndex(RgbState mode) => mode switch
        {
            RgbState.Off => 0,
            RgbState.Static => 1,
            RgbState.Breathe => 2,
            RgbState.Colorful => 3,
            RgbState.BreatheColor => 4,
            _ => 0
        };

        private int GetRgbColorIndex(RgbColor color) => color switch
        {
            RgbColor.Red => 0,
            RgbColor.Green => 1,
            RgbColor.Blue => 2,
            RgbColor.White => 3,
            _ => 0
        };

        private PumpVoltage GetSelectedPumpVoltage()
        {
            return (cbxPumpVoltage.SelectedIndex + 1) switch
            {
                1 => PumpVoltage.Off,
                2 => PumpVoltage.V7,
                3 => PumpVoltage.V8,
                _ => PumpVoltage.V11
            };
        }

        private FanSpeed GetSelectedFanSpeed()
        {
            return (cbxFanSpeed.SelectedIndex + 1) switch
            {
                1 => FanSpeed.Off,
                2 => FanSpeed.Percent25,
                3 => FanSpeed.Percent50,
                4 => FanSpeed.Percent75,
                5 => FanSpeed.Percent90,
                6 => FanSpeed.Percent95,
                _ => FanSpeed.Percent100
            };
        }

        private RgbState GetSelectedRgbMode()
        {
            return (cbxRgbMode.SelectedIndex + 1) switch
            {
                1 => RgbState.Off,
                2 => RgbState.Static,
                3 => RgbState.Breathe,
                4 => RgbState.Colorful,
                _ => RgbState.BreatheColor
            };
        }

        private RgbColor GetSelectedRgbColor()
        {
            return (cbxRgbColor.SelectedIndex + 1) switch
            {
                1 => RgbColor.Red,
                2 => RgbColor.Green,
                3 => RgbColor.Blue,
                _ => RgbColor.White
            };
        }

        private void SetControlsEnabled(bool enabled)
        {
            cbxPumpVoltage.IsEnabled = enabled;
            cbxFanSpeed.IsEnabled = enabled;
            cbxRgbMode.IsEnabled = enabled;
            cbxRgbColor.IsEnabled = enabled;
        }

        #region Device Discovery & Connection

        private async void btnScan_Click(object sender, RoutedEventArgs e)
        {
            if (_waterCoolerService == null) return;

            btnScan.IsEnabled = false;
            cbxDevices.Items.Clear();

            try
            {
                var devices = await _waterCoolerService.DiscoverDevicesAsync(10000);

                foreach (var device in devices)
                    cbxDevices.Items.Add(device);

                if (cbxDevices.Items.Count > 0)
                    cbxDevices.SelectedIndex = 0;
                else
                    txtConnectionStatus.Text = "No devices found";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnScan.IsEnabled = true;
            }
        }

        private void cbxDevices_SelectionChanged(object sender, EventArgs e)
        {
            if (cbxDevices.SelectedItem is WaterCoolerDeviceInfo device)
            {
                _selectedDeviceAddress = device.Address;
                btnConnect.IsEnabled = _waterCoolerService == null || !_waterCoolerService.IsConnected;
            }
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_waterCoolerService == null || _selectedDeviceAddress == null) return;

            btnConnect.IsEnabled = false;
            txtConnectionStatus.Text = "Connecting...";

            var connected = await _waterCoolerService.ConnectAsync(_selectedDeviceAddress);

            if (connected)
            {
                // Pause after connection for device BLE stack to stabilize.
                await Task.Delay(500);

                // Restore saved settings to device.
                // WriteWithResponse ensures reliable delivery — no artificial delays needed.
                try
                {
                    var pumpVoltage = GetSelectedPumpVoltage();
                    if (pumpVoltage != PumpVoltage.Off)
                        await _waterCoolerService.WritePumpModeAsync(pumpVoltage);

                    var fanSpeed = GetSelectedFanSpeed();
                    if (fanSpeed != FanSpeed.Off)
                        await _waterCoolerService.WriteFanModeAsync(fanSpeed);

                    var rgbMode = GetSelectedRgbMode();
                    var rgbColor = GetSelectedRgbColor();
                    if (rgbMode != RgbState.Off)
                        await _waterCoolerService.WriteRgbModeAsync(rgbMode, rgbColor);
                }
                catch { /* non-critical during connect */ }
            }
            else
            {
                txtConnectionStatus.Text = "Connection failed";
                btnConnect.IsEnabled = true;
            }
        }

        private async void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_waterCoolerService == null) return;

            await _waterCoolerService.DisconnectAsync();
            SetControlsEnabled(false);
        }

        #endregion

        #region Control Handlers

        private async void cbxPumpVoltage_SelectionChanged(object sender, EventArgs e)
        {
            if (_waterCoolerService == null || !_waterCoolerService.IsConnected) return;

            var voltage = GetSelectedPumpVoltage();
            await _waterCoolerService.WritePumpModeAsync(voltage);
            _waterCoolerService.UpdatePumpVoltage(voltage);
        }

        private async void cbxFanSpeed_SelectionChanged(object sender, EventArgs e)
        {
            if (_waterCoolerService == null || !_waterCoolerService.IsConnected) return;

            var speed = GetSelectedFanSpeed();
            await _waterCoolerService.WriteFanModeAsync(speed);
            _waterCoolerService.UpdateFanSpeed(speed);
        }

        private async void cbxRgbMode_SelectionChanged(object sender, EventArgs e)
        {
            if (_waterCoolerService == null || !_waterCoolerService.IsConnected) return;

            var mode = GetSelectedRgbMode();
            var color = GetSelectedRgbColor();
            await _waterCoolerService.WriteRgbModeAsync(mode, color);
            _waterCoolerService.UpdateRgbMode(mode);
        }

        private async void cbxRgbColor_SelectionChanged(object sender, EventArgs e)
        {
            if (_waterCoolerService == null || !_waterCoolerService.IsConnected) return;

            var mode = GetSelectedRgbMode();
            var color = GetSelectedRgbColor();
            await _waterCoolerService.WriteRgbModeAsync(mode, color);
            _waterCoolerService.UpdateRgbColor(color);
        }

        private void tsAutoConnect_Checked(object sender, RoutedEventArgs e)
        {
            if (_waterCoolerService != null)
                _waterCoolerService.UpdateAutoConnect(true);
        }

        private void tsAutoConnect_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_waterCoolerService != null)
                _waterCoolerService.UpdateAutoConnect(false);
        }

        #endregion

        #region Event Handlers

        private void OnConnectionStateChanged(object? sender, WaterCoolerService.WatercoolerConnectionState state)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (state)
                {
                    case WaterCoolerService.WatercoolerConnectionState.Connected:
                        iconConnected.Visibility = Visibility.Visible;
                        iconDisconnected.Visibility = Visibility.Collapsed;
                        txtConnectionStatus.Text = "Connected";
                        btnConnect.IsEnabled = false;
                        btnDisconnect.IsEnabled = true;
                        SetControlsEnabled(true);
                        cardDiscovery.Visibility = Visibility.Collapsed;
                        spControls.Visibility = Visibility.Visible;
                        break;

                    case WaterCoolerService.WatercoolerConnectionState.Disconnected:
                        iconConnected.Visibility = Visibility.Collapsed;
                        iconDisconnected.Visibility = Visibility.Visible;
                        txtConnectionStatus.Text = "Not connected";
                        btnConnect.IsEnabled = _selectedDeviceAddress != null;
                        btnDisconnect.IsEnabled = false;
                        SetControlsEnabled(false);
                        cardDiscovery.Visibility = Visibility.Visible;
                        spControls.Visibility = Visibility.Collapsed;
                        break;

                    case WaterCoolerService.WatercoolerConnectionState.Scanning:
                        txtConnectionStatus.Text = "Scanning...";
                        break;
                }
            });
        }

        private void OnStatusChanged(object? sender, string status)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                txtConnectionStatus.Text = status;
            });
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_waterCoolerService != null)
            {
                _waterCoolerService.ConnectionStateChanged -= OnConnectionStateChanged;
                _waterCoolerService.StatusChanged -= OnStatusChanged;
            }
        }

        #endregion
    }
}
