// Kor - Clevo kasali (Monster Abra) tek bolgeli RGB klavye icin isik yoneticisi.
// Isik, Clevo'nun InsydeDCHU.dll arayuzu uzerinden yonetilir (komut 103):
//   0xF0BBRRGG renk, 0xF40000VV parlaklik, 0xE007B001 isigi ac.
// Fn tuslari, Clevo'nun "Fn hot keys and OSD" uygulamasinin yazdigi "OutLog" olay gunlugunden okunur.
// Arayuz: Arayuz.xaml (exe icine gomulu). Derleme: build.ps1
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace KlavyeIsik
{
    static class Native
    {
        [DllImport("InsydeDCHU.dll", CallingConvention = CallingConvention.StdCall)]
        static extern int SetDCHU_Data(int command, byte[] buffer, int length);

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);

        static readonly object gate = new object();

        static void Send(uint v)
        {
            lock (gate) { SetDCHU_Data(103, BitConverter.GetBytes(v), 4); }
        }

        // Tek bolge: 0xF0. Renk sirasi B, R, G.
        public static void SetColor(int r, int g, int b) { Send((0xF0u << 24) | ((uint)b << 16) | ((uint)r << 8) | (uint)g); }
        public static void SetBrightness(int v) { Send(0xF4000000u | (uint)(v & 255)); }
        public static void PowerOn() { Send(0xE007B001u); }
    }

    // Simge: altindan isik sizan bir klavye tusu. Exe, pencere ve tepsi simgesi buradan cizilir.
    static class Logo
    {
        public static readonly SD.Color Warm = SD.Color.FromArgb(255, 138, 31);
        public static readonly SD.Color Hot = SD.Color.FromArgb(255, 46, 126);

        static SD.Drawing2D.GraphicsPath Round(float x, float y, float w, float h, float r)
        {
            SD.Drawing2D.GraphicsPath p = new SD.Drawing2D.GraphicsPath();
            float d = Math.Max(1f, r * 2);
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static SD.Bitmap Draw(int size, SD.Color c1, SD.Color c2)
        {
            SD.Bitmap bmp = new SD.Bitmap(size, size, SD.Imaging.PixelFormat.Format32bppArgb);
            float s = size;
            using (SD.Graphics g = SD.Graphics.FromImage(bmp))
            using (SD.Drawing2D.LinearGradientBrush grad = new SD.Drawing2D.LinearGradientBrush(new SD.PointF(0, 0), new SD.PointF(s, s), c1, c2))
            using (SD.SolidBrush dark = new SD.SolidBrush(SD.Color.FromArgb(0x14, 0x16, 0x1C)))
            {
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = SD.Drawing2D.PixelOffsetMode.HighQuality;
                g.Clear(SD.Color.Transparent);
                // Isik: tusun altindan tasan renkli govde
                using (SD.Drawing2D.GraphicsPath outer = Round(s * 0.06f, s * 0.06f, s * 0.88f, s * 0.88f, s * 0.22f)) g.FillPath(grad, outer);
                // Tusun ust yuzu (yukari kaydirilmis; altta daha kalin isik kalir)
                using (SD.Drawing2D.GraphicsPath key = Round(s * 0.17f, s * 0.13f, s * 0.66f, s * 0.60f, s * 0.14f)) g.FillPath(dark, key);
                // Tus uzerindeki aydinlatmali isaret
                float lh = Math.Max(1.5f, s * 0.07f);
                using (SD.Drawing2D.GraphicsPath leg = Round(s * 0.28f, s * 0.25f, s * 0.22f, lh, lh / 2)) g.FillPath(grad, leg);
            }
            return bmp;
        }

        public static SD.Icon ToIcon(SD.Bitmap bmp)
        {
            IntPtr h = bmp.GetHicon();
            SD.Icon ic = (SD.Icon)SD.Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            return ic;
        }

        // .ico icin 32 bit bitmap girdisi: baslik + alttan yukari BGRA pikseller + bos maske.
        static byte[] Dib(SD.Bitmap bmp)
        {
            int n = bmp.Width;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(40); bw.Write(n); bw.Write(n * 2); bw.Write((short)1); bw.Write((short)32);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                for (int y = n - 1; y >= 0; y--)
                    for (int x = 0; x < n; x++) bw.Write(bmp.GetPixel(x, y).ToArgb());
                bw.Write(new byte[((n + 31) / 32) * 4 * n]);
                bw.Flush();
                return ms.ToArray();
            }
        }

        // Cok boyutlu .ico dosyasi yazar (derleme sirasinda exe simgesi icin).
        public static void WriteIco(string path)
        {
            int[] sizes = { 256, 64, 48, 32, 24, 16 };
            List<byte[]> pngs = new List<byte[]>();
            foreach (int size in sizes)
                using (SD.Bitmap bmp = Draw(size, Warm, Hot))
                using (MemoryStream ms = new MemoryStream())
                {
                    // 256 px PNG olarak, kucuk boyutlar eski araclarla da uyumlu olsun diye bitmap olarak yazilir.
                    if (size >= 256) { bmp.Save(ms, SD.Imaging.ImageFormat.Png); pngs.Add(ms.ToArray()); }
                    else pngs.Add(Dib(bmp));
                }
            using (BinaryWriter bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((short)1); bw.Write((short)32);
                    bw.Write(pngs[i].Length); bw.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] png in pngs) bw.Write(png);
            }
        }
    }

    static class Modes
    {
        public const int Static = 0, Breath = 1, Rainbow = 2, Reactive = 3, RandomKey = 4, Heat = 5, Police = 6, Aurora = 7;
        public const int Count = 8;
        public static readonly string[] Names = {
            "Sabit renk", "Nefes", "Gökkuşağı", "Tuş vuruşu", "Renk zarı", "Yazma ısısı", "Polis", "Aurora"
        };
        // Sekme/menu sirasi
        public static readonly int[] Order = { 0, 1, 2, 7, 3, 4, 5, 6 };
        public static bool UsesKeys(int m) { return m == Reactive || m == RandomKey || m == Heat; }
        public static bool UsesColor(int m) { return m == Static || m == Breath || m == Reactive; }
        public static bool UsesSpeed(int m) { return m != Static; }
    }

    static class ColorUtil
    {
        public static bool TryHex(string t, bool allowShort, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (t == null) return false;
            t = t.Trim().TrimStart('#');
            if (allowShort && t.Length == 3) t = "" + t[0] + t[0] + t[1] + t[1] + t[2] + t[2];
            if (t.Length != 6) return false;
            int v;
            if (!int.TryParse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out v)) return false;
            r = (v >> 16) & 255; g = (v >> 8) & 255; b = v & 255;
            return true;
        }

        public static string Hex(int r, int g, int b)
        {
            return "#" + r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture);
        }

        // h 0..1, s 0..1, v 0..1
        public static void FromHsv(double h, double s, double v, out double r, out double g, out double b)
        {
            h = (h - Math.Floor(h)) * 6.0;
            int i = (int)h; double f = h - i;
            double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        public static void ToHsv(int r, int g, int b, out double h, out double s, out double v)
        {
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == R) h = ((G - B) / d) / 6.0;
            else if (max == G) h = ((B - R) / d + 2) / 6.0;
            else h = ((R - G) / d + 4) / 6.0;
            if (h < 0) h += 1;
        }
    }

    class Rule
    {
        public string Exe;
        public int R, G, B;
    }

    class Settings
    {
        public int Mode = Modes.Static, R = 160, G = 0, B = 255, Brightness = 100, Speed = 5;
        public bool On = true;
        public readonly List<Rule> Rules = new List<Rule>();
        public readonly List<int> Custom = new List<int>();

        static string FilePath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kor", "ayar.ini"); }
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string val = line.Substring(i + 1).Trim();
                    int r, g, b;
                    if (k == "Rule")
                    {
                        int bar = val.LastIndexOf('|');
                        if (bar > 0 && ColorUtil.TryHex(val.Substring(bar + 1), false, out r, out g, out b))
                        {
                            Rule rule = new Rule();
                            rule.Exe = val.Substring(0, bar).Trim().ToLowerInvariant();
                            rule.R = r; rule.G = g; rule.B = b;
                            if (rule.Exe.Length > 0) s.Rules.Add(rule);
                        }
                        continue;
                    }
                    if (k == "Custom")
                    {
                        if (ColorUtil.TryHex(val, false, out r, out g, out b)) s.Custom.Add((r << 16) | (g << 8) | b);
                        continue;
                    }
                    int v;
                    if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) continue;
                    if (k == "Mode") s.Mode = Clamp(v, 0, Modes.Count - 1);
                    else if (k == "On") s.On = v != 0;
                    else if (k == "R") s.R = Clamp(v, 0, 255);
                    else if (k == "G") s.G = Clamp(v, 0, 255);
                    else if (k == "B") s.B = Clamp(v, 0, 255);
                    else if (k == "Brightness") s.Brightness = Clamp(v, 0, 100);
                    else if (k == "Speed") s.Speed = Clamp(v, 1, 10);
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                List<string> lines = new List<string>();
                lines.Add("Mode=" + Mode);
                lines.Add("On=" + (On ? 1 : 0));
                lines.Add("R=" + R); lines.Add("G=" + G); lines.Add("B=" + B);
                lines.Add("Brightness=" + Brightness);
                lines.Add("Speed=" + Speed);
                foreach (int c in Custom) lines.Add("Custom=" + ColorUtil.Hex((c >> 16) & 255, (c >> 8) & 255, c & 255).Substring(1));
                foreach (Rule r in Rules) lines.Add("Rule=" + r.Exe + "|" + ColorUtil.Hex(r.R, r.G, r.B).Substring(1));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { }
        }
    }

    // Efektleri hesaplayip klavyeye gonderen arka plan dongusu.
    class Engine
    {
        public volatile int Mode, R, G, B, Bright = 255, Speed = 5;
        public volatile bool On = true;
        public volatile int OutPacked;   // onizleme icin: parlaklik uygulanmamis son renk

        int keyCount;
        long lastKeyTicks;
        volatile bool running;
        volatile int lastColor = -1, lastBright = -1;
        volatile bool needPower = true;
        Thread thread;
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly Random rnd = new Random();

        public void KeyPressed()
        {
            Interlocked.Increment(ref keyCount);
            Interlocked.Exchange(ref lastKeyTicks, sw.ElapsedTicks);
        }

        public void ForceResend() { needPower = true; lastBright = -1; lastColor = -1; }

        public void Start()
        {
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            if (thread != null) thread.Join(1000);
            // Cikista animasyonun ortasinda kalmasin.
            try
            {
                if (On) { Native.SetBrightness(Bright); Native.SetColor(R, G, B); }
                else Native.SetColor(0, 0, 0);
            }
            catch { }
        }

        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        void Loop()
        {
            int seenKeys = 0;
            double phase = 0, hue = 0, heat = 0;
            double cr = 0, cg = 0, cb = 0, tr = 1, tg = 0, tb = 0;
            double prev = sw.Elapsed.TotalSeconds;

            while (running)
            {
                double now = sw.Elapsed.TotalSeconds;
                double dt = now - prev; prev = now;
                if (dt > 0.5) dt = 0.5;

                int mode = Mode;
                bool on = On;
                double sp = (Speed - 1) / 9.0;
                double br = R / 255.0, bg = G / 255.0, bb = B / 255.0;

                int kc = Thread.VolatileRead(ref keyCount);
                int newKeys = kc - seenKeys; seenKeys = kc;
                double sinceKey = (sw.ElapsedTicks - Interlocked.Read(ref lastKeyTicks)) / (double)Stopwatch.Frequency;

                double r = 0, g = 0, b = 0, k;
                switch (mode)
                {
                    case Modes.Static:
                        r = br; g = bg; b = bb;
                        break;
                    case Modes.Breath:
                        phase += dt / Lerp(8.0, 1.2, sp);
                        k = 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase);
                        k = 0.04 + 0.96 * k * k;
                        r = br * k; g = bg * k; b = bb * k;
                        break;
                    case Modes.Rainbow:
                        phase += dt / Lerp(40.0, 3.0, sp);
                        ColorUtil.FromHsv(phase, 1, 1, out r, out g, out b);
                        break;
                    case Modes.Aurora:
                        phase += dt / Lerp(30.0, 4.0, sp);
                        k = 0.52 + 0.20 * Math.Sin(2 * Math.PI * phase) + 0.10 * Math.Sin(2 * Math.PI * phase * 2.7 + 1.3);
                        ColorUtil.FromHsv(k, 1, 0.75 + 0.25 * Math.Sin(2 * Math.PI * phase * 1.9), out r, out g, out b);
                        break;
                    case Modes.Reactive:
                        k = kc == 0 ? 0 : Math.Exp(-sinceKey / Lerp(1.0, 0.12, sp));
                        k = 0.10 + 0.90 * k;
                        r = br * k; g = bg * k; b = bb * k;
                        break;
                    case Modes.RandomKey:
                        if (newKeys > 0)
                        {
                            hue = hue + 0.25 + rnd.NextDouble() * 0.5;
                            hue -= Math.Floor(hue);
                            ColorUtil.FromHsv(hue, 1, 1, out tr, out tg, out tb);
                        }
                        k = 1 - Math.Exp(-dt / Lerp(0.5, 0.04, sp));
                        cr += (tr - cr) * k; cg += (tg - cg) * k; cb += (tb - cb) * k;
                        r = cr; g = cg; b = cb;
                        break;
                    case Modes.Heat:
                        heat = Math.Min(1.0, heat + newKeys * 0.07);
                        heat *= Math.Exp(-dt / Lerp(4.0, 1.0, sp));
                        ColorUtil.FromHsv(0.62 * (1 - heat), 1, 1, out r, out g, out b);
                        break;
                    case Modes.Police:
                        phase += dt / Lerp(1.2, 0.15, sp);
                        if (phase - Math.Floor(phase) < 0.5) r = 1; else b = 1;
                        break;
                }
                if (phase > 1000000) phase -= 1000000;
                if (!on) { r = 0; g = 0; b = 0; }

                int ir = (int)Math.Round(r * 255), ig = (int)Math.Round(g * 255), ib = (int)Math.Round(b * 255);
                int packed = (ir << 16) | (ig << 8) | ib;
                OutPacked = packed;
                try
                {
                    if (needPower) { needPower = false; Native.PowerOn(); }
                    int bv = Bright;
                    if (on && bv != lastBright) { Native.SetBrightness(bv); lastBright = bv; }
                    if (packed != lastColor) { Native.SetColor(ir, ig, ib); lastColor = packed; }
                }
                catch { }
                Thread.Sleep(mode == Modes.Static || !on ? 60 : 25);
            }
        }
    }

    class Ui
    {
        const int WM_HOTKEY = 0x0312;
        const int HK_MODE = 1;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "Kor";

        readonly Application app;
        readonly Settings s;
        readonly Engine engine = new Engine();
        readonly Window w;
        Color accentColor = Colors.White;

        // Onizleme fircalari (tek ornek; renk degisince tum tuslar guncellenir)
        readonly SolidColorBrush keyBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2A, 0x35));
        readonly SolidColorBrush legendBrush = new SolidColorBrush(Colors.Transparent);
        readonly GradientStop glowIn = new GradientStop(Colors.Transparent, 0);
        readonly GradientStop glowOut = new GradientStop(Colors.Transparent, 1);
        readonly GradientStop satFrom = new GradientStop(Colors.White, 0);
        readonly GradientStop satTo = new GradientStop(Colors.Red, 1);
        int lastPreview = -1;

        readonly RadioButton[] tiles = new RadioButton[Modes.Count];
        readonly TextBox hexBox;
        readonly Slider hueSl, satSl, brightSl, speedSl;
        readonly TextBlock brightTxt, speedTxt, statusText, powerTxt, emptyRules;
        readonly Border swatch, colorCard;
        readonly ToggleButton swPower, swAuto;
        readonly WrapPanel presetPanel, customPanel, runningPanel;
        readonly StackPanel rulesPanel;
        readonly FrameworkElement pageLight, pageApps, pageSettings;

        readonly WF.NotifyIcon tray = new WF.NotifyIcon();
        readonly WF.ContextMenuStrip menu = new WF.ContextMenuStrip();
        SD.Icon currentIcon;
        int lastIconColor = -1;

        readonly DispatcherTimer saveTimer = new DispatcherTimer();
        readonly DispatcherTimer previewTimer = new DispatcherTimer();
        readonly DispatcherTimer fgTimer = new DispatcherTimer();
        readonly DispatcherTimer resendTimer = new DispatcherTimer();
        readonly DispatcherTimer pendingTimer = new DispatcherTimer();
        int resendLeft;
        int pendBright;
        bool pendToggle;

        Native.HookProc hookProc;
        IntPtr hook = IntPtr.Zero;
        EventLogWatcher logWatcher;
        EventWaitHandle showSignal;
        Rule activeRule;
        bool loading, exiting;
        string source = "";   // degisikligi baslatan denetim; kendi kendini ezmesin diye
        readonly int myPid = Process.GetCurrentProcess().Id;

        T F<T>(string name) where T : class { return w.FindName(name) as T; }

        public Ui(Application application, Settings settings, bool hidden, EventWaitHandle signal)
        {
            app = application;
            s = settings;
            showSignal = signal;
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("Arayuz.xaml"))
                w = (Window)XamlReader.Load(st);
            w.Resources["Accent"] = new SolidColorBrush(accentColor);
            using (SD.Bitmap logo = Logo.Draw(64, Logo.Warm, Logo.Hot))
            {
                IntPtr hi = logo.GetHicon();
                w.Icon = Imaging.CreateBitmapSourceFromHIcon(hi, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                Native.DestroyIcon(hi);
            }

            for (int i = 0; i < Modes.Count; i++) tiles[i] = F<RadioButton>("m" + i);
            hexBox = F<TextBox>("hexBox");
            hueSl = F<Slider>("hueSl"); satSl = F<Slider>("satSl");
            brightSl = F<Slider>("brightSl"); speedSl = F<Slider>("speedSl");
            brightTxt = F<TextBlock>("brightTxt"); speedTxt = F<TextBlock>("speedTxt");
            statusText = F<TextBlock>("statusText"); powerTxt = F<TextBlock>("powerTxt");
            emptyRules = F<TextBlock>("emptyRules");
            swatch = F<Border>("swatch"); colorCard = F<Border>("colorCard");
            swPower = F<ToggleButton>("swPower"); swAuto = F<ToggleButton>("swAuto");
            presetPanel = F<WrapPanel>("presetPanel"); customPanel = F<WrapPanel>("customPanel");
            runningPanel = F<WrapPanel>("runningPanel"); rulesPanel = F<StackPanel>("rulesPanel");
            pageLight = F<FrameworkElement>("pageLight"); pageApps = F<FrameworkElement>("pageApps");
            pageSettings = F<FrameworkElement>("pageSettings");

            BuildPreview();
            BuildPresets();
            RebuildCustom();
            RebuildRules();
            Wire();
            BuildTray();

            saveTimer.Interval = TimeSpan.FromMilliseconds(600);
            saveTimer.Tick += delegate { saveTimer.Stop(); s.Save(); };
            previewTimer.Interval = TimeSpan.FromMilliseconds(33);
            previewTimer.Tick += delegate { UpdatePreview(); };
            previewTimer.Start();
            fgTimer.Interval = TimeSpan.FromMilliseconds(400);
            fgTimer.Tick += delegate { CheckForeground(); };
            fgTimer.Start();
            resendTimer.Tick += delegate
            {
                engine.ForceResend();
                resendLeft--;
                if (resendLeft <= 0) resendTimer.Stop();
                else resendTimer.Interval = TimeSpan.FromMilliseconds(1200);
            };
            pendingTimer.Interval = TimeSpan.FromMilliseconds(700);
            pendingTimer.Tick += delegate { pendingTimer.Stop(); RunPending(); };

            SystemEvents.PowerModeChanged += OnPowerMode;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            Apply(false);
            engine.Start();
            StartLogWatch();
            StartShowListener();

            if (hidden)
            {
                new WindowInteropHelper(w).EnsureHandle();
                ResendSoon(4000);   // acilista Clevo kendi rengini yazabilir; ustune tekrar yaz
            }
            else w.Show();
        }

        // ---------- kurulum ----------

        void BuildPreview()
        {
            Grid host = F<Grid>("preview");

            RadialGradientBrush rg = new RadialGradientBrush();
            rg.GradientStops.Add(glowIn);
            rg.GradientStops.Add(glowOut);
            Ellipse glow = new Ellipse();
            glow.Width = 900; glow.Height = 236; glow.Fill = rg;
            glow.HorizontalAlignment = HorizontalAlignment.Center;
            glow.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(glow);

            double[][] main = {
                new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
                new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2 },
                new double[] { 1.5, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.5 },
                new double[] { 1.75, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.25 },
                new double[] { 2.25, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.75 },
                new double[] { 1.25, 1, 1.25, 1.25, 5.5, 1.25, 1.25, 0.75, 0.75, 0.75 }
            };
            double[][] pad = {
                new double[] { 1, 1, 1, 1 }, new double[] { 1, 1, 1, 1 }, new double[] { 1, 1, 1, 1 },
                new double[] { 1, 1, 1, 1 }, new double[] { 1, 1, 1, 1 }, new double[] { 2, 1, 1 }
            };
            const double pitch = 32, gap = 4, padX = 15 * pitch + 12;

            Canvas kb = new Canvas();
            kb.Width = padX + 4 * pitch - gap;
            kb.Height = 22 + gap + 5 * pitch - gap;
            kb.HorizontalAlignment = HorizontalAlignment.Center;
            kb.VerticalAlignment = VerticalAlignment.Center;

            double y = 0;
            for (int row = 0; row < 6; row++)
            {
                double h = row == 0 ? 22 : pitch - gap;
                double x = 0;
                foreach (double u in main[row]) { kb.Children.Add(MakeKey(x, y, u * pitch - gap, h)); x += u * pitch; }
                x = padX;
                foreach (double u in pad[row]) { kb.Children.Add(MakeKey(x, y, u * pitch - gap, h)); x += u * pitch; }
                y += h + gap;
            }
            host.Children.Add(kb);
        }

        Border MakeKey(double x, double y, double width, double height)
        {
            Border key = new Border();
            key.Width = width; key.Height = height;
            key.CornerRadius = new CornerRadius(5);
            key.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x19));
            key.BorderBrush = keyBrush;
            key.BorderThickness = new Thickness(1);
            Border legend = new Border();
            legend.Width = Math.Min(9, width - 12); legend.Height = 2;
            legend.CornerRadius = new CornerRadius(1);
            legend.Background = legendBrush;
            legend.HorizontalAlignment = HorizontalAlignment.Left;
            legend.VerticalAlignment = VerticalAlignment.Top;
            legend.Margin = new Thickness(6, 6, 0, 0);
            key.Child = legend;
            Canvas.SetLeft(key, x); Canvas.SetTop(key, y);
            return key;
        }

        void BuildPresets()
        {
            int[] cs = { 0xFF0000, 0xFF6000, 0xFFDC00, 0x00FF00, 0x00FFFF, 0x0000FF, 0xA000FF, 0xFF0080, 0xFFFFFF };
            foreach (int c in cs) presetPanel.Children.Add(MakeSwatch(c, false));

            LinearGradientBrush sat = new LinearGradientBrush();
            sat.StartPoint = new Point(0, 0); sat.EndPoint = new Point(1, 0);
            sat.GradientStops.Add(satFrom); sat.GradientStops.Add(satTo);
            satSl.Background = sat;
        }

        Button MakeSwatch(int packed, bool removable)
        {
            Button b = new Button();
            b.Style = (Style)w.FindResource("Swatch");
            b.Background = new SolidColorBrush(Color.FromRgb((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed));
            b.Click += delegate { SetColor((packed >> 16) & 255, (packed >> 8) & 255, packed & 255, "swatch"); };
            if (removable)
                b.MouseRightButtonUp += delegate { s.Custom.Remove(packed); RebuildCustom(); QueueSave(); };
            return b;
        }

        void RebuildCustom()
        {
            customPanel.Children.Clear();
            foreach (int c in s.Custom) customPanel.Children.Add(MakeSwatch(c, true));
            if (s.Custom.Count > 0)
            {
                TextBlock hint = new TextBlock();
                hint.Text = "sağ tık: sil";
                hint.FontSize = 11;
                hint.Foreground = (Brush)w.FindResource("Faint");
                hint.VerticalAlignment = VerticalAlignment.Center;
                hint.Margin = new Thickness(2, 0, 0, 8);
                customPanel.Children.Add(hint);
            }
        }

        void Wire()
        {
            F<Button>("btnMin").Click += delegate { w.WindowState = WindowState.Minimized; };
            F<Button>("btnClose").Click += delegate { w.Hide(); };
            w.Closing += delegate(object o, System.ComponentModel.CancelEventArgs e)
            {
                if (!exiting) { e.Cancel = true; w.Hide(); }
            };
            w.SourceInitialized += delegate
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                HwndSource.FromHwnd(h).AddHook(WndProc);
                Native.RegisterHotKey(h, HK_MODE, 0x0001 | 0x0002 | 0x4000, 0x4D);   // Ctrl+Alt+M
            };
            w.Activated += delegate { CheckForeground(); };

            for (int i = 0; i < Modes.Count; i++)
            {
                int m = i;
                tiles[i].Checked += delegate { if (!loading) { s.Mode = m; Apply(true); } };
            }

            swPower.Click += delegate { SetOn(swPower.IsChecked == true); };
            swAuto.IsChecked = IsAutoStart();
            swAuto.Click += delegate { SetAutoStart(swAuto.IsChecked == true); };

            hexBox.TextChanged += delegate
            {
                if (loading) return;
                int r, g, b;
                if (ColorUtil.TryHex(hexBox.Text, false, out r, out g, out b)) { hexBox.Tag = null; SetColor(r, g, b, "hex"); }
            };
            hexBox.KeyDown += delegate(object o, KeyEventArgs e) { if (e.Key == Key.Enter) { CommitHex(); Keyboard.ClearFocus(); } };
            hexBox.LostKeyboardFocus += delegate { CommitHex(); };

            RoutedPropertyChangedEventHandler<double> hs = delegate
            {
                if (loading) return;
                double r, g, b;
                ColorUtil.FromHsv(hueSl.Value / 360.0, satSl.Value / 100.0, 1, out r, out g, out b);
                SetColor((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255), "hsv");
            };
            hueSl.ValueChanged += hs;
            satSl.ValueChanged += hs;

            brightSl.ValueChanged += delegate { if (!loading) { s.Brightness = (int)Math.Round(brightSl.Value); source = "bright"; Apply(true); source = ""; } };
            speedSl.ValueChanged += delegate { if (!loading) { s.Speed = (int)Math.Round(speedSl.Value); source = "speed"; Apply(true); source = ""; } };

            F<Button>("btnSaveColor").Click += delegate
            {
                int c = (s.R << 16) | (s.G << 8) | s.B;
                if (s.Custom.Contains(c)) return;
                if (s.Custom.Count >= 11) s.Custom.RemoveAt(0);
                s.Custom.Add(c);
                RebuildCustom();
                QueueSave();
            };

            RadioButton tabLight = F<RadioButton>("tabLight"), tabApps = F<RadioButton>("tabApps"), tabSettings = F<RadioButton>("tabSettings");
            RoutedEventHandler tabs = delegate
            {
                pageLight.Visibility = tabLight.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                pageApps.Visibility = tabApps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                pageSettings.Visibility = tabSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                if (tabApps.IsChecked == true) RefreshRunning();
            };
            tabLight.Checked += tabs; tabApps.Checked += tabs; tabSettings.Checked += tabs;
            F<Button>("btnRefresh").Click += delegate { RefreshRunning(); };
        }

        void CommitHex()
        {
            int r, g, b;
            if (ColorUtil.TryHex(hexBox.Text, true, out r, out g, out b))
            {
                hexBox.Tag = null;
                SetColor(r, g, b, "");
            }
            else
            {
                hexBox.Tag = "bad";
            }
        }

        void BuildTray()
        {
            menu.Items.Add("Aç", null, delegate { ShowWindow(); });
            menu.Items.Add(new WF.ToolStripSeparator());
            foreach (int i in Modes.Order)
            {
                int m = i;
                WF.ToolStripMenuItem it = new WF.ToolStripMenuItem(Modes.Names[i]);
                it.Tag = m;
                it.Click += delegate { s.Mode = m; if (!s.On) s.On = true; Apply(true); };
                menu.Items.Add(it);
            }
            menu.Items.Add(new WF.ToolStripSeparator());
            WF.ToolStripMenuItem power = new WF.ToolStripMenuItem("Işığı kapat");
            power.Click += delegate { SetOn(!s.On); };
            menu.Items.Add(power);
            menu.Items.Add("Çıkış", null, delegate { Exit(); });
            menu.Opening += delegate
            {
                power.Text = s.On ? "Işığı kapat" : "Işığı aç";
                foreach (WF.ToolStripItem it in menu.Items)
                {
                    WF.ToolStripMenuItem mi = it as WF.ToolStripMenuItem;
                    if (mi != null && mi.Tag is int) mi.Checked = ((int)mi.Tag == s.Mode);
                }
            };
            tray.ContextMenuStrip = menu;
            tray.Text = "Kor · Klavye ışığı";
            tray.MouseClick += delegate(object o, WF.MouseEventArgs e) { if (e.Button == WF.MouseButtons.Left) ShowWindow(); };
            tray.Visible = true;
        }

        // ---------- durum ----------

        void QueueSave() { saveTimer.Stop(); saveTimer.Start(); }

        void SetColor(int r, int g, int b, string from)
        {
            s.R = r; s.G = g; s.B = b;
            source = from;
            Apply(true);
            source = "";
        }

        void SetOn(bool on)
        {
            if (s.On == on) { Apply(false); return; }
            s.On = on;
            if (on) engine.ForceResend();
            Apply(true);
        }

        // Ayarlari motora ve arayuze yansitir.
        void Apply(bool save)
        {
            int mode = s.Mode, r = s.R, g = s.G, b = s.B;
            if (activeRule != null)
            {
                r = activeRule.R; g = activeRule.G; b = activeRule.B;
                if (!Modes.UsesColor(mode)) mode = Modes.Static;
            }
            engine.R = r; engine.G = g; engine.B = b;
            engine.Speed = s.Speed;
            engine.Bright = (int)Math.Round(s.Brightness * 2.55);
            engine.Mode = mode;
            engine.On = s.On;
            SetHook(s.On && Modes.UsesKeys(mode));

            loading = true;
            tiles[s.Mode].IsChecked = true;
            if (source != "bright") brightSl.Value = s.Brightness;
            if (source != "speed") speedSl.Value = s.Speed;
            if (source != "hex") { hexBox.Text = ColorUtil.Hex(s.R, s.G, s.B); hexBox.Tag = null; }
            double h, sat, v;
            ColorUtil.ToHsv(s.R, s.G, s.B, out h, out sat, out v);
            if (source != "hsv")
            {
                if (sat > 0.001) hueSl.Value = h * 360.0;
                satSl.Value = sat * 100.0;
            }
            swPower.IsChecked = s.On;
            loading = false;

            double pr, pg, pb;
            ColorUtil.FromHsv(sat > 0.001 ? h : hueSl.Value / 360.0, 1, 1, out pr, out pg, out pb);
            satTo.Color = Color.FromRgb((byte)(pr * 255), (byte)(pg * 255), (byte)(pb * 255));

            swatch.Background = new SolidColorBrush(Color.FromRgb((byte)s.R, (byte)s.G, (byte)s.B));
            brightTxt.Text = "%" + s.Brightness;
            speedTxt.Text = s.Speed + " / 10";
            powerTxt.Text = s.On ? "Açık" : "Kapalı";
            bool col = Modes.UsesColor(s.Mode);
            colorCard.IsEnabled = col;
            colorCard.Opacity = col ? 1.0 : 0.4;
            speedSl.IsEnabled = Modes.UsesSpeed(s.Mode);

            // Vurgu rengi: secili rengin tam parlak hali (cok koyu renkte arayuz kaybolmasin)
            int ar = s.R, ag = s.G, ab = s.B;
            if (!col) { ar = 0; ag = 200; ab = 255; }
            int mx = Math.Max(ar, Math.Max(ag, ab));
            if (mx < 8) { ar = ag = ab = 255; mx = 255; }
            Color ac = Color.FromRgb((byte)(ar * 255 / mx), (byte)(ag * 255 / mx), (byte)(ab * 255 / mx));
            if (ac != accentColor)
            {
                // Kaynaktaki firca dondurulur; rengi degistirmek yerine yenisiyle degistir.
                accentColor = ac;
                w.Resources["Accent"] = new SolidColorBrush(ac);
            }

            string st = Modes.Names[s.Mode];
            if (col) st += "  ·  " + ColorUtil.Hex(s.R, s.G, s.B);
            st += "  ·  Parlaklık %" + s.Brightness;
            if (!s.On) st = "Işık kapalı";
            else if (activeRule != null) st += "  ·  " + activeRule.Exe + " kuralı etkin";
            statusText.Text = st;

            int ic = s.On ? ((accentColor.R << 16) | (accentColor.G << 8) | accentColor.B) : 0x464646;
            if (ic != lastIconColor) { lastIconColor = ic; UpdateIcon(ic); }

            if (save) QueueSave();
        }

        void UpdatePreview()
        {
            if (!w.IsVisible) return;
            int bright = s.On ? engine.Bright : 0;
            int key = engine.OutPacked ^ (bright << 24);
            if (key == lastPreview) return;
            lastPreview = key;

            double f = s.On ? 0.25 + 0.75 * bright / 255.0 : 0;
            if (bright == 0) f = 0;
            int p = engine.OutPacked;
            double r = ((p >> 16) & 255) * f, g = ((p >> 8) & 255) * f, b = (p & 255) * f;
            double i = Math.Max(r, Math.Max(g, b)) / 255.0;

            keyBrush.Color = Color.FromRgb(
                (byte)Math.Min(255, 0x26 * (1 - i) + r), (byte)Math.Min(255, 0x2A * (1 - i) + g), (byte)Math.Min(255, 0x35 * (1 - i) + b));
            byte cr = (byte)r, cg = (byte)g, cb = (byte)b;
            if (i > 0.001) { cr = (byte)Math.Min(255, r / i); cg = (byte)Math.Min(255, g / i); cb = (byte)Math.Min(255, b / i); }
            legendBrush.Color = Color.FromArgb((byte)(255 * Math.Sqrt(i)), cr, cg, cb);
            glowIn.Color = Color.FromArgb((byte)(95 * i), cr, cg, cb);
            glowOut.Color = Color.FromArgb(0, cr, cg, cb);
        }

        void UpdateIcon(int packed)
        {
            // Tepsi simgesi: ayni logo, o anki isik renginde.
            int cr = (packed >> 16) & 255, cg = (packed >> 8) & 255, cb = packed & 255;
            SD.Color c1 = SD.Color.FromArgb(cr, cg, cb);
            SD.Color c2 = SD.Color.FromArgb(cr + (255 - cr) * 2 / 5, cg + (255 - cg) * 2 / 5, cb + (255 - cb) * 2 / 5);
            using (SD.Bitmap bmp = Logo.Draw(32, c1, c2))
            {
                SD.Icon ic = Logo.ToIcon(bmp);
                tray.Icon = ic;
                if (currentIcon != null) currentIcon.Dispose();
                currentIcon = ic;
            }
        }

        // ---------- uygulama kurallari ----------

        void CheckForeground()
        {
            Rule found = null;
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                if (pid != 0 && pid != (uint)myPid)
                {
                    string exe;
                    using (Process p = Process.GetProcessById((int)pid)) exe = p.ProcessName.ToLowerInvariant() + ".exe";
                    foreach (Rule r in s.Rules) if (r.Exe == exe) { found = r; break; }
                }
            }
            catch { }
            if (found != activeRule) { activeRule = found; Apply(false); }
        }

        void RebuildRules()
        {
            rulesPanel.Children.Clear();
            emptyRules.Visibility = s.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (Rule rule in s.Rules) rulesPanel.Children.Add(MakeRuleRow(rule));
        }

        UIElement MakeRuleRow(Rule rule)
        {
            Border row = new Border();
            row.Style = (Style)w.FindResource("Card");
            row.Padding = new Thickness(14, 9, 8, 9);
            row.Margin = new Thickness(0, 0, 0, 8);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Ellipse dot = new Ellipse();
            dot.Width = 18; dot.Height = 18;
            dot.Stroke = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
            dot.Fill = new SolidColorBrush(Color.FromRgb((byte)rule.R, (byte)rule.G, (byte)rule.B));
            dot.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(dot);

            TextBlock name = new TextBlock();
            name.Text = rule.Exe;
            name.FontSize = 13.5;
            name.FontWeight = FontWeights.SemiBold;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(12, 0, 12, 0);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            g.Children.Add(name);

            TextBox hex = new TextBox();
            hex.Style = (Style)w.FindResource("Hex");
            hex.Width = 104; hex.Height = 32; hex.FontSize = 13;
            hex.Text = ColorUtil.Hex(rule.R, rule.G, rule.B);
            hex.TextChanged += delegate
            {
                int r, gg, b;
                if (!ColorUtil.TryHex(hex.Text, false, out r, out gg, out b)) return;
                hex.Tag = null;
                rule.R = r; rule.G = gg; rule.B = b;
                dot.Fill = new SolidColorBrush(Color.FromRgb((byte)r, (byte)gg, (byte)b));
                if (activeRule == rule) Apply(false);
                QueueSave();
            };
            hex.LostKeyboardFocus += delegate
            {
                int r, gg, b;
                if (ColorUtil.TryHex(hex.Text, true, out r, out gg, out b)) hex.Text = ColorUtil.Hex(r, gg, b);
                else hex.Tag = "bad";
            };
            Grid.SetColumn(hex, 2);
            g.Children.Add(hex);

            Button del = new Button();
            del.Style = (Style)w.FindResource("Del");
            del.Margin = new Thickness(6, 0, 0, 0);
            del.Click += delegate
            {
                s.Rules.Remove(rule);
                if (activeRule == rule) activeRule = null;
                RebuildRules();
                RefreshRunning();
                Apply(true);
            };
            Grid.SetColumn(del, 3);
            g.Children.Add(del);

            row.Child = g;
            return row;
        }

        void RefreshRunning()
        {
            runningPanel.Children.Clear();
            SortedDictionary<string, bool> names = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id != myPid && p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0)
                        names[p.ProcessName.ToLowerInvariant() + ".exe"] = true;
                }
                catch { }
                finally { p.Dispose(); }
            }
            foreach (Rule r in s.Rules) names.Remove(r.Exe);
            foreach (string n in names.Keys)
            {
                string exe = n;
                Button chip = new Button();
                chip.Style = (Style)w.FindResource("Chip");
                chip.Content = "+  " + exe;
                chip.Click += delegate
                {
                    Rule rule = new Rule();
                    rule.Exe = exe; rule.R = s.R; rule.G = s.G; rule.B = s.B;
                    s.Rules.Add(rule);
                    RebuildRules();
                    RefreshRunning();
                    QueueSave();
                };
                runningPanel.Children.Add(chip);
            }
            if (names.Count == 0)
            {
                TextBlock t = new TextBlock();
                t.Text = "Eklenecek açık uygulama yok.";
                t.Foreground = (Brush)w.FindResource("Muted");
                runningPanel.Children.Add(t);
            }
        }

        // ---------- Fn tuslari (Clevo gunlugu) ----------

        void StartLogWatch()
        {
            try
            {
                logWatcher = new EventLogWatcher(new EventLogQuery("OutLog", PathType.LogName));
                logWatcher.EventRecordWritten += delegate(object o, EventRecordWrittenEventArgs e)
                {
                    string msg = null;
                    try
                    {
                        if (e.EventRecord == null) return;
                        using (EventRecord rec = e.EventRecord)
                        {
                            if (rec.Properties.Count > 0) msg = rec.Properties[0].Value as string;
                            if (msg == null) msg = rec.FormatDescription();
                        }
                    }
                    catch { }
                    if (msg == null) return;
                    string clean = Regex.Replace(msg, @"\s+", " ").Trim();
                    w.Dispatcher.BeginInvoke((Action)delegate { OnClevoLog(clean); });
                };
                logWatcher.Enabled = true;
            }
            catch { logWatcher = null; }
        }

        void OnClevoLog(string msg)
        {
            if (msg.StartsWith("Arg001^", StringComparison.Ordinal))
            {
                int code;
                if (!int.TryParse(msg.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out code)) return;
                if (code == 131) ShowWindow();                                     // Fn + Numpad /
                else if (code == 129 || code == 130) { pendBright = code == 130 ? 1 : -1; pendingTimer.Stop(); pendingTimer.Start(); }
                else if (code == 159) { pendToggle = true; pendingTimer.Stop(); pendingTimer.Start(); }   // Fn + Numpad *
                return;
            }
            Match m = Regex.Match(msg, @"^Clevo\^Brightness\^(\d+)");
            if (m.Success)
            {
                // Clevo'nun 0-4 kademesini benimse; boylece ekrandaki gosterge ile ayni kalir.
                pendBright = 0;
                int level = Math.Max(0, Math.Min(4, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
                s.Brightness = level * 25;
                Apply(true);
                ResendSoon(350);
                return;
            }
            m = Regex.Match(msg, @"^Clevo\^Status\^\d+ .*Set Status: \d+ -> (\d+)");
            if (m.Success)
            {
                pendToggle = false;
                SetOn(m.Groups[1].Value != "0");
                ResendSoon(350);
                return;
            }
            if (msg.StartsWith("Fnkey:WMI_Write cmd=103", StringComparison.Ordinal) || msg.StartsWith("LED KB init", StringComparison.Ordinal))
                ResendSoon(350);
        }

        // Clevo tarafi tusa kendisi tepki vermediyse isi biz yapariz.
        void RunPending()
        {
            if (pendToggle) { pendToggle = false; SetOn(!s.On); }
            if (pendBright != 0)
            {
                int level = (int)Math.Round(s.Brightness / 25.0) + pendBright;
                pendBright = 0;
                s.Brightness = Math.Max(0, Math.Min(4, level)) * 25;
                if (!s.On) s.On = true;
                engine.ForceResend();
                Apply(true);
            }
        }

        void ResendSoon(int ms)
        {
            resendLeft = 2;
            resendTimer.Stop();
            resendTimer.Interval = TimeSpan.FromMilliseconds(ms);
            resendTimer.Start();
        }

        // ---------- sistem ----------

        // Tus efektleri icin: yalnizca "bir tusa basildi" bilgisini alir; hangi tus oldugunu okumaz, kaydetmez.
        void SetHook(bool on)
        {
            if (on && hook == IntPtr.Zero)
            {
                hookProc = HookCallback;
                hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0);
            }
            else if (!on && hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x0100 || msg == 0x0104) engine.KeyPressed();
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HK_MODE)
            {
                int idx = Array.IndexOf(Modes.Order, s.Mode);
                s.Mode = Modes.Order[(idx + 1) % Modes.Order.Length];
                if (!s.On) { s.On = true; engine.ForceResend(); }
                Apply(true);
                handled = true;
            }
            return IntPtr.Zero;
        }

        void StartShowListener()
        {
            Thread t = new Thread(delegate()
            {
                while (true)
                {
                    try { showSignal.WaitOne(); } catch { return; }
                    if (exiting) return;
                    w.Dispatcher.BeginInvoke((Action)delegate { ShowWindow(); });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunName, "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }

        void OnPowerMode(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) w.Dispatcher.BeginInvoke((Action)delegate { engine.ForceResend(); ResendSoon(3000); });
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock) w.Dispatcher.BeginInvoke((Action)delegate { engine.ForceResend(); });
        }

        void ShowWindow()
        {
            w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            w.Topmost = true; w.Topmost = false;   // tepsiden cagrilinca one gelsin
        }

        void Exit()
        {
            if (exiting) return;
            exiting = true;
            SystemEvents.PowerModeChanged -= OnPowerMode;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            try { if (logWatcher != null) { logWatcher.Enabled = false; logWatcher.Dispose(); } } catch { }
            try { Native.UnregisterHotKey(new WindowInteropHelper(w).Handle, HK_MODE); } catch { }
            SetHook(false);
            previewTimer.Stop(); fgTimer.Stop(); resendTimer.Stop(); pendingTimer.Stop(); saveTimer.Stop();
            s.Save();
            activeRule = null;
            engine.R = s.R; engine.G = s.G; engine.B = s.B;
            engine.Stop();
            tray.Visible = false;
            tray.Dispose();
            app.Shutdown();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Derleme yardimcisi: exe simgesini uretir (build.ps1 kullanir).
            if (args.Length == 2 && args[0] == "--make-icon") { Logo.WriteIco(args[1]); return; }

            bool created;
            using (Mutex mtx = new Mutex(true, "Kor_SingleInstance", out created))
            {
                bool newSignal;
                EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Kor_Show", out newSignal);
                if (!created)
                {
                    signal.Set();   // zaten calisiyor: penceresini one getir
                    return;
                }

                // Beklenmeyen hatalar %AppData%\Kor\hata.log dosyasina yazilir.
                AppDomain.CurrentDomain.UnhandledException += delegate(object o, UnhandledExceptionEventArgs e)
                {
                    try
                    {
                        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kor");
                        Directory.CreateDirectory(dir);
                        File.AppendAllText(System.IO.Path.Combine(dir, "hata.log"), DateTime.Now.ToString("s") + "\r\n" + e.ExceptionObject + "\r\n\r\n");
                    }
                    catch { }
                };

                Settings s = Settings.Load();
                try
                {
                    Native.PowerOn();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Klavye ışığına erişilemedi. InsydeDCHU.dll bu programla aynı klasörde olmalı.\n\n" + ex.Message,
                        "Kor", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                bool hidden = Array.IndexOf(args, "--tray") >= 0;
                Ui ui = new Ui(app, s, hidden, signal);
                app.Run();
                GC.KeepAlive(ui);
                GC.KeepAlive(mtx);
            }
        }
    }
}
