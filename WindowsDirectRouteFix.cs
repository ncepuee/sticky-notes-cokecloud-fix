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

[assembly: AssemblyTitle("Windows Direct Route Fix")]
[assembly: AssemblyDescription("Reversible direct-route diagnostics for Windows desktop applications")]
[assembly: AssemblyProduct("Windows Direct Route Fix")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

internal sealed class ProxyState
{
    public int Enable;
    public string Server = "";
    public string Override = "";
}

internal sealed class RouteEvent
{
    public string Time = "";
    public string Namespace = "";
    public string Name = "";
    public string Code = "";
}

internal sealed class TargetState
{
    public string Version = "";
    public string Log = "";
    public string LastOpened = "";
    public string LastUpdated = "";
    public string LastFailure = "";
    public string LastCode = "";
    public readonly List<RouteEvent> Events = new List<RouteEvent>();
}

internal sealed class TargetProfile
{
    public string Name = "Microsoft Sticky Notes";
    public string AppId = "Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App";
    public string PackageName = "Microsoft.MicrosoftStickyNotes";
    public string ProcessName = "Microsoft.Notes";
    public string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe\LocalState\DiagOutputDir");
    public string[] DirectDomains = new[]
    {
        "substrate.office.com",
        "graph.microsoft.com",
        "outlook.office365.com",
        "login.live.com",
        "login.microsoftonline.com"
    };
}

internal static class ProfileStore
{
    public static string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsDirectRouteFix", "profile.json");

    public static void Load(TargetProfile profile)
    {
        if (!File.Exists(FilePath)) return;
        string json = File.ReadAllText(FilePath);
        string appId = RouteEngine.JsonField(json, "AppId");
        string logDirectory = RouteEngine.JsonField(json, "LogDirectory");
        string packageName = RouteEngine.JsonField(json, "PackageName");
        string processName = RouteEngine.JsonField(json, "ProcessName");
        string name = RouteEngine.JsonField(json, "Name");
        string domains = RouteEngine.JsonField(json, "DirectDomains");
        if (name.Length > 0) profile.Name = Unescape(name);
        if (appId.Length > 0) profile.AppId = Unescape(appId);
        if (logDirectory.Length > 0) profile.LogDirectory = Unescape(logDirectory);
        if (packageName.Length > 0) profile.PackageName = Unescape(packageName);
        if (processName.Length > 0) profile.ProcessName = Unescape(processName);
        if (domains.Length > 0) profile.DirectDomains = Unescape(domains).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public static void Save(TargetProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        string json = "{\"Name\":\"" + Escape(profile.Name) + "\",\"AppId\":\"" + Escape(profile.AppId) +
            "\",\"PackageName\":\"" + Escape(profile.PackageName) + "\",\"ProcessName\":\"" + Escape(profile.ProcessName) + "\",\"LogDirectory\":\"" + Escape(profile.LogDirectory) + "\",\"DirectDomains\":\"" +
            Escape(String.Join(";", profile.DirectDomains)) + "\"}";
        File.WriteAllText(FilePath, json, new UTF8Encoding(false));
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static string Unescape(string value)
    {
        return (value ?? "").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}

internal static class RouteEngine
{
    private const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);

    public static ProxyState ReadProxy()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettings, false))
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

    public static void WriteProxy(ProxyState state)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettings, true))
        {
            if (key == null) throw new InvalidOperationException("The current-user WinINet settings key was not found.");
            key.SetValue("ProxyEnable", state.Enable, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", state.Server ?? "", RegistryValueKind.String);
            key.SetValue("ProxyOverride", state.Override ?? "", RegistryValueKind.String);
        }
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    public static string AddDirectDomains(ProxyState current, IEnumerable<string> domains)
    {
        List<string> entries = new List<string>();
        foreach (string item in (current.Override ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            entries.Add(item.Trim());
        foreach (string domain in domains)
        {
            string value = (domain ?? "").Trim();
            if (value.Length == 0) continue;
            bool exists = false;
            foreach (string entry in entries)
                if (String.Equals(entry, value, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
            if (!exists) entries.Add(value);
        }
        return String.Join(";", entries.ToArray());
    }

    public static void ApplyDirectDomains(IEnumerable<string> domains)
    {
        ProxyState state = ReadProxy();
        state.Override = AddDirectDomains(state, domains);
        WriteProxy(state);
    }

    public static void ApplyBroadFallback()
    {
        ProxyState state = ReadProxy();
        state.Enable = 0;
        WriteProxy(state);
    }

    public static void SaveRollback(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        ProxyState state = ReadProxy();
        string json = "{\"SavedAt\":\"" + Escape(DateTime.Now.ToString("o")) + "\",\"ProxyEnable\":" + state.Enable +
            ",\"ProxyServer\":\"" + Escape(state.Server) + "\",\"ProxyOverride\":\"" + Escape(state.Override) + "\"}";
        File.WriteAllText(file, json, new UTF8Encoding(false));
    }

    public static void RestoreRollback(string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("No rollback point exists.");
        string json = File.ReadAllText(file);
        WriteProxy(new ProxyState
        {
            Enable = IntValue(JsonField(json, "ProxyEnable")),
            Server = JsonField(json, "ProxyServer"),
            Override = JsonField(json, "ProxyOverride")
        });
    }

    public static TargetState ReadTarget(TargetProfile profile)
    {
        TargetState result = new TargetState();
        result.Version = FindPackageVersion(profile.PackageName);
        if (!Directory.Exists(profile.LogDirectory)) return result;
        FileInfo latest = null;
        foreach (FileInfo file in new DirectoryInfo(profile.LogDirectory).GetFiles("*.txt"))
            if (latest == null || file.LastWriteTimeUtc > latest.LastWriteTimeUtc) latest = file;
        if (latest == null) return result;
        result.Log = latest.FullName;
        foreach (string line in ReadLinesShared(latest.FullName))
        {
            try
            {
                string json = line.Trim().TrimEnd(',');
                string eventName = JsonField(json, "EventName");
                if (eventName.Length == 0) continue;
                result.Events.Add(new RouteEvent
                {
                    Time = JsonField(json, "Time"),
                    Namespace = JsonField(json, "Namespace"),
                    Name = eventName,
                    Code = JsonField(json, "Code")
                });
            }
            catch { }
        }
        if (result.Events.Count > 16) result.Events.RemoveRange(0, result.Events.Count - 16);
        for (int i = result.Events.Count - 1; i >= 0; i--)
        {
            RouteEvent item = result.Events[i];
            if (result.LastOpened.Length == 0 && item.Name == "RealTimeConnectionOpened") result.LastOpened = item.Time;
            if (result.LastUpdated.Length == 0 && item.Name == "NoteContentUpdated") result.LastUpdated = item.Time;
            if (result.LastFailure.Length == 0 && (item.Name.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Name.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 || item.Name == "SyncRequestFailed"))
            {
                result.LastFailure = item.Time + " " + item.Name;
                result.LastCode = item.Code;
            }
        }
        return result;
    }

    public static void RestartTarget(TargetProfile profile)
    {
        string processName = Path.GetFileNameWithoutExtension(profile.ProcessName ?? "");
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try { process.Kill(); } catch { }
        }
        System.Threading.Thread.Sleep(1200);
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + profile.AppId) { UseShellExecute = true });
    }

    public static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
    }

    private static string FindPackageVersion(string packageName)
    {
        const string packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(packages, false))
            {
                if (key == null) return "";
                foreach (string name in key.GetSubKeyNames())
                {
                    if (packageName.Length == 0) continue;
                    Match match = Regex.Match(name, "^" + Regex.Escape(packageName) + @"_([^_]+)_");
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
        for (int i = 1; i < match.Groups.Count; i++) if (match.Groups[i].Success) return match.Groups[i].Value;
        return "";
    }

    private static int IntValue(string value)
    {
        int result;
        return Int32.TryParse(value, out result) ? result : 0;
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}

internal sealed class MainForm : Form
{
    private readonly TargetProfile profile = new TargetProfile();
    private readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
    private readonly TextBox domainsBox = new TextBox();
    private readonly TextBox appIdBox = new TextBox();
    private readonly TextBox packageBox = new TextBox();
    private readonly TextBox processBox = new TextBox();
    private readonly TextBox logDirBox = new TextBox();
    private readonly RichTextBox logBox = new RichTextBox();
    private readonly string rollbackFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsDirectRouteFix", "rollback-state.json");

    public MainForm()
    {
        ProfileStore.Load(profile);
        Text = "Windows Direct Route Fix";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 780);
        MinimumSize = new Size(900, 680);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        Controls.Add(new Label { Text = "Windows Direct Route Fix", AutoSize = true, Location = new Point(20, 15), Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold) });
        Controls.Add(new Label { Text = "General-purpose direct-route helper: target app + domains + reversible WinINet policy.", AutoSize = true, Location = new Point(22, 50), ForeColor = Color.DimGray });

        GroupBox target = new GroupBox { Text = "Target profile", Location = new Point(20, 78), Size = new Size(940, 237) };
        Controls.Add(target);
        AddField(target, "Target name", profile.Name, 15, 25, 120, 790, delegate(string value) { profile.Name = value; });
        appIdBox.Text = profile.AppId;
        AddControl(target, "App launch ID", appIdBox, 15, 57, 120, 790);
        packageBox.Text = profile.PackageName;
        AddControl(target, "Package name", packageBox, 15, 89, 120, 790);
        processBox.Text = profile.ProcessName;
        AddControl(target, "Process name", processBox, 15, 121, 120, 790);
        logDirBox.Text = profile.LogDirectory;
        AddControl(target, "Log directory", logDirBox, 15, 153, 120, 790);
        domainsBox.Multiline = true;
        domainsBox.ScrollBars = ScrollBars.Vertical;
        domainsBox.Text = String.Join(";", profile.DirectDomains);
        AddControl(target, "Direct domains", domainsBox, 15, 185, 120, 790);

        GroupBox status = new GroupBox { Text = "Observed state", Location = new Point(20, 327), Size = new Size(940, 145) };
        Controls.Add(status);
        TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        status.Controls.Add(table);
        AddStatus(table, 0, "WinINet system proxy", "Proxy");
        AddStatus(table, 1, "Effective route scope", "Scope");
        AddStatus(table, 2, "Target version", "Version");
        AddStatus(table, 3, "Sync channel evidence", "Sync");
        AddStatus(table, 4, "Last content update", "Updated");

        FlowLayoutPanel buttons = new FlowLayoutPanel { Location = new Point(20, 487), Size = new Size(940, 95) };
        Controls.Add(buttons);
        AddButton(buttons, "Save profile", 110).Click += delegate { SaveProfile(); };
        AddButton(buttons, "Diagnose", 100).Click += delegate { RefreshUi(); };
        AddButton(buttons, "Apply direct domains", 155).Click += delegate { ApplyDirect(); };
        AddButton(buttons, "Broad fallback: proxy OFF", 180).Click += delegate { ApplyBroad(); };
        AddButton(buttons, "Save rollback", 120).Click += delegate { SaveRollback(); };
        AddButton(buttons, "Restore rollback", 130).Click += delegate { RestoreRollback(); };
        AddButton(buttons, "Restart target", 120).Click += delegate { RestartTarget(); };
        AddButton(buttons, "Open logs", 100).Click += delegate { RouteEngine.OpenFolder(logDirBox.Text.Trim()); };

        GroupBox output = new GroupBox { Text = "Policy explanation / diagnostic log", Location = new Point(20, 592), Size = new Size(940, 150) };
        Controls.Add(output);
        logBox.Dock = DockStyle.Fill;
        logBox.ReadOnly = true;
        logBox.BackColor = Color.White;
        logBox.BorderStyle = BorderStyle.None;
        logBox.Font = new Font("Consolas", 9F);
        output.Controls.Add(logBox);
        Shown += delegate { RefreshUi(); };
    }

    private void AddField(GroupBox box, string name, string initial, int x, int y, int labelWidth, int fieldWidth, Action<string> setter)
    {
        TextBox field = new TextBox { Text = initial, Location = new Point(x + labelWidth, y), Width = fieldWidth, Height = 22 };
        field.TextChanged += delegate { setter(field.Text); };
        AddControl(box, name, field, x, y, labelWidth, fieldWidth);
    }

    private static void AddControl(Control parent, string name, Control control, int x, int y, int labelWidth, int width)
    {
        parent.Controls.Add(new Label { Text = name, AutoSize = false, Width = labelWidth - 8, Height = 22, Location = new Point(x, y + 3), TextAlign = ContentAlignment.MiddleLeft });
        control.Location = new Point(x + labelWidth, y);
        control.Width = width;
        parent.Controls.Add(control);
    }

    private void AddStatus(TableLayoutPanel table, int row, string name, string key)
    {
        table.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) }, 0, row);
        Label value = new Label { Text = "-", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        values[key] = value;
        table.Controls.Add(value, 1, row);
    }

    private static Button AddButton(FlowLayoutPanel panel, string text, int width)
    {
        Button button = new Button { Text = text, Width = width, Height = 34, Margin = new Padding(4) };
        panel.Controls.Add(button);
        return button;
    }

    private string[] Domains()
    {
        List<string> result = new List<string>();
        foreach (string item in domainsBox.Text.Split(new[] { ';', '\r', '\n', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (item.Trim().Length > 0) result.Add(item.Trim());
        return result.ToArray();
    }

    private TargetProfile CurrentProfile()
    {
        profile.AppId = appIdBox.Text.Trim();
        profile.PackageName = packageBox.Text.Trim();
        profile.ProcessName = processBox.Text.Trim();
        profile.LogDirectory = logDirBox.Text.Trim();
        profile.DirectDomains = Domains();
        return profile;
    }

    private void RefreshUi()
    {
        try
        {
            TargetProfile current = CurrentProfile();
            ProxyState proxy = RouteEngine.ReadProxy();
            TargetState target = RouteEngine.ReadTarget(current);
            values["Proxy"].Text = proxy.Enable == 0 ? "OFF" : "ON -> " + proxy.Server;
            values["Scope"].Text = proxy.Enable == 0 ? "Direct for all WinINet apps" : "Proxy for WinINet; domain exceptions are global WinINet bypasses";
            values["Version"].Text = target.Version.Length == 0 ? "not detected" : target.Version;
            values["Sync"].Text = target.LastOpened.Length > 0 ? "opened: " + target.LastOpened :
                (target.LastFailure.Length > 0 ? "failed: " + target.LastFailure + " " + target.LastCode : "no clear evidence");
            values["Updated"].Text = target.LastUpdated.Length == 0 ? "none" : target.LastUpdated;
            values["Proxy"].ForeColor = proxy.Enable == 0 ? Color.ForestGreen : Color.DarkOrange;
            values["Sync"].ForeColor = target.LastOpened.Length > 0 ? Color.ForestGreen :
                (target.LastFailure.Length > 0 ? Color.Crimson : Color.DimGray);
            logBox.Clear();
            WriteLog("Target=" + current.Name + "; AppId=" + current.AppId);
            WriteLog("Policy seam=Windows current-user WinINet settings");
            WriteLog("ProxyEnable=" + proxy.Enable + "; ProxyServer=" + proxy.Server);
            WriteLog("Direct domains=" + String.Join(";", current.DirectDomains));
            if (target.Log.Length > 0) WriteLog("Latest log=" + target.Log);
            foreach (RouteEvent item in target.Events)
                WriteLog(item.Time + " " + item.Namespace + "/" + item.Name + (item.Code.Length == 0 ? "" : " code=" + item.Code));
        }
        catch (Exception ex) { WriteLog("Diagnose failed: " + ex.Message); }
    }

    private void ApplyDirect()
    {
        if (MessageBox.Show("This adds the listed domains to the Windows WinINet bypass list. It is domain-scoped, but the bypass list is shared by WinINet applications. Continue?", "Confirm direct-domain policy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { RouteEngine.SaveRollback(rollbackFile); RouteEngine.ApplyDirectDomains(Domains()); WriteLog("Applied direct-domain exceptions. Other WinINet apps may also use them."); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void ApplyBroad()
    {
        if (MessageBox.Show("This disables the Windows WinINet system proxy for all applications using it, then restarts the target app. Use only as a fallback. Continue?", "Confirm broad fallback", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { RouteEngine.SaveRollback(rollbackFile); RouteEngine.ApplyBroadFallback(); RouteEngine.RestartTarget(CurrentProfile()); WriteLog("Applied broad fallback: WinINet system proxy OFF."); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fallback failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void SaveRollback()
    {
        try { RouteEngine.SaveRollback(rollbackFile); WriteLog("Rollback saved to " + rollbackFile); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveProfile()
    {
        try { ProfileStore.Save(CurrentProfile()); WriteLog("Target profile saved to " + ProfileStore.FilePath); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Profile save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RestoreRollback()
    {
        if (MessageBox.Show("Restore the saved WinINet proxy state? This may turn the system proxy back on.", "Confirm restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { RouteEngine.RestoreRollback(rollbackFile); WriteLog("Rollback restored."); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void RestartTarget()
    {
        try { RouteEngine.RestartTarget(CurrentProfile()); WriteLog("Target app restarted."); }
        catch (Exception ex) { WriteLog("Restart failed: " + ex.Message); }
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
        if (args.Length > 0 && String.Equals(args[0], "--check-only-text", StringComparison.OrdinalIgnoreCase))
        {
            TargetProfile profile = new TargetProfile();
            ProxyState proxy = RouteEngine.ReadProxy();
            TargetState target = RouteEngine.ReadTarget(profile);
            Console.WriteLine("Proxy: " + (proxy.Enable == 0 ? "OFF" : "ON -> " + proxy.Server));
            Console.WriteLine("Scope: " + (proxy.Enable == 0 ? "direct for all WinINet apps" : "WinINet proxy with shared domain bypass list"));
            Console.WriteLine("Target version: " + target.Version);
            Console.WriteLine("Sync opened: " + target.LastOpened);
            Console.WriteLine("Last content update: " + target.LastUpdated);
            Console.WriteLine("Latest failure: " + target.LastFailure + " " + target.LastCode);
            return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
