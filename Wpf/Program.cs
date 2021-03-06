﻿using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Properties;
using Application = System.Windows.Application;
using FontStyle = System.Drawing.FontStyle;
using MessageBox = System.Windows.MessageBox;
using PowerLineStatus = System.Windows.Forms.PowerLineStatus;
using SystemFonts = System.Drawing.SystemFonts;

namespace Wpf
{
    internal class Program
    {
        internal const string Id = "f05f920a-c997-4817-84bd-c54d87e40625";

        [STAThread]
        public static void Main()
        {
            using (new Mutex(true, Id, out var isNewInstance))
            {
                if (!isNewInstance)
                {
                    return;
                }

                var app = new Application {ShutdownMode = ShutdownMode.OnExplicitShutdown};

                app.DispatcherUnhandledException += (_, e) => { ShowFeedbackMessage(e.Exception); };

                AppDomain.CurrentDomain.UnhandledException += (_, e) => { ShowFeedbackMessage(e.ExceptionObject); };

                TaskScheduler.UnobservedTaskException += (_, e) => { ShowFeedbackMessage(e.Exception); };

                void ShowFeedbackMessage(object exception)
                {
                    MessageBox.Show("Battery Percentage Icon has run into an error. You can help to fix this by:\r\n" +
                                    "1. press Ctrl+C on this message\r\n" +
                                    "2. paste it in an email\r\n" +
                                    "3. send it to soleon@live.com\r\n\r\n" +
                                    (exception is Exception exp
                                        ? exp.ToString()
                                        : $"Error type: {exception.GetType().FullName}\r\n{exception}"),
                        "You Found An Error");
                }

                // Right click menu with "Exit" item to exit this application.
                using (var exitMenuItem = new ToolStripMenuItem("Exit"))
                using (var settingsMenuItem = new ToolStripMenuItem("Settings"))
                using (var detailsMenuItem = new ToolStripMenuItem("Battery Details"))
                using (var menu = new ContextMenuStrip {Items = {detailsMenuItem, settingsMenuItem, exitMenuItem}})
                using (var notifyIcon = new NotifyIcon {Visible = true, ContextMenuStrip = menu})
                {
                    void ActivateDialog<T>() where T : Window, new()
                    {
                        var existingWindow = app.Windows.OfType<T>().FirstOrDefault();
                        if (existingWindow == null)
                        {
                            new T().Show();
                        }
                        else
                        {
                            existingWindow.Activate();
                        }
                    }

                    exitMenuItem.Click += (_, __) => app.Shutdown();
                    settingsMenuItem.Click += (_, __) => ActivateDialog<SettingsWindow>();
                    detailsMenuItem.Click += (_, __) => ActivateDialog<DetailsWindow>();

                    // Setup variables used in the repetitively ran "Update" local function.
                    (NotificationType Type, DateTime DateTime) lastNotification = (default, default);
                    var settings = Settings.Default;
                    var chargingBrush = new SolidBrush(settings.ChargingColor);
                    var lowBrush = new SolidBrush(settings.LowColor);
                    var criticalBrush = new SolidBrush(settings.CriticalColor);

                    // Show balloon notification when the tray icon is clicked.
                    notifyIcon.MouseClick += (_, e) =>
                    {
                        if (e.Button == MouseButtons.Left)
                        {
                            ActivateDialog<DetailsWindow>();
                        }
                    };

                    // Update battery status when the computer resumes or when the power status changes.
                    SystemEvents.PowerModeChanged += (_, e) =>
                    {
                        if (e.Mode == PowerModes.Resume || e.Mode == PowerModes.StatusChange)
                        {
                            Update();
                        }
                    };

                    // Update battery status when the display settings change.
                    // This will redrew the tray icon to ensure optimal icon resolution
                    // under the current display settings.
                    SystemEvents.DisplaySettingsChanged += (_, __) => Update();

                    // Initial update.
                    Update();

                    // Setup timer to update the tray icon.
                    var timer = new DispatcherTimer {Interval = TimeSpan.FromSeconds(settings.RefreshSeconds)};
                    timer.Tick += (_, __) => Update();
                    timer.Start();

                    // Handle settings change.
                    settings.PropertyChanged += (_, e) =>
                    {
                        switch (e.PropertyName)
                        {
                            case nameof(settings.RefreshSeconds):
                                timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
                                break;
                            case nameof(settings.ChargingColor):
                                chargingBrush.Color = settings.ChargingColor;
                                Update();
                                break;
                            case nameof(settings.LowColor):
                                lowBrush.Color = settings.LowColor;
                                Update();
                                break;
                            case nameof(settings.CriticalColor):
                                criticalBrush.Color = settings.CriticalColor;
                                Update();
                                break;
                            case nameof(settings.HighNotification):
                            case nameof(settings.LowNotification):
                            case nameof(settings.CriticalNotification):
                            case nameof(settings.FullNotification):
                                Update();
                                break;
                        }
                    };

                    // Run application and hold the thread.
                    app.Run();

                    // Local function to update the tray icon.
                    void Update()
                    {
                        var powerStatus = SystemInformation.PowerStatus;
                        var batteryChargeStatus = powerStatus.BatteryChargeStatus;
                        var percent = (int) Math.Round(powerStatus.BatteryLifePercent * 100);

                        var notificationType = NotificationType.None;
                        Brush brush;
                        string trayIconText;
                        if (batteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
                        {
                            // When no battery detected.
                            trayIconText = "❌";
                            SetBrush();
                            notifyIcon.BalloonTipTitle = null;
                            notifyIcon.BalloonTipText = "No battery detected";
                            notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                        }
                        else if (batteryChargeStatus.HasFlag(BatteryChargeStatus.Unknown))
                        {
                            // When battery status is unknown.
                            trayIconText = "❓";
                            SetBrush();
                            notifyIcon.BalloonTipTitle = null;
                            notifyIcon.BalloonTipText = "Battery status unknown";
                            notifyIcon.BalloonTipIcon = ToolTipIcon.Error;
                        }
                        else
                        {
                            // When battery status is normal, display percentage in tray icon.
                            trayIconText = percent.ToString();
                            if (batteryChargeStatus.HasFlag(BatteryChargeStatus.Charging))
                            {
                                // When the battery is charging.
                                brush = chargingBrush;
                                notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                                if (powerStatus.BatteryFullLifetime > 0)
                                {
                                    notifyIcon.BalloonTipTitle = percent + "% charging";
                                    notifyIcon.BalloonTipText =
                                        GetReadableDuration(powerStatus.BatteryFullLifetime) + " until fully charged";
                                }
                                else
                                {
                                    notifyIcon.BalloonTipTitle = null;
                                    notifyIcon.BalloonTipText = percent + "% charging";
                                }

                                SetHighOrFullNotification();
                            }
                            else
                            {
                                // When battery is not charging.
                                if (batteryChargeStatus.HasFlag(BatteryChargeStatus.Critical))
                                {
                                    // When battery capacity is critical.
                                    brush = criticalBrush;
                                    notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                                    if (settings.CriticalNotification)
                                    {
                                        notificationType = NotificationType.Critical;
                                    }
                                }
                                else if (batteryChargeStatus.HasFlag(BatteryChargeStatus.Low))
                                {
                                    // When battery capacity is low.
                                    brush = lowBrush;
                                    notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                                    if (settings.LowNotification)
                                    {
                                        notificationType = NotificationType.Low;
                                    }
                                }
                                else
                                {
                                    // When battery capacity is normal.
                                    SetBrush();
                                    notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                                    SetHighOrFullNotification();
                                }

                                if (powerStatus.BatteryLifeRemaining > 0)
                                {
                                    notifyIcon.BalloonTipTitle = percent + "% " +
                                                                 (powerStatus.PowerLineStatus == PowerLineStatus.Online
                                                                     ? "connected (not charging)"
                                                                     : "on battery");
                                    notifyIcon.BalloonTipText =
                                        GetReadableDuration(powerStatus.BatteryLifeRemaining) + " remaining";
                                }
                                else
                                {
                                    notifyIcon.BalloonTipTitle = null;
                                    notifyIcon.BalloonTipText = percent + "% " +
                                                                (powerStatus.PowerLineStatus == PowerLineStatus.Online
                                                                    ? "connected (not charging)"
                                                                    : "on battery");
                                }
                            }

                            void SetHighOrFullNotification()
                            {
                                if (percent == settings.HighNotificationValue && settings.HighNotification)
                                {
                                    notificationType = NotificationType.High;
                                }
                                else if (percent == 100 && settings.FullNotification)
                                {
                                    notificationType = NotificationType.Full;
                                }
                            }

                            // Local function to get readable time span from seconds.
                            string GetReadableDuration(int seconds)
                            {
                                var hours = seconds / 3600;
                                var minutes = seconds % 3600 / 60;
                                return hours > 0
                                    ? hours > 1
                                        ? hours + " hours"
                                        : hours + " hour"
                                    : minutes > 0
                                        ? minutes > 1
                                            ? minutes + " minutes"
                                            : minutes + "minute"
                                        : seconds > 0
                                            ? "less than 1 minute"
                                            : null;
                            }
                        }

                        // Set tray icon tool tip based on the balloon notification texts.
                        notifyIcon.Text = notifyIcon.BalloonTipTitle == null
                            ? notifyIcon.BalloonTipText
                            : notifyIcon.BalloonTipTitle + Environment.NewLine + notifyIcon.BalloonTipText;

                        // Local function to set normal brush according to the Windows theme used.
                        void SetBrush()
                        {
                            brush = IsUsingLightTheme() ? Brushes.Black : Brushes.White;
                        }

                        int textWidth, textHeight;
                        Font font;

                        // Measure the rendered size of tray icon text under the current system DPI setting.
                        using (var bitmap = new Bitmap(1, 1))
                        {
                            SizeF size;
                            using (var graphics = Graphics.FromImage(bitmap))
                            {
                                // Use the default menu font scaled to the current system DPI setting.
                                font = new Font(SystemFonts.DefaultFont.FontFamily, 8);

                                // Measure the rendering size of the tray icon text using this fort.
                                size = graphics.MeasureString(trayIconText, font);
                            }

                            // Round the size to integer.
                            textWidth = (int) Math.Round(size.Width);
                            textHeight = (int) Math.Round(size.Height);
                        }

                        // Use the larger number of the text size as the dimension of the square tray icon.
                        var iconDimension = Math.Max(textWidth, textHeight);

                        // Draw the tray icon.
                        using (var bitmap = new Bitmap(iconDimension, iconDimension))
                        {
                            using (var graphics = Graphics.FromImage(bitmap))
                            {
                                if (IsUsingLightTheme())
                                {
                                    // Using anti aliasing provides the best clarity in Windows 10 light theme.
                                    // The default ClearType rendering causes black edges around the text making
                                    // it thick and pixelated.
                                    graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                                }

                                // Scale the text to 1.3 times bigger.
                                graphics.ScaleTransform(1.4f, 1.4f);

                                // Draw the text, with a starting position aim to centre align the text,
                                // but removing about 1 percent from top and left.
                                graphics.DrawString(trayIconText, font, brush,
                                    (iconDimension - textWidth) / 2f - textWidth * 0.13f,
                                    (iconDimension - textHeight) / 2f - textHeight * 0.13f);

                                // The above scaling and start position alignments aim to remove the
                                // padding of the font so that the text fills the tray icon edge to edge.
                            }

                            // Set tray icon from the drawn bitmap image.
                            var handle = bitmap.GetHicon();
                            try
                            {
                                notifyIcon.Icon?.Dispose();
                                notifyIcon.Icon = Icon.FromHandle(handle);
                            }
                            finally
                            {
                                // Destroy icon hand to release it from memory as soon as it's set to the tray.
                                DestroyIcon(handle);

                                // Note, updating the tray icon in anyway after this call
                                // will cause the tray icon to become blank.
                                // This should be the very last call when updating the tray icon.
                            }
                        }

                        // Check and send notification.
                        if (notificationType == NotificationType.None)
                        {
                            // No notification required.
                            return;
                        }

                        var utcNow = DateTime.UtcNow;
                        if (lastNotification.Type != notificationType)
                        {
                            // Notification required, and battery status has changed from last notification, notify user.
                            notifyIcon.ShowBalloonTip(0);
                        }
                        else
                        {
                            if (utcNow - lastNotification.DateTime > TimeSpan.FromMinutes(5))
                            {
                                // Notification required, but battery status is the same as last notification for more than 5 minutes,
                                // notify user.
                                notifyIcon.ShowBalloonTip(0);
                            }
                        }

                        lastNotification = (notificationType, utcNow);
                    }

                    bool IsUsingLightTheme()
                    {
                        return 1.Equals(Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\",
                            false)?.GetValue("SystemUsesLightTheme", null));
                    }
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private enum NotificationType : byte
        {
            None = 0,
            Critical,
            Low,
            High,
            Full
        }
    }
}