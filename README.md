# PinWindow

A small Windows tool that keeps the windows you choose always on top. Single exe, no install.

## Download

Get `PinWindow.exe` from [Releases](../../releases/latest) and double-click it.

## Usage

1. Open `PinWindow.exe`. It lists the windows that are currently open.
2. Select the windows to pin and press **OK** (or double-click; hold Ctrl to select several).
3. After pinning, the app minimizes to the taskbar. Pinned windows are marked with 📌.
4. To unpin:
   - Press **ESC** in **any window** to unpin all (ESC still reaches that window as usual)
   - Or bring the app back, select a window and press **Unpin**
5. Closing the app restores every window it pinned.

Opening the exe again doesn't start a second copy; it brings the existing one back.

## Requirements

- Windows 10 / 11 (.NET Framework 4.x is built in)
- Windows 7 / 8 need .NET Framework 4.x installed

## Notes

- The exe isn't code-signed, so the first run may show SmartScreen's "Windows protected your PC". Click "More info" → "Run anyway".
- To make ESC work in every window, the app uses a global keyboard hook (it only watches for ESC and records nothing else). Some antivirus software may flag it; add an exception if so.
- To pin a window that runs as administrator, run this app as administrator too.

## Build it yourself

No Visual Studio needed; it uses the C# compiler built into Windows:

```bat
build.bat
```
