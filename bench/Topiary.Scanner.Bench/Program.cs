using BenchmarkDotNet.Running;
using Topiary.Scanner.Bench;

// Allow user to select benchmark type
Console.WriteLine("Topiary Scanner Performance Benchmarks");
Console.WriteLine("=====================================");
Console.WriteLine();
Console.WriteLine("Select benchmark type:");
Console.WriteLine("1. Full Scanner Comparison (Old vs New)");
Console.WriteLine("2. Memory Usage Analysis"); 
Console.WriteLine("3. Quick Performance Test");
Console.WriteLine("4. All Benchmarks");
Console.WriteLine();
Console.Write("Enter your choice (1-4): ");

var choice = Console.ReadLine();

try
{
    switch (choice)
    {
        case "1":
            BenchmarkRunner.Run<ScannerBenchmarks>();
            break;
            
        case "2":
            BenchmarkRunner.Run<MemoryBenchmarks>();
            break;
            
        case "3":
            await RunQuickTest();
            break;
            
        case "4":
            BenchmarkRunner.Run<ScannerBenchmarks>();
            BenchmarkRunner.Run<MemoryBenchmarks>();
            break;
            
        default:
            Console.WriteLine("Invalid choice, running quick test...");
            await RunQuickTest();
            break;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Benchmark failed: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("This might be due to:");
    Console.WriteLine("- Insufficient permissions to scan system drives");
    Console.WriteLine("- Drive not ready or inaccessible");
    Console.WriteLine("- Antivirus blocking low-level disk access");
    Console.WriteLine();
    Console.WriteLine("Try running as Administrator or selecting a different drive.");
}

static async Task RunQuickTest()
{
    Console.WriteLine("Running quick performance comparison...");
    Console.WriteLine();
    
    var oldScanner = new Topiary.App.Services.ResponsiveDiskScanService();
    var newScanner = new Topiary.App.Services.HighPerformanceScanService();
    
    var drives = await newScanner.GetAvailableDrivesAsync();
    if (drives.Length == 0)
    {
        Console.WriteLine("No drives available for testing.");
        return;
    }
    
    var testDrive = drives[0];
    Console.WriteLine($"Testing on drive: {testDrive}");
    Console.WriteLine();
    
    // Progress tracking
    var oldProgress = 0L;
    var newProgress = 0L;
    
    var oldProgressReporter = new Progress<Topiary.App.Models.ScanProgress>(p => 
    {
        Interlocked.Exchange(ref oldProgress, p.FilesProcessed);
    });
    
    var newProgressReporter = new Progress<Topiary.App.Models.ScanProgress>(p => 
    {
        Interlocked.Exchange(ref newProgress, p.FilesProcessed);
    });
    
    // Test old scanner
    Console.WriteLine("Testing ResponsiveDiskScanService (old)...");
    var oldStart = DateTime.UtcNow;
    var oldStartMemory = GC.GetTotalAllocatedBytes(precise: true);
    
    try
    {
        var oldResult = await oldScanner.ScanDriveAsync(testDrive, oldProgressReporter);
        var oldEnd = DateTime.UtcNow;
        var oldEndMemory = GC.GetTotalAllocatedBytes(precise: true);
        
        Console.WriteLine($"  Time: {(oldEnd - oldStart).TotalSeconds:F2}s");
        Console.WriteLine($"  Files: {oldProgress:N0}");
        Console.WriteLine($"  Tree Size: {oldResult.Root.SizeBytes:N0} bytes");
        Console.WriteLine($"  Memory: {(oldEndMemory - oldStartMemory):N0} bytes allocated");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Failed: {ex.Message}");
        Console.WriteLine();
    }
    
    // Force GC between tests
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    
    // Test new scanner  
    Console.WriteLine("Testing HighPerformanceScanService (new)...");
    var newStart = DateTime.UtcNow;
    var newStartMemory = GC.GetTotalAllocatedBytes(precise: true);
    
    try
    {
        var newResult = await newScanner.ScanDriveAsync(testDrive, newProgressReporter);
        var newEnd = DateTime.UtcNow;
        var newEndMemory = GC.GetTotalAllocatedBytes(precise: true);
        
        Console.WriteLine($"  Time: {(newEnd - newStart).TotalSeconds:F2}s");
        Console.WriteLine($"  Files: {newProgress:N0}");
        Console.WriteLine($"  Tree Size: {newResult.Root.SizeBytes:N0} bytes");
        Console.WriteLine($"  Memory: {(newEndMemory - newStartMemory):N0} bytes allocated");
        Console.WriteLine();
        
        // Show improvement
        var oldTime = (oldStart != default) ? (DateTime.UtcNow - oldStart).TotalSeconds : double.MaxValue;
        var newTime = (newEnd - newStart).TotalSeconds;
        
        if (oldTime < double.MaxValue && newTime > 0)
        {
            var speedup = oldTime / newTime;
            Console.WriteLine($"Performance improvement: {speedup:F1}x faster");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Failed: {ex.Message}");
    }
}