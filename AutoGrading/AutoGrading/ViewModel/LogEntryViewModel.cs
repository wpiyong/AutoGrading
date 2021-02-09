using AutoGrading.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Interactivity;
using ViewModelLib;

namespace AutoGrading.ViewModel
{
    public class AutoScrollBehavior : Behavior<ListBox>
    {
        protected override void OnAttached()
        {
            ListBox listBox = AssociatedObject;
            ((INotifyCollectionChanged)listBox.Items).CollectionChanged += OnListBox_CollectionChanged;
        }

        protected override void OnDetaching()
        {
            ListBox listBox = AssociatedObject;
            ((INotifyCollectionChanged)listBox.Items).CollectionChanged -= OnListBox_CollectionChanged;
        }

        private void OnListBox_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ListBox listBox = AssociatedObject;
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // scroll the new item into view   
                listBox.ScrollIntoView(e.NewItems[0]);
            }
        }
    }

    public class LogEntryViewModel : ViewModelBase
    {
        ObservableCollection<LogEntry> _logEntries;

        readonly object _locker = new object();

        public LogEntryViewModel()
        {
            base.DisplayName = "LogEntryViewModel";
            ShowPopup = false;
            _message = "";
            LogEntries = new ObservableCollection<LogEntry>();
            CommandExportLog = new RelayCommand(param => this.ExportLog());
        }

        public RelayCommand CommandExportLog { get; set; }


        string _message;
        public string Message
        {
            get
            {
                return _message;
            }
            set
            {
                _message = value;
                OnPropertyChanged("Message");
                ShowPopup = true;
            }
        }
        bool _showPopup;
        public bool ShowPopup
        {
            get
            {
                return _showPopup;
            }
            set
            {
                _showPopup = value;
                OnPropertyChanged("ShowPopup");
            }
        }

        void ExportLog()
        {
            try
            {
                String buffer = "";
                List<LogEntry> logCopy = LogEntries.OrderByDescending(o => o.DateTime).ToList();
                int lineNumber = 0;
                foreach (LogEntry log in logCopy)
                {
                    buffer += log.DateTime + " " + log.Message + System.Environment.NewLine;
                    if (lineNumber++ > 1000)
                        break;
                }

                NotepadHelper.ShowMessage(buffer, "gColorFancy Log");
            }
            catch (Exception /*ex*/)
            {
            }
        }

        public ObservableCollection<LogEntry> LogEntries
        {
            get
            {
                return _logEntries;
            }
            set
            {
                _logEntries = value;
                base.OnPropertyChanged("LogEntries");
            }
        }


        public void AddEntry(string message, bool showPopup = false)
        {
            if (App.Current.Dispatcher.CheckAccess())
                AddEntry2(message, showPopup);
            else
            {
                App.Current.Dispatcher.BeginInvoke((Action)(() => AddEntry2(message, showPopup)));
            }

        }

        void AddEntry2(string message, bool showPopup)
        {
            lock (_locker)
            {
                LogEntries.Add(new Model.LogEntry(DateTime.Now, message));
#if !DEBUG
            if (showPopup)
            {
                Message = message;
            }
#endif
            }
        }
    }

    static class NotepadHelper
    {
        [DllImport("user32.dll", EntryPoint = "SetWindowText")]
        private static extern int SetWindowText(IntPtr hWnd, string text);

        [DllImport("user32.dll", EntryPoint = "FindWindowEx")]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("User32.dll", EntryPoint = "SendMessage")]
        private static extern int SendMessage(IntPtr hWnd, int uMsg, int wParam, string lParam);

        public static void ShowMessage(string message = null, string title = null)
        {
            Process notepad = Process.Start(new ProcessStartInfo("notepad.exe"));
            notepad.WaitForInputIdle();

            if (!string.IsNullOrEmpty(title))
                SetWindowText(notepad.MainWindowHandle, title);

            if (notepad != null && !string.IsNullOrEmpty(message))
            {
                IntPtr child = FindWindowEx(notepad.MainWindowHandle, new IntPtr(0), "Edit", null);
                SendMessage(child, 0x000C, 0, message);
            }
        }
    }
}
