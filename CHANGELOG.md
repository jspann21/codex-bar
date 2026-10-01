# Changelog

All notable changes to CodexBar are documented here.

## [Unreleased]

- Adds clickable usage and reset-details faces at a shared 290-pixel width. The 290 × 100 usage face shows weekly remaining, daily average and estimated remaining at reset, with centered labels, a reset countdown in the title and a capacity bar below.
- Calculates average daily usage over the inferred seven-day cycle and estimates capacity remaining at reset or time until exhaustion. Withholds forecasts during the first six hours and marks offline data without a live pace warning.
- Warns with a muted red background and white text on both faces when the weekly pace predicts exhaustion before reset, with a small buffer to prevent flicker near the limit.
- Shows dates on the left and green countdowns on the right of reset details. Fits longer date text within its column, shows whole days for banked expiries and hours below one day, and marks expired/offline states explicitly. Long lists grow vertically and scroll when taller than the screen.
- Restores the compact location after page switches and hover expansion at screen edges, preserving deliberate drag movement without accumulating resize drift.
- Adds optional hover expansion at the same width, with delayed entry and collapse, screen-edge positioning, and a saved right-click preference that starts off.
- Reserves the title strip for dragging without hover expansion or face switching. Starting a title drag collapses hover content under the pointer, and release suppresses expansion until body reentry.
- Adds a saved Show in taskbar toggle, keeping the widget visible and the tray icon available when Off; defaults to Off and respects the preference on restore.
- Defaults to hover Off, always on top On and taskbar Off. Enables Windows startup once at the first launch of this build, preserves later Off choices and reports startup failures.
- Preserves dragging without flipping faces; adds keyboard and tray-menu face switching.
- Adds isolated calculation and actual-form checks with rendered preview fixtures, plus screenshots of both finished faces in the README.

## [1.1.1] - 2026-07-31

- Makes the Start with Windows status explicit in the tray menu.
- Adds notification setup guidance for Gmail app passwords and carrier email-to-text addresses.
- Clarifies that the configured refresh interval controls the real rate-limit request frequency.

## [1.1.0] - 2026-07-31

### Added

- Exact local expiration dates and times for every available banked rate-limit reset, ordered nearest first.
- Explicit `Always on top — On/Off` status in the tray menu.
- Optional weekly-capacity reset notifications through SMTP or a prefilled draft in the default mail app.
- A test-alert command and a live/offline connection indicator.

### Improved

- Reuses the supported Codex app-server response for reset-credit details without reading authentication files or requiring a separate login.
- Protects stored SMTP passwords with Windows Data Protection API.
- Validates notification settings, bounds delivery time, and writes settings atomically.
- Falls back across installed Codex executables and distinguishes retryable transport failures from semantic errors.
- Reduces unnecessary settings writes and widget repaints.

## [1.0.0] - 2026-07-22

- Initial public release of the lightweight Windows widget.
- Live weekly Codex capacity and reset-time display.
- Configurable refresh interval, transparency, always-on-top mode, Windows startup, and notification-area support.

[Unreleased]: https://github.com/jspann21/codex-bar/compare/v1.1.1...HEAD
[1.1.1]: https://github.com/jspann21/codex-bar/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/jspann21/codex-bar/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/jspann21/codex-bar/releases/tag/v1.0.0
