# PCP Phone Forwarder

A lightweight Windows (WinForms + WebView2) utility to set and verify call forwarding on Polycom VVX handsets, with quick actions, diagnostics, and speed dials.

---

## What’s new (UX34/UX35)

- **Cleaner header:** Removed the “HTTPS + Teams Check” text from the title and in‑app header.
- **Instructions updated:** New main screen and speed‑dial screenshots in the in‑app Help.
- **Faster, smoother network status:** Immediate check on launch and on **Settings → Save**, then **silent polling every 5s** (no “Checking…” flicker).
- **Branding background:** Optional, subtle company wallpaper that doesn’t interfere with UI legibility.
- **Embedded Windows icon:** App EXE/icon is now embedded for proper display in Explorer, Start, taskbar, and the window title bar.

---

## Requirements

- Windows 10/11 with .NET SDK 8.x (or compatible).
- Network reachability to the VVX phone over HTTPS.
- (Optional) Basic Auth credentials if your phone requires it.

---

## Build & Run

```powershell
dotnet restore
dotnet run
```

> The app is a WinForms host for a WebView2 UI (served from `wwwroot/`).

---

## Quick Start

1. Click **Settings**.
2. Enter the phone’s **Base URL** (must start with `https://`, e.g. `https://192.168.24.12`).
3. (Optional) Enter **API user** / **API pass** if your device requires Basic Auth.
4. Keep the default endpoints for Polycom VVX (you can customize if needed):
   - **Forward (POST):** `/api/v1/callctrl/dial`
   - **Check (GET):** `/api/v1/mgmt/device/runningConfig`
   - **Reboot (POST):** `/api/v1/mgmt/safeReboot`
5. **Save**. The app performs an immediate online check and returns to the main screen.

---

## Main Screen

- **Mobile number**: Enter a 10‑digit number (no spaces/dashes).
- **Actions:**
  - **Forward** — Sends `*33*{mobile}` via the configured endpoint/body.
  - **Check Forward** — Fetches phone running config and surfaces the active forward state (and other validation where available).
  - **Clear** — Clears UI fields only (does not change the phone).
- **Online status** (top‑right): A pill shows **Online / Unreachable / Error / Checking / No URL**.
  - On launch and after settings save: brief **Checking…** is shown.
  - Background polling runs every **5s** silently with no flicker; the pill only updates when the state actually changes.

### Speed dials

Use speed dials for common targets:
- Click **+ Add**, give it a name and a **10‑digit** mobile.
- Click the **arrow** to copy into the main field, or click **Forward** on that row to send immediately.
- **Export / Import** lets you back up and share your list.

> The in‑app Instructions include a screenshot with dummy examples for clarity.

### Diagnostics

Open **Diagnostics** (top bar) to find:
- **Verbose logs** — toggle detailed request/response output.
- **Activity log** — with **Copy** and **Clear**.
- **Test Connection** — reads running config to confirm connectivity.
- **Reboot Phone** — issues the reboot command.

---

## Settings Reference

- **Base URL** (`https://<ip-or-host>`): Required.
- **API user / pass**: Optional (Basic Auth) if your device requires it.
- **Endpoints** (default VVX):
  - Forward (POST) → `/api/v1/callctrl/dial`
  - Check (GET) → `/api/v1/mgmt/device/runningConfig`
  - Reboot (POST) → `/api/v1/mgmt/safeReboot`
- **Forward Body Template**: JSON with `{{mobile}}` placeholder. Example:
  ```json
  {
    "data": { "Dest": "*33*{{mobile}}", "Line": "1", "Type": "TEL" }
  }
  ```

Saving settings triggers an **immediate** online check and closes the modal.

---

## Branding Background (optional)

- File: `wwwroot/assets/branding/brand_bg.webp`
- Applied via a subtle, fixed overlay with reduced opacity.
- To adjust intensity, edit `.brand-bg::before { opacity: .18 }` in `wwwroot/index.html` (lower = softer, higher = stronger).
- To disable, remove the `brand-bg` class from the `<body>` element or comment out the CSS block.

---

## Embedded App Icon (Windows)

- Project embeds `appicon.ico` so the EXE shows the correct icon in Explorer/Start/Taskbar.
- The main window icon is set at runtime to match the EXE icon.

Implementation:
- `PCPForwarder.Win.csproj` contains:
  ```xml
  <ApplicationIcon>appicon.ico</ApplicationIcon>
  ```
- `MainForm.cs` (after `InitializeComponent()`):
  ```csharp
  this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
  ```
- Replace `appicon.ico` with your official logo (multi‑size ICO recommended: 16–256px).

---

## File Layout (high level)

```
PCPForwarder.Win_HTTPS_Functional_Teams/
├─ MainForm.cs
├─ MainForm.Designer.cs
├─ PCPForwarder.Win.csproj
└─ wwwroot/
   ├─ index.html
   ├─ phone.ico
   ├─ instructions/
   │  ├─ main_screen_ux33.png
   │  └─ speeddials_example_ux33.png
   └─ assets/
      └─ branding/
         └─ brand_bg.webp
```

---

## Troubleshooting

- **Online status stays “Unknown”**  
  Ensure **Base URL** is set and starts with `https://` in **Settings**, then click **Save**.

- **Unreachable** but you can ping:  
  Phones can block ICMP. Ensure HTTPS from the app host to the phone is permitted and cert trust policy allows connection.

- **Forward fails (400/401/403)**  
  Confirm endpoint path, JSON body/template, and credentials (Basic Auth) are correct for your device/firmware.

- **Icon not updating**  
  After replacing `appicon.ico`, clean and rebuild:
  ```powershell
  dotnet clean
  dotnet build
  ```

---

## Notes

- The app stores settings in local storage for convenience.
- Authentication is sent only to your configured phone (Basic Auth if provided).
- Screenshots in the in‑app Instructions reflect the current UX (UX33+).

---

## License / Attribution

Internal tooling for call‑forwarding management. Logos and artwork remain property of their respective owners.
