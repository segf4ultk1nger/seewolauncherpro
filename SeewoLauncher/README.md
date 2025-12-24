# SeewoLauncher (WPF + .NET 9)

This is a **Single-File** WPF Desktop Assistant built with a C#-only UI (No XAML) and Convention-based Dependency Injection.

## Prerequisites
- Windows 10/11
- .NET 8.0 or .NET 9.0 SDK installed

## Project Structure
- `SeewoLauncher.csproj`: Project file (WPF, Windows Only).
- `Program.cs`: Contains **ALL** source code (Entry, DI, Models, Services, ViewModels, UI).
- `config.json`: Configuration for Apps and Tools.

## How to Run (On Windows)
1. Open a terminal in this directory.
2. Run the application:
   ```powershell
   dotnet run
   ```

## How to Publish (Single File)
To create a standalone `.exe`:
```powershell
dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true --self-contained
```

## Features
- **Convention DI**: Uses custom attributes `[RegisterSingleton]` to auto-register services.
- **Hot Reload**: Modify `config.json` while the app is running to update the launcher instantly.
- **USB Monitor**: Automatically detects U-Disks (Insert/Remove events).
- **Pure C# UI**: No XAML files used.

## Note for macOS/Linux Users
This application utilizes WPF, which is a **Windows-only** technology. It will not run on macOS or Linux.
