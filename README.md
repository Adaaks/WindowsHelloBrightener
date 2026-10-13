# Hello Brightener: Windows Hello face recognition that works in the dark

**Windows Hello face unlock failing in a dark room? Hello Brightener turns your screen white around Windows Hello prompts so the camera can see your face.**

Made for the **Tobii Eye Tracker 5**. Its Windows Hello face recognition struggles in the dark, and extra light on your face fixes it. It also helps any other Windows Hello camera that has trouble in low light.

![Screen turned white around a Windows Hello "Making sure it's you" prompt](docs/screenshot.png)

## What it does

When any **Windows Security / Windows Hello** prompt appears, Hello Brightener fades the rest of that monitor to **white**. Your monitor becomes a big soft light pointed at your face. When the prompt closes, the white disappears and your screen goes back to normal.

It works with:

- 🔑 **Passkeys** in Chrome, Edge, Firefox, Brave and others ("Sign in with a passkey")
- 🔐 Apps that ask you to **verify it's you** with Windows Hello (password managers, Microsoft Store, Settings and more)
- 🪟 Any other prompt that uses the Windows Security dialog

It is designed to stay out of your way:

- **Click-through, never takes focus.** The prompt stays fully usable, so you can still type your PIN or press Cancel.
- **Fits the prompt exactly.** The cut-out matches the Windows 11 dialog's rounded corners at any display scaling.
- **Only covers the monitor the prompt is on.** Your other screens are untouched.
- **Adjustable strength.** 70% by default, from 10% (a gentle glow) to 100% (solid white).
- **Tiny and idle.** One small `.exe` with no dependencies that just checks for the prompt a few times per second.
- **No network access, no data collection, no admin rights needed.**

## Why the Tobii Eye Tracker 5 needs this

The Tobii Eye Tracker 5 supports Windows Hello face recognition, but many people find it **fails or takes ages in a dark room** and works instantly with a light on. Monitors are bright, but most Windows Hello prompts are small dark dialogs, so your face gets very little light at exactly the moment the camera needs it.

Hello Brightener makes the whole screen behind the prompt white, so your face is evenly lit and recognition works first time, even with the room lights off.

## Install

**Option A: download (easiest)**

1. Download `HelloBrightener.exe` from the [latest release](../../releases/latest).
2. Double-click it. **That's it.**

It installs itself to `%LOCALAPPDATA%\HelloBrightener`, **starts automatically with Windows**, and puts a 🛡️ shield icon in your system tray. There are no shortcuts to make and no settings to change.

> Windows SmartScreen may warn about an unknown publisher because the exe isn't code-signed. If you'd rather not run a downloaded exe, use Option B. It builds the same exe from the source in this repo in a few seconds.

**Option B: build it yourself (no tools to install)**

Windows already includes the C# compiler this needs. In PowerShell, from the repo folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

This builds `HelloBrightener.exe` and runs it, which installs it just like Option A. Leave off `-Install` to only build the exe.

## Usage

Nothing to do. It runs in the tray and kicks in on its own. Right-click the tray icon for:

- **Test white frame (3 s):** shows the white frame without a real prompt, so you can see it working.
- **White strength:** 10% to 100% (solid). The default is **70%**. Lower values are gentler on your eyes, because the desktop shows through. Higher values give the camera more light. Picking a value previews it for 3 seconds, and your choice is remembered.
- **Start with Windows:** on by default. Untick it to stop auto-start.
- **Exit:** closes it until your next sign-in.

**Finding the right strength:** sit in the dark the way you normally do and try a passkey or Windows Hello prompt. If recognition is slow or fails, go up a step. If it works instantly, you can try a step lower.

## Bonus tip: the Windows lock screen

Hello Brightener can't draw on the **lock screen** or on **UAC prompts**. Windows shows those on a protected desktop that no app is allowed to draw on. For the lock screen, use a **bright or white lock screen picture** instead: *Settings → Personalization → Lock screen → Picture*. Make sure *"Show the lock screen background picture on the sign-in screen"* is turned on. With a Tobii Eye Tracker 5 this makes unlocking in the dark much more reliable.

## Uninstall

1. Right-click the 🛡️ tray icon → **Exit**.
2. Remove it from startup: open **Task Manager → Startup apps**, right-click **HelloBrightener** and choose **Disable**. Or press **Win + R**, run `regedit`, and delete the `HelloBrightener` value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.
3. Delete the program folder: press **Win + R**, paste `%LOCALAPPDATA%\HelloBrightener` and delete the folder that opens.
4. *(Optional)* Delete its settings: in `regedit`, delete the key `HKEY_CURRENT_USER\Software\HelloBrightener`.

Or do all of it at once by pasting this into PowerShell:

```powershell
Stop-Process -Name HelloBrightener -ErrorAction SilentlyContinue; Start-Sleep 1
Remove-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run -Name HelloBrightener -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\HelloBrightener", HKCU:\Software\HelloBrightener -ErrorAction SilentlyContinue
```

## How it works

- Every ~120 ms it looks for a visible window belonging to `CredentialUIBroker.exe`, the process behind the Windows Security / Windows Hello dialog, or with the class `Credential Dialog Xaml Host`.
- When it finds one, it shows a borderless, topmost, **click-through, non-activating** white window over that monitor, with a rounded hole cut out exactly where the dialog is. It is per-monitor DPI aware, so the hole lines up on scaled displays.
- When the dialog closes, the overlay hides.
- On first run it copies itself to `%LOCALAPPDATA%\HelloBrightener` and registers in the standard per-user `Run` registry key, so it starts with Windows. Settings live in `HKEY_CURRENT_USER\Software\HelloBrightener`. No admin rights are needed.

All the code is in a single file: [`src/HelloBrightener.cs`](src/HelloBrightener.cs).

## Requirements

- Windows 10 or Windows 11
- A Windows Hello face camera: Tobii Eye Tracker 5, laptop IR camera, Windows Hello webcam and so on
- .NET Framework 4.x, which is built into Windows

## Keywords

Windows Hello not working in the dark · Windows Hello face recognition low light · Tobii Eye Tracker 5 Windows Hello dark room · passkey face unlock dark · Windows Hello IR camera fails at night · brighten screen for Windows Hello

## License

[MIT](LICENSE)
