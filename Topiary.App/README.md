# Topiary.App - Modern Avalonia UI

A high-performance disk analyzer built with Avalonia UI that provides:

## Features

- **Modern Dark Theme**: Clean, minimal interface inspired by VS Code
- **Live Progress Tracking**: Real-time scan progress with file counts and elapsed time
- **Drive Usage Visualization**: Interactive pie chart showing used vs free space
- **File Tree Explorer**: Hierarchical view of largest folders and files  
- **AI-Powered Insights**: OpenAI integration for cleanup recommendations

## Architecture

- **Frontend**: Avalonia UI (.NET 8) with MVVM pattern
- **Charts**: LiveChartsCore for drive usage visualization
- **AI Integration**: OpenAI API for intelligent cleanup suggestions
- **Cross-Platform**: Runs on Windows, macOS, and Linux

## Getting Started

### Prerequisites
- .NET 8.0 Runtime
- Windows OS (for MFT scanner integration)
- OpenAI API Key (optional, for AI insights)

### Running the Application

```bash
cd Topiary.App
dotnet restore
dotnet run
```

### Environment Variables

Set the following environment variable for AI insights:
```
OPENAI_API_KEY=your_openai_api_key_here
```

## Usage

1. **Select Drive**: Choose a drive from the dropdown (C:, D:, etc.)
2. **Start Scan**: Click "Scan Drive" to begin analysis
3. **Monitor Progress**: Watch the live progress bar with file count and timing
4. **View Results**: 
   - Left panel shows file tree sorted by size
   - Top right shows pie chart of drive usage
   - Bottom right displays AI cleanup recommendations

## Integration with Existing MFT Scanner

The application uses a clean service interface (`IScanService`) that can be easily wired to the existing high-performance MFT scanner:

```csharp
// Replace MockScanService with real implementation
services.AddSingleton<IScanService, MftScanService>();
```

## Performance Features

- **Virtualized Tree View**: Handles large file trees without UI lag
- **Async Operations**: Non-blocking UI during scan operations
- **Memory Efficient**: Optimized data structures for large datasets
- **Progress Throttling**: Smooth progress updates without overwhelming UI

## Limitations on Non-NTFS Systems

- MFT direct access requires NTFS file system
- Fallback to standard file system APIs on other platforms
- Admin privileges recommended for optimal performance

## Architecture Decisions

- **Avalonia over WPF**: Cross-platform support and modern performance
- **MVVM Pattern**: Clean separation of concerns and testability
- **Service Interfaces**: Easy integration with existing scanning backend
- **Dark Theme**: Modern, professional appearance
- **LiveCharts**: High-performance chart rendering with SkiaSharp

The application demonstrates a complete replacement for the WPF UI while maintaining all existing MFT scanning capabilities through clean service abstractions.