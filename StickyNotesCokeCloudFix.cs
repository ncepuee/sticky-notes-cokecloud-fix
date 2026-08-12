using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Reflection;

[assembly: AssemblyTitle("Sticky Notes CokeCloud Fix")]
[assembly: AssemblyDescription("Diagnostics and reversible proxy repair tool for Microsoft Sticky Notes on Windows")]
[assembly: AssemblyProduct("Sticky Notes CokeCloud Fix")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

internal sealed class ProxyState
{
    public int Enable;
    public string Server = "";
    public string Override = "";
}

internal sealed class CokeState
{
    public string Mode = "";
    public string Port = "";
    public string Connected = "";
    public int Processes;
}

internal sealed class NoteEvent
{
    public string Time = "";
    public string Namespace = "";
    public string Event = "";
    public string Code = "";
}

internal sealed class NotesState
{
    public bool Installed;
    public string Version = "";
    public string Log = "";
    public string LogTime = "";
    public string Opened = "";
    public string Updated = "";
    public string Failure = "";
    public string Code = "";
    public readonly List<NoteEvent> Events = new List<NoteEvent>();
}

internal static class AppData
{
    public const string AppId = "Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App";
    public const string PackageName = "Microsoft.MicrosoftStickyNotes";
    public static readonly string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    public static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe\LocalState\DiagOutputDir");
    public static readonly string CokeState = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"cokecloud\vortex.json");
    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyNotes-CokeCloud-Fix");
    public static readonly string RollbackFile = Path.Combine(DataDirectory, "rollback-state.json");
}

