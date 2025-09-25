using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Topiary.App.Models;
using Topiary.App.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Topiary.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IScanService _scanService;
    private readonly IAiInsightsService _aiService;
    private CancellationTokenSource? _scanCancellation;

    private string[] _availableDrives = [];
    private string? _selectedDrive;
    private bool _isScanning;
    private double _scanProgress;
    private long _filesProcessed;
    private TimeSpan _elapsedTime;
    private string? _currentPath;
    private ScanResult? _scanResult;
    private bool _hasScanResult;
    private ISeries[] _pieChartSeries = [];
    private ObservableCollection<TreeNodeViewModel>? _treeNodes;
    private string _aiRequest = "";
    private string _aiResponse = "";
    private string? _errorMessage;
    private bool _hasError;

    public MainViewModel(IScanService scanService, IAiInsightsService aiService)
    {
        _scanService = scanService;
        _aiService = aiService;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && !string.IsNullOrEmpty(SelectedDrive));
        
        _ = LoadDrivesAsync();
    }

    public string[] AvailableDrives
    {
        get => _availableDrives;
        set => SetProperty(ref _availableDrives, value);
    }

    public string? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            SetProperty(ref _selectedDrive, value);
            ((AsyncRelayCommand)ScanCommand).NotifyCanExecuteChanged();
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            SetProperty(ref _isScanning, value);
            ((AsyncRelayCommand)ScanCommand).NotifyCanExecuteChanged();
        }
    }

    public double ScanProgress
    {
        get => _scanProgress;
        set => SetProperty(ref _scanProgress, value);
    }

    public long FilesProcessed
    {
        get => _filesProcessed;
        set => SetProperty(ref _filesProcessed, value);
    }

    public TimeSpan ElapsedTime
    {
        get => _elapsedTime;
        set => SetProperty(ref _elapsedTime, value);
    }

    public string? CurrentPath
    {
        get => _currentPath;
        set => SetProperty(ref _currentPath, value);
    }

    public ScanResult? ScanResult
    {
        get => _scanResult;
        set
        {
            if (SetProperty(ref _scanResult, value))
            {
                HasScanResult = value != null;
            }
        }
    }

    public ISeries[] PieChartSeries
    {
        get => _pieChartSeries;
        set => SetProperty(ref _pieChartSeries, value);
    }

    public bool HasScanResult
    {
        get => _hasScanResult;
        set => SetProperty(ref _hasScanResult, value);
    }

    public ObservableCollection<TreeNodeViewModel>? TreeNodes
    {
        get => _treeNodes;
        set => SetProperty(ref _treeNodes, value);
    }

    public string AiRequest
    {
        get => _aiRequest;
        set => SetProperty(ref _aiRequest, value);
    }

    public string AiResponse
    {
        get => _aiResponse;
        set => SetProperty(ref _aiResponse, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public ICommand ScanCommand { get; }

    public void CancelScan()
    {
        _scanCancellation?.Cancel();
    }

    private void ClearError()
    {
        ErrorMessage = null;
        HasError = false;
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private async Task LoadDrivesAsync()
    {
        try
        {
            ClearError();
            AvailableDrives = await _scanService.GetAvailableDrivesAsync();
            SelectedDrive = AvailableDrives.FirstOrDefault();

            if (AvailableDrives.Length == 0)
            {
                SetError("No suitable drives found for scanning.");
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to load available drives: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Failed to load drives: {ex}");
        }
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(SelectedDrive)) return;

        IsScanning = true;
        _scanCancellation = new CancellationTokenSource();

        try
        {
            ClearError();
            var progress = new Progress<ScanProgress>(UpdateProgress);
            var result = await _scanService.ScanDriveAsync(SelectedDrive, progress, _scanCancellation.Token);

            ScanResult = result;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdatePieChart(result.Drive);
                UpdateTreeView(result.Root);
            });

            // Get AI insights
            _ = GetAiInsightsAsync(result);
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled - this is expected behavior, don't show as error
            ScanProgress = 0;
            CurrentPath = "Scan cancelled";
        }
        catch (UnauthorizedAccessException ex)
        {
            SetError($"Access denied while scanning {SelectedDrive}. Try running as administrator or select a different drive.");
            System.Diagnostics.Debug.WriteLine($"Access denied: {ex}");
        }
        catch (IOException ex) when (ex.Message.Contains("not found") || ex.Message.Contains("not accessible"))
        {
            SetError($"Drive {SelectedDrive} is not accessible. Please ensure the drive is ready and try again.");
            System.Diagnostics.Debug.WriteLine($"IO error: {ex}");
        }
        catch (Exception ex)
        {
            var friendlyMessage = ex.Message;

            // Provide more user-friendly error messages based on common scenarios
            if (ex.Message.Contains("NTFS"))
            {
                friendlyMessage = $"The selected drive requires NTFS file system and administrator privileges. {ex.Message}";
            }
            else if (ex.Message.Contains("access") || ex.Message.Contains("permission"))
            {
                friendlyMessage = $"Permission denied. Try running as administrator: {ex.Message}";
            }
            else if (ex.Message.Contains("not ready"))
            {
                friendlyMessage = $"Drive is not ready. Please ensure the drive is accessible: {ex.Message}";
            }

            SetError($"Failed to scan {SelectedDrive}: {friendlyMessage}");
            System.Diagnostics.Debug.WriteLine($"Scan failed: {ex}");
        }
        finally
        {
            IsScanning = false;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    private void UpdateProgress(ScanProgress progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ScanProgress = progress.Percent;
            FilesProcessed = progress.FilesProcessed;
            ElapsedTime = progress.Elapsed;
            CurrentPath = progress.CurrentPath;
        });
    }

    private void UpdatePieChart(DriveStats drive)
    {
        PieChartSeries = new ISeries[]
        {
            new PieSeries<double>
            {
                Values = new[] { drive.UsedBytes / 1024.0 / 1024.0 / 1024.0 },
                Name = $"Used ({drive.FormattedUsed})",
                Fill = new SolidColorPaint(SKColors.DeepSkyBlue)
            },
            new PieSeries<double>
            {
                Values = new[] { drive.FreeBytes / 1024.0 / 1024.0 / 1024.0 },
                Name = $"Free ({drive.FormattedFree})",
                Fill = new SolidColorPaint(SKColors.LightGray)
            }
        };
    }

    private void UpdateTreeView(TreeNode root)
    {
        var rootVm = new TreeNodeViewModel(root);
        TreeNodes = new ObservableCollection<TreeNodeViewModel> { rootVm };
    }

    private async Task GetAiInsightsAsync(ScanResult result)
    {
        try
        {
            var (request, response) = await _aiService.GetInsightsAsync(result);
            AiRequest = request;
            AiResponse = response;
        }
        catch (Exception ex)
        {
            AiResponse = $"Failed to get AI insights: {ex.Message}";
        }
    }
}

public class TreeNodeViewModel
{
    private readonly TreeNode _node;
    private TreeNodeViewModel[]? _children;

    public TreeNodeViewModel(TreeNode node)
    {
        _node = node;
    }

    public string Name => _node.Name;
    public string FormattedSize => _node.FormattedSize;
    public string ParentPercent => ""; // Will be calculated when needed
    public bool HasChildren => _node.Children.Length > 0;

    public TreeNodeViewModel[] Children
    {
        get
        {
            if (_children == null)
            {
                _children = _node.Children
                    .OrderByDescending(x => x.SizeBytes)
                    .Select(x => new TreeNodeViewModel(x))
                    .ToArray();
            }
            return _children;
        }
    }
}