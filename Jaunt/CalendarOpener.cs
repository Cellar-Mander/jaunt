using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Jaunt.Core.Browser;

namespace Jaunt;

/// <summary>
/// Adds an opening to the user's calendar: writes an <c>.ics</c> and opens it with the system's handler,
/// or opens Google Calendar's or Outlook.com's new-event page. File work runs off the draw thread.
/// </summary>
internal sealed class CalendarOpener(string folder, INotificationManager notifications, IPluginLog log)
{
    /// <summary>Each format and its label, in the order Settings and the button's menu list them.</summary>
    public static readonly (CalendarFormat Format, string Label)[] Formats =
    [
        (CalendarFormat.Ics, "Calendar app (.ics)"),
        (CalendarFormat.Google, "Google Calendar"),
        (CalendarFormat.Outlook, "Outlook.com"),
    ];

    /// <summary>How long a written <c>.ics</c> is kept for the calendar app to read before the next export removes it.</summary>
    private static readonly TimeSpan KeepFiles = TimeSpan.FromDays(1);

    public static string Label(CalendarFormat format)
    {
        foreach (var (option, label) in Formats)
        {
            if (option == format)
                return label;
        }

        return Formats[0].Label;
    }

    public void Add(CalendarEvent calendarEvent, CalendarFormat format)
    {
        switch (format)
        {
            case CalendarFormat.Google:
                Util.OpenLink(CalendarExport.GoogleLink(calendarEvent).AbsoluteUri);
                break;
            case CalendarFormat.Outlook:
                Util.OpenLink(CalendarExport.OutlookLink(calendarEvent).AbsoluteUri);
                break;
            default:
                var ics = CalendarExport.Ics(calendarEvent, DateTimeOffset.UtcNow);
                _ = Task.Run(() => OpenIcs(calendarEvent, ics));
                break;
        }
    }

    /// <summary>
    /// Writes the file and opens it with the system's handler. If that fails, as it may under Wine with no
    /// handler for <c>.ics</c>, opens Google Calendar's page instead and says so.
    /// </summary>
    private void OpenIcs(CalendarEvent calendarEvent, string ics)
    {
        try
        {
            Directory.CreateDirectory(folder);
            RemoveOld();
            var path = Path.Combine(folder, CalendarExport.FileName(calendarEvent));
            File.WriteAllText(path, ics);
            if (Util.IsWine())
                OpenUnderWine(path);
            else
                using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Warning(e, "Couldn't open the .ics file");
            Util.OpenLink(CalendarExport.GoogleLink(calendarEvent).AbsoluteUri);
            notifications.AddNotification(new Notification
            {
                Title = "Jaunt",
                Content = Util.IsWine()
                    ? "No app opened the calendar file under Wine, so Jaunt opened Google Calendar instead. Choose Google Calendar or Outlook.com in Settings → Calendar to skip this."
                    : "No app opened the calendar file, so Jaunt opened Google Calendar instead. Choose a link format in Settings → Calendar to skip this.",
                MinimizedText = "Opened Google Calendar instead",
                Type = NotificationType.Warning,
            });
        }
    }

    /// <summary>
    /// Wine has no handler for <c>.ics</c>, so ShellExecute on the file fails ("Invalid handle", seen
    /// 2026-11-22). Its <c>winebrowser</c> hands a <c>file:</c> URL to the host's <c>xdg-open</c> (or
    /// <c>open</c> on macOS), which opens it with the host's calendar app.
    /// </summary>
    /// <exception cref="InvalidOperationException">winebrowser didn't start, or reported that nothing opened the file.</exception>
    private static void OpenUnderWine(string path)
    {
        var winebrowser = Path.Combine(Environment.SystemDirectory, "winebrowser.exe");
        using var process = Process.Start(new ProcessStartInfo(winebrowser, new Uri(path).AbsoluteUri) { UseShellExecute = false })
            ?? throw new InvalidOperationException("winebrowser didn't start");
        // It exits once the host's opener has started; still running after 10 seconds counts as started
        if (process.WaitForExit(TimeSpan.FromSeconds(10)) && process.ExitCode != 0)
            throw new InvalidOperationException($"winebrowser exited with {process.ExitCode}");
    }

    private void RemoveOld()
    {
        var cutoff = DateTime.UtcNow - KeepFiles;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("jaunt-*.ics"))
        {
            if (file.LastWriteTimeUtc < cutoff)
                file.Delete();
        }
    }
}