internal static class Diagnostics
{
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);

    public static ProxyState GetProxyState()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppData.RegistryPath, false))
        {
            if (key == null) return new ProxyState();
            return new ProxyState
            {
                Enable = Convert.ToInt32(key.GetValue("ProxyEnable", 0)),
                Server = Convert.ToString(key.GetValue("ProxyServer", "")) ?? "",
                Override = Convert.ToString(key.GetValue("ProxyOverride", "")) ?? ""
            };
        }
    }

    public static void SetProxyState(ProxyState state)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppData.RegistryPath, true))
        {
            if (key == null) throw new InvalidOperationException("Windows Internet Settings registry key was not found.");
            key.SetValue("ProxyEnable", state.Enable, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", state.Server ?? "", RegistryValueKind.String);
            key.SetValue("ProxyOverride", state.Override ?? "", RegistryValueKind.String);
        }
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    public static void SetProxyEnabled(int enabled)
    {
        ProxyState state = GetProxyState();
        state.Enable = enabled;
        SetProxyState(state);
    }

    public static CokeState GetCokeState()
    {
        CokeState result = new CokeState
        {
            Processes = Process.GetProcessesByName("CokeCloud").Length
        };
        if (File.Exists(AppData.CokeState))
        {
            string text = File.ReadAllText(AppData.CokeState);
            result.Mode = JsonField(text, "proxy_mode");
            result.Port = JsonField(text, "proxy_port");
            result.Connected = JsonField(text, "connected");
        }
        return result;
    }

    public static NotesState GetNotesState()
    {
        NotesState result = new NotesState { Installed = Directory.Exists(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe")) };
        result.Version = FindStickyNotesVersion();
        if (!Directory.Exists(AppData.LogDirectory)) return result;

        FileInfo latest = null;
        DirectoryInfo directory = new DirectoryInfo(AppData.LogDirectory);
        foreach (FileInfo file in directory.GetFiles("*.txt"))
        {
            if (latest == null || file.LastWriteTimeUtc > latest.LastWriteTimeUtc) latest = file;
        }
        if (latest == null) return result;
        result.Log = latest.FullName;
        result.LogTime = latest.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

        foreach (string line in ReadLinesShared(latest.FullName))
        {
            try
            {
                string json = line.Trim().TrimEnd(',');
                string eventName = JsonField(json, "EventName");
                if (String.IsNullOrEmpty(eventName)) continue;
                result.Events.Add(new NoteEvent
                {
                    Time = JsonField(json, "Time"),
                    Namespace = JsonField(json, "Namespace"),
                    Event = eventName,
                    Code = JsonField(json, "Code")
                });
            }
            catch { }
        }
        if (result.Events.Count > 12)
            result.Events.RemoveRange(0, result.Events.Count - 12);

        for (int i = result.Events.Count - 1; i >= 0; i--)
        {
            NoteEvent item = result.Events[i];
            if (String.IsNullOrEmpty(result.Opened) && item.Event == "RealTimeConnectionOpened") result.Opened = item.Time;
            if (String.IsNullOrEmpty(result.Updated) && item.Event == "NoteContentUpdated") result.Updated = item.Time;
            if (String.IsNullOrEmpty(result.Failure) && (item.Event.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Event.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 || item.Event == "SyncRequestFailed"))
            {
                result.Failure = item.Time + " " + item.Event;
                result.Code = item.Code;
            }
        }
        return result;
    }

    private static string FindStickyNotesVersion()
    {
        const string packagesPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(packagesPath, false))
            {
                if (key == null) return "";
                foreach (string name in key.GetSubKeyNames())
                {
                    Match match = Regex.Match(name, @"^Microsoft\.MicrosoftStickyNotes_([^_]+)_");
                    if (match.Success) return match.Groups[1].Value;
                }
            }
        }
        catch { }
        return "";
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
        {
            while (!reader.EndOfStream) yield return reader.ReadLine();
        }
    }

    public static string JsonField(string text, string name)
    {
        Match match = Regex.Match(text ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*(?:\"([^\"]*)\"|(-?\\d+(?:\\.\\d+)?)|(true|false))");
        if (!match.Success) return "";
        for (int i = 1; i < match.Groups.Count; i++)
            if (match.Groups[i].Success) return match.Groups[i].Value;
        return "";
    }

    public static void SaveRollback()
    {
        Directory.CreateDirectory(AppData.DataDirectory);
        ProxyState state = GetProxyState();
        string json = "{\"SavedAt\":\"" + Escape(DateTime.Now.ToString("o")) + "\",\"ProxyEnable\":" + state.Enable +
            ",\"ProxyServer\":\"" + Escape(state.Server) + "\",\"ProxyOverride\":\"" + Escape(state.Override) + "\"}";
        File.WriteAllText(AppData.RollbackFile, json, new UTF8Encoding(false));
    }

    public static void RestoreRollback()
    {
        if (!File.Exists(AppData.RollbackFile)) throw new FileNotFoundException("No rollback point exists.");
        string text = File.ReadAllText(AppData.RollbackFile);
        SetProxyState(new ProxyState
        {
            Enable = ToInt(JsonField(text, "ProxyEnable")),
            Server = JsonField(text, "ProxyServer"),
            Override = JsonField(text, "ProxyOverride")
        });
    }

    private static int ToInt(string value)
    {
        int result;
        return Int32.TryParse(value, out result) ? result : 0;
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    public static void RestartStickyNotes()
    {
        foreach (Process process in Process.GetProcessesByName("Microsoft.Notes"))
        {
            try { process.Kill(); } catch { }
        }
        System.Threading.Thread.Sleep(1500);
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + AppData.AppId) { UseShellExecute = true });
    }

    public static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
    }
}

internal sealed class MainForm : Form
{
    private readonly Dictionary<string, Label> labels = new Dictionary<string, Label>();
    private readonly RichTextBox logBox = new RichTextBox();

    public MainForm()
    {
        Text = "Sticky Notes Sync Fix - CokeCloud";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(930, 690);
        MinimumSize = new Size(850, 600);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        Label header = new Label { Text = "Windows Sticky Notes Sync Fix", AutoSize = true, Location = new Point(20, 15), Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold) };
        Controls.Add(header);
        Label subtitle = new Label { Text = "Recommended: CokeCloud can stay running; Windows system proxy stays OFF for Sticky Notes.", AutoSize = true, Location = new Point(22, 50), ForeColor = Color.DimGray };
        Controls.Add(subtitle);

