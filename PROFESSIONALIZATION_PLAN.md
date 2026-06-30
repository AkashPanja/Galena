# Galena Action Ring — Professionalization Plan

## Overview

Systematic refactoring across three codebases (WinUI 3 app, ESP32-S3 Action Ring, ESP32-C3 Light Bar) to fix bugs, improve robustness, and professionalize code quality.

---

## Phase 1 — Critical Bug Fixes

### 1.1 Light Bar: Fix merge conflict in `sdkconfig.defaults`
- **File:** `GalenaLightBar-ESP32C3/sdkconfig.defaults:8-13`
- **Requirement:** Remove committed `<<<<<<< HEAD` / `=======` / `>>>>>>>` merge conflict markers. Ensure `CONFIG_ESPTOOLPY_FLASHSIZE_4MB=y` is applied.
- **Why:** Broken `sdkconfig.defaults` causes `idf.py menuconfig` failures and may silently drop flash-size configuration, causing build issues.

### 1.2 Light Bar: Handle `PKT_BRIGHTNESS` in receive switch
- **File:** `GalenaLightBar-ESP32C3/main/main.c:171-206`
- **Requirement:** Add `case PKT_BRIGHTNESS:` to `espnow_task()` that sets `g_brightness` from `evt.pkt.value / 100.0f` and calls `set_brightness()` (respecting `g_light_on`).
- **Why:** The Action Ring sends `PKT_BRIGHTNESS` to set absolute brightness, but the light bar silently ignores it. This breaks the ability to remotely set brightness.

### 1.3 Light Bar: Check NVS return values
- **File:** `GalenaLightBar-ESP32C3/main/main.c:70-82, 291-294`
- **Requirement:** Check all `nvs_open()`, `nvs_get_u8()`, `nvs_set_u8()`, `nvs_commit()` return values. Log warnings on failure. Provide fallback defaults.
- **Why:** Silent NVS failures cause undebuggable state corruption.

### 1.4 App: Call `CancelIoEx` on HID disconnect
- **File:** `GalenaActionRing/Services/NativeMethods.cs:91`, `MainWindow.xaml.cs:274-298`
- **Requirement:** Call `NativeMethods.CancelRead(_hidReadHandle)` before closing handles in `DisconnectHidDevice()`. The `CancelIoEx` P/Invoke is already declared but never called.
- **Why:** Pending `ReadFile` blocks a thread-pool thread until device removal. Without `CancelIoEx`, disconnect hangs the thread.

### 1.5 Action Ring: Protect `g_shadow_brightness` between task and callback contexts
- **File:** `GalenaActionRing-ESP32S3/main/main.c:38, 179-190, 349-350`
- **Requirement:** Wrap `g_shadow_brightness` reads/writes with a mutex or use `_Atomic` operations. `float` writes are non-atomic on Xtensa, and `tud_hid_get_report_cb()` runs in TinyUSB task context while `espnow_task()` writes it.
- **Why:** Concurrent non-atomic float read/write can produce torn values, corrupting the brightness reported via GET_REPORT.

---

## Phase 2 — Stability & Robustness

### 2.1 App: Replace empty `catch { }` with logged exceptions
- **Files:** All `.cs` files (~30+ locations)
- **Requirement:** Every empty `catch { }` must at minimum log the exception via `System.Diagnostics.Debug.WriteLine()` or a proper `ILogger`. Use typed catch blocks where possible.
- **Why:** Silent exception swallowing makes production debugging impossible.

### 2.2 App: Remove `async void` from timer callbacks
- **File:** `GalenaActionRing/Services/OsdService.cs:592`
- **Requirement:** Convert `ApplyToggleStatesToOsd()` from `async void` to `async Task`. Replace the timer tick handler with a fire-and-forget-safe wrapper that catches and logs exceptions.
- **Why:** Exceptions in `async void` timer callbacks crash the entire process.

### 2.3 App: Thread-safe `_lastBrightness` and `_lightBarOn`
- **File:** `GalenaActionRing/MainWindow.xaml.cs:50-51`
- **Requirement:** Add `volatile` qualifier or use `Interlocked` for these fields. They are read/written from UI thread and from `DispatcherQueue.TryEnqueue` callbacks.
- **Why:** Prevents compiler reordering and ensures memory visibility across threads (low risk currently but technically a data race).

### 2.4 Light Bar: NVS write debounce on encoder changes
- **File:** `GalenaLightBar-ESP32C3/main/main.c:84-92, 183`
- **Requirement:** Only call `nvs_save_brightness()` when brightness changes by ≥1% (absolute) relative to last persisted value. Track `last_saved_brightness_pct` and skip if unchanged.
- **Why:** Every encoder tick writes to NVS, burning flash lifetime. NVS has ~100k erase cycles; rapid encoder use could exhaust it in hours.

### 2.5 Both Firmwares: Check queue creation results
- **Files:** Both `main.c`
- **Requirement:** After `xQueueCreate()`, assert or log fatal if NULL. The device cannot function without queues.
- **Why:** Silent queue failure causes all queue operations to silently fail (return false), making the device unresponsive with no debug output.

### 2.6 Both Firmwares: Log `esp_now_send()` failures
- **Files:** Both `main.c` (action ring: lines 283, 298; light bar: line 112)
- **Requirement:** Check return value of `esp_now_send()`. Log at `ESP_LOGW` on failure. (Action ring currently ignores it entirely in send functions.)
- **Why:** Send failures indicate WiFi congestion or peer disconnection; silent drops hide connectivity problems.

---

## Phase 3 — Architecture Cleanup

### 3.1 App: Extract `HidService` from `MainWindow.xaml.cs`
- **Files:** New `Services/HidService.cs`; modify `MainWindow.xaml.cs`
- **Requirement:** Move HID device discovery (`FindHidDevicesAsync`), connect/disconnect, `HidReadLoop`, `ProcessHidEvent` into a dedicated `HidService` class. Expose events: `OnBrightnessChanged`, `OnEncoderTurned`, `OnButtonClicked`, `OnLightStateChanged`, `OnOsdAcknowledged`. `MainWindow` subscribes to these events.
- **Why:** `MainWindow` is 1473 lines and violates SRP. HID logic is self-contained and testable in isolation.

### 3.2 App: Extract `ProfileManager` from `MainWindow.xaml.cs`
- **Files:** New `Services/ProfileManager.cs`; modify `MainWindow.xaml.cs`
- **Requirement:** Move `InitAppProfiles()`, `SaveCurrentProfile()`, `LoadCurrentProfile()`, `DeleteProfile()`, profile repair/migration, and profile tab binding into a `ProfileManager` class.
- **Why:** Profile CRUD logic is ~200 lines embedded in the window class.

### 3.3 App: Extract `CanvasEditor` from `MainWindow.xaml.cs`
- **Files:** New `Services/CanvasEditor.cs`; modify `MainWindow.xaml.cs`
- **Requirement:** Move `RenderCanvas()`, `RingCanvas_PointerPressed/Moved/Released`, node property editing, `AddActionToRing()`, `DeleteRingBtn_Click()`, `RenameRingBtn_Click()` into a `CanvasEditor` class.
- **Why:** Canvas geometry, drag-and-drop, and node manipulation are ~300 lines independent of HID or profiles.

### 3.4 Firmware: Split `main.c` into modules
- **Files:** New `espnow.c/.h`, `hid.c/.h` (action ring only), `encoder.c/.h` (action ring only), `button.c/.h` (action ring only), `cdc_log.c/.h` (action ring only); update `CMakeLists.txt`
- **Requirement:** Each functional area gets its own source file with a clean header interface. `main.c` becomes ~50 lines: init sequence and task creation.
- **Why:** Single-file firmware is hard to navigate and maintain. Module boundaries enforce discipline and enable unit testing.

---

## Phase 4 — Polish

### 4.1 App: Rename namespace `Galena_Action_Ring` → `GalenaActionRing`
- **Files:** `GalenaActionRing.csproj`, all `.cs` files
- **Requirement:** Update root namespace in `.csproj` and all `namespace` declarations. Update `using` references.
- **Why:** C# convention is PascalCase; underscores are non-standard and visually inconsistent.

### 4.2 Firmware: Consolidate send functions with `send_packet()` helper
- **Files:** Both `main.c`
- **Requirement:** Replace 3–5 near-identical `espnow_send_*()` functions with a single `static esp_err_t send_packet(uint8_t type, int32_t value)` that builds the packet, calls `esp_now_send()`, and logs.
- **Why:** Eliminates code duplication; one place to change send behavior (e.g., add retry or encryption).

### 4.3 App: Cap debug log to prevent UI lag
- **File:** `GalenaActionRing/MainWindow.xaml.cs:383-389`
- **Requirement:** When `_debugLog` exceeds 1000 lines, trim oldest entries. Use `StringBuilder.Remove()` to maintain O(1) append. Consider using `VirtualizedListView` for log display.
- **Why:** `_debugLog.ToString()` every 50ms HID event causes O(n²) string allocation and layout invalidation.

### 4.4 App: Fix `SetBrightness()` to use incremental steps
- **File:** `GalenaActionRing/Services/OsdService.cs:479-501`
- **Requirement:** `SetBrightness(true)` should query current brightness via WMI, add 10%, and set. Currently sets to 100% or 0% (absolute), not incremental.
- **Why:** Brightness up/down buttons are unusable — they jump to max/min instead of stepping.

### 4.5 Light Bar: Remove unused `esp_timer` dependency
- **File:** `GalenaLightBar-ESP32C3/main/CMakeLists.txt:8`
- **Requirement:** `esp_timer` is listed in `REQUIRES` but `main.c` uses only `vTaskDelay`, not `esp_timer_*` APIs. Remove it.
- **Why:** Unnecessary dependency slows compilation and consumes flash.

---

## Verification Steps

After each phase:
1. **App:** `dotnet build` — must compile without errors or warnings
2. **Action Ring:** `idf.py build` — must compile without errors or warnings
3. **Light Bar:** `idf.py build` — must compile without errors or warnings
4. Push to respective GitHub repos after each phase
