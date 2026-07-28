using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Universal_x86_Tuning_Utility.Models;
using Universal_x86_Tuning_Utility.Services;

namespace Universal_x86_Tuning_Utility.Views.Pages
{
    /// <summary>
    /// Keyboard RGB backlight control page.
    /// Uses HID feature reports to control the ITE lighting controller (vid_048d).
    /// The keyboard backlight is NOT controlled through EC registers.
    /// </summary>
    public partial class Keyboard : Page
    {
        private readonly KeyboardHidService? _hidService;

        public Keyboard()
        {
            InitializeComponent();

            // Try to initialize the HID keyboard controller
            _hidService = new KeyboardHidService();
            bool hidAvailable = false;

            try
            {
                hidAvailable = _hidService.Open();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KBD] HID keyboard init failed: {ex.Message}");
            }

            if (hidAvailable)
            {
                KeyboardAvailable.Visibility = Visibility.Visible;
                KeyboardUnavailable.Visibility = Visibility.Collapsed;

                // Load persisted settings
                var settings = KeyboardSettingsService.Load();
                ApplySettings(settings);
            }
            else
            {
                KeyboardAvailable.Visibility = Visibility.Collapsed;
                KeyboardUnavailable.Visibility = Visibility.Visible;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Apply settings on first navigation
            ApplySettingsToHid();
        }

        private void TsKeyboardPower_Toggled(object sender, RoutedEventArgs e)
        {
            ApplySettingsToHid();
        }

        private void ColorPicker_ColorChangedDelayed(object sender, EventArgs e)
        {
            ApplySettingsToHid();
        }

        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            BrightnessValueText.Text = ((int)BrightnessSlider.Value).ToString();
            ApplySettingsToHid();
        }

        private void ApplySettingsToHid()
        {
            if (_hidService is null) return;

            try
            {
                bool powerOn = tsKeyboardPower.IsChecked == true;

                // Brightness slider is 0-7, convert to 0-100 for HID
                int brightnessLevel = (int)BrightnessSlider.Value;
                int brightnessPercent = (brightnessLevel * 100) / 7;

                if (powerOn)
                {
                    var color = ColorPicker.SelectedColor;
                    _hidService.TurnOn(color.R, color.G, color.B, brightnessPercent);
                }
                else
                {
                    _hidService.TurnOff();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KBD] Keyboard HID error: {ex.Message}");
            }
        }

        private void ApplySettings(KeyboardSettings settings)
        {
            tsKeyboardPower.IsChecked = settings.PowerOn;
            BrightnessSlider.Value = Math.Clamp(settings.Brightness, 0, 7);
            BrightnessValueText.Text = Math.Clamp(settings.Brightness, 0, 7).ToString();

            ColorPicker.SelectedColor = Color.FromRgb(settings.ColorR, settings.ColorG, settings.ColorB);
        }

        private void Page_Unloaded(object sender, EventArgs e)
        {
            _hidService?.Dispose();
        }
    }
}