        GroupBox group = new GroupBox { Text = "Current status", Location = new Point(20, 78), Size = new Size(875, 185) };
        Controls.Add(group);
        TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 7 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.Controls.Add(table);
        AddStatusRow(table, 0, "Windows system proxy", "Proxy");
        AddStatusRow(table, 1, "CokeCloud mode", "CokeMode");
        AddStatusRow(table, 2, "CokeCloud connected", "CokeConnected");
        AddStatusRow(table, 3, "CokeCloud processes", "CokeProcesses");
        AddStatusRow(table, 4, "Sticky Notes version", "NotesVersion");
        AddStatusRow(table, 5, "Sync channel", "Sync");
        AddStatusRow(table, 6, "Last content update", "Updated");

        FlowLayoutPanel buttons = new FlowLayoutPanel { Location = new Point(20, 275), Size = new Size(875, 90) };
        Controls.Add(buttons);
        Button refresh = AddButton(buttons, "Refresh status", 110);
        Button fix = AddButton(buttons, "One-click fix (recommended)", 195);
        Button restart = AddButton(buttons, "Restart Sticky Notes", 145);
        Button save = AddButton(buttons, "Save rollback point", 140);
        Button restore = AddButton(buttons, "Restore proxy state", 140);
        Button logs = AddButton(buttons, "Open log folder", 120);
        Button data = AddButton(buttons, "Open app data", 120);

        GroupBox logGroup = new GroupBox { Text = "Operation log / diagnostic summary", Location = new Point(20, 375), Size = new Size(875, 245) };
        Controls.Add(logGroup);
        logBox.Dock = DockStyle.Fill;
        logBox.ReadOnly = true;
        logBox.BackColor = Color.White;
        logBox.BorderStyle = BorderStyle.None;
        logBox.Font = new Font("Consolas", 9F);
        logGroup.Controls.Add(logBox);

