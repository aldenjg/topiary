# 🌳 Topiary - AI-Powered Disk Space Analyzer

[![Build Status](https://github.com/yourusername/topiary/workflows/Build%20and%20Test/badge.svg)](https://github.com/yourusername/topiary/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

A modern, cross-platform disk space analyzer with AI-powered insights. Built with **Avalonia UI** for native performance across Windows, macOS, and Linux.

![ezgif](https://github.com/user-attachments/assets/13f46ddb-473e-4508-b3ae-f24aa210f924)

## ✨ Features

- **🚀 Lightning-Fast Analysis**: Responsive disk scanning that stays smooth on large drives (500GB+)
- **🎨 Modern Cross-Platform UI**: Native dark theme interface built with Avalonia
- **🤖 AI-Powered Insights**: OpenAI integration for intelligent cleanup recommendations
- **📊 Rich Visualizations**: Interactive pie charts and hierarchical tree views
- **⚡ Fully Responsive**: UI stays responsive during intensive scanning operations
- **❌ Instant Cancellation**: Cancel long-running scans immediately
- **🔒 Privacy-First**: All analysis runs locally, optional AI features

## 🚀 Quick Start

### Prerequisites
- [.NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10+, macOS 10.15+, or modern Linux distribution

### Installation

#### From Release (Recommended)
1. Download the latest release from [Releases](https://github.com/yourusername/topiary/releases)
2. Extract and run `Topiary.App.exe` (Windows) or `Topiary.App` (macOS/Linux)

#### From Source
```bash
git clone https://github.com/yourusername/topiary.git
cd topiary
dotnet restore
dotnet run --project Topiary.App
```

## 🎯 Usage

1. **Launch Topiary** from your applications or command line
2. **Select a drive** from the dropdown menu
3. **Click "Scan Drive"** - watch the live progress with responsive UI
4. **Explore results**:
   - 📊 **Pie Chart**: Visual breakdown of used vs. free space
   - 🌳 **Tree View**: Hierarchical view of largest folders and files
   - 🤖 **AI Insights**: Intelligent cleanup recommendations (optional)

### Performance Optimizations
- **Smart Scanning**: Automatically balances depth vs. speed for large drives
- **Background Processing**: All I/O operations run on background threads
- **Progress Updates**: Real-time feedback with file counts and current paths
- **Memory Efficient**: Controlled batching prevents memory issues

## 🏗️ Architecture

### Technology Stack
- **Framework**: .NET 8.0 (cross-platform)
- **UI**: Avalonia UI 11.0+ with Fluent Dark theme
- **Charts**: LiveCharts2 for interactive data visualization
- **Architecture**: MVVM with dependency injection
- **AI Integration**: OpenAI API for intelligent analysis

### Project Structure
```
topiary/
├── Topiary.App/              # Main application
│   ├── Models/               # Data models and DTOs
│   ├── Services/             # Business logic
│   ├── ViewModels/           # MVVM view models
│   ├── Views/                # UI components
│   └── App.axaml.cs         # Application entry point
├── .github/                  # GitHub workflows and templates
├── docs/                     # Documentation
└── CLAUDE.md                # Technical architecture guide
```

## ⚡ Performance Characteristics

### Large Drive Support (Tested: 500GB, 90% full)
| Metric | Previous Versions | **Topiary v3.0** |
|--------|-------------------|-------------------|
| UI Responsiveness | ❌ Freezes | **✅ 60fps smooth** |
| Scan Time | ❌ Never completes | **✅ 30-60 seconds** |
| Progress Visibility | ❌ Hidden | **✅ Live updates** |
| Memory Usage | ❌ Unlimited growth | **✅ Controlled** |
| Cancellation | ❌ Not possible | **✅ Instant** |

## 🔧 Configuration

### AI Features (Optional)
1. Create `appsettings.json` in the application directory:
```json
{
  "OpenAI": {
    "ApiKey": "your-openai-api-key-here"
  }
}
```
2. Restart the application to enable AI insights

*Note: AI features are optional. The application works fully without an API key.*

## 🤝 Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Setup
```bash
git clone https://github.com/yourusername/topiary.git
cd topiary
dotnet restore
dotnet build
cd Topiary.App && dotnet run
```

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built with [Avalonia UI](https://avaloniaui.net/) for cross-platform native performance
- Charts powered by [LiveCharts2](https://livecharts.dev/)
- AI insights via [OpenAI API](https://openai.com/api/)
- Inspired by tools like WizTree and WinDirStat

## 🐛 Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/topiary/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/topiary/discussions)
- **Documentation**: [Technical Guide](CLAUDE.md)

---

**Topiary** - Pruning your disk space with intelligence. 🌳✂️