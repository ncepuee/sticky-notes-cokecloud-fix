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
[assembly: AssemblyVersion("0.3.1.0")]
[assembly: AssemblyFileVersion("0.3.1.0")]

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

internal static class UiStrings
{
    public static bool Chinese;
    private static readonly string LanguageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsDirectRouteFix", "language.txt");

    public static void Load()
    {
        try { Chinese = File.Exists(LanguageFile) && File.ReadAllText(LanguageFile).Trim() == "zh-CN"; }
        catch { Chinese = false; }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LanguageFile));
        File.WriteAllText(LanguageFile, Chinese ? "zh-CN" : "en-US", new UTF8Encoding(false));
    }

    public static string T(string chinese, string english)
    {
        return Chinese ? chinese : english;
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
    private readonly Dictionary<Control, string[]> localized = new Dictionary<Control, string[]>();
    private readonly TextBox domainsBox = new TextBox();
    private readonly TextBox appIdBox = new TextBox();
    private readonly TextBox packageBox = new TextBox();
    private readonly TextBox processBox = new TextBox();
    private readonly TextBox logDirBox = new TextBox();
    private readonly RichTextBox logBox = new RichTextBox();
    private readonly string rollbackFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsDirectRouteFix", "rollback-state.json");
    private Button languageButton;
    private Button primaryButton;
    private GroupBox targetGroup;
    private GroupBox statusGroup;
    private GroupBox outputGroup;

    public MainForm()
    {
        UiStrings.Load();
        ProfileStore.Load(profile);
        Text = UiStrings.T("Windows 直连策略工具", "Windows Direct Route Fix");
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 780);
        MinimumSize = new Size(900, 680);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        Label header = new Label { AutoSize = true, Location = new Point(20, 15), Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold) };
        Localize(header, "Windows 直连策略工具", "Windows Direct Route Fix");
        Controls.Add(header);
        Label subtitle = new Label { AutoSize = true, Location = new Point(22, 50), ForeColor = Color.DimGray };
        Localize(subtitle, "通用目标应用直连诊断：应用 + 域名 + 可回滚 WinINet 策略。", "General-purpose direct-route helper: target app + domains + reversible WinINet policy.");
        Controls.Add(subtitle);
        languageButton = new Button { Location = new Point(850, 18), Width = 105, Height = 30 };
        languageButton.Click += delegate { UiStrings.Chinese = !UiStrings.Chinese; UiStrings.Save(); ApplyLanguage(); };
        Controls.Add(languageButton);

        targetGroup = new GroupBox { Location = new Point(20, 78), Size = new Size(940, 237) };
        Localize(targetGroup, "目标应用配置", "Target profile");
        Controls.Add(targetGroup);
        AddField(targetGroup, "目标名称", "Target name", profile.Name, 15, 25, 120, 790, delegate(string value) { profile.Name = value; });
        appIdBox.Text = profile.AppId;
        AddControl(targetGroup, "应用启动 ID", "App launch ID", appIdBox, 15, 57, 120, 790);
        packageBox.Text = profile.PackageName;
        AddControl(targetGroup, "应用包名", "Package name", packageBox, 15, 89, 120, 790);
        processBox.Text = profile.ProcessName;
        AddControl(targetGroup, "进程名", "Process name", processBox, 15, 121, 120, 790);
        logDirBox.Text = profile.LogDirectory;
        AddControl(targetGroup, "日志目录", "Log directory", logDirBox, 15, 153, 120, 790);
        domainsBox.Multiline = true;
        domainsBox.ScrollBars = ScrollBars.Vertical;
        domainsBox.Text = String.Join(";", profile.DirectDomains);
        AddControl(targetGroup, "直连域名", "Direct domains", domainsBox, 15, 185, 120, 790);

        statusGroup = new GroupBox { Location = new Point(20, 327), Size = new Size(940, 145) };
        Localize(statusGroup, "当前状态", "Observed state");
        Controls.Add(statusGroup);
        TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusGroup.Controls.Add(table);
        AddStatus(table, 0, "WinINet 系统代理", "WinINet system proxy", "Proxy");
        AddStatus(table, 1, "实际策略范围", "Effective route scope", "Scope");
        AddStatus(table, 2, "目标应用版本", "Target version", "Version");
        AddStatus(table, 3, "同步通道证据", "Sync channel evidence", "Sync");
        AddStatus(table, 4, "最近内容更新", "Last content update", "Updated");

        FlowLayoutPanel buttons = new FlowLayoutPanel { Location = new Point(20, 487), Size = new Size(940, 95), WrapContents = true };
        Controls.Add(buttons);
        primaryButton = AddButton(buttons, "一键设置便笺同步", "One-click Sticky Notes sync setup", 260, 42);
        primaryButton.Click += delegate { ApplyRecommended(); };
        Button diagnoseButton = AddButton(buttons, "诊断", "Diagnose", 90, 34);
        diagnoseButton.Click += delegate { RefreshUi(); };
        Button profileButton = AddButton(buttons, "保存配置", "Save profile", 105, 34);
        profileButton.Click += delegate { SaveProfile(); };
        Button restartButton = AddButton(buttons, "重启目标应用", "Restart target", 125, 34);
        restartButton.Click += delegate { RestartTarget(); };
        Button logsButton = AddButton(buttons, "打开日志", "Open logs", 100, 34);
        logsButton.Click += delegate { RouteEngine.OpenFolder(logDirBox.Text.Trim()); };
        Button fallbackButton = AddButton(buttons, "高级：关闭系统代理", "Advanced: proxy OFF", 155, 34);
        fallbackButton.Click += delegate { ApplyBroad(); };
        Button restoreButton = AddButton(buttons, "恢复回滚状态", "Restore rollback", 125, 34);
        restoreButton.Click += delegate { RestoreRollback(); };

        outputGroup = new GroupBox { Location = new Point(20, 592), Size = new Size(940, 150) };
        Localize(outputGroup, "策略说明 / 诊断日志", "Policy explanation / diagnostic log");
        Controls.Add(outputGroup);
        logBox.Dock = DockStyle.Fill;
        logBox.ReadOnly = true;
        logBox.BackColor = Color.White;
        logBox.BorderStyle = BorderStyle.None;
        logBox.Font = new Font("Consolas", 9F);
        outputGroup.Controls.Add(logBox);
        ApplyLanguage();
        Shown += delegate { RefreshUi(); };
    }

    private void AddField(GroupBox box, string chinese, string english, string initial, int x, int y, int labelWidth, int fieldWidth, Action<string> setter)
    {
        TextBox field = new TextBox { Text = initial, Location = new Point(x + labelWidth, y), Width = fieldWidth, Height = 22 };
        field.TextChanged += delegate { setter(field.Text); };
        AddControl(box, chinese, english, field, x, y, labelWidth, fieldWidth);
    }

    private void AddControl(Control parent, string chinese, string english, Control control, int x, int y, int labelWidth, int width)
    {
        Label label = new Label { AutoSize = false, Width = labelWidth - 8, Height = 22, Location = new Point(x, y + 3), TextAlign = ContentAlignment.MiddleLeft };
        Localize(label, chinese, english);
        parent.Controls.Add(label);
        control.Location = new Point(x + labelWidth, y);
        control.Width = width;
        parent.Controls.Add(control);
    }

    private void AddStatus(TableLayoutPanel table, int row, string chinese, string english, string key)
    {
        Label name = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
        Localize(name, chinese, english);
        table.Controls.Add(name, 0, row);
        Label value = new Label { Text = "-", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        values[key] = value;
        table.Controls.Add(value, 1, row);
    }

    private Button AddButton(FlowLayoutPanel panel, string chinese, string english, int width, int height)
    {
        Button button = new Button { Width = width, Height = height, Margin = new Padding(4), FlatStyle = FlatStyle.Standard };
        Localize(button, chinese, english);
        panel.Controls.Add(button);
        return button;
    }

    private void Localize(Control control, string chinese, string english)
    {
        localized[control] = new[] { chinese, english };
        control.Text = UiStrings.T(chinese, english);
    }

    private void ApplyLanguage()
    {
        Text = UiStrings.T("Windows 直连策略工具", "Windows Direct Route Fix");
        languageButton.Text = UiStrings.T("English", "中文");
        foreach (KeyValuePair<Control, string[]> item in localized)
            item.Key.Text = UiStrings.T(item.Value[0], item.Value[1]);
        primaryButton.BackColor = Color.FromArgb(0, 120, 215);
        primaryButton.ForeColor = Color.White;
        primaryButton.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        RefreshUi();
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
            values["Proxy"].Text = proxy.Enable == 0 ? UiStrings.T("关闭（便笺直连）", "OFF (direct for target)") : UiStrings.T("开启 -> ", "ON -> ") + proxy.Server;
            values["Scope"].Text = proxy.Enable == 0 ? UiStrings.T("所有 WinINet 应用直连", "Direct for all WinINet apps") : UiStrings.T("WinINet 代理；域名例外为共享设置", "WinINet proxy; domain exceptions are shared");
            values["Version"].Text = target.Version.Length == 0 ? UiStrings.T("未检测到", "not detected") : target.Version;
            values["Sync"].Text = target.LastOpened.Length > 0 ? UiStrings.T("已建立：", "opened: ") + target.LastOpened :
                (target.LastFailure.Length > 0 ? UiStrings.T("失败：", "failed: ") + target.LastFailure + " " + target.LastCode : UiStrings.T("暂无明确证据", "no clear evidence"));
            values["Updated"].Text = target.LastUpdated.Length == 0 ? UiStrings.T("暂无", "none") : target.LastUpdated;
            values["Proxy"].ForeColor = proxy.Enable == 0 ? Color.ForestGreen : Color.DarkOrange;
            values["Sync"].ForeColor = target.LastOpened.Length > 0 ? Color.ForestGreen :
                (target.LastFailure.Length > 0 ? Color.Crimson : Color.DimGray);
            logBox.Clear();
            WriteLog(UiStrings.T("目标应用=", "Target=") + current.Name + "; AppId=" + current.AppId);
            WriteLog(UiStrings.T("策略接口=当前用户 WinINet 设置", "Policy seam=Windows current-user WinINet settings"));
            WriteLog(UiStrings.T("系统代理开关=", "ProxyEnable=") + proxy.Enable + "; ProxyServer=" + proxy.Server);
            WriteLog(UiStrings.T("直连域名=", "Direct domains=") + String.Join(";", current.DirectDomains));
            if (target.Log.Length > 0) WriteLog(UiStrings.T("最新日志=", "Latest log=") + target.Log);
            foreach (RouteEvent item in target.Events)
                WriteLog(item.Time + " " + item.Namespace + "/" + item.Name + (item.Code.Length == 0 ? "" : " code=" + item.Code));
        }
        catch (Exception ex) { WriteLog("Diagnose failed: " + ex.Message); }
    }

    private void ApplyDirect()
    {
        if (MessageBox.Show(UiStrings.T("这会把列表中的域名加入 Windows WinINet 直连例外。它是域名级策略，其他 WinINet 应用也可能共享。继续吗？", "This adds the listed domains to the Windows WinINet bypass list. It is domain-scoped, but the bypass list is shared by WinINet applications. Continue?"), UiStrings.T("确认设置域名直连", "Confirm direct-domain policy"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { RouteEngine.SaveRollback(rollbackFile); RouteEngine.ApplyDirectDomains(Domains()); WriteLog(UiStrings.T("已添加域名直连例外；其他 WinINet 应用也可能使用。", "Applied direct-domain exceptions. Other WinINet apps may also use them.")); }
        catch (Exception ex) { MessageBox.Show(ex.Message, UiStrings.T("设置失败", "Apply failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void ApplyRecommended()
    {
        string targetName = CurrentProfile().Name;
        if (MessageBox.Show(
            UiStrings.T("将执行：保存配置 → 保存回滚点 → 添加目标域名直连例外 → 重启目标应用。不会关闭全局系统代理。继续吗？", "This will save the profile, save a rollback point, add the target domains to the WinINet bypass list, and restart the target app. The global system proxy will remain enabled. Continue?"),
            UiStrings.T("一键设置便笺同步", "One-click sync setup"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            TargetProfile current = CurrentProfile();
            ProfileStore.Save(current);
            RouteEngine.SaveRollback(rollbackFile);
            RouteEngine.ApplyDirectDomains(current.DirectDomains);
            RouteEngine.RestartTarget(current);
            WriteLog(UiStrings.T("已完成目标应用直连设置并重启：", "Direct-route policy applied and target restarted: ") + targetName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, UiStrings.T("设置失败", "Setup failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshUi();
    }

    private void ApplyBroad()
    {
        if (MessageBox.Show(UiStrings.T("这会关闭所有依赖 WinINet 系统代理的应用的代理，仅作为兜底，并重启目标应用。继续吗？", "This disables the Windows WinINet system proxy for all applications using it, then restarts the target app. Use only as a fallback. Continue?"), UiStrings.T("确认高级兜底", "Confirm broad fallback"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { RouteEngine.SaveRollback(rollbackFile); RouteEngine.ApplyBroadFallback(); RouteEngine.RestartTarget(CurrentProfile()); WriteLog(UiStrings.T("已启用兜底：WinINet 系统代理关闭。", "Applied broad fallback: WinINet system proxy OFF.")); }
        catch (Exception ex) { MessageBox.Show(ex.Message, UiStrings.T("兜底失败", "Fallback failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void SaveRollback()
    {
        try { RouteEngine.SaveRollback(rollbackFile); WriteLog(UiStrings.T("回滚点已保存：", "Rollback saved to ") + rollbackFile); }
        catch (Exception ex) { MessageBox.Show(ex.Message, UiStrings.T("保存失败", "Save failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveProfile()
    {
        try { ProfileStore.Save(CurrentProfile()); WriteLog(UiStrings.T("目标配置已保存：", "Target profile saved to ") + ProfileStore.FilePath); }
        catch (Exception ex) { MessageBox.Show(ex.Message, UiStrings.T("配置保存失败", "Profile save failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RestoreRollback()
    {
        if (MessageBox.Show(UiStrings.T("恢复已保存的 WinINet 代理状态？这可能重新打开系统代理。", "Restore the saved WinINet proxy state? This may turn the system proxy back on."), UiStrings.T("确认恢复", "Confirm restore"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { RouteEngine.RestoreRollback(rollbackFile); WriteLog(UiStrings.T("回滚状态已恢复。", "Rollback restored.")); }
        catch (Exception ex) { MessageBox.Show(ex.Message, UiStrings.T("恢复失败", "Restore failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefreshUi();
    }

    private void RestartTarget()
    {
        try { RouteEngine.RestartTarget(CurrentProfile()); WriteLog(UiStrings.T("目标应用已重启。", "Target app restarted.")); }
        catch (Exception ex) { WriteLog(UiStrings.T("重启失败：", "Restart failed: ") + ex.Message); }
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