        refresh.Click += delegate { RefreshUi(); };
        fix.Click += delegate { OneClickFix(); };
        restart.Click += delegate { RestartNotes(); };
        save.Click += delegate { SaveRollback(); };
        restore.Click += delegate { RestoreProxy(); };
        logs.Click += delegate { Diagnostics.OpenFolder(AppData.LogDirectory); };
        data.Click += delegate { Diagnostics.OpenFolder(AppData.DataDirectory); };
        Shown += delegate { RefreshUi(); };
    }

    private void AddStatusRow(TableLayoutPanel table, int row, string name, string key)
    {
        Label nameLabel = new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
        Label valueLabel = new Label { Text = "-", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        labels[key] = valueLabel;
        table.Controls.Add(nameLabel, 0, row);
        table.Controls.Add(valueLabel, 1, row);
    }

    private static Button AddButton(FlowLayoutPanel panel, string text, int width)
    {
        Button button = new Button { Text = text, Width = width, Height = 34, Margin = new Padding(4) };
        panel.Controls.Add(button);
        return button;
    }

    private void RefreshUi()
    {
        try
        {
            ProxyState proxy = Diagnostics.GetProxyState();
            CokeState coke = Diagnostics.GetCokeState();
            NotesState notes = Diagnostics.GetNotesState();
            labels["Proxy"].Text = proxy.Enable == 0 ? "OFF (direct for Sticky Notes)" : "ON -> " + proxy.Server;
            labels["CokeMode"].Text = String.IsNullOrEmpty(coke.Mode) ? "unknown" : coke.Mode;
            labels["CokeConnected"].Text = String.IsNullOrEmpty(coke.Connected) ? "unknown" : coke.Connected;
            labels["CokeProcesses"].Text = coke.Processes.ToString();
            labels["NotesVersion"].Text = String.IsNullOrEmpty(notes.Version) ? "not detected" : notes.Version;
            labels["Sync"].Text = !String.IsNullOrEmpty(notes.Opened) ? "opened: " + notes.Opened :
                (!String.IsNullOrEmpty(notes.Failure) ? "failed: " + notes.Failure + " " + notes.Code : "no clear channel evidence");
            labels["Updated"].Text = String.IsNullOrEmpty(notes.Updated) ? "none" : notes.Updated;
            labels["Proxy"].ForeColor = proxy.Enable == 0 ? Color.ForestGreen : Color.DarkOrange;
            labels["Sync"].ForeColor = !String.IsNullOrEmpty(notes.Opened) ? Color.ForestGreen :
                (!String.IsNullOrEmpty(notes.Failure) ? Color.Crimson : Color.DimGray);

            logBox.Clear();
            WriteLog("ProxyEnable=" + proxy.Enable + "; ProxyServer=" + proxy.Server);
            WriteLog("CokeCloud mode=" + coke.Mode + "; connected=" + coke.Connected + "; processes=" + coke.Processes);
            if (!String.IsNullOrEmpty(notes.Log)) WriteLog("Latest log=" + notes.Log);
            foreach (NoteEvent item in notes.Events)
                WriteLog(item.Time + " " + item.Namespace + "/" + item.Event + (String.IsNullOrEmpty(item.Code) ? "" : " code=" + item.Code));
        }
        catch (Exception ex) { WriteLog("Refresh failed: " + ex.Message); }
    }

    private void OneClickFix()
    {
        if (MessageBox.Show("This saves a rollback point, turns OFF the Windows system proxy, and restarts Sticky Notes. CokeCloud core will not be closed. Continue?", "Confirm one-click fix", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            if (!File.Exists(AppData.RollbackFile)) Diagnostics.SaveRollback();
            Diagnostics.SetProxyEnabled(0);
            Diagnostics.RestartStickyNotes();
            WriteLog("System proxy disabled and Sticky Notes restarted.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fix failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void RestartNotes()
    {
        try { Diagnostics.RestartStickyNotes(); WriteLog("Sticky Notes restarted."); }
        catch (Exception ex) { WriteLog("Restart failed: " + ex.Message); }
        RefreshUi();
    }

    private void SaveRollback()
    {
        try { Diagnostics.SaveRollback(); WriteLog("Rollback point saved."); MessageBox.Show("Current proxy state was saved.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void RestoreProxy()
    {
        if (!File.Exists(AppData.RollbackFile)) { MessageBox.Show("No rollback point. Save one first.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show("Restore the saved proxy state? This may turn the Windows system proxy back ON.", "Confirm restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { Diagnostics.RestoreRollback(); WriteLog("Proxy state restored."); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void WriteLog(string text)
    {
        logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
        logBox.ScrollToCaret();
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && (String.Equals(args[0], "--check-only", StringComparison.OrdinalIgnoreCase) || String.Equals(args[0], "--check-only-text", StringComparison.OrdinalIgnoreCase)))
        {
            ProxyState proxy = Diagnostics.GetProxyState();
            CokeState coke = Diagnostics.GetCokeState();
            NotesState notes = Diagnostics.GetNotesState();
            string summary = "Proxy: " + (proxy.Enable == 0 ? "OFF (direct for Sticky Notes)" : "ON -> " + proxy.Server) + Environment.NewLine +
                "CokeCloud mode: " + coke.Mode + Environment.NewLine +
                "CokeCloud connected: " + coke.Connected + Environment.NewLine +
                "CokeCloud processes: " + coke.Processes + Environment.NewLine +
                "Sticky Notes version: " + notes.Version + Environment.NewLine +
                "Sync opened: " + notes.Opened + Environment.NewLine +
                "Last content update: " + notes.Updated + Environment.NewLine +
                "Latest failure: " + notes.Failure + " " + notes.Code;
            if (String.Equals(args[0], "--check-only-text", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(summary);
            else
            {
                Application.EnableVisualStyles();
                MessageBox.Show(summary, "Sticky Notes read-only status", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
