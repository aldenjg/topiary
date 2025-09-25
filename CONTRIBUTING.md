# Contributing to Topiary

Thank you for considering contributing to Topiary! This document provides guidelines and information for contributors.

## Development Setup

### Prerequisites
- .NET 8.0 SDK or later
- Git
- Your favorite IDE (VS Code, Visual Studio, JetBrains Rider)

### Getting Started
1. Fork the repository
2. Clone your fork:
   ```bash
   git clone https://github.com/yourusername/topiary.git
   cd topiary
   ```
3. Restore dependencies:
   ```bash
   dotnet restore
   ```
4. Build the project:
   ```bash
   dotnet build
   ```
5. Run the application:
   ```bash
   cd Topiary.App
   dotnet run
   ```

## Code Style

- Follow standard C# conventions
- Use meaningful variable and method names
- Add XML documentation for public APIs
- Keep methods focused and small
- Use async/await for I/O operations

## Architecture Guidelines

- Maintain MVVM pattern separation
- Keep UI operations on the UI thread
- Use dependency injection for services
- Follow the existing project structure
- Write responsive, non-blocking code

## Submitting Changes

1. Create a new branch for your feature/fix:
   ```bash
   git checkout -b feature/your-feature-name
   ```
2. Make your changes
3. Test your changes thoroughly
4. Commit with descriptive messages:
   ```bash
   git commit -m "Add feature: description of what you added"
   ```
5. Push to your fork:
   ```bash
   git push origin feature/your-feature-name
   ```
6. Create a Pull Request

## Pull Request Guidelines

- Provide a clear description of the changes
- Include screenshots for UI changes
- Ensure all builds pass
- Update documentation if needed
- Keep PRs focused and atomic

## Reporting Issues

- Use the appropriate issue template
- Provide clear reproduction steps
- Include system information
- Add screenshots for UI issues

## Code of Conduct

- Be respectful and inclusive
- Focus on constructive feedback
- Help maintain a welcoming community
- Follow GitHub's community guidelines

Thank you for contributing to Topiary! 🌳