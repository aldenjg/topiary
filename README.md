# 🌳 Topiary

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)

![ezgif](https://github.com/user-attachments/assets/13f46ddb-473e-4508-b3ae-f24aa210f924)

Topiary is a modern, lightweight disk space analyzer powered by AI insights. It helps users understand and manage their disk usage through an intuitive WPF interface and intelligent file analysis.

## ✨ Features

- **Lightning-Fast Disk Analysis**: Efficiently scan and analyze disk space usage using advanced parsing
- **Modern WPF Interface**: Clean, intuitive visualization of disk space consumption
- **AI-Powered Insights**: Leverage OpenAI to provide intelligent suggestions about file management and space optimization
- **Secure by Design**: Local-only processing with secure API key management
- **Rich Data Visualization**: Interactive charts and treemaps powered by LiveCharts
- **Zero-Installation Option**: Standalone executable available alongside installer

## 🚀 Getting Started

### Prerequisites

- Windows OS
- .NET 8.0 Runtime
- OpenAI API Key (for AI insights)

### Installation

1. Download the latest installer from the [Releases](../../releases) page
2. Run the installer and follow the setup wizard
3. Launch Topiary from your Start Menu or Desktop

### Building from Source

```powershell
# Clone the repository
git clone https://github.com/yourusername/topiary.git
cd topiary

# Build and create installer
.\build.ps1
```

## 🛠️ Technology Stack

- **.NET 8.0**: Core framework
- **WPF**: UI framework
- **OpenAI API**: AI-powered insights
- **LiveChartsCore**: Data visualization
- **Inno Setup**: Installer creation

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📝 License

Distributed under the MIT License. See `LICENSE` for more information.

## 🔍 Repository Structure

```
topiary/
├── Converters/           # Value converters for WPF
├── Models/              # Data models
├── Services/           # Business logic and services
├── ViewModels/         # MVVM view models
├── Views/              # WPF views
└── installer/          # Installation scripts and resources
```
