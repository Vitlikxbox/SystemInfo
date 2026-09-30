using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

class SystemInfo : Form
{
    ListView list;
    Label statusLabel;
    readonly List<KeyValuePair<string, List<string[]>>> sections = new List<KeyValuePair<string, List<string[]>>>();

    SystemInfo()
    {
        Text = "SystemInfo";
        ClientSize = new Size(700, 560);
        MinimumSize = new Size(520, 400);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

        list = new ListView
        {
            Location = new Point(12, 12), Size = new Size(676, 490), View = View.Details, FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        list.Columns.Add("", 190);
        list.Columns.Add("", 460);
        list.Resize += (s, e) => list.Columns[1].Width = Math.Max(200, list.ClientSize.Width - list.Columns[0].Width - 4);
        list.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) CopySelected(); };
        list.DoubleClick += (s, e) => CopySelected();
        Controls.Add(list);

        statusLabel = new Label { Location = new Point(12, 522), AutoSize = true, ForeColor = Color.DimGray, Text = "Собираю информацию…", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        Controls.Add(statusLabel);
        var save = new Button { Text = "Сохранить в файл", Location = new Point(390, 514), Size = new Size(150, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        save.Click += (s, e) => SaveReport();
        Controls.Add(save);
        var copy = new Button { Text = "Копировать всё", Location = new Point(548, 514), Size = new Size(140, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        copy.Click += (s, e) => { Clipboard.SetText(Report()); statusLabel.Text = "Отчёт скопирован в буфер обмена"; };
        Controls.Add(copy);

        Shown += (s, e) => new Thread(Collect) { IsBackground = true }.Start();
    }

    static List<ManagementObject> Query(string wql, string scope = @"root\cimv2")
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher(scope, wql))
                return searcher.Get().Cast<ManagementObject>().ToList();
        }
        catch { return new List<ManagementObject>(); }
    }

    static string S(ManagementObject o, string name)
    {
        try { var v = o[name]; return v == null ? "" : v.ToString().Trim(); }
        catch { return ""; }
    }

    static long L(ManagementObject o, string name)
    {
        long v;
        return long.TryParse(S(o, name), out v) ? v : 0;
    }

    void Add(string section, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var s = sections.FirstOrDefault(x => x.Key == section);
        if (s.Key == null) { s = new KeyValuePair<string, List<string[]>>(section, new List<string[]>()); sections.Add(s); }
        s.Value.Add(new[] { name, value });
    }

    void Collect()
    {
        // System
        foreach (var os in Query("SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem"))
        {
            string display = Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", ""));
            string caption = S(os, "Caption").Replace("Microsoft ", "").Replace("Майкрософт ", "");
            int build;
            if (int.TryParse(S(os, "BuildNumber"), out build) && build >= 22000) caption = caption.Replace("Windows 10", "Windows 11");
            Add("💻 Система", "Windows", caption + (display.Length > 0 ? " " + display : "") + ", " + S(os, "OSArchitecture"));
            Add("💻 Система", "Сборка", S(os, "Version"));
            Add("💻 Система", "Установлена", Date(S(os, "InstallDate")));
            var boot = ManagementDateTimeConverter.ToDateTime(S(os, "LastBootUpTime"));
            var up = DateTime.Now - boot;
            Add("💻 Система", "Работает без перезагрузки", (up.Days > 0 ? up.Days + " д " : "") + up.Hours + " ч " + up.Minutes + " мин");
        }
        Add("💻 Система", "Имя компьютера", Environment.MachineName);
        foreach (var cs in Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            if (!Placeholder(S(cs, "Manufacturer")) && !Placeholder(S(cs, "Model")))
                Add("💻 Система", "Модель", S(cs, "Manufacturer") + " " + S(cs, "Model"));

        // CPU
        foreach (var cpu in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L3CacheSize FROM Win32_Processor"))
        {
            Add("⚙️ Процессор", "Модель", S(cpu, "Name"));
            Add("⚙️ Процессор", "Ядер / потоков", S(cpu, "NumberOfCores") + " / " + S(cpu, "NumberOfLogicalProcessors"));
            Add("⚙️ Процессор", "Базовая частота", (L(cpu, "MaxClockSpeed") / 1000.0).ToString("0.00") + " ГГц");
            if (L(cpu, "L3CacheSize") > 0) Add("⚙️ Процессор", "Кэш L3", L(cpu, "L3CacheSize") / 1024 + " МБ");
        }

        // Memory
        var sticks = Query("SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        long totalRam = sticks.Sum(m => L(m, "Capacity"));
        int slots = Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").Sum(a => (int)L(a, "MemoryDevices"));
        var free = Query("SELECT FreePhysicalMemory FROM Win32_OperatingSystem").Select(o => L(o, "FreePhysicalMemory") * 1024).FirstOrDefault();
        if (totalRam > 0)
        {
            string type = sticks.Select(m => MemoryType((int)L(m, "SMBIOSMemoryType"))).FirstOrDefault(t => t.Length > 0) ?? "";
            Add("🧠 Память", "Всего", FormatSize(totalRam) + (type.Length > 0 ? " " + type : "") + (free > 0 ? " (свободно " + FormatSize(free) + ")" : ""));
            Add("🧠 Память", "Планки", sticks.Count + (slots > 0 ? " из " + slots + " слотов" : ""));
            int i = 1;
            foreach (var m in sticks)
            {
                long speed = L(m, "ConfiguredClockSpeed") > 0 ? L(m, "ConfiguredClockSpeed") : L(m, "Speed");
                Add("🧠 Память", "Планка " + i++, FormatSize(L(m, "Capacity")) + (speed > 0 ? ", " + speed + " МГц" : "") + Vendor(Clean(S(m, "Manufacturer")) + " " + Clean(S(m, "PartNumber"))));
            }
        }

        // GPU
        foreach (var gpu in Query("SELECT Name, AdapterRAM, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
        {
            string name = S(gpu, "Name");
            long vram = GpuMemory(name);
            if (vram == 0) vram = L(gpu, "AdapterRAM");
            Add("🎮 Видеокарта", name, (vram > 0 ? FormatSize(vram) + ", " : "") + "драйвер " + S(gpu, "DriverVersion"));
            if (L(gpu, "CurrentHorizontalResolution") > 0)
                Add("🎮 Видеокарта", "Экран", S(gpu, "CurrentHorizontalResolution") + " × " + S(gpu, "CurrentVerticalResolution") + ", " + S(gpu, "CurrentRefreshRate") + " Гц");
        }

        // Motherboard
        foreach (var b in Query("SELECT Manufacturer, Product FROM Win32_BaseBoard")) Add("🔌 Материнская плата", "Модель", S(b, "Manufacturer") + " " + S(b, "Product"));
        foreach (var b in Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS")) Add("🔌 Материнская плата", "BIOS", S(b, "SMBIOSBIOSVersion") + " от " + Date(S(b, "ReleaseDate")));

        // Disks
        var physical = Query("SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
        foreach (var d in physical)
        {
            string media = L(d, "MediaType") == 4 ? "SSD" : L(d, "MediaType") == 3 ? "HDD" : "";
            string bus = L(d, "BusType") == 17 ? "NVMe" : L(d, "BusType") == 11 ? "SATA" : L(d, "BusType") == 7 ? "USB" : "";
            Add("💾 Диски", S(d, "FriendlyName"), FormatSize(L(d, "Size")) + (media.Length > 0 ? ", " + media : "") + (bus.Length > 0 ? " " + bus : ""));
        }
        if (physical.Count == 0)
            foreach (var d in Query("SELECT Model, Size FROM Win32_DiskDrive")) Add("💾 Диски", S(d, "Model"), FormatSize(L(d, "Size")));
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady && (x.DriveType == DriveType.Fixed || x.DriveType == DriveType.Removable)))
            Add("💾 Диски", "Раздел " + drive.Name.TrimEnd('\\'), "свободно " + FormatSize(drive.AvailableFreeSpace) + " из " + FormatSize(drive.TotalSize) + (drive.VolumeLabel.Length > 0 ? " (" + drive.VolumeLabel + ")" : ""));

        // Network
        foreach (var n in Query("SELECT Name, Speed, NetConnectionID FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE AND PhysicalAdapter = TRUE"))
            Add("🌐 Сеть", S(n, "NetConnectionID"), S(n, "Name") + (L(n, "Speed") > 0 ? ", " + (L(n, "Speed") / 1000000) + " Мбит/с" : ""));

        // Monitors
        foreach (var screen in Screen.AllScreens)
            Add("🖥️ Мониторы", screen.Primary ? "Основной" : "Дополнительный", screen.Bounds.Width + " × " + screen.Bounds.Height);

        BeginInvoke(new Action(ShowResults));
    }

    // AdapterRAM is 32-bit and tops out at 4 GB, so read the real value from the driver's registry key.
    static long GpuMemory(string name)
    {
        try
        {
            using (var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                foreach (var sub in root.GetSubKeyNames())
                    using (var k = root.OpenSubKey(sub))
                    {
                        if (k == null || Convert.ToString(k.GetValue("DriverDesc")) != name) continue;
                        var v = k.GetValue("HardwareInformation.qwMemorySize");
                        if (v is long) return (long)v;
                        if (v is byte[] && ((byte[])v).Length >= 8) return BitConverter.ToInt64((byte[])v, 0);
                    }
        }
        catch { }
        return 0;
    }

    static bool Placeholder(string s)
    {
        s = s.ToLowerInvariant();
        return s.Length == 0 || s.Contains("to be filled") || s.Contains("system manufacturer") || s.Contains("system product name") || s.Contains("default string");
    }

    static string Vendor(string s)
    {
        s = s.Trim();
        return s.Length > 0 ? ", " + s : "";
    }

    static string Clean(string s)
    {
        return s.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || s.Equals("Undefined", StringComparison.OrdinalIgnoreCase) ? "" : s;
    }

    static string MemoryType(int t)
    {
        switch (t)
        {
            case 20: return "DDR";
            case 21: return "DDR2";
            case 24: return "DDR3";
            case 26: return "DDR4";
            case 34: return "DDR5";
            default: return "";
        }
    }

    static string Date(string wmi)
    {
        try { return ManagementDateTimeConverter.ToDateTime(wmi).ToString("dd.MM.yyyy"); }
        catch { return wmi; }
    }

    static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return (v >= 100 || u == 0 ? v.ToString("0") : v.ToString("0.#")) + " " + units[u];
    }

    void ShowResults()
    {
        list.BeginUpdate();
        foreach (var s in sections)
        {
            var group = new ListViewGroup(s.Key);
            list.Groups.Add(group);
            foreach (var row in s.Value) list.Items.Add(new ListViewItem(row, group));
        }
        list.EndUpdate();
        statusLabel.Text = "Двойной клик или Ctrl+C копирует строку";
    }

    void CopySelected()
    {
        var rows = list.SelectedItems.Cast<ListViewItem>().Select(i => i.SubItems[0].Text + ": " + i.SubItems[1].Text).ToArray();
        if (rows.Length == 0) return;
        Clipboard.SetText(string.Join("\r\n", rows));
        statusLabel.Text = "Скопировано";
    }

    string Report()
    {
        var sb = new StringBuilder("Характеристики компьютера (SystemInfo, " + DateTime.Now.ToString("dd.MM.yyyy") + ")\r\n");
        foreach (var s in sections)
        {
            sb.Append("\r\n").Append(s.Key.Substring(s.Key.IndexOf(' ') + 1)).Append("\r\n");
            foreach (var row in s.Value) sb.Append("  ").Append(row[0]).Append(": ").Append(row[1]).Append("\r\n");
        }
        return sb.ToString();
    }

    void SaveReport()
    {
        using (var d = new SaveFileDialog { FileName = "Характеристики " + Environment.MachineName + ".txt", Filter = "Текст|*.txt" })
            if (d.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(d.FileName, Report(), Encoding.UTF8); statusLabel.Text = "Сохранено: " + d.FileName; }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new SystemInfo());
    }
}
