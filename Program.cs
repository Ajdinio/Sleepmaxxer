using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SleepMaxxer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            NativeMethods.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var context = new SleepMaxxerContext())
            {
                Application.Run(context);
            }
        }
    }

    internal sealed class SleepMaxxerContext : ApplicationContext
    {
        private readonly AppSettings settings;
        private readonly ColorFilterManager colorFilter;
        private readonly ControlForm control;
        private readonly NotifyIcon trayIcon;
        private bool exiting;

        public SleepMaxxerContext()
        {
            settings = AppSettings.Load();
            colorFilter = new ColorFilterManager();
            control = new ControlForm(settings);
            trayIcon = CreateTrayIcon();

            control.FilterToggled += delegate { ApplySettings(); };
            control.IntensityChanged += delegate { ApplySettings(); };
            control.MinimizeToTrayChanged += delegate { SaveSettings(); };
            control.ExitRequested += delegate { ExitApplication(); };
            control.FormClosing += Control_FormClosing;

            SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;

            control.Show();
            ApplySettings();
        }

        private NotifyIcon CreateTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open window", null, delegate { ShowControlWindow(); });
            menu.Items.Add("Toggle filter", null, delegate
            {
                settings.FilterEnabled = !settings.FilterEnabled;
                control.SyncFromSettings(settings);
                ApplySettings();
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitApplication(); });

            var icon = new NotifyIcon();
            icon.Icon = SystemIcons.Application;
            icon.Text = "SleepMaxxer";
            icon.Visible = true;
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate { ShowControlWindow(); };
            return icon;
        }

        private void ApplySettings()
        {
            SaveSettings();

            if (settings.FilterEnabled)
            {
                colorFilter.Apply(settings.IntensityPercent);
            }
            else
            {
                colorFilter.Restore();
            }

            trayIcon.Text = "SleepMaxxer - Filter " + (settings.FilterEnabled ? "on" : "off");
        }

        private void SaveSettings()
        {
            settings.Save();
        }

        private void ShowControlWindow()
        {
            if (!control.Visible)
            {
                control.Show();
            }

            if (control.WindowState == FormWindowState.Minimized)
            {
                control.WindowState = FormWindowState.Normal;
            }

            control.Activate();
            control.BringToFront();
        }

        private void Control_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (exiting)
            {
                return;
            }

            if (settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                control.Hide();
                trayIcon.ShowBalloonTip(1200, "SleepMaxxer is still running", "Double-click the tray icon to open controls.", ToolTipIcon.Info);
                return;
            }

            e.Cancel = true;
            control.BeginInvoke(new MethodInvoker(ExitApplication));
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (control.IsHandleCreated)
            {
                control.BeginInvoke(new MethodInvoker(delegate
                {
                    RefreshDisplayFilter();
                }));
                return;
            }

            RefreshDisplayFilter();
        }

        private void RefreshDisplayFilter()
        {
            if (settings.FilterEnabled)
            {
                colorFilter.Apply(settings.IntensityPercent);
            }
        }

        private void ExitApplication()
        {
            exiting = true;
            SaveSettings();
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            colorFilter.Restore();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            colorFilter.Dispose();
            control.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trayIcon.Dispose();
                colorFilter.Dispose();
                control.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class ControlForm : Form
    {
        private readonly AppSettings settings;
        private readonly CheckBox filterToggle;
        private readonly TrackBar intensitySlider;
        private readonly Label intensityValue;
        private readonly CheckBox minimizeToTrayToggle;
        private bool syncing;

        public event EventHandler FilterToggled;
        public event EventHandler IntensityChanged;
        public event EventHandler MinimizeToTrayChanged;
        public event EventHandler ExitRequested;

        public ControlForm(AppSettings settings)
        {
            this.settings = settings;
            Text = "SleepMaxxer";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(360, 236);
            BackColor = Color.FromArgb(24, 24, 24);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

            var title = new Label
            {
                Text = "SleepMaxxer",
                AutoSize = false,
                Location = new Point(22, 18),
                Size = new Size(316, 30),
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 92, 92)
            };

            filterToggle = new CheckBox
            {
                Text = "Red filter active",
                AutoSize = true,
                Location = new Point(26, 68),
                Checked = settings.FilterEnabled
            };
            filterToggle.CheckedChanged += delegate
            {
                if (syncing) return;
                settings.FilterEnabled = filterToggle.Checked;
                OnFilterToggled();
            };

            var intensityLabel = new Label
            {
                Text = "Intensity",
                AutoSize = true,
                Location = new Point(26, 106)
            };

            intensityValue = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(284, 102),
                Size = new Size(54, 24)
            };

            intensitySlider = new TrackBar
            {
                Location = new Point(22, 132),
                Size = new Size(318, 38),
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 1,
                LargeChange = 10,
                Value = Clamp(settings.IntensityPercent, 0, 100)
            };
            intensitySlider.ValueChanged += delegate
            {
                settings.IntensityPercent = intensitySlider.Value;
                intensityValue.Text = settings.IntensityPercent + "%";
                if (!syncing) OnIntensityChanged();
            };

            minimizeToTrayToggle = new CheckBox
            {
                Text = "Minimize to tray when closing",
                AutoSize = true,
                Location = new Point(26, 178),
                Checked = settings.MinimizeToTrayOnClose
            };
            minimizeToTrayToggle.CheckedChanged += delegate
            {
                if (syncing) return;
                settings.MinimizeToTrayOnClose = minimizeToTrayToggle.Checked;
                OnMinimizeToTrayChanged();
            };

            var exitButton = new Button
            {
                Text = "Exit",
                Location = new Point(242, 198),
                Size = new Size(96, 28),
                FlatStyle = FlatStyle.Flat
            };
            exitButton.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            exitButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(72, 36, 36);
            exitButton.ForeColor = Color.White;
            exitButton.Click += delegate { OnExitRequested(); };

            Controls.Add(title);
            Controls.Add(filterToggle);
            Controls.Add(intensityLabel);
            Controls.Add(intensityValue);
            Controls.Add(intensitySlider);
            Controls.Add(minimizeToTrayToggle);
            Controls.Add(exitButton);

            SyncFromSettings(settings);
        }

        public void SyncFromSettings(AppSettings source)
        {
            syncing = true;
            filterToggle.Checked = source.FilterEnabled;
            intensitySlider.Value = Clamp(source.IntensityPercent, 0, 100);
            intensityValue.Text = source.IntensityPercent + "%";
            minimizeToTrayToggle.Checked = source.MinimizeToTrayOnClose;
            syncing = false;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void OnFilterToggled()
        {
            var handler = FilterToggled;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnIntensityChanged()
        {
            var handler = IntensityChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnMinimizeToTrayChanged()
        {
            var handler = MinimizeToTrayChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnExitRequested()
        {
            var handler = ExitRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    internal sealed class ColorFilterManager : IDisposable
    {
        private readonly GammaRampFilter gammaRampFilter = new GammaRampFilter();
        private bool magnifierInitialized;
        private bool disposed;

        public ColorFilterManager()
        {
            try
            {
                magnifierInitialized = NativeMethods.MagInitialize();
            }
            catch
            {
                magnifierInitialized = false;
            }
        }

        public void Apply(int percent)
        {
            percent = Clamp(percent, 0, 100);
            if (percent == 0)
            {
                Restore();
                return;
            }

            if (TryApplyColorMatrix(percent))
            {
                gammaRampFilter.Restore();
                return;
            }

            gammaRampFilter.Apply(percent);
        }

        public void Restore()
        {
            if (magnifierInitialized)
            {
                var identity = NativeMethods.CreateColorEffect(CreateIdentityMatrix());
                NativeMethods.MagSetFullscreenColorEffect(ref identity);
            }

            gammaRampFilter.Restore();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Restore();

            if (magnifierInitialized)
            {
                NativeMethods.MagUninitialize();
                magnifierInitialized = false;
            }

            disposed = true;
        }

        private bool TryApplyColorMatrix(int percent)
        {
            if (!magnifierInitialized)
            {
                return false;
            }

            var effect = NativeMethods.CreateColorEffect(CreateRedTintMatrix(percent / 100.0f));
            return NativeMethods.MagSetFullscreenColorEffect(ref effect);
        }

        private static float[] CreateIdentityMatrix()
        {
            return new[]
            {
                1f, 0f, 0f, 0f, 0f,
                0f, 1f, 0f, 0f, 0f,
                0f, 0f, 1f, 0f, 0f,
                0f, 0f, 0f, 1f, 0f,
                0f, 0f, 0f, 0f, 1f
            };
        }

        private static float[] CreateRedTintMatrix(float strength)
        {
            strength = Math.Max(0f, Math.Min(1f, strength));

            var identity = CreateIdentityMatrix();
            var redTint = new[]
            {
                1.00f, 0.05f, 0.00f, 0f, 0f,
                0.62f, 0.04f, 0.00f, 0f, 0f,
                0.32f, 0.01f, 0.00f, 0f, 0f,
                0.00f, 0.00f, 0.00f, 1f, 0f,
                0.00f, 0.00f, 0.00f, 0f, 1f
            };

            var result = new float[25];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = identity[i] + ((redTint[i] - identity[i]) * strength);
            }

            return result;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    internal sealed class GammaRampFilter
    {
        private const int RampSize = 256;
        private const int ChannelCount = 3;
        private readonly Dictionary<string, ushort[]> originalRamps = new Dictionary<string, ushort[]>();

        public void Apply(int percent)
        {
            var ramp = BuildRamp(percent / 100.0);
            foreach (var screen in Screen.AllScreens)
            {
                using (var dc = DisplayDeviceContext.Create(screen.DeviceName))
                {
                    if (dc.Handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!originalRamps.ContainsKey(screen.DeviceName))
                    {
                        var original = new ushort[RampSize * ChannelCount];
                        if (NativeMethods.GetDeviceGammaRamp(dc.Handle, original))
                        {
                            originalRamps[screen.DeviceName] = original;
                        }
                    }

                    NativeMethods.SetDeviceGammaRamp(dc.Handle, ramp);
                }
            }
        }

        public void Restore()
        {
            foreach (var pair in originalRamps)
            {
                using (var dc = DisplayDeviceContext.Create(pair.Key))
                {
                    if (dc.Handle != IntPtr.Zero)
                    {
                        NativeMethods.SetDeviceGammaRamp(dc.Handle, pair.Value);
                    }
                }
            }

            originalRamps.Clear();
        }

        private static ushort[] BuildRamp(double strength)
        {
            strength = Math.Max(0.0, Math.Min(1.0, strength));

            var ramp = new ushort[RampSize * ChannelCount];
            var redGamma = 1.0 - (0.18 * strength);
            var greenGamma = 1.0 + (7.0 * strength);
            var blueGamma = 1.0 + (11.0 * strength);
            var greenScale = 1.0 - (0.92 * strength);
            var blueScale = 1.0 - (0.99 * strength);

            for (var i = 0; i < RampSize; i++)
            {
                var x = i / 255.0;
                ramp[i] = ToRampValue(Math.Pow(x, redGamma));
                ramp[i + RampSize] = ToRampValue(Math.Pow(x, greenGamma) * greenScale);
                ramp[i + (RampSize * 2)] = ToRampValue(Math.Pow(x, blueGamma) * blueScale);
            }

            return ramp;
        }

        private static ushort ToRampValue(double value)
        {
            value = Math.Max(0.0, Math.Min(1.0, value));
            return (ushort)Math.Round(value * 65535.0);
        }
    }

    internal sealed class DisplayDeviceContext : IDisposable
    {
        public IntPtr Handle { get; private set; }

        private DisplayDeviceContext(IntPtr handle)
        {
            Handle = handle;
        }

        public static DisplayDeviceContext Create(string deviceName)
        {
            return new DisplayDeviceContext(NativeMethods.CreateDC("DISPLAY", deviceName, null, IntPtr.Zero));
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }

    internal sealed class AppSettings
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SleepMaxxer");

        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.ini");

        public bool FilterEnabled = true;
        public int IntensityPercent = 35;
        public bool MinimizeToTrayOnClose = true;

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(SettingsPath))
            {
                return settings;
            }

            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key.Equals("FilterEnabled", StringComparison.OrdinalIgnoreCase))
                {
                    settings.FilterEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (key.Equals("IntensityPercent", StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (int.TryParse(value, out parsed))
                    {
                        settings.IntensityPercent = Math.Max(0, Math.Min(100, parsed));
                    }
                }
                else if (key.Equals("MinimizeToTrayOnClose", StringComparison.OrdinalIgnoreCase))
                {
                    settings.MinimizeToTrayOnClose = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }

            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(SettingsDirectory);
            var content = new StringBuilder();
            content.AppendLine("FilterEnabled=" + FilterEnabled);
            content.AppendLine("IntensityPercent=" + IntensityPercent);
            content.AppendLine("MinimizeToTrayOnClose=" + MinimizeToTrayOnClose);
            File.WriteAllText(SettingsPath, content.ToString());
        }
    }

    internal static class SystemEvents
    {
        public static event EventHandler DisplaySettingsChanged
        {
            add { Microsoft.Win32.SystemEvents.DisplaySettingsChanged += value; }
            remove { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= value; }
        }
    }

    internal static class NativeMethods
    {
        public static void EnableDpiAwareness()
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // Older Windows versions can still run the filter without this hint.
            }
        }

        public static MagColorEffect CreateColorEffect(float[] matrix)
        {
            return new MagColorEffect
            {
                Transform = matrix
            };
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("Magnification.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool MagInitialize();

        [DllImport("Magnification.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool MagUninitialize();

        [DllImport("Magnification.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool MagSetFullscreenColorEffect(ref MagColorEffect effect);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateDC(string driverName, string deviceName, string output, IntPtr initData);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool GetDeviceGammaRamp(IntPtr dc, ushort[] ramp);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool SetDeviceGammaRamp(IntPtr dc, ushort[] ramp);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MagColorEffect
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] Transform;
    }
}
