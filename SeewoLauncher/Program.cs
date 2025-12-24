using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Injectio.Attributes;
using CSharpMarkup.Wpf;
using static CSharpMarkup.Wpf.Helpers;
using System.Windows.Input;
using System.Runtime.CompilerServices;

// Static imports for Colors and Brushes
using static System.Windows.Media.Colors;
using static System.Windows.Media.Brushes;

// Using aliases
using Window = System.Windows.Window;
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using Orientation = System.Windows.Controls.Orientation;
using TextAlignment = System.Windows.TextAlignment;
using CPoint = System.Windows.Point; // Avoid conflict if any

namespace SeewoLauncher
{
    #region Entry & DI
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSeewoLauncher();
                    services.AddSingleton<App>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            var app = host.Services.GetRequiredService<App>();
            app.Run(host.Services.GetRequiredService<MainWindow>());
        }
    }

    public class App : Application
    {
        public App()
        {
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }
    #endregion

    #region Models & Interfaces

    public class AppConfig
    {
        public List<AppItem> Apps { get; set; } = new();
        public List<ToolItem> Tools { get; set; } = new();
    }

    public class AppItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string Icon { get; set; } = "";
    }

    public class ToolItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Icon { get; set; } = "";
    }

    public class DiskItem
    {
        public string DriveLetter { get; set; } = "";
        public string Label { get; set; } = "";
        public string TotalSize { get; set; } = "";
        public string FreeSpace { get; set; } = "";
        public double UsagePercentage { get; set; }
    }

    public interface IConfigService
    {
        AppConfig CurrentConfig { get; }
        event Action<AppConfig> ConfigChanged;
        void LoadConfig();
    }

    public interface IDeviceMonitor
    {
        event Action<List<DiskItem>> DisksChanged;
        void StartMonitoring();
        void OpenDisk(string path);
        void EjectDisk(string path);
    }

    public interface ILauncherService
    {
        void LaunchApp(string path, string args);
    }

    #endregion

    #region Services

    [RegisterSingleton]
    public class ConfigService : IConfigService
    {
        private const string ConfigFile = "config.json";
        private FileSystemWatcher _watcher;
        public AppConfig CurrentConfig { get; private set; } = new();
        public event Action<AppConfig> ConfigChanged;

        public ConfigService()
        {
            LoadConfig();
            SetupWatcher();
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        CurrentConfig = config;
                        ConfigChanged?.Invoke(CurrentConfig);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Config Load Error: {ex.Message}");
            }
        }

        private void SetupWatcher()
        {
            _watcher = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
            _watcher.NotifyFilter = NotifyFilters.LastWrite;
            _watcher.Changed += (s, e) => Task.Delay(500).ContinueWith(_ => LoadConfig());
            _watcher.EnableRaisingEvents = true;
        }
    }

    [RegisterSingleton]
    public class LauncherService : ILauncherService
    {
        public void LaunchApp(string path, string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch: {ex.Message}", "Error");
            }
        }
    }

    [RegisterSingleton]
    public class DeviceMonitor : IDeviceMonitor
    {
        public event Action<List<DiskItem>> DisksChanged;
        private ManagementEventWatcher _watcher;

        public void StartMonitoring()
        {
            RefreshDisks();
            try
            {
                var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2 or EventType = 3");
                _watcher = new ManagementEventWatcher(query);
                _watcher.EventArrived += (s, e) => RefreshDisks();
                _watcher.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI Error: {ex.Message}");
            }
        }

        private void RefreshDisks()
        {
            var disks = new List<DiskItem>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType == DriveType.Removable && drive.IsReady)
                    {
                        disks.Add(new DiskItem
                        {
                            DriveLetter = drive.Name,
                            Label = string.IsNullOrEmpty(drive.VolumeLabel) ? "Removable Disk" : drive.VolumeLabel,
                            TotalSize = FormatBytes(drive.TotalSize),
                            FreeSpace = FormatBytes(drive.AvailableFreeSpace),
                            UsagePercentage = 100 - ((double)drive.AvailableFreeSpace / drive.TotalSize * 100)
                        });
                    }
                }
            }
            catch { }
            
            DisksChanged?.Invoke(disks);
        }

        public void OpenDisk(string path) => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        
        public void EjectDisk(string path) => MessageBox.Show($"Safely Ejecting {path}...", "Eject");

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    #endregion

    #region ViewModels

    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) 
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged;
    }

    [RegisterSingleton]
    public class MainViewModel : ViewModelBase
    {
        private readonly IConfigService _configService;
        private readonly IDeviceMonitor _deviceMonitor;
        private readonly ILauncherService _launcherService;

        public ObservableCollection<AppItem> Apps { get; } = new();
        public ObservableCollection<ToolItem> Tools { get; } = new();
        public ObservableCollection<DiskItem> Disks { get; } = new();

        public ICommand LaunchAppCommand { get; }
        public ICommand OpenDiskCommand { get; }
        public ICommand EjectDiskCommand { get; }

        private string _dateText = "";
        public string DateText { get => _dateText; set { _dateText = value; OnPropertyChanged(); } }
        
        private string _timeText = "";
        public string TimeText { get => _timeText; set { _timeText = value; OnPropertyChanged(); } }

        public MainViewModel(IConfigService configService, IDeviceMonitor deviceMonitor, ILauncherService launcherService)
        {
            _configService = configService;
            _deviceMonitor = deviceMonitor;
            _launcherService = launcherService;

            LaunchAppCommand = new RelayCommand(o => 
            {
                if (o is AppItem app) _launcherService.LaunchApp(app.Path, app.Arguments);
                if (o is ToolItem tool) _launcherService.LaunchApp(tool.Path, "");
            });

            OpenDiskCommand = new RelayCommand(o => _deviceMonitor.OpenDisk(o?.ToString() ?? ""));
            EjectDiskCommand = new RelayCommand(o => _deviceMonitor.EjectDisk(o?.ToString() ?? ""));

            _configService.ConfigChanged += OnConfigChanged;
            _deviceMonitor.DisksChanged += OnDisksChanged;

            OnConfigChanged(_configService.CurrentConfig);
            _deviceMonitor.StartMonitoring();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => UpdateTime();
            timer.Start();
            UpdateTime();
        }

        private void UpdateTime()
        {
            DateText = DateTime.Now.ToString("MM月dd日 dddd");
            TimeText = DateTime.Now.ToString("HH:mm");
        }

        private void OnConfigChanged(AppConfig config)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Apps.Clear();
                if (config.Apps != null) foreach (var app in config.Apps) Apps.Add(app);

                Tools.Clear();
                if (config.Tools != null) foreach (var tool in config.Tools) Tools.Add(tool);
            });
        }

        private void OnDisksChanged(List<DiskItem> disks)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Disks.Clear();
                foreach (var d in disks) Disks.Add(d);
            });
        }
    }

    #endregion

    #region View

    public class MainWindow : Window
    {
        public MainWindow(MainViewModel vm)
        {
            DataContext = vm;
            Title = "Seewo Launcher";
            WindowState = WindowState.Maximized;
            WindowStyle = WindowStyle.None;
            
            var bgImage = new BitmapImage(new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui.png"), UriKind.RelativeOrAbsolute));
            Background = new ImageBrush(bgImage) { Stretch = Stretch.UniformToFill };

            Content = Grid(
                Columns(
                    Star, 
                    400   
                ),

                Border(
                    // Changed VStack to Grid to allow "Spacer" behavior via RowDefinitions
                    Grid(
                        Rows(
                            Auto, // Header
                            Auto, // Tools Header
                            Auto, // Tools List
                            Auto, // U-Disk List
                            Auto, // Apps Header
                            Auto, // Apps List
                            Star, // Spacer (Pushes bottom content down)
                            Auto  // Bottom Actions
                        ),

                        // Header: Weather & Time
                        Grid(
                            Rows(Auto, Auto),
                            HStack(
                                TextBlock().FontSize(24).FontWeight(FontWeights.Bold).Foreground(Brushes.White)
                                    .Bind(TextBlock.TextProperty, nameof(vm.TimeText)),
                                TextBlock("大雨 16°C - 23°C").Foreground(Brushes.White).Margin(10,0,0,0) // Corrected Margin logic
                            ),
                            TextBlock().Grid_Row(1).FontSize(12).Foreground(Brushes.Gray)
                                .Bind(TextBlock.TextProperty, nameof(vm.DateText))
                        ).Grid_Row(0).Margin(0, 0, 0, 20),

                        // Tools Header
                        TextBlock("常用工具").Grid_Row(1).Foreground(Brushes.White).Margin(0, 20, 0, 10),

                        // Tools List
                        ItemsControl()
                            .Grid_Row(2)
                            .Bind(ItemsControl.ItemsSourceProperty, nameof(vm.Tools))
                            .ItemsPanel(
                                ItemsPanelTemplate(() => WrapPanel().IsItemsHost(true))
                            )
                            .ItemTemplate(DataTemplate(() => 
                                Button(
                                    VStack(
                                        TextBlock("🕒").FontSize(24).Center(),
                                        TextBlock().FontSize(10).Foreground(Brushes.Gray).Center()
                                            .Bind(TextBlock.TextProperty, "Name")
                                    )
                                )
                                .Background(Brushes.Transparent)
                                .BorderThickness(0)
                                .Margin(5)
                                .Command(Binding("DataContext.LaunchAppCommand", RelativeSource: RelativeSource(typeof(Window))))
                                .CommandParameter(Binding("."))
                            )),

                        // U-Disk Monitor Section
                        ItemsControl()
                            .Grid_Row(3)
                            .Bind(ItemsControl.ItemsSourceProperty, nameof(vm.Disks))
                            .ItemTemplate(DataTemplate(() =>
                                Grid(
                                    Columns(50, Star, 80),
                                    // Icon
                                    TextBlock("💾").FontSize(20).Center().Foreground(Brushes.White),
                                    
                                    // Info
                                    VStack(
                                        TextBlock().Foreground(Brushes.White).Bind(TextBlock.TextProperty, "Label"),
                                        TextBlock().Foreground(Brushes.Gray).FontSize(10)
                                            .Bind(TextBlock.TextProperty, binding => binding.Path("FreeSpace").StringFormat("剩余 {0}"))
                                    ).Grid_Column(1).VCenter(),

                                    // Actions
                                    HStack(
                                        Button("打开").FontSize(10).Padding(5,2)
                                            .Command(Binding("DataContext.OpenDiskCommand", RelativeSource: RelativeSource(typeof(Window))))
                                            .CommandParameter(Binding("DriveLetter"))
                                    ).Grid_Column(2).Center()
                                )
                                .Margin(0, 10, 0, 10)
                                .Background(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)))
                                .CornerRadius(8)
                            )),

                        // Apps Header
                        TextBlock("常用应用").Grid_Row(4).Foreground(Brushes.Gray).Margin(0, 20, 0, 10),

                        // Apps List
                        ItemsControl()
                            .Grid_Row(5)
                            .Bind(ItemsControl.ItemsSourceProperty, nameof(vm.Apps))
                            .ItemsPanel(
                                ItemsPanelTemplate(() => WrapPanel().IsItemsHost(true))
                            )
                            .ItemTemplate(DataTemplate(() =>
                                Button(
                                    VStack(
                                        Border(
                                            TextBlock("EN").Foreground(Brushes.White).Center()
                                        )
                                        .Width(40).Height(40)
                                        .Background(new SolidColorBrush(Color.FromRgb(0, 180, 100)))
                                        .CornerRadius(5)
                                        .Margin(0,0,0,5),
                                        
                                        TextBlock().Foreground(Brushes.White).FontSize(10).Center().TextWrapping(TextWrapping.Wrap)
                                            .Bind(TextBlock.TextProperty, "Name")
                                    )
                                )
                                .Width(60).Height(70)
                                .Background(Brushes.Transparent)
                                .BorderThickness(0)
                                .Margin(5)
                                .Command(Binding("DataContext.LaunchAppCommand", RelativeSource: RelativeSource(typeof(Window))))
                                .CommandParameter(Binding("."))
                            )),
                        
                        // Spacer (Handled by Row Definition Star)

                        // Bottom Actions
                        DockPanel(
                            Button("一键下课")
                                .DockPanel_Dock(Dock.Left)
                                .Height(40)
                                .Background(Brushes.Transparent)
                                .Foreground(Brushes.White)
                                .BorderBrush(Brushes.Gray)
                                .BorderThickness(1)
                                .Invoke(b => b.Click += (s, e) => Application.Current.Shutdown())
                        ).Grid_Row(7)
                    )
                    .Padding(20)
                    .Background(new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)))
                    .CornerRadius(20, 0, 0, 20)
                    .Margin(0, 40, 0, 40)
                ).Grid_Column(1)
            );
        }
    }
    #endregion
}