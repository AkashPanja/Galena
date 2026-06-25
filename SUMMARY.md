# Galena Action Ring — Build Summary

## Goal
Build out the Galena Action Ring WinUI 3 app with radial groups, Material Symbols, color picker, full icon gallery, toggle state tracking, playback control submenu with seek mode via GSMTC, back-button navigation in sub-menus, deep-copy editor isolation, startup launch, and all action execution wired — now running correctly as **Unpackaged**.

## Constraints & Preferences
- Volume/Brightness → Group actions with radial progress overlay (not sub-ring) using direct set via `IAudioEndpointVolume` (not `keybd_event`)
- Media → **Playback Control** folder sub-menu order: Seek (top), Next (right), Play/Pause (bottom), Prev (left)
- Seek mode: ring stays visible; rotary sends `TryChangePlaybackPositionAsync` via GSMTC (5s delta); double-arrow indicator flies ±20→±80 and fades (400ms); single persistent storyboard created once, never re-created per rotation; same-direction spam does not restart; direction change resets to opposite start position; animation plays through last rotation only (no queuing)
- Seek background: 160×160 radial gradient using primary color (`#CC{primary}`→`#00{primary}`) behind sub-menu nodes, visible while sub-menu is active; no appear/disappear animation
- Seek icon color: secondary color
- Category auto-determined by ActionType (no manual selector)
- Color: WinUI ColorPicker for primary, secondary auto-complementary; colors are **ring-level** (not per-node); collapsible section on right side, default minimized, auto-collapses on node click
- Icons: Material Symbols Outlined font, full 4258-icon gallery with search
- Volume/Brightness: exact 1% per tick via COM absolute set, no drift
- Toggle nodes (Mute, Play/Pause): **live system state** for mute (COM read/write, not just `keybd_event`); GSMTC-based media status for Play/Pause; predictive toggle after click (no re-query); icon gallery hidden for toggle types (glyph is managed by runtime)
- Play/Pause default glyph: `\uEF6A` (play_disabled); `InitPlayPauseIcons()` sync-sets to `\uEF6A` after every `LoadNodes` before async correction
- App picker: available applications list (Start Menu scan) AND browse button
- All FontIcon references must set `FontFamily="Assets/MaterialSymbols.ttf#Material Symbols Outlined"` or they render as rectangles
- Editor works on a **deep copy** of the profile — unsaved edits never leak to live OSD
- New rings are seeded with the full default node structure (not empty)
- Changing ActionType in the property editor auto-applies sensible glyph/label defaults
- Corrupted profiles auto-repaired on load
- ActionType dropdown uses grouped categories with thin separator lines and per-type Material Symbols icons; uses single `ItemTemplate` with `BoolToVisibilityConverter` instead of `DataTemplateSelector`
- Converters must be registered in code-behind (not XAML `StaticResource`) to avoid XAML compiler resolution failures
- Startup: **Unpackaged profile → Task Scheduler** (with explicit `WorkingDirectory`); **Packaged profile → Registry `Run` key** (`HKCU\...\Run`); first-run ContentDialog asking to enable, Settings page toggle to manage; silent background launch via `--startup` flag
- Configure icon: handyman (`\uF10B`); Settings icon: gear (`\uE8B8`)
- **Registry for settings** when unpackaged — `ApplicationData.Current.LocalSettings` requires package identity (fails in unpackaged)

## Progress
### Done
- Added `ActionCategory` enum (`Individual`, `Group`, `Folder`) and `Category` property on `RingNode`
- Added `VolumeControl`, `BrightnessControl` to `ActionType`
- Updated `CreateDefault()`: Volume/Brightness as Group actions with radial; Playback Control folder with reordered children (Seek, Next, Play/Pause, Prev)
- Bundled Material Symbols Outlined variable TTF + complete codepoints file
- Full icon gallery with search filtering from 4258 icons
- OSD radial progress overlay (arc + percentage + label) with show/update/hide animations
- Radial mode: volume via `AudioVolumeControl`, brightness via WMI, exact COM set
- Seek mode: `_seekMode` flag; rotary sends `TryChangePlaybackPositionAsync` (5s via GSMTC); double-arrow indicator with persistent looping storyboard (created once, `Completed` handler resets flag); start offset ±20px, flies to ±80; same-direction spam never restarts; direction change resets to opposite start; animation plays through last rotation only; secondary color icon; no queued animations
- Seek background: `SubMenuBgLayer` Grid with 160×160 Ellipse + radial gradient (`#CC{primary}`→`#00{primary}`, MappingMode=Absolute, Center=80,80, RadiusX=80, RadiusY=80); `IsHitTestVisible=False`; shown/hidden with sub-menu (not seek mode); no animate
- `ExecuteAction()` dispatches by `Category`: Group → radial, Folder → sub-ring, Individual → immediate
- `ExecuteAction()` for Play/Pause: checks media status via `GetMediaStatusAsync()`, if session exists sends `keybd_event(0xB3)`, then predictively toggles icon (no re-query); if no session, no key sent, stays `\uEF6A`
- Editor: conditional URL/app picker, WinUI ColorPicker, icon gallery with search, CanvasBackBtn sub-menu nav
- All `FontFamily` refs updated to Material Symbols
- `GetCategoryForType()` auto-determines from ActionType
- Toggle state tracking: **MuteToggle uses live COM read** (`AudioVolumeControl.GetMute()`/`SetMute()`) instead of toggled state; Play/Pause uses GSMTC live status
- App picker with Start Menu `.lnk` scan + browse button
- Created `Services/AudioVolumeControl.cs` with `GetMute()`/`SetMute()` methods via `IAudioEndpointVolume`
- Back-button navigation in sub-menu editor with canvas stack + deep copy isolation
- Deep copy save logic prevents root overwrite with children
- Corrupted profile auto-repair in `InitAppProfiles()`
- New rings seeded from `CreateDefault()` template
- ActionType dropdown: grouped categories, separator lines, per-type Material Symbols icons, uses single `ItemTemplate` + converters (registered in code-behind), handles separator clicks by reverting to last valid selection
- Collapsible Ring Colors section: moved out of NodeEditPanel, default minimized, auto-collapses on node click, Format Pain icon (`\uE243`), keyboard_arrow_up/down toggle icons (`\uE316`/`\uE313`), smooth 250ms height animation
- Canvas color preview: `SyncCanvasColors()` updates static brushes from profile colors, called in `PrimaryColorPicker_ColorChanged` and `SelectProfileColors`; brushes changed from `static readonly` to `static` for reassignment
- Color chevron `FontFamily` added (was missing, showing rectangle); icon sizes bumped to 16
- Icon gallery hidden for toggle-type nodes (MuteToggle, MediaPlayPause)
- Playback Control folder icon changed to autoplay (`\uF6B5`)
- Seek/Backward glyphs updated to keyboard_double_arrow_right/left (`\uEAC9`/`\uEAC3`)
- File `ActionTypeTemplateSelector.cs` deleted (unused)
- Profile migration: `MigrateFolderGlyphs()` updates old `\uE05F` (playlist_play) to `\uF6B5` (autoplay) on load
- `ApplyToggleStatesToOsd()` made async void; uses GSMTC to set initial MediaPlayPause glyph
- `InitPlayPauseIcons()` synchronously sets all MediaPlayPause nodes to `\uEF6A` (play_disabled) after every `LoadNodes` call — called from Initialize, ReloadProfile, EnterSubMenu, ExitSubMenu, and hidden-state Click handler
- Play/Pause default glyph changed from `\uE1C4` (play_circle) to `\uEF6A` (play_disabled) in `ProfileService.CreateDefault()` and `GetDefaultsForAction()`
- Play/Pause icons: `\uE034` (pause) when playing, `\uE037` (play_arrow) when paused, `\uEF6A` (play_disabled) when no session
- Removed broken `WM_APPCOMMAND` (0xB2/0xB4) seek mechanism; replaced with `SeekMediaAsync()` via `GlobalSystemMediaTransportControlsSession.TryChangePlaybackPositionAsync(ticks)`
- Added `Services/StartupService.cs` with dual-mode support: **Packaged** → `StartupTask.GetAsync/RequestEnableAsync/Disable`; **Unpackaged** → `UnpackagedStartupManager.ToggleStartup`
- Created `Services/UnpackagedStartupManager.cs`: wraps `TaskService` (`TaskScheduler` NuGet v2.12.2) — creates a logon-triggered task with explicit `WorkingDirectory` and `--startup` argument
- Added `TaskScheduler` NuGet package reference to csproj
- Added Settings page with `ToggleSwitch` for startup launch; nav item with gear icon (`\uE8B8`); Configure nav icon changed to handyman (`\uF10B`)
- First-run `ContentDialog` ("Launch at startup?") with primary "Yes" / close "Not now"; default Yes; called from constructor
- `App.xaml.cs`: detects `--startup` CLI arg to skip `Activate()` for silent background launch
- `Package.appxmanifest`: updated `desktop:StartupTask` with `DisplayName="Galena Action Ring"`, `EntryPoint="Windows.FullTrustApplication"`, `desktop:` namespace (instead of `uap5:`)
- `Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory` at top of `OnLaunched`
- Diagnostic logging to `%LOCALAPPDATA%\GalenaActionRing\startup.log` in both `StartupService` and `UnpackagedStartupManager`
- **Unpackaged crash FIXED** — root cause: `WindowsAppSdkDeploymentManagerInitialize` was auto-set to `true` by `DeploymentManagerCommon.targets` (condition: `WindowsPackageType == 'MSIX'` and OutputType is Exe/WinExe), which triggered the WinAppSDK module initializer (`WindowsAppRuntimeAutoInitializer.cs`) that tried to create `DeploymentInitializeOptions` WinRT type — this fails in unpackaged mode because the framework package isn't registered yet
- **`<WindowsAppSdkDeploymentManagerInitialize>false</WindowsAppSdkDeploymentManagerInitialize>`** added to csproj to prevent the auto-initializer from being added
- **`Services/BootstrapInitializer.cs`** created with `[ModuleInitializer]` that calls `MddBootstrapInitialize2` directly — runs BEFORE `Application.Start()` so WinUI WinRT types are registered by the time `Main()` calls `Application.Start()`
- P/Invoke declarations removed from `App.xaml.cs` — now only in `BootstrapInitializer.cs`
- `ApplicationData.Current` replaced with `App.GetSetting/SetSetting` using `HKCU\Software\GalenaActionRing` Registry

### In Progress / Blocked
- None

## Key Decisions
- **Module initializer approach for bootstrap** — `[ModuleInitializer]` in `BootstrapInitializer.cs` calls `MddBootstrapInitialize2` before `Main()` runs, which means WinUI WinRT types are registered by the time `Application.Start()` tries to activate `IApplicationStatics`. This is the only way to fix the unpackaged crash since `Application.Start()` (called in the generated `Main`) requires framework package types to be registered.
- `WindowsAppSdkDeploymentManagerInitialize=false` — prevents the WinAppSDK from adding its own module initializer (which would crash with `0x80040154` trying to create `DeploymentInitializeOptions`). We don't need DeploymentManager features.
- MuteToggle uses COM `AudioVolumeControl.GetMute()`/`SetMute()` for live system state sync instead of tracked boolean + `keybd_event`
- Play/Pause uses GSMTC `GlobalSystemMediaTransportControlsSessionManager` for live media status; predictive toggle after click avoids stale re-query
- Seek uses GSMTC `TryChangePlaybackPositionAsync` instead of `WM_APPCOMMAND` (0xB2=VK_MEDIA_STOP/0xB4=VK_LAUNCH_MAIL) — old approach launched Outlook / did nothing in browsers/VLC
- Seek animation: single persistent `Storyboard` + two `DoubleAnimation` objects created once, reused entire session; no allocations per trigger; `Completed` handler resets flag; same-direction spam never stops/restarts; direction change stops + restarts from opposite start (intentional visual break)
- Seek background: radial gradient (matching volume pattern but with primary color), no appear/disappear animations, hidden via `Visibility` on sub-menu exit
- Play/Pause default glyph is `\uEF6A` (play_disabled) so initial load is correct; async media check corrects UP if media detected
- `InitPlayPauseIcons()` called synchronously after every `LoadNodes` to guarantee correct initial glyph before async check
- ActionType dropdown uses single `ItemTemplate` with converter-based visibility toggling instead of `DataTemplateSelector` (WinUI 3 ComboBox ignores selector in some scenarios)
- Converters registered in code-behind rather than XAML `StaticResource` to avoid XAML compiler type resolution issues
- Icon gallery hidden for toggle nodes (Mute, Play/Pause) since glyph is runtime-determined and manual changes would be overwritten
- Collapsible colors section: height-animated via `Storyboard`+`DoubleAnimation` with `EnableDependentAnimation=True`; `CollapseColors()` instant when called from `ShowNodeProperties`, animated when manually toggled
- New rings always cloned from `CreateDefault()` template
- **Startup mechanism split by profile**: Packaged → Registry `Run` key (`HKCU\...\Run`); Unpackaged → Task Scheduler with `WorkingDirectory` + `--startup` flag (verified working). Both are robust.
- Task Scheduler used for unpackaged startup (over Registry Run key or Startup folder) because only it can set `WorkingDirectory` before native code runs — fixes CWD defaulting to `System32` which breaks WinAppSDK bootstrapper
- **Registry for settings** instead of `ApplicationData.Current.LocalSettings` — works in both packaged and unpackaged, no package identity required

## Critical Context
- `\uE04E` = `volume_mute` (NOT full volume); `\uE050` = `volume_up` (full volume); `\uE04F` = `volume_off` (muted)
- `\uE04D` = `volume_down`, `\uE050` = `volume_up`, `\uE04F` = `volume_off`
- `keyboard_arrow_down` = `\uE313`; `keyboard_arrow_up` = `\uE316`
- `autoplay` = `\uF6B5`; `keyboard_double_arrow_right` = `\uEAC9`; `keyboard_double_arrow_left` = `\uEAC3`; `play_disabled` = `\uEF6A`; `pause` = `\uE034`; `play_arrow` = `\uE037`
- `format_paint` = `\uE243`; `handyman` = `\uF10B`; `settings_gear` = `\uE8B8`
- `AudioVolumeControl` exposes `GetMute()`/`SetMute(bool)`, `GetVolume()`/`SetVolume(int)` via `IAudioEndpointVolume`
- GSMTC seek: `TryChangePlaybackPositionAsync(newPos.Ticks)` — takes ticks (long), not `TimeSpan`; clamped to `[0, EndTime]`
- Seek animation start: ±20px (just outside 36×36 center button), flies to ±80px; 400ms duration; `RepeatBehavior=None`; `Completed` handler sets `_seekLoopActive=false` + hides indicator
- Seek animation life: created once on first seek; mutated on direction change (stop + new From/To); `StopSeekLoop()` called from `Hide()`, `Show()`, and Click seek exit
- Seek mode enters via `_seekMode = true` in ExecuteAction (MediaSeekForward/MediaSeekBackward); exits via Click center, Show, or Hide
- Sub-menu bg: `SubMenuBgLayer` shown in `EnterSubMenu()`, hidden in `ExitSubMenu()`, `Hide()`, and hidden-state Click `_previousFolderNode` handler; NOT hidden in `Show()` (was the bug that caused invisible bg)
- All FontIcons using Material Symbols must set `FontFamily="Assets/MaterialSymbols.ttf#Material Symbols Outlined"` or they render as rectangles
- Converters that are referenced in DataTemplates must be registered in code-behind (e.g. `AppRoot.Resources["BoolToVis"] = new BoolToVisibilityConverter()`) — XAML `StaticResource` fails for local types inside DataTemplates
- `Grid` does NOT clip children in WinUI 3 → elements can overflow into adjacent cells
- **Packaged startup**: Uses Registry `Run` key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) instead of `StartupTask` API — avoids WinAppSDK 2.2 `0x80040154` crash at login. The `Registry.Run` key launches the app in a normal user-session context where WinRT classes are already registered. See `GetProcessPath()` in `StartupService.cs` for how the packaged exe path is resolved via `Package.Current.InstalledLocation.Path`.
- **Unpackaged launch now working** — module initializer in `BootstrapInitializer.cs` calls `MddBootstrapInitialize2(0x00020002, "", 0)` before `Application.Start()` via `[ModuleInitializer]`
- **`ApplicationData.Current` throws `InvalidOperationException (0x80073D54)`** in unpackaged mode (no package identity) — replaced all 6 call sites in `MainWindow.xaml.cs` with `App.GetSetting/SetSetting` using `HKCU\Software\GalenaActionRing`
- **Error codes**: `0xE0434352` = unhandled managed exception; `0xC000027B` = `STATUS_STACK_BUFFER_OVERRUN` (CRT invalid parameter, from stale native host EXE); `0x80040154` = `REGDB_E_CLASSNOTREG` (WinRT type not registered)
- **Framework package**: `Microsoft.WindowsAppRuntime.2` version `2.2.0.0` at `C:\Program Files\WindowsApps\Microsoft.WindowsAppRuntime.2_2.2.0.0_x64__8wekyb3d8bbwe`
- **WinAppSDK Foundation 2.1.0** auto-initializer flow: `WindowsAppRuntimeAutoInitializer.cs` (module initializer) → `DeploymentManagerAutoInitializer.cs` → `DeploymentInitializeOptions` WinRT activation → fails `0x80040154` if bootstrapper not called first. **Disabled** via `<WindowsAppSdkDeploymentManagerInitialize>false</WindowsAppSdkDeploymentManagerInitialize>`
- **Diagnostic log location**: Packaged → `%LOCALAPPDATA%\Packages\088fc4aa-...\LocalCache\Local\GalenaActionRing\startup.log`; Unpackaged → `%LOCALAPPDATA%\GalenaActionRing\startup.log`
- `StartupTask` is no longer used for packaged startup. `IsEnabled()` checks the Registry `Run` key. Migration from old `StartupTask` happens automatically in `IsEnabled()` (disables old task) and `CleanupOldArtifacts()` (defensive cleanup on every toggle).
- First-run sentinel: `HKCU\Software\GalenaActionRing\FirstRunDone` (DWORD 1)

## Relevant Files
- `Services/BootstrapInitializer.cs`: `[ModuleInitializer]` calls `MddBootstrapInitialize2` before `Main()`, enabling unpackaged WinRT activation
- `Services/AudioVolumeControl.cs`: COM interop for `IAudioEndpointVolume` with GetMute/SetMute
- `Services/StartupService.cs`: dual-mode startup management — Registry `Run` key for packaged, `UnpackagedStartupManager` for unpackaged; diagnostic logging to `startup.log`
- `Services/UnpackagedStartupManager.cs`: wraps `TaskScheduler` library (`TaskService`) — `LogonTrigger`, `ExecAction` with `--startup` arg and explicit `WorkingDirectory`; logging to `startup.log`
- `Models/RingNode.cs`: ActionType, ActionCategory, Category property, DeepCopy
- `Models/RingProfile.cs`: DeepCopy, PrimaryColor/SecondaryColor
- `Models/ActionTypeItem.cs`: lightweight dropdown item model with Type, Glyph, Name, IsSeparator
- `Services/ProfileService.cs`: CreateDefault() with reordered Playback Control sub-menu, MediaPlayPause default `\uEF6A`
- `Services/MaterialIcons.cs`: AllIcons runtime parser, FontFamilyName constant
- `Services/OsdService.cs`: seek mode with `SeekMediaAsync()` via GSMTC, `GetMediaStatusAsync()`, `InitPlayPauseIcons()`, `ApplyToggleStatesToOsd()` async, `UpdateToggleIcon` with predictive toggle removed for Play/Pause, `EnterSubMenu`/`ExitSubMenu` with sub-menu bg + icons init, `SendMediaSeekCommand` replaced by `SeekMediaAsync`, old `WM_APPCOMMAND` P/Invokes removed
- `Converters.cs`: BoolToVisibilityConverter, InverseBoolToVisibilityConverter (registered in code-behind)
- `OsdWindow.xaml`: `SubMenuBgLayer` Grid (160×160 Ellipse, `IsHitTestVisible=False`), `SeekBg` removed, `SeekIndicator` FontIcon centered
- `OsdWindow.xaml.cs`: `ShowSeekIndicator()` with persistent storyboard (one-time create, `Completed` resets flag, direction change = stop+restart from opposite start), `StopSeekLoop()`, `ShowSubMenuBg()`/`HideSubMenuBg()`, `ApplyProfileColors` sets `SubMenuBgEllipse.Fill` radial gradient from primary, `_seekAnimX`/`_seekFade` fields, `_seekLoopInitialized`/`_seekLoopActive`, subtitle glyphs `\uE037`/`\uE034`/`\uEF6A` in `UpdateMediaPlayPauseIcon`
- `MainWindow.xaml`: collapsible Ring Colors section, grouped ActionType ComboBox, IconSection named container, `NavSettings` with gear icon `\uE8B8`, `NavConfigure` icon changed to handyman `\uF10B`, `SettingsPage` grid with `StartupToggle` ToggleSwitch
- `MainWindow.xaml.cs`: `PopulateActionTypeBox()` with grouped list, `ShowNodeProperties` hides IconSection for toggle types, `CollapseColors`/`ExpandColors`, `SyncCanvasColors`, `NavView_ItemInvoked` handles Settings tag + loads `StartupService.IsEnabled()`, `StartupToggle_Toggled` calls `StartupService.SetEnabledAsync()`, `ShowFirstRunPromptAsync()` ContentDialog on first run, `ResetAppButton_Click` with confirmation dialog; all `ApplicationData.Current.LocalSettings.Values[...]` replaced with `App.GetSetting/SetSetting`
- `Package.appxmanifest`: No startup extensions — `StartupTask` declaration removed; `runFullTrust` capability retained for COM/System access
- `Galena Action Ring.csproj`: added `TaskScheduler` NuGet v2.12.2; added `<WindowsAppSdkDeploymentManagerInitialize>false</WindowsAppSdkDeploymentManagerInitialize>` (prevents WinAppSDK module initializer that crashes unpackaged)
- `Properties/launchSettings.json`: two profiles — `"Galena Action Ring (Package)"` (MsixPackage) and `"Galena Action Ring (Unpackaged)"` (Project)
- `App.xaml.cs`: silent startup detection via `--startup` CLI arg; `Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory`; `App.GetSetting/SetSetting` helpers using `HKCU\Software\GalenaActionRing` Registry; P/Invokes moved to `BootstrapInitializer.cs`
