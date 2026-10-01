using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using CodexBar;

internal static class Checks
{
    private static int passed;
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowStyle(IntPtr window, int index);

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
        if (output is not null) Directory.CreateDirectory(output);

        // Exercise startup policy without changing this PC's registry or preferences.
        var startupDefaults = new AppSettings();
        var startupWrites = 0;
        Check("First launch enables Windows startup once", startupDefaults.ApplyStartupDefault(() => startupWrites++) &&
            startupDefaults.WindowsStartupInitialized && startupWrites == 1);
        var restartedPreferences = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(startupDefaults))!;
        Check("Restart preserves a later startup off choice", !restartedPreferences.ApplyStartupDefault(() => startupWrites++) &&
            startupWrites == 1);
        var failedStartup = new AppSettings();
        var failureObserved = false;
        try { failedStartup.ApplyStartupDefault(() => throw new UnauthorizedAccessException("Isolated startup failure")); }
        catch (UnauthorizedAccessException) { failureObserved = true; }
        Check("Startup failure remains observable and retryable", failureObserved && !failedStartup.WindowsStartupInitialized &&
            failedStartup.ApplyStartupDefault(() => startupWrites++) && startupWrites == 2);

        var light = UsagePace.Calculate(10, Now.AddDays(4), Now)!;
        Check("10% after three days", Near(light.AveragePerDay, 10d / 3) &&
            Near(light.DaysOfCapacity, 27) && Near(light.RemainingAtReset, 76.6666666667) && !light.AbovePace);
        var heavy = UsagePace.Calculate(50, Now.AddDays(5), Now)!;
        Check("50% after two days", Near(heavy.AveragePerDay, 25) &&
            Near(heavy.DaysOfCapacity, 2) && heavy.AbovePace);
        Check("Partial days", Near(UsagePace.Calculate(10, Now.AddDays(5.5), Now)!.AveragePerDay, 10d / 1.5));
        Check("No usage has no exhaustion estimate", UsagePace.Calculate(0, Now.AddDays(4), Now) is
            { AveragePerDay: 0, DaysOfCapacity: null, RemainingAtReset: 100, AbovePace: false });
        Check("Early cycle avoids prediction", UsagePace.Calculate(10, Now.AddHours(167), Now) is
            { AveragePerDay: null, AbovePace: false });
        Check("Exhausted even early", UsagePace.Calculate(100, Now.AddHours(167), Now) is
            { Exhausted: true, AbovePace: true, DaysOfCapacity: 0 });
        Check("Six-hour boundary", UsagePace.Calculate(1, Now.AddHours(162), Now)!.AveragePerDay is not null);
        Check("Exact sustainable pace", UsagePace.Calculate(100d * 2 / 7, Now.AddDays(5), Now) is
            { AbovePace: false });
        Check("Warning stays stable around boundary", !UsagePace.Calculate(100.25 * 2 / 7, Now.AddDays(5), Now)!.AbovePace &&
            UsagePace.Calculate(99.75 * 2 / 7, Now.AddDays(5), Now, true)!.AbovePace &&
            !UsagePace.Calculate(99 * 2 / 7, Now.AddDays(5), Now, true)!.AbovePace);
        Check("Invalid and expired data", UsagePace.Calculate(double.NaN, Now.AddDays(4), Now) is null &&
            UsagePace.Calculate(double.PositiveInfinity, Now.AddDays(4), Now) is null &&
            UsagePace.Calculate(-1, Now.AddDays(4), Now) is null &&
            UsagePace.Calculate(101, Now.AddDays(4), Now) is null &&
            UsagePace.Calculate(10, Now, Now) is null &&
            UsagePace.Calculate(10, Now.AddDays(8), Now) is null);

        // Exercise the real form with isolated preferences. Do not show it, pump
        // its refresh timers, write settings, send email, or contact Codex.
        using var form = new WidgetForm(new AppSettings
        {
            X = 100, Y = 100, AlwaysOnTop = false, NotifyOnUsageLimitReached = false
        });
        ((System.Windows.Forms.Timer)Get(form, "refreshTimer")!).Stop();
        ((System.Windows.Forms.Timer)Get(form, "positionSaveTimer")!).Stop();
        var topmostTimer = (System.Windows.Forms.Timer)Get(form, "topmostTimer")!;
        Check("Topmost recovery runs independently of usage refresh", topmostTimer.Enabled && topmostTimer.Interval == 2_000);
        topmostTimer.Stop();
        ((NotifyIcon)Get(form, "trayIcon")!).Visible = false;
        form.CreateControl();
        Check("Taskbar button stays off by default", !form.ShowInTaskbar);
        Check("Default preferences match requested hover, topmost and taskbar choices", new AppSettings() is
            { ExpandOnHover: false, AlwaysOnTop: true, ShowInTaskbar: false });
        Check("Overview uses requested 290 by 100 size", form.ClientSize ==
            new Size((int)Math.Round(290 * form.DeviceDpi / 96f), (int)Math.Round(100 * form.DeviceDpi / 96f)));
        Save(form, output, "loading");

        SetSnapshot(form, 10, 4, 2);
        Check("Main face has normal background", form.BackColor == Color.FromArgb(8, 10, 9));
        Save(form, output, "on-track");
        SetSnapshot(form, 5.1, 6.3, 2);
        Check("Overview title gives remaining cycle time", (string)Invoke(form, "OverviewTitle")! == "CODEX  ·  6.3 days until reset");
        Save(form, output, "requested-layout");
        SetSnapshot(form, 10, 23.9 / 24, 2);
        Save(form, output, "hours-to-reset");
        SetSnapshot(form, 0, 4, 2);
        Save(form, output, "full-capacity");
        SetSnapshot(form, 99.75, 6.75, 2);
        Save(form, output, "high-daily-average");
        SetSnapshot(form, 100, 6.9, 2);
        Save(form, output, "early-exhausted");
        SetSnapshot(form, 10, 4, 2);
        Click(form, 40, 70);
        Check("Click opens details", (bool)Get(form, "showResetDetails")!);
        Check("Details shares the requested 290 pixel width", form.ClientSize.Width == (int)Math.Round(290 * form.DeviceDpi / 96f));
        Check("Two numbered expiry rows fit in a shorter face", form.ClientSize.Height == (int)Math.Round(198 * form.DeviceDpi / 96f));
        var smallHeight = form.ClientSize.Height;
        Save(form, output, "reset-details");
        SetSnapshot(form, 5.1, 6.3, 2);
        Check("Weekly row retains fractional reset countdown", (string)Invoke(form, "ResetTimeLeft", Now.AddDays(6.3), true)! == "6.3 days");
        Check("Banked expiry countdown uses the observation time", (string)Invoke(form, "ResetTimeLeft", Now.AddDays(21.7), false)! == "22 days");
        Check("Hours and imminent expiries remain readable", (string)Invoke(form, "ResetTimeLeft", Now.AddHours(12), false)! == "12 hours" &&
            (string)Invoke(form, "ResetTimeLeft", Now.AddMinutes(30), false)! == "<1 hour");
        Check("Expired banked resets are explicit", (string)Invoke(form, "ResetTimeLeft", Now, false)! == "Expired" &&
            (string)Invoke(form, "ResetTimeLeft", Now.AddDays(-1), false)! == "Expired");
        Save(form, output, "requested-details");
        SetSnapshot(form, 10, 4, 1);
        Save(form, output, "single-expiry");
        Set(form, "liveConnected", false);
        Invoke(form, "UpdatePace");
        Check("Offline reset countdowns do not look live", (string)Invoke(form, "ResetTimeLeft", Now.AddDays(4), true)! == "Offline" &&
            (string)Invoke(form, "ResetTimeLeft", Now.AddDays(22), false)! == "—");
        Save(form, output, "offline-details");
        SetSnapshot(form, 10, 8, 2);
        Check("Unknown weekly timing stays explicit", (string)Invoke(form, "ResetTimeLeft", Now.AddDays(8), true)! == "Unknown");
        Save(form, output, "unknown-reset-details");
        Set(form, "snapshot", new UsageSnapshot(10, Now.AddDays(4), Now,
            new[] { Now.AddMinutes(-1), Now.AddMinutes(30), Now.AddHours(12), Now.AddDays(1), Now.AddDays(21.7) }));
        Invoke(form, "UpdatePace");
        Invoke(form, "UpdateFaceSize");
        Save(form, output, "expiry-boundaries");
        Set(form, "snapshot", new UsageSnapshot(10, Now.AddHours(23.9), Now,
            new[] { new DateTimeOffset(2026, 11, 25, 2, 59, 0, TimeSpan.Zero), Now.AddHours(23.9) }));
        Invoke(form, "UpdatePace");
        Invoke(form, "UpdateFaceSize");
        Save(form, output, "long-dates-and-hours");
        SetSnapshot(form, 10, 4, 2);
        Click(form, 40, 70);
        Check("Click returns to overview", !(bool)Get(form, "showResetDetails")!);
        Check("Returning restores compact dimensions", form.ClientSize ==
            new Size((int)Math.Round(290 * form.DeviceDpi / 96f), (int)Math.Round(100 * form.DeviceDpi / 96f)));
        var scale = form.DeviceDpi / 96f;
        var tinyMove = new MouseEventArgs(MouseButtons.Left, 1, (int)(41 * scale), (int)(70 * scale), 0);
        Invoke(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, (int)(40 * scale), (int)(70 * scale), 0));
        Invoke(form, "OnMouseMove", tinyMove);
        Invoke(form, "OnMouseUp", tinyMove);
        Check("Small pointer movement remains a click", (bool)Get(form, "showResetDetails")!);
        Invoke(form, "ToggleFace");

        Invoke(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, (int)(40 * scale), (int)(70 * scale), 0));
        form.Capture = false;
        Invoke(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, (int)(40 * scale), (int)(70 * scale), 0));
        Check("Lost capture cancels the click", !(bool)Get(form, "showResetDetails")!);

        var original = form.Location;
        Invoke(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 40, 70, 0));
        Invoke(form, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 1, 80, 100, 0));
        Invoke(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 40, 70, 0));
        Check("Drag moves without flipping", form.Location != original && !(bool)Get(form, "showResetDetails")!);
        Invoke(form, "OnKeyDown", new KeyEventArgs(Keys.Space));
        Check("Keyboard switches face", (bool)Get(form, "showResetDetails")!);

        SetSnapshot(form, 50, 5, 2);
        Check("Risk colours details red", form.BackColor == Color.FromArgb(132, 38, 48));
        Save(form, output, "warning-details");
        Invoke(form, "ToggleFace");
        Save(form, output, "above-pace");
        Set(form, "liveConnected", false);
        Invoke(form, "UpdatePace");
        Check("Offline clears live warning", form.BackColor == Color.FromArgb(8, 10, 9));
        Check("Offline title identifies stale data", ((string)Invoke(form, "OverviewTitle")!).Contains("OFFLINE"));
        Save(form, output, "offline");

        SetSnapshot(form, 0, 7 - 1d / 24, 0);
        Check("New cycle clears warning", !(bool)Get(form, "abovePace")!);
        Save(form, output, "new-cycle");
        Invoke(form, "ToggleFace");
        Save(form, output, "no-banked-resets");
        SetSnapshot(form, 10, 4, 6);
        Check("Details grows rather than shrinking text", form.ClientSize.Height > smallHeight);
        Save(form, output, "many-resets");
        SetSnapshot(form, 10, 4, 100);
        Check("Details stays on screen", form.Height <= Screen.FromControl(form).WorkingArea.Height);
        Invoke(form, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 40, 150, -120));
        Check("Overflow resets remain reachable", (int)Get(form, "firstVisibleExpiration")! == 1);
        Save(form, output, "overflow-resets");
        for (var i = 0; i < 100; i++)
            Invoke(form, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 40, 150, -120));
        var lastFirstRow = (int)Get(form, "firstVisibleExpiration")!;
        Check("Last expiry remains reachable with one-line scrolling", lastFirstRow > 1 && lastFirstRow < 100);
        Save(form, output, "last-expiries");
        SetSnapshot(form, 10, 4, 1);
        Save(form, output, "shrunk-expiry-list");
        Check("Shortened expiry list resets the visible index", (int)Get(form, "firstVisibleExpiration")! == 0);

        Invoke(form, "ToggleFace");
        var preferences = (AppSettings)Get(form, "settings")!;
        var hoverTimer = (System.Windows.Forms.Timer)Get(form, "hoverTimer")!;
        EnterBody(form);
        Check("Hover is optional and off by default", !preferences.ExpandOnHover && !hoverTimer.Enabled &&
            !(bool)Get(form, "hoverExpanded")!);
        Check("Older settings keep hover disabled", !System.Text.Json.JsonSerializer
            .Deserialize<AppSettings>("{\"AlwaysOnTop\":false}")!.ExpandOnHover);
        preferences.ExpandOnHover = true;
        Check("Hover preference survives serialization", System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(preferences))!.ExpandOnHover);
        SetSnapshot(form, 10, 4, 2);
        MovePointer(form, 40, 17);
        HoverTick(form, false);
        Check("Header hover never expands", !hoverTimer.Enabled && !(bool)Get(form, "hoverExpanded")!);
        EnterBody(form);
        Check("Entry delays expansion", hoverTimer.Enabled && hoverTimer.Interval == 350 &&
            !(bool)Get(form, "hoverExpanded")!);
        MovePointer(form, 40, 17);
        Check("Moving to header cancels pending expansion", !hoverTimer.Enabled);
        EnterBody(form);
        HoverTick(form, false);
        Check("Passing pointer does not expand", !(bool)Get(form, "hoverExpanded")!);
        EnterBody(form);
        HoverTick(form, true, menuVisible: true);
        Check("Open menu postpones expansion", hoverTimer.Enabled && !(bool)Get(form, "hoverExpanded")!);
        HoverTick(form, true);
        Check("Hover expands vertically at the same width", (bool)Get(form, "hoverExpanded")! &&
            form.ClientSize == new Size((int)Math.Round(290 * scale), (int)Math.Round(236 * scale)));
        Save(form, output, "hover-expanded");
        Invoke(form, "OnMouseLeave", EventArgs.Empty);
        Check("Leaving delays collapse", hoverTimer.Enabled && hoverTimer.Interval == 450 &&
            (bool)Get(form, "hoverExpanded")!);
        EnterBody(form);
        Check("Reentry cancels collapse", !hoverTimer.Enabled && (bool)Get(form, "hoverExpanded")!);
        Invoke(form, "OnMouseLeave", EventArgs.Empty);
        HoverTick(form, true);
        Check("Pointer still inside keeps expanded content", (bool)Get(form, "hoverExpanded")!);
        Set(form, "mouseDownScreen", form.Location);
        HoverTick(form, false);
        Check("Dragging postpones resize", hoverTimer.Enabled && (bool)Get(form, "hoverExpanded")!);
        Set(form, "mouseDownScreen", null!);
        HoverTick(form, false);
        Check("Leaving collapses to original size", !(bool)Get(form, "hoverExpanded")! &&
            form.ClientSize == new Size((int)Math.Round(290 * scale), (int)Math.Round(100 * scale)));
        Click(form, 40, 17);
        Check("Header click is reserved for dragging", !(bool)Get(form, "showResetDetails")!);

        var area = Screen.FromControl(form).WorkingArea;
        form.Location = new Point(area.Right - form.Width, area.Bottom - form.Height);
        var compactPosition = form.Location;
        EnterBody(form);
        HoverTick(form, true);
        Check("Expansion stays within screen bounds", area.Contains(form.Bounds));
        Invoke(form, "QueueHover", false);
        HoverTick(form, false);
        Check("Collapse restores compact position", form.Location == compactPosition);
        Invoke(form, "ToggleFace");
        Check("Details fits inward at screen edge", area.Contains(form.Bounds));
        Invoke(form, "ToggleFace");
        Check("Returning from details restores the original position", form.Location == compactPosition);
        for (var i = 0; i < 3; i++)
        {
            Invoke(form, "ToggleFace");
            Invoke(form, "ToggleFace");
        }
        Check("Repeated page switches do not drift", form.Location == compactPosition);
        Invoke(form, "ToggleFace");
        SetSnapshot(form, 10, 4, 6);
        Check("Details refresh preserves compact anchor", (Point)Get(form, "compactAnchor")! == compactPosition);
        SetSnapshot(form, 10, 4, 2);
        var expandedPosition = form.Location;
        Invoke(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 80, 70, 0));
        Invoke(form, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 1, 40, 40, 0));
        var dragDelta = new Size(form.Left - expandedPosition.X, form.Top - expandedPosition.Y);
        Invoke(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 40, 40, 0));
        var draggedPosition = form.Location;
        SetSnapshot(form, 10, 4, 2);
        Check("Refresh keeps dragged details in place", form.Location == draggedPosition);
        Invoke(form, "ToggleFace");
        Check("Dragging details translates compact position without resize drift", form.Location == compactPosition + dragDelta);
        compactPosition = form.Location;

        SetSnapshot(form, 50, 5, 2);
        EnterBody(form);
        HoverTick(form, true);
        Check("Hover retains warning colour", form.BackColor == Color.FromArgb(132, 38, 48));
        Save(form, output, "hover-warning");
        Click(form, 40, 70);
        Check("Click on expanded face still opens reset details", (bool)Get(form, "showResetDetails")! &&
            !(bool)Get(form, "hoverExpanded")!);
        Check("Hover-to-details preserves compact anchor", (Point)Get(form, "compactAnchor")! == compactPosition);
        EnterBody(form);
        Check("Details face does not hover-resize", !hoverTimer.Enabled);
        Invoke(form, "ToggleFace");
        Check("Hover-to-details-to-compact restores location", form.Location == compactPosition);
        EnterBody(form);
        HoverTick(form, true);
        expandedPosition = form.Location;
        Invoke(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 80, 70, 0));
        Invoke(form, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 1, 40, 40, 0));
        dragDelta = new Size(form.Left - expandedPosition.X, form.Top - expandedPosition.Y);
        Invoke(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 40, 40, 0));
        draggedPosition = form.Location;
        SetSnapshot(form, 50, 5, 2);
        Check("Refresh keeps dragged hover content in place", form.Location == draggedPosition);
        preferences.ExpandOnHover = false;
        Invoke(form, "CancelHover");
        Check("Disabling hover collapses immediately", !(bool)Get(form, "hoverExpanded")! && !hoverTimer.Enabled);
        Check("Dragging hover content preserves compact offset", form.Location == compactPosition + dragDelta);

        preferences.ExpandOnHover = true;
        form.Location = new Point(area.Right - form.Width, area.Bottom - form.Height);
        compactPosition = form.Location;
        EnterBody(form);
        HoverTick(form, true);
        MovePointer(form, 80, 17);
        Check("Header offers a drag cursor and queues collapse", form.Cursor == Cursors.SizeAll && hoverTimer.Enabled);
        expandedPosition = form.Location;
        var headerDown = new MouseEventArgs(MouseButtons.Left, 1, (int)(80 * scale), (int)(17 * scale), 0);
        Invoke(form, "OnMouseDown", headerDown);
        Check("Press holds geometry until drag starts", (bool)Get(form, "hoverExpanded")! && !hoverTimer.Enabled);
        var headerMove = new MouseEventArgs(MouseButtons.Left, 1, (int)(60 * scale), (int)(17 * scale), 0);
        Invoke(form, "OnMouseMove", headerMove);
        Check("Header drag shrinks immediately without flipping", !(bool)Get(form, "hoverExpanded")! &&
            !(bool)Get(form, "showResetDetails")! && form.Height == (int)Math.Round(100 * scale));
        Check("Shrinking keeps header under pointer", form.Location == new Point(
            expandedPosition.X + (int)(60 * scale) - (int)(80 * scale), expandedPosition.Y));
        var bottomMove = new MouseEventArgs(MouseButtons.Left, 1, (int)(60 * scale),
            (int)(17 * scale) + compactPosition.Y - expandedPosition.Y, 0);
        Invoke(form, "OnMouseMove", bottomMove);
        Check("Header drag can reach original bottom edge", form.Bottom == area.Bottom);
        Invoke(form, "OnMouseLeave", EventArgs.Empty);
        Invoke(form, "OnMouseUp", headerDown);
        MovePointer(form, 40, 70);
        HoverTick(form, true);
        Check("Release does not immediately re-expand over body", !hoverTimer.Enabled && !(bool)Get(form, "hoverExpanded")!);
        MovePointer(form, 45, 70);
        Check("Movement within body remains suppressed after drag", !hoverTimer.Enabled);
        MovePointer(form, 40, 17);
        MovePointer(form, 40, 70);
        Check("Leaving and reentering body rearms hover", hoverTimer.Enabled && hoverTimer.Interval == 350);
        HoverTick(form, true);
        MovePointer(form, 40, 17);
        HoverTick(form, false);
        Check("Hovering header collapses the expanded face", !(bool)Get(form, "hoverExpanded")!);
        EnterBody(form);
        Invoke(form, "OnMouseDown", headerDown);
        Invoke(form, "OnMouseMove", headerMove);
        Invoke(form, "OnMouseUp", headerDown);
        Check("Compact header drag cancels pending hover", !hoverTimer.Enabled && !(bool)Get(form, "hoverExpanded")!);
        preferences.ExpandOnHover = false;
        Invoke(form, "CancelHover");

        var recoveryHandle = form.Handle;
        var repairs = 0;
        var probes = 0;
        Func<bool> covered = () => { probes++; return true; };
        Action recordRepair = () => repairs++;
        preferences.AlwaysOnTop = true;
        Invoke(form, "MaintainAlwaysOnTop", false, false, covered, recordRepair);
        Check("Recovery respects a deliberately hidden widget", probes == 0 && repairs == 0 && !form.Visible);
        preferences.AlwaysOnTop = false;
        Invoke(form, "MaintainAlwaysOnTop", true, false, covered, recordRepair);
        Check("Recovery respects always-on-top off", probes == 0 && repairs == 0);
        preferences.AlwaysOnTop = true;
        Invoke(form, "MaintainAlwaysOnTop", true, true, covered, recordRepair);
        form.Enabled = false;
        Invoke(form, "MaintainAlwaysOnTop", true, false, covered, recordRepair);
        form.Enabled = true;
        Set(form, "mouseDownScreen", new Point(100, 100));
        Invoke(form, "MaintainAlwaysOnTop", true, false, covered, recordRepair);
        Set(form, "mouseDownScreen", null!);
        Set(form, "exiting", true);
        Invoke(form, "MaintainAlwaysOnTop", true, false, covered, recordRepair);
        Set(form, "exiting", false);
        form.WindowState = FormWindowState.Minimized;
        Invoke(form, "MaintainAlwaysOnTop", true, false, covered, recordRepair);
        form.WindowState = FormWindowState.Normal;
        Check("Recovery pauses for menus, dialogs, drags, exit and minimization", probes == 0 && repairs == 0);
        Invoke(form, "MaintainAlwaysOnTop", true, false, (Func<bool>)(() => false), recordRepair);
        Check("Healthy window order is left alone", repairs == 0);
        Invoke(form, "MaintainAlwaysOnTop", true, false, covered, recordRepair);
        Check("Coverage triggers repair even when the topmost preference remains on", probes == 1 && repairs == 1);

        var widgetRect = new Rectangle(100, 100, 290, 100);
        var overlappingRect = new Rectangle(150, 120, 500, 400);
        var separateRect = new Rectangle(600, 100, 100, 100);
        Check("Ordinary overlapping window above needs recovery", Occludes(widgetRect, overlappingRect, true, false, false));
        Check("Other topmost windows are not fought", !Occludes(widgetRect, overlappingRect, true, true, false));
        Check("Hidden and inactive-desktop windows are ignored", !Occludes(widgetRect, overlappingRect, false, false, false) &&
            !Occludes(widgetRect, overlappingRect, true, false, true));
        Check("Separate and edge-touching windows do not trigger recovery", !Occludes(widgetRect, separateRect, true, false, false) &&
            !Occludes(widgetRect, new Rectangle(widgetRect.Right, 100, 100, 100), true, false, false));

        form.TopMost = false;
        Check("Native detector recognizes a lost topmost style", (bool)Invoke(form, "NeedsTopmostRepair")!);
        var foregroundBeforeRepair = GetForegroundWindow();
        var boundsBeforeRepair = form.Bounds;
        Invoke(form, "MaintainAlwaysOnTop", true, false,
            (Func<bool>)(() => (bool)Invoke(form, "NeedsTopmostRepair")!),
            (Action)(() => Invoke(form, "ApplyAlwaysOnTopState")));
        Check("Native repair reapplies actual topmost style", form.TopMost && (GetWindowStyle(recoveryHandle, -20) & 8) != 0);
        Check("Native repair preserves focus, position, size and hidden state", GetForegroundWindow() == foregroundBeforeRepair &&
            form.Bounds == boundsBeforeRepair && !form.Visible);
        preferences.AlwaysOnTop = false;
        Invoke(form, "ApplyAlwaysOnTopState");
        Check("Turning always-on-top off removes the native style", !form.TopMost && (GetWindowStyle(recoveryHandle, -20) & 8) == 0);

        Check("Missing taskbar setting uses off default", !System.Text.Json.JsonSerializer
            .Deserialize<AppSettings>("{\"AlwaysOnTop\":false}")!.ShowInTaskbar);
        Check("Explicit saved choices override the defaults", System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            "{\"AlwaysOnTop\":false,\"ExpandOnHover\":true,\"ShowInTaskbar\":true}") is
            { AlwaysOnTop: false, ExpandOnHover: true, ShowInTaskbar: true });
        var taskbarPosition = form.Location;
        var taskbarSize = form.Size;
        preferences.ShowInTaskbar = false;
        Invoke(form, "ApplyTaskbarVisibility", true);
        Check("Taskbar off applies while widget is intended visible", !form.ShowInTaskbar);
        Check("Taskbar setting preserves widget geometry and face", form.Location == taskbarPosition &&
            form.Size == taskbarSize && !(bool)Get(form, "showResetDetails")!);
        Check("Taskbar off survives serialization", !System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(preferences))!.ShowInTaskbar);
        Invoke(form, "ApplyTaskbarVisibility", false);
        Invoke(form, "ApplyTaskbarVisibility", true);
        Check("Restoring widget respects taskbar off", !form.ShowInTaskbar);
        preferences.ShowInTaskbar = true;
        Invoke(form, "ApplyTaskbarVisibility", false);
        Check("Enabling taskbar while hidden keeps its button hidden", !form.ShowInTaskbar);
        Invoke(form, "ApplyTaskbarVisibility", true);
        Check("Restoring widget respects taskbar on", form.ShowInTaskbar);

        using (var taskbarOffForm = new WidgetForm(new AppSettings
        {
            X = 100, Y = 100, ShowInTaskbar = false, AlwaysOnTop = false, NotifyOnUsageLimitReached = false
        }))
        {
            ((System.Windows.Forms.Timer)Get(taskbarOffForm, "refreshTimer")!).Stop();
            ((System.Windows.Forms.Timer)Get(taskbarOffForm, "positionSaveTimer")!).Stop();
            ((System.Windows.Forms.Timer)Get(taskbarOffForm, "topmostTimer")!).Stop();
            ((NotifyIcon)Get(taskbarOffForm, "trayIcon")!).Visible = false;
            var taskbarOption = taskbarOffForm.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>()
                .Single(item => item.Name == "showInTaskbar");
            Check("Saved off preference applies at startup and in menu", !taskbarOffForm.ShowInTaskbar &&
                taskbarOption.CheckOnClick && !taskbarOption.Checked && taskbarOption.Text == "Show in taskbar — Off");
        }

        ((System.Windows.Forms.Timer)Get(form, "positionSaveTimer")!).Stop();
        Click(form, 242, 17);
        Check("Minimise control does not flip face", !(bool)Get(form, "showResetDetails")! && !form.ShowInTaskbar);
        Click(form, 270, 17);
        Check("Close control keeps app in tray", !(bool)Get(form, "showResetDetails")! && !form.IsDisposed);

        Console.WriteLine($"PASS: {passed} checks. Rendered fixtures: {output ?? "not requested"}");
    }

    private static void SetSnapshot(WidgetForm form, double used, double daysLeft, int resets)
    {
        Set(form, "snapshot", new UsageSnapshot(used, Now.AddDays(daysLeft), Now,
            Enumerable.Range(1, resets).Select(i => Now.AddDays(14 + i * 7)).ToArray()));
        Set(form, "liveConnected", true);
        Invoke(form, "UpdatePace");
        Invoke(form, "UpdateFaceSize");
    }

    private static void Click(WidgetForm form, int x, int y)
    {
        var scale = form.DeviceDpi / 96f;
        var args = new MouseEventArgs(MouseButtons.Left, 1, (int)(x * scale), (int)(y * scale), 0);
        Invoke(form, "OnMouseDown", args);
        Invoke(form, "OnMouseUp", args);
    }

    private static void HoverTick(WidgetForm form, bool pointerInside, bool menuVisible = false)
    {
        ((System.Windows.Forms.Timer)Get(form, "hoverTimer")!).Stop();
        Invoke(form, "ProcessHoverTick", pointerInside, menuVisible);
    }

    private static void EnterBody(WidgetForm form)
    {
        Invoke(form, "OnMouseLeave", EventArgs.Empty);
        MovePointer(form, 40, 70);
    }

    private static void MovePointer(WidgetForm form, int x, int y)
    {
        var scale = form.DeviceDpi / 96f;
        Invoke(form, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, (int)(x * scale), (int)(y * scale), 0));
    }

    private static void Save(WidgetForm form, string? path, string name)
    {
        if (path is null) return;
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(path, name + ".png"), ImageFormat.Png);
    }

    private static bool Near(double? value, double expected) => value.HasValue && Math.Abs(value.Value - expected) < 0.00001;
    private static bool Occludes(Rectangle widget, Rectangle other, bool visible, bool topmost, bool cloaked) =>
        (bool)typeof(WidgetForm).GetMethod("IsOrdinaryOccluder", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { widget, other, visible, topmost, cloaked })!;
    private static void Check(string label, bool success)
    {
        if (!success) throw new InvalidOperationException("FAIL: " + label);
        passed++;
        Console.WriteLine("PASS: " + label);
    }
    private static object? Get(object instance, string name) => typeof(WidgetForm)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    private static void Set(object instance, string name, object value) => typeof(WidgetForm)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    private static object? Invoke(object instance, string name, params object[] args) => typeof(WidgetForm)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
}
