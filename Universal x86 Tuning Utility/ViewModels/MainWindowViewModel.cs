using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Universal_x86_Tuning_Utility.Scripts;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Mvvm.Contracts;

namespace Universal_x86_Tuning_Utility.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private string _applicationTitle = String.Empty;

        [ObservableProperty]
        private ObservableCollection<INavigationControl> _navigationItems = new();

        [ObservableProperty]
        private ObservableCollection<INavigationControl> _navigationFooter = new();

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems = new();

        [ObservableProperty]
        private string _downloads = "Downloads: ";

        [ObservableProperty]
        private bool _isDownloads = false;

        public MainWindowViewModel(INavigationService navigationService)
        {
            if (!_isInitialized)
                InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            ApplicationTitle = "Universal x86 Tuning Utility";
            if (Family.TYPE == Family.ProcessorType.Intel)
            {
                NavigationItems = new ObservableCollection<INavigationControl>
                {
                new NavigationItem()
                {
                    Content = "Home",
                    PageTag = "dashboard",
                    Icon = SymbolRegular.Home20,
                    PageType = typeof(Views.Pages.DashboardPage)
                },
                //new NavigationItem()
                //{
                //    Content = "Premade",
                //    PageTag = "premade",
                //    Icon = SymbolRegular.Predictions20,
                //    PageType = typeof(Views.Pages.Premade)
                //},
                new NavigationItem()
                {
                    Content = "Custom",
                    PageTag = "custom",
                    Icon = SymbolRegular.Book20,
                    PageType = typeof(Views.Pages.CustomPresets)
                },
                new NavigationItem()
                {
                    Content = "Adaptive",
                    PageTag = "adaptive",
                    Icon = SymbolRegular.Radar20,
                    PageType = typeof(Views.Pages.Adaptive)
                },
                new NavigationItem()
                {
                    Content = "Games",
                    PageTag = "games",
                    Icon = SymbolRegular.Games20,
                    PageType = typeof(Views.Pages.Games)
                },
                new NavigationItem()
                {
                    Content = "Auto",
                    PageTag = "auto",
                    Icon = SymbolRegular.Transmission20,
                    PageType = typeof(Views.Pages.Automations)
                },
                //new NavigationItem()
                //{
                //    Content = "Fan",
                //    PageTag = "fan",
                //    Icon = SymbolRegular.WeatherDuststorm20,
                //    PageType = typeof(Views.Pages.FanControl)
                //},
                // new NavigationItem()
                //{
                //    Content = "Magpie",
                //    PageTag = "magpie",
                //    Icon = SymbolRegular.FullScreenMaximize20,
                //    PageType = typeof(Views.Pages.DataPage)
                //},
                new NavigationItem()
                {
                    Content = "Info",
                    PageTag = "info",
                    Icon = SymbolRegular.Info20,
                    PageType = typeof(Views.Pages.SystemInfo)
                }
            };

                // Conditionally insert Hydro UI before Info for supported watercooler hardware
                if (WaterCoolerHardwareDetector.IsSupportedHardware())
                {
                    NavigationItems.Insert(NavigationItems.Count - 1, new NavigationItem()
                    {
                        Content = "Hydro UI",
                        PageTag = "watercooler",
                        Icon = SymbolRegular.Water20,
                        PageType = typeof(Views.Pages.Watercooler)
                    });
                }

                // Conditionally insert Flydigi cooler before Info for connected cooling pads
                if (FlydigiHardwareDetector.IsDeviceAvailable())
                {
                    NavigationItems.Insert(NavigationItems.Count - 1, new NavigationItem()
                    {
                        Content = FlydigiHardwareDetector.GetDetectedModelName(),
                        PageTag = "flydigicooler",
                        Image = CreateFlydigiLogoImageSource(),
                        PageType = typeof(Views.Pages.FlydigiCooler)
                    });
                }

                NavigationFooter = new ObservableCollection<INavigationControl>
            {
                new NavigationItem()
                {
                    Content = "Settings",
                    PageTag = "settings",
                    Icon = SymbolRegular.Settings20,
                    PageType = typeof(Views.Pages.SettingsPage)
                }
            };

                TrayMenuItems = new ObservableCollection<MenuItem>
            {
                new MenuItem
                {
                    Header = "Home",
                    Tag = "tray_home"
                }
            };
            }
            else
            {
                NavigationItems = new ObservableCollection<INavigationControl>
                {
                new NavigationItem()
                {
                    Content = "Home",
                    PageTag = "dashboard",
                    Icon = SymbolRegular.Home20,
                    PageType = typeof(Views.Pages.DashboardPage)
                },
                new NavigationItem()
                {
                    Content = "Premade",
                    PageTag = "premade",
                    Icon = SymbolRegular.Predictions20,
                    PageType = typeof(Views.Pages.Premade)
                },
                new NavigationItem()
                {
                    Content = "Custom",
                    PageTag = "custom",
                    Icon = SymbolRegular.Book20,
                    PageType = typeof(Views.Pages.CustomPresets)
                },
                new NavigationItem()
                {
                    Content = "Adaptive",
                    PageTag = "adaptive",
                    Icon = SymbolRegular.Radar20,
                    PageType = typeof(Views.Pages.Adaptive)
                },
                new NavigationItem()
                {
                    Content = "Games",
                    PageTag = "games",
                    Icon = SymbolRegular.Games20,
                    PageType = typeof(Views.Pages.Games)
                },
                new NavigationItem()
                {
                    Content = "Auto",
                    PageTag = "auto",
                    Icon = SymbolRegular.Transmission20,
                    PageType = typeof(Views.Pages.Automations)
                },
                //new NavigationItem()
                //{
                //    Content = "Fan",
                //    PageTag = "fan",
                //    Icon = SymbolRegular.WeatherDuststorm20,
                //    PageType = typeof(Views.Pages.FanControl)
                //},
                // new NavigationItem()
                //{
                //    Content = "Magpie",
                //    PageTag = "magpie",
                //    Icon = SymbolRegular.FullScreenMaximize20,
                //    PageType = typeof(Views.Pages.DataPage)
                //},
                new NavigationItem()
                {
                    Content = "Info",
                    PageTag = "info",
                    Icon = SymbolRegular.Info20,
                    PageType = typeof(Views.Pages.SystemInfo)
                }
            };

                // Conditionally insert Hydro UI before Info for supported watercooler hardware
                if (WaterCoolerHardwareDetector.IsSupportedHardware())
                {
                    NavigationItems.Insert(NavigationItems.Count - 1, new NavigationItem()
                    {
                        Content = "Hydro UI",
                        PageTag = "watercooler",
                        Icon = SymbolRegular.Drop20,
                        PageType = typeof(Views.Pages.Watercooler)
                    });
                }

                // Conditionally insert Flydigi cooler before Info for connected cooling pads
                if (FlydigiHardwareDetector.IsDeviceAvailable())
                {
                    NavigationItems.Insert(NavigationItems.Count - 1, new NavigationItem()
                    {
                        Content = FlydigiHardwareDetector.GetDetectedModelName(),
                        PageTag = "flydigicooler",
                        Image = CreateFlydigiLogoImageSource(),
                        PageType = typeof(Views.Pages.FlydigiCooler)
                    });
                }

                NavigationFooter = new ObservableCollection<INavigationControl>
            {
                new NavigationItem()
                {
                    Content = "Settings",
                    PageTag = "settings",
                    Icon = SymbolRegular.Settings20,
                    PageType = typeof(Views.Pages.SettingsPage)
                }
            };

                TrayMenuItems = new ObservableCollection<MenuItem>
            {
                new MenuItem
                {
                    Header = "Home",
                    Tag = "tray_home"
                }
            };
            }

            _isInitialized = true;
        }
        private ICommand _navigateCommand;
        public ICommand NavigateCommand => _navigateCommand ??= new RelayCommand<string>(OnNavigate);

        private void OnNavigate(string parameter)
        {
            switch (parameter)
            {
                case "download":
                    Process.Start(new ProcessStartInfo("https://github.com/JamesCJ60/Universal-x86-Tuning-Utility/releases") { UseShellExecute = true });
                    return;

                case "discord":
                    Process.Start(new ProcessStartInfo("http://www.discord.gg/3EkYMZGJwq") { UseShellExecute = true });
                    return;

                case "support":
                    Process.Start(new ProcessStartInfo("https://www.paypal.com/paypalme/JamesCJ60") { UseShellExecute = true });
                    Process.Start(new ProcessStartInfo("https://patreon.com/uxtusoftware") { UseShellExecute = true });
                    return;
            }
        }

        /// <summary>
        /// Creates a Flydigi logo bitmap from the SVG path data converted to a WPF DrawingImage,
        /// then rendered to a BitmapSource for use in the NavigationItem.Image property.
        /// </summary>
        private static BitmapSource CreateFlydigiLogoImageSource()
        {
            var drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(
                        Brushes.White,
                        new Pen(Brushes.White, 1),
                        Geometry.Parse(
                            "M19.015,7.83 L0,0 L15.659,23.488 L16.777,21.251 L10.066,10.066 L15.659,11.185 " +
                            "L20.287,18.706 L16.777,25.726 L22.369,40.267 L27.962,25.726 L25.609,21.02 " +
                            "L23.645,24.163 L24.607,25.726 L22.369,40.267 L20.132,25.726 L29.079,11.185 " +
                            "L34.673,10.066 L27.962,21.251 L29.08,23.488 L45,0 L25.985,7.829 " +
                            "L22.63,14.54 L19.274,7.83 Z"))
                }
            };

            var drawingImage = new DrawingImage(drawing);
            drawingImage.Freeze();

            // Render to a 32x32 bitmap to match the SVG viewBox (45x41) at nav icon size
            var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            var visual = new System.Windows.Controls.Image { Source = drawingImage, Width = 32, Height = 32 };
            visual.Arrange(new Rect(0, 0, 32, 32));
            bitmap.Render(visual);
            bitmap.Freeze();

            return bitmap;
        }
    }
}
