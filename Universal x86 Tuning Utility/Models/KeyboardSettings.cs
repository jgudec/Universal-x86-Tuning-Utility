namespace Universal_x86_Tuning_Utility.Models
{
    /// <summary>
    /// Persisted settings for the Keyboard RGB page.
    /// Stored in %APPDATA%\UXTU\keyboard_settings.json
    /// </summary>
    public class KeyboardSettings
    {
        /// <summary>
        /// Whether the keyboard backlight is powered on.
        /// </summary>
        public bool PowerOn { get; set; } = true;

        /// <summary>
        /// Red channel 0-255 for static color mode.
        /// </summary>
        public byte ColorR { get; set; } = 0;

        /// <summary>
        /// Green channel 0-255 for static color mode.
        /// </summary>
        public byte ColorG { get; set; } = 255;

        /// <summary>
        /// Blue channel 0-255 for static color mode.
        /// </summary>
        public byte ColorB { get; set; } = 255;

        /// <summary>
        /// Brightness level 0-7.
        /// </summary>
        public int Brightness { get; set; } = 5;
    }
}
