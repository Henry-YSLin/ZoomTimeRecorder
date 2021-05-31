using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Win32Interop.WinHandles;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Converters;
using System.ComponentModel;

namespace ZoomTimeRecorder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        System.Windows.Forms.NotifyIcon nIcon = new System.Windows.Forms.NotifyIcon();
        ContextMenu cm;
        ZoomState lastState;
        System.Timers.Timer timer;
        List<ZoomRecord> records = new List<ZoomRecord>();
        public List<ZoomSummary> Summary
        {
            get
            {
                List<ZoomSummary> ret = new List<ZoomSummary>();
                for (int i = 0; i < records.Count;i++)
                {
                    var x = records[i];
                    if (!x.State.IsOpened())
                        continue;
                    ret.Add(new ZoomSummary
                    {
                        StartTime = x.Time,
                        StartTimeApprox = x.State.IsLate(),
                        EndTime = i == records.Count - 1 ? DateTime.Now : records[i + 1].Time,
                        EndTimeApprox = i == records.Count - 1 ? true : records[i + 1].State.IsLate(),
                    });
                }
                for (int i = ret.Count - 1; i >= 1; i--)
                {
                    DateTime curr = ret[i].StartTime.Date.AddDays(-ret[i].StartTime.Day);
                    DateTime prev = ret[i - 1].StartTime.Date.AddDays(-ret[i - 1].StartTime.Day);
                    if (curr != prev)
                    {
                        ret.Insert(i, new ZoomSummary
                        {
                            MonthSeparator = true,
                            StartTime = curr,
                        });
                    }
                }
                if (ret.Count > 0)
                    ret.Insert(0, new ZoomSummary
                    {
                        MonthSeparator = true,
                        StartTime = ret[0].StartTime.Date.AddDays(-ret[0].StartTime.Day),
                    });
                return ret; 
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowThreadProcessId(IntPtr handle, out uint processId);

        public static string GetProcessName(IntPtr hwnd)
        {
            uint pid = 0;
            GetWindowThreadProcessId(hwnd, out pid);
            Process proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }

        private bool checkZoom()
        {
            var window = TopLevelWindowUtils.FindWindow(x =>
            {
                var s = x.GetWindowText();
                return (s.Contains("Zoom Meeting") || s.Contains("Meeting Controls")) && GetProcessName(x.RawPtr).Contains("Zoom");
            });
            return window.IsValid;
        }

        private void saveRecords()
        {
            File.WriteAllText("record.json", JsonConvert.SerializeObject(records, Formatting.Indented));
        }

        public MainWindow()
        {
            InitializeComponent();
            nIcon.Icon = new System.Drawing.Icon(@"Icons/normal.ico");
            nIcon.Visible = true;
            nIcon.Click += NIcon_Click;

            cm = FindResource("trayMenu") as ContextMenu;

            int freq = 1000 * 60 * 15;

            if (File.Exists("freq.config"))
            {
                int.TryParse(File.ReadAllText("freq.config"), out freq);
            }
            else
            {
                File.WriteAllText("freq.config", freq.ToString());
            }

            timer = new System.Timers.Timer(freq);
            timer.Elapsed += Timer_Elapsed;
            timer.Start();

            if (File.Exists("record.json"))
            {
                records = JsonConvert.DeserializeObject<List<ZoomRecord>>(File.ReadAllText("record.json"));
            }
            bool isOpen = checkZoom();
            if (isOpen)
            {
                lastState = ZoomState.AlreadyOpen;
            }
            else if (records.Any() && records.Last().State.IsOpened())
            {
                lastState = ZoomState.AlreadyClosed;
            }
            else
            {
                lastState = ZoomState.Closing;
            }
            if (lastState.IsLate())
            {
                records.Add(new ZoomRecord(DateTime.Now, lastState));
                saveRecords();
            }
            if (lastState.IsOpened())
                nIcon.Icon = new System.Drawing.Icon(@"Icons/recording.ico");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        }

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            ZoomState prevState = lastState;
            bool isOpen = checkZoom();
            if (lastState.IsOpened() && !isOpen)
                lastState = ZoomState.Closing;
            if (lastState.IsClosed() && isOpen)
                lastState = ZoomState.Opening;
            if (prevState != lastState)
            {
                records.Add(new ZoomRecord(DateTime.Now, lastState));
                saveRecords();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
                if (lastState.IsOpened())
                    nIcon.Icon = new System.Drawing.Icon(@"Icons/recording.ico");
                else
                    nIcon.Icon = new System.Drawing.Icon(@"Icons/normal.ico");
            }
        }

        private void NIcon_Click(object sender, EventArgs e)
        {
            cm.IsOpen = true;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (nIcon != null)
            {
                nIcon.Icon = null;
                nIcon.Dispose();
                nIcon = null;
            }
            Environment.Exit(0);
        }

        private void MenuItem_Click_1(object sender, RoutedEventArgs e)
        {
            cm.IsOpen = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Visibility = Visibility.Hidden;
        }

        private void btnViewRecords_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Visible;
        }
    }

    public enum ZoomState
    {
        AlreadyOpen,
        AlreadyClosed,
        Opening,
        Closing
    }

    public static class ZoomStateExtension
    {
        public static bool IsOpened(this ZoomState state)
        {
            return state == ZoomState.AlreadyOpen || state == ZoomState.Opening;
        }
        public static bool IsClosed(this ZoomState state)
        {
            return state == ZoomState.AlreadyClosed || state == ZoomState.Closing;
        }
        public static bool IsLate(this ZoomState state)
        {
            return state == ZoomState.AlreadyClosed || state == ZoomState.AlreadyOpen;
        }
    }

    public class ZoomRecord
    {
        public DateTime Time { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public ZoomState State { get; set; }
        public ZoomRecord(DateTime time, ZoomState state) => (Time, State) = (time, state);
    }

    public class ZoomSummary
    {
        /// <summary>
        /// Indicate that this item is only a separator, not a real record
        /// </summary>
        public bool MonthSeparator { get; set; } = false;
        public DateTime StartTime { get; set; }
        public bool StartTimeApprox { get; set; }
        public DateTime EndTime { get; set; }
        public bool EndTimeApprox { get; set; }
        public TimeSpan Duration { 
            get {
                return EndTime - StartTime;
            }
        }
    }
}
