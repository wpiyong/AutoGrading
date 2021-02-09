using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViewModelLib;
using AutoGrading.Model;
using UtilsLib;
using System.Windows;
using gColorLib;
using System.Windows.Controls;

namespace AutoGrading.ViewModel
{
    class StoneDataViewModel : ViewModelBase
    {
        public StoneDataViewModel()
        {
            base.DisplayName = "StoneDataViewModel";

            CommandCancel = new RelayCommand(param => this.Close(param));
            CommandOK = new RelayCommand(param => this.Continue(param));
        }

        public RelayCommand CommandCancel { get; set; }
        public RelayCommand CommandOK { get; set; }

        public string ControlNumber { get; set; }
        public string Location = "";

        bool _stage1;
        public bool Stage1
        {
            get { return _stage1; }
            set
            {
                _stage1 = value;
                OnPropertyChanged("Stage1");
                if (_stage1)
                {
                    Location = "1";
                }
            }
        }

        bool _stage2;
        public bool Stage2
        {
            get { return _stage2; }
            set
            {
                _stage2 = value;
                OnPropertyChanged("Stage2");
                if (_stage2)
                {
                    Location = "6";
                }
            }
        }

        void Continue(object param)
        {
            if (ControlNumber != null && ControlNumber.Length > 0 && Location != "")
            {
                ((Window)param).DialogResult = true;
                Close(param);
            }
        }

        void Close(object param)
        {
            ((Window)param).Close();
        }
    }
}
