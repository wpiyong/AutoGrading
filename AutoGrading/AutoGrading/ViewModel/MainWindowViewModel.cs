﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViewModelLib;

namespace AutoGrading.ViewModel
{
    class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            base.DisplayName = "MainWindowViewModel";

            ControlVM = new ControlViewModel();

            App.LogEntry.AddEntry("Application Started");
        }

        public LogEntryViewModel LogEntryVM { get { return App.LogEntry; } }

        public ControlViewModel ControlVM { get; set; }


        new void Dispose()
        {
            ControlVM.Dispose();
        }

        public void OnWindowLoaded(object sender, EventArgs e)
        {
            ControlVM.ConnectingDevices();
        }

        public void OnWindowClosing(object sender, CancelEventArgs e)
        {
            Dispose();
            App.Current.Shutdown();
        }
    }
}
