using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using Computer = LibreHardwareMonitor.Hardware.Computer;
using SensorType = LibreHardwareMonitor.Hardware.SensorType;

namespace Universal_x86_Tuning_Utility.Models
{
    /// <summary>
    /// Singleton temperature provider that reads CPU and GPU temperatures
    /// via LibreHardwareMonitorLib. Manages a single Computer instance
    /// for efficient repeated reads.
    /// </summary>
    public class FlydigiTemperatureProvider : IDisposable
    {
        private readonly Computer _computer;
        private bool _disposed;

        /// <summary>
        /// Creates a new temperature provider and opens the hardware sensors.
        /// Enables CPU and GPU monitoring.
        /// </summary>
        public FlydigiTemperatureProvider()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };
            _computer.Open();
        }

        /// <summary>
        /// Gets the current CPU temperature in degrees Celsius.
        /// Returns null if no CPU temperature sensor is available or on failure.
        /// </summary>
        public double? GetCpuTemperature()
        {
            try
            {
                if (_disposed)
                    return null;

                var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpu == null)
                    return null;

                cpu.Update();

                var sensor = cpu.Sensors.FirstOrDefault(
                    s => s.SensorType == SensorType.Temperature && s.Value.HasValue);
                return sensor?.Value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the current GPU temperature in degrees Celsius.
        /// Returns null if no GPU temperature sensor is available or on failure.
        /// Checks both NVIDIA and AMD GPU hardware types.
        /// </summary>
        public double? GetGpuTemperature()
        {
            try
            {
                if (_disposed)
                    return null;

                // Try NVIDIA first, then AMD
                var gpu = _computer.Hardware.FirstOrDefault(
                    h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd);

                if (gpu == null)
                    return null;

                gpu.Update();

                var sensor = gpu.Sensors.FirstOrDefault(
                    s => s.SensorType == SensorType.Temperature && s.Value.HasValue);
                return sensor?.Value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the maximum temperature across CPU and GPU.
        /// Returns null if both readings are unavailable, or the available reading if only one is present.
        /// </summary>
        public double? GetMaxTemperature()
        {
            try
            {
                var cpuTemp = GetCpuTemperature();
                var gpuTemp = GetGpuTemperature();

                if (cpuTemp == null && gpuTemp == null)
                    return null;
                if (cpuTemp == null)
                    return gpuTemp;
                if (gpuTemp == null)
                    return cpuTemp;

                return Math.Max(cpuTemp.Value, gpuTemp.Value);
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _computer.Close();
                }
                catch
                {
                    // Ignore disposal errors
                }
                _disposed = true;
            }
        }
    }
}
