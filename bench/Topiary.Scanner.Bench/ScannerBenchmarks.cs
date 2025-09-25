using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Topiary.App.Models;
using Topiary.App.Services;

namespace Topiary.Scanner.Bench;

/// <summary>
/// Benchmarks comparing old ResponsiveDiskScanService vs new HighPerformanceScanService.
/// Measures scanning performance, memory usage, and accuracy.
/// </summary>
[Config(typeof(ScannerBenchmarkConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class ScannerBenchmarks
{
    private ResponsiveDiskScanService _oldScanner = null!;
    private HighPerformanceScanService _newScanner = null!;
    private string _testDrive = "";
    private readonly Progress<ScanProgress> _nullProgress = new(p => { });
    
    [GlobalSetup]
    public void Setup()
    {
        _oldScanner = new ResponsiveDiskScanService();
        _newScanner = new HighPerformanceScanService();
        
        // Find a suitable test drive (prefer smaller drives for benchmarking)
        var drives = DriveInfo.GetDrives();
        foreach (var drive in drives)
        {
            if (drive.IsReady && drive.DriveType == DriveType.Fixed)
            {
                // Prefer drives under 100GB for faster benchmarks, but use any available
                if (drive.TotalSize < 100L * 1024 * 1024 * 1024 || string.IsNullOrEmpty(_testDrive))
                {
                    _testDrive = drive.Name;
                }
            }
        }
        
        if (string.IsNullOrEmpty(_testDrive))
        {
            throw new InvalidOperationException("No suitable drives found for benchmarking");
        }
        
        Console.WriteLine($"Benchmarking on drive: {_testDrive}");
    }
    
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Scanner")]
    public async Task<ScanResult> OldResponsiveScanner()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)); // Timeout for large drives
        return await _oldScanner.ScanDriveAsync(_testDrive, _nullProgress, cts.Token);
    }
    
    [Benchmark]
    [BenchmarkCategory("Scanner")]
    public async Task<ScanResult> NewHighPerformanceScanner()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        return await _newScanner.ScanDriveAsync(_testDrive, _nullProgress, cts.Token);
    }
    
    [Benchmark]
    [BenchmarkCategory("Scanner")]
    public async Task<ScanResult> NewHighPerformanceScanner_WithProgress()
    {
        var progressReports = 0;
        var progress = new Progress<ScanProgress>(p => Interlocked.Increment(ref progressReports));
        
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var result = await _newScanner.ScanDriveAsync(_testDrive, progress, cts.Token);
        
        // Ensure progress was reported
        if (progressReports == 0)
            throw new InvalidOperationException("No progress reports received");
            
        return result;
    }
    
    [Benchmark]
    [BenchmarkCategory("DriveList")]
    public async Task<string[]> OldGetDrives()
    {
        return await _oldScanner.GetAvailableDrivesAsync();
    }
    
    [Benchmark]
    [BenchmarkCategory("DriveList")]
    public async Task<string[]> NewGetDrives()
    {
        return await _newScanner.GetAvailableDrivesAsync();
    }
}

/// <summary>
/// Memory-focused benchmark that tests allocation patterns.
/// </summary>
[Config(typeof(MemoryBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
public class MemoryBenchmarks
{
    private HighPerformanceScanService _scanner = null!;
    private string _smallTestPath = "";
    
    [GlobalSetup]
    public void Setup()
    {
        _scanner = new HighPerformanceScanService();
        
        // Find a relatively small directory for memory testing
        var candidates = new[] { 
            @"C:\Windows\System32", 
            @"C:\Program Files\Common Files",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"C:\"
        };
        
        foreach (var path in candidates)
        {
            if (Directory.Exists(path))
            {
                _smallTestPath = path;
                break;
            }
        }
        
        if (string.IsNullOrEmpty(_smallTestPath))
            _smallTestPath = @"C:\";
    }
    
    [Benchmark]
    [Arguments(1)]
    [Arguments(5)]
    [Arguments(10)]
    public async Task ScanMultipleTimes(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await _scanner.ScanDriveAsync(_smallTestPath, null!, cts.Token);
            
            // Force GC between iterations to measure true allocation
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}

public class ScannerBenchmarkConfig : ManualConfig
{
    public ScannerBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(1)    // Reduced warmup for faster benchmarks
            .WithIterationCount(3) // Reduced iterations
            .WithInvocationCount(1)
            .WithUnrollFactor(1));
        
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);
        
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);
        AddColumn(BaselineRatioColumn.RatioMean);
        
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}

public class MemoryBenchmarkConfig : ManualConfig
{
    public MemoryBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(1)
            .WithIterationCount(5)
            .WithInvocationCount(1));
        
        AddExporter(MarkdownExporter.GitHub);
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.Mean);
        AddColumn(BaselineRatioColumn.RatioMean);
    }
}