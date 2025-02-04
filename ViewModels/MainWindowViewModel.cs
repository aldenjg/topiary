using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Topiary.Models;
using Topiary.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using Topiary.Views;
using InsightType = Topiary.Models.InsightType;

namespace Topiary.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly ISettingsService _settingsService;
        private readonly IDiskScanningService _diskScanningService;
        private readonly IAIAnalysisService _aiAnalysisService;
        private FileSystemEntry _rootEntry;
        private string _selectedDrive;
        private bool _isScanning;
        private ISeries[] _pieChartData;
        private ISeries[] _barChartData;
        private ObservableCollection<DiskInsight> _insights;
        private double _scanProgress;
        private string _scanTimeText;
        private string _totalSpaceText;
        private string _usedSpaceText;
        private string _freeSpaceText;
        private Axis[] _xAxes;
        private Axis[] _yAxes;
        private SolidColorPaint _legendTextPaint = new SolidColorPaint(SKColors.Black);
        private SolidColorPaint _dataLabelsPaint = new SolidColorPaint(SKColors.Black);

        public SolidColorPaint DataLabelsPaint => _dataLabelsPaint;
        private readonly Stopwatch _scanTimer;

        public MainWindowViewModel(
            IDiskScanningService diskScanningService,
            IAIAnalysisService aiAnalysisService,
            ISettingsService settingsService)
        {
            _diskScanningService = diskScanningService;
            _aiAnalysisService = aiAnalysisService;
            _settingsService = settingsService;
            _scanTimer = new Stopwatch();
            
            ScanDriveCommand = new RelayCommand(async () => await ScanDriveAsync(), () => !IsScanning);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            AvailableDrives = GetAvailableDrives();
            
            //initial drive selection
            if (AvailableDrives.Count > 0)
            {
                SelectedDrive = AvailableDrives[0];
            }
            
            Insights = new ObservableCollection<DiskInsight>();

            //initialize empty charts
            InitializeEmptyCharts();
        }

        private void InitializeEmptyCharts()
        {
            XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = Array.Empty<string>(),
                    LabelsRotation = -45,
                    TextSize = 12,
                    LabelsPaint = new SolidColorPaint(SKColors.DimGray),
                    SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 }
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Labeler = value => $"{value:N1} GB",
                    TextSize = 12,
                    LabelsPaint = new SolidColorPaint(SKColors.DimGray),
                    SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 }
                }
            };

            PieChartData = Array.Empty<ISeries>();
            BarChartData = Array.Empty<ISeries>();
        }

        public ICommand OpenSettingsCommand { get; }
        public ICommand ScanDriveCommand { get; }

        public double ScanProgress
        {
            get => _scanProgress;
            set
            {
                _scanProgress = value;
                OnPropertyChanged();
            }
        }

        public string ScanTimeText
        {
            get => _scanTimeText;
            set
            {
                _scanTimeText = value;
                OnPropertyChanged();
            }
        }



        public string TotalSpaceText
        {
            get => _totalSpaceText;
            set
            {
            _totalSpaceText = value;
            OnPropertyChanged();
            }
        }

        public string UsedSpaceText
        {
            get => _usedSpaceText;
            set
            {
                _usedSpaceText = value;
                OnPropertyChanged();
            }
        }

        public string FreeSpaceText
        {
            get => _freeSpaceText;
            set
            {
                _freeSpaceText = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> AvailableDrives { get; }

        public string SelectedDrive
        {
            get => _selectedDrive;
            set
            {
                _selectedDrive = value;
                OnPropertyChanged();
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
                ((RelayCommand)ScanDriveCommand).RaiseCanExecuteChanged();
            }
        }

        public FileSystemEntry RootEntry
        {
            get => _rootEntry;
            set
            {
                _rootEntry = value;
                OnPropertyChanged();
                UpdateCharts();
            }
        }

        public ISeries[] PieChartData
        {
            get => _pieChartData;
            set
            {
                _pieChartData = value;
                OnPropertyChanged();
            }
        }

        public ISeries[] BarChartData
        {
            get => _barChartData;
            set
            {
                _barChartData = value;
                OnPropertyChanged();
            }
        }

        public Axis[] XAxes
        {
            get => _xAxes;
            set
            {
                _xAxes = value;
                OnPropertyChanged();
            }
        }

        public SolidColorPaint LegendTextPaint => _legendTextPaint;

        public Axis[] YAxes
        {
            get => _yAxes;
            set
            {
                _yAxes = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<DiskInsight> Insights
        {
            get => _insights;
            set
            {
                _insights = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<string> GetAvailableDrives()
        {
            return new ObservableCollection<string>(
                DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed)
                    .Select(d => d.Name[0].ToString())
            );
        }

        private void OpenSettings()
        {
            var settingsWindow = new SettingsWindow(_settingsService)
            {
                Owner = Application.Current.MainWindow
            };
            settingsWindow.ShowDialog();
        }

        private async Task ScanDriveAsync()
        {
            if (string.IsNullOrEmpty(SelectedDrive))
            {
                return;
            }

            IsScanning = true;
            ScanProgress = 0;
            ScanTimeText = "Scanning...";
            Insights.Clear();
            _scanTimer.Restart();

            try
            {
                var progress = new Progress<double>(value =>
                {
                    ScanProgress = value;
                    ScanTimeText = $"Scanning... {value:F0}%";
                });

                RootEntry = await _diskScanningService.ScanDriveAsync(SelectedDrive, progress);
                
                _scanTimer.Stop();
                ScanTimeText = $"Scan complete in {_scanTimer.Elapsed.TotalSeconds:F1}s";

                UpdateCharts();

                try
                {
                    var insights = await _aiAnalysisService.AnalyzeDiskUsageAsync(RootEntry);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Insights.Clear();
                        foreach (var insight in insights)
                        {
                            Insights.Add(insight);
                        }
                    });
                }
                catch (Exception aiEx)
                {
                    Debug.WriteLine($"AI Analysis error: {aiEx}");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Insights.Add(new DiskInsight
                        {
                            Title = "Analysis Warning",
                            Description = "Scan completed but analysis encountered an error.",
                            Type = InsightType.SystemHealth
                        });
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                ScanTimeText = "Access Denied";
                Insights.Add(new DiskInsight
                {
                    Title = "Access Denied",
                    Description = "Some folders could not be accessed. Try running the application as administrator.",
                    Type = InsightType.SystemHealth
                });
            }
            catch (Exception ex)
            {
                ScanTimeText = "Scan Failed";
                Insights.Add(new DiskInsight
                {
                    Title = "Scan Error",
                    Description = $"Error scanning drive: {ex.Message}",
                    Type = InsightType.SystemHealth
                });
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void UpdateCharts()
        {
            if (RootEntry == null || !RootEntry.Children.Any()) return;

    try
    {
        var driveInfo = new DriveInfo(SelectedDrive + ":");
        var totalSize = driveInfo.TotalSize;
        var freeSpace = driveInfo.AvailableFreeSpace;
        var usedSpace = totalSize - freeSpace;
        
        TotalSpaceText = $"Total Space: {FormatSize(totalSize)}";
        UsedSpaceText = $"Space Used: {FormatSize(usedSpace)} ({(double)usedSpace / totalSize * 100:F1}%)";
        FreeSpaceText = $"Space Free: {FormatSize(freeSpace)} ({(double)freeSpace / totalSize * 100:F1}%)";

        PieChartData = new ISeries[]
        {
            new PieSeries<double>
            {
                Values = new[] { (double)usedSpace / totalSize * 100 },
                Fill = new SolidColorPaint(SKColor.Parse("#007BFF")),
                DataLabelsSize = 0,
                IsVisibleAtLegend = false
            },
            new PieSeries<double>
            {
                Values = new[] { (double)freeSpace / totalSize * 100 },
                Fill = new SolidColorPaint(SKColor.Parse("#B3D7FF")),
                DataLabelsSize = 0,
                IsVisibleAtLegend = false
            }
        };

                var chartData = _diskScanningService.GetLargestEntries()
                    .Where(e => e.Size > 0)
                    .OrderByDescending(c => c.Size)
                    .Take(10)
                    .ToList();

                if (chartData.FirstOrDefault()?.Name == SelectedDrive + ":")
                    chartData.RemoveAt(0);

                XAxes = new Axis[]
                {
                    new Axis
                    {
                        Labels = Array.Empty<string>(),
                        SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 }
                    }
                };

                YAxes = new Axis[]
                {
                    new Axis
                    {
                        Labeler = value => $"{value:N1} GB",
                        TextSize = 12,
                        LabelsPaint = new SolidColorPaint(SKColors.DimGray),
                        SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 }
                    }
                };

                BarChartData = new ISeries[]
                {
                    new ColumnSeries<double>
                    {
                        Values = chartData.Select(c => Math.Round((double)c.Size / (1024 * 1024 * 1024), 2)).ToArray(),
                        Name = "Size (GB)",
                        Fill = new SolidColorPaint(SKColor.Parse("#007BFF")),
                        DataLabelsPaint = new SolidColorPaint(SKColors.White),
                        DataLabelsSize = 12,
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                        DataLabelsFormatter = point => FormatSize(chartData[point.Context.Index].Size),
                        TooltipLabelFormatter = point => $"{chartData[point.Context.Index].Name}: {FormatSize(chartData[point.Context.Index].Size)}"
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating charts: {ex.Message}");
            }
        }

        private string FormatSize(long bytes)
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}