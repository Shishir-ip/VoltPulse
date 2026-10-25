# VoltPulse manual QA checklist

Run through this list before each release. Hardware used for the last pass: Acer Nitro ANV15-41
(Windows 11, single battery). [ ] = not yet verified in the automated build.

## Install / launch

- [ ] Portable zip: extract to a clean folder, run VoltPulse.exe - no prerequisites, no SmartScreen crash (unsigned builds warn; verify hash against SHA256SUMS.txt).
- [ ] Installer: per-user install succeeds without admin; desktop/autostart options work; uninstall removes the app but keeps %APPDATA%\VoltPulse.
- [ ] Second launch while running: no popup - the first instance's window is brought to the foreground (works whether it was minimized to tray, hidden, or minimized).
- [ ] Cold start < 2 s; no window flicker; starts centered.

## Live screen

- [ ] Big reading shows "measuring..." for ~3 s, then a number; number animates smoothly (no flicker/jump).
- [ ] Discharging = orange, charging = teal, full = green, critical/hot = red; color cross-fades on state change.
- [ ] 5 pills fill according to current magnitude and use the state color.
- [ ] Min/max track the session; reset (round ×) clears them; values look plausible vs. the big reading.
- [ ] Plugging/unplugging the charger updates status, state color and pills within ~1 s (instant reaction to the power event).
- [ ] Every fact row copies its value on click ("Copied" hint appears); clipboard content is correct.
- [ ] Tooltips show a plain-language explanation and the data source.
- [ ] Onboarding chip shows once; "Got it" dismisses it permanently (persists across restart).
- [ ] Rate 0 + plugged at 100% → "Idle"/"Fully charged" instead of 0 mA.
- [ ] Values with no data show "Not supported by this device" (never 0, never a crash).

## FAB / quick actions

- [ ] FAB opens the menu; Pause/Resume logging works (facts freeze, history stops).
- [ ] Demo mode toggle switches to the simulated battery (and back).
- [ ] Mini widget toggle shows/hides the overlay.

## Navigation (regression found in testing)

- [ ] Click EVERY navigation item once right after launch: Live, Graphs, Health, Sessions, Insights, Power usage, Alerts, Reports, Settings, About - each must render without errors.
  (Regression caught: DI-constructed views without a default constructor crashed WPF DataTemplate
  instantiation on first navigation. All views now use parameterless constructors + DataContext wiring;
  never add constructor parameters to a page view again.)

## Graphs

- [ ] Live updates at 1 Hz without stutter for 10+ minutes.
- [ ] Each series (current/power/voltage/level/temperature) renders and scales.
- [ ] Ranges 1 min / 10 min / 1 h use in-memory data; 24 h / 7 d / 30 d load from the database.
- [ ] Pause freezes the chart; Resume catches up.
- [ ] Export PNG produces a readable dark-themed image.
- [ ] 24 h of data does not degrade rendering (downsampled series for 7 d/30 d).

## Health

- [ ] Health ring/bars match the fact list values; verdict matches the stated rule (≥80 Healthy, 60-79 Fair, <60 Replace soon).
- [ ] "Import Windows battery report" completes and the fade chart shows entries (or a clear message when none).
- [ ] On machines without capacity data the page degrades gracefully.

## Sessions

- [ ] Sessions appear after plug/unplug cycles with correct kind, duration, % change.
- [ ] Clicking a session draws its graph; empty history shows the hint text.
- [ ] Clear history (Settings and Sessions) asks for confirmation and empties everything.

## Insights / Power usage

- [ ] Insights populate after a day of history; tips match the observed behavior.
- [ ] Power usage lists top processes by CPU; clearly labeled as estimate; GPU column explicitly unavailable.

## Alerts

- [ ] Low battery alert fires at the threshold while discharging; cooldown prevents spam.
- [ ] Charge limit alert fires while charging past the threshold (when enabled).
- [ ] Charger connected/disconnected fire on plug/unplug.
- [ ] Test notification shows the toast and plays the sound (when enabled).
- [ ] Snooze 1 h silences everything for an hour.
- [ ] Threshold/cooldown edits persist across restart.

## Tray / mini widget

- [ ] Tray icon color matches state; tooltip shows percent and current; left-click opens the window.
- [ ] Tray menu: Show, Pause, Mini widget, Snooze, Settings, Exit all work; Exit really exits (process gone).
- [ ] Close button hides to tray (when close-to-tray on) and Exit from the tray is the only remaining close path.
- [ ] Mini widget: draggable, remembers position, opacity applies, click-through toggles, × hides it.

## Settings

- [ ] Theme switch is instant (dark/light/follow Windows); high contrast takes over automatically when enabled system-wide.
- [ ] °F/24 h settings change every relevant display.
- [ ] Sampling interval change takes effect without restart.
- [ ] Sign convention flips the displayed current sign everywhere (Live, widget, tray, exports).
- [ ] Start with Windows writes/removes the HKCU Run key.
- [ ] Font scale slider resizes the UI; layout stays intact at 0.8 and 1.5.
- [ ] Backup/restore/reset settings behave as labeled.

## Data integrity / edge cases

- [ ] Sleep the machine, resume: providers re-initialize, no errors in the log, data continues.
- [ ] Kill power mid-charge (unplug), verify a discharge session is recorded; plug back → charging session.
- [ ] Delete/corrupt history.db → app recreates it, logs the corruption, keeps running.
- [ ] Corrupt settings.json → app backs it up (.corrupt) and starts with defaults.
- [ ] Fill the disk partially - history writes fail gracefully with log entries, UI keeps working.
- [ ] VM / desktop without a battery: friendly no-battery state; demo mode works from Settings.
- [ ] Run 24 h soak (tools\soak.ps1): no crash, working set stable (no upward trend), CPU < 1% average.

## Display / accessibility

- [ ] 100% / 150% / 200% DPI: no clipped or blurry text (PerMonitorV2).
- [ ] Window restored while minimized-to-tray appears on a visible monitor (not off-screen).
- [ ] Full keyboard navigation: tab through nav, buttons, rows; focus visuals visible; Enter/Space activate.
- [ ] Screen reader (Narrator) reads page names, fact labels and alert names (UI Automation names set).
- [ ] Light theme remains readable everywhere (no dark-on-dark text).
