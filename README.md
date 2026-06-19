# Galena Action Ring

A Windows app that connects to a Bluetooth/Serial ring controller and displays a floating radial OSD menu on top of any application, including fullscreen games.

## Features

- **Radial OSD Menu** — 8 action buttons arranged in a circle with bloom/collapse animations
- **Always on Top** — floats over fullscreen games, auto-hides after 5 seconds of inactivity
- **Ring Controller** — connect via Bluetooth SPP or USB serial at 115200 baud
- **System Tray** — runs minimized to tray with context menu (Open / Exit)
- **Auto-Connect** — remembers your last paired device and reconnects automatically
- **Live Debug Log** — view raw serial data for troubleshooting

## Getting Started

### Prerequisites

- Windows 10 (version 19041+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (recommended) or any C# IDE

### Build & Run

```powershell
git clone https://github.com/AkashPanja/Galena.git
cd Galena/Galena\ Action\ Ring
dotnet build -p:Platform=x64
dotnet run -p:Platform=x64
```

### Connecting a Ring

1. Launch the app — it minimises to the system tray
2. Click the tray icon to open the main window
3. Go to the **Devices** tab and select your ring from the list
4. Click **Connect** — the app auto-reconnects on next launch

## OSD Controls

| Gesture | Action |
|---------|--------|
| Ring gesture R+ | Select next option |
| Ring gesture R- | Select previous option |
| Ring gesture C  | Toggle OSD / Click selected |

The OSD appears at the centre of your screen and disappears after 5 seconds.

## License

This project is licensed under **CC BY-NC 4.0** — see [LICENSE](LICENSE).

You are free to share and adapt the code for non-commercial purposes, provided you give appropriate credit.
