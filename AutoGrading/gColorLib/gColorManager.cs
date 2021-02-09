﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UtilsLib;
using gColorLib.Model;
using System.Windows.Media.Imaging;
using System.Data;
using ClassOpenCV;
using System.IO;
using System.Windows;
using System.Threading;

namespace gColorLib
{
    public delegate void ImageReceivedEventHandler(ImageReceived img);

    public class gColorManager : DeviceManager
    {
        enum Mode
        {
            Regular = 0,
            ColorCalculation = 1,
            BackgroundCalculation,
            CalibrationAutoSetting,
            CalibrationCheck,
            CalibrationManualSetting,
            CalibrationAdjustWhiteBalance,
            CalibrationCollectBackground
        };

        public event ImageReceivedEventHandler ImageReceivedEvent;

        void RaiseImageReceivedEvent(ImageReceived image)
        {
            ImageReceivedEvent?.Invoke(image);
        }

        public void AddImageReceivedEventSubscriber(ImageReceivedEventHandler handler)
        {
            ImageReceivedEvent += handler;
        }

        public gColorManager(string name, bool connecting) : base(name)
        {
            LoadSpecialSettings();

            MeasureIconImageSource = "..\\Images\\measurement.png";
            MeasureIconText = "";
            MeasureIconToolTip = "Measure";
            InitializeReferenceStoneCalibrationTable();

            ObjectDetectOn = false;

            _bwImageRetriever = new BackgroundWorker();
            _bwImageRetriever.WorkerReportsProgress = true;
            _bwImageRetriever.DoWork += new DoWorkEventHandler(_bwImageRetriever_DoWork);
            _bwImageRetriever.ProgressChanged += new ProgressChangedEventHandler(_bwImageRetriever_ProgressChanged);

            if (connecting)
            {
                BackgroundWorker bwConnectMotor = new BackgroundWorker();
                bwConnectMotor.DoWork += new DoWorkEventHandler(ConnectToMotor);
                bwConnectMotor.RunWorkerAsync();

                BackgroundWorker bwConnectHemisphere = new BackgroundWorker();
                bwConnectHemisphere.DoWork += new DoWorkEventHandler(ConnectToHemisphere);
                bwConnectHemisphere.RunWorkerAsync();
            }

            ST5.DriveReady += new EventHandler(ST5_BusyStateEvent);
            ST5.DriveNotReady += new EventHandler(ST5_BusyStateEvent);

            //Load OpenCvDlls so that they are in cache////////////////////
            ImageProcessingUtility.LoadMaskSettings();
            int dummyCount = -1;
            double area = -1;
            System.Drawing.Bitmap dummyBmp = new System.Drawing.Bitmap(1, 1);
            ImageProcessingUtility.ObjectDetector(dummyBmp, out dummyCount, out area);
            ////////////////////////////////////////////////////////////////
        }

        int _calibrationStablizeCount = 0;
        int _whitebalanceStabilizeCount = 0;

        bool _connected = false;
        bool _firstCalibration = true;
        bool calibrated = false;

        BackgroundWorker _bwImageRetriever;
        bool _keepRetrievingImages = false;

        int _rotationAngle;
        volatile int _numberOfRotations;
        volatile int _continuousImageCount;
        volatile bool _takeSingleMeasurement = false;
        volatile bool _takeContinuousMeasurement = false;
        volatile bool _takingBackgroundImages = false;
        volatile bool _takeImage = false;
        volatile bool _ready = true;
        volatile Mode _mode = Mode.Regular;

        volatile bool _buttonPressed = false;

        int _imageDiffLimit;

        Dictionary<double, BitmapSource> _measurementImages;
        Dictionary<double, BitmapSource> _dustImages;

        BitmapSource _backgroundImage = null;
        List<System.Drawing.Bitmap> _backgroundImageList = new List<System.Drawing.Bitmap>();

        Camera _camera = null;

        DataTable _dtReferenceStoneResults = null;

        //Hiroshi Add for backround monitoring
        double _cameraTempBG;
        double _cameraTempMeasurement;
        double _shutterTimeBG;
        double _blue;
        double _red;
        bool _firstCalib = true;
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        bool _tempChange = false;
        Stone _stone = null;

        public System.Windows.Visibility IsAdmin
        {
            get
            {
                return GlobalVariables.IsAdmin ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
        }

        const string _lockOutFileName = "adminfile";

        public void UpdateIsAdminView()
        {
            if (GlobalVariables.IsAdmin && File.Exists(_lockOutFileName))
            {
                File.Delete(_lockOutFileName);
            }
        }

        bool IsHashCorrect
        {
            get
            {
#if DEBUG
                return true;
#else
                string hash = StringCipher.Decrypt(AppSettings.Instance.BoundaryHash);
                return ImageProcessing.check_Hash_Boundary(hash);
#endif
            }

        }

        public int CameraModelProperty;

        bool MeasurementActive
        {
            get { return (_takeSingleMeasurement || _takeContinuousMeasurement || _takingBackgroundImages); }
            set
            {
                _takeSingleMeasurement = false;
                _takeContinuousMeasurement = false;
                _takeImage = false;
            }
        }

        public string Status;

        public bool IsReady
        {
            get
            {
                return _ready;
            }
            set
            {
                if (value != _ready)
                {
                    _ready = value;
                    //OnPropertyChanged("IsReady");
                }
                Busy = !_ready;
                //Application.Current.Dispatcher.BeginInvoke(new Action(CommandManager.InvalidateRequerySuggested));
            }
        }

        public bool MotorBusy;
        public bool Busy;

        public BitmapSource CameraImage;
        BitmapSource CachedCameraImage;

        public bool NotConnected
        {
            get { return !_connected; }
        }

        int _dailyMonitorTargetIndex = 0;
        public string MeasureIconText;
        public string MeasureIconImageSource;
        public string MeasureIconToolTip;
        public bool ObjectDetectOn;

        void LoadSpecialSettings()
        {
            try
            {
                _imageDiffLimit = 100;
                string currentDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

                using (StreamReader sr = new StreamReader(currentDirectory + @"\imageDiffThreshold.txt"))
                {
                    String line;
                    // Read and display lines from the file until the end of 
                    // the file is reached.
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line[0] != '!')
                        {
                            string value = line.Substring(line.IndexOf('=') + 1);
                            if (line.Contains("diff_threshold"))
                                _imageDiffLimit = Convert.ToInt32(value);
                        }
                    }
                }
            }
            catch
            {
                _imageDiffLimit = 100;
            }
        }

        void OpenHemisphereCommand()
        {
            BackgroundWorker bwOpen = new BackgroundWorker();
            bwOpen.DoWork += new DoWorkEventHandler(Hemisphere.Open);
            bwOpen.RunWorkerAsync();
        }

        void CloseHemisphereCommand()
        {
            BackgroundWorker bwClose = new BackgroundWorker();
            bwClose.DoWork += new DoWorkEventHandler(Hemisphere.Close);
            bwClose.RunWorkerAsync();
        }

        public void EditCameraSettings()
        {
            _camera.EditCameraSettings();
        }

        public void InitializeReferenceStoneCalibrationTable()
        {
            _dtReferenceStoneResults = new DataTable();
            _dtReferenceStoneResults.Columns.Add("ReferenceStone");
            _dtReferenceStoneResults.Columns.Add("Description");
            _dtReferenceStoneResults.Columns.Add("L");
            _dtReferenceStoneResults.Columns.Add("a");
            _dtReferenceStoneResults.Columns.Add("b");
            _dtReferenceStoneResults.Columns.Add("C");
            _dtReferenceStoneResults.Columns.Add("H");
            _dtReferenceStoneResults.Columns.Add("MaskL");
            _dtReferenceStoneResults.Columns.Add("MaskA");

            //ImageProcessing.setLabAdjustment(
            //    (double)AppSettings.Instance.LConv"), (double)AppSettings.Instance.AConv"), (double)AppSettings.Instance.BConv"),
            //    (double)AppSettings.Instance.Lshift"), (double)AppSettings.Instance.Ashift"), (double)AppSettings.Instance.Bshift"));
            ImageProcessing.setLabAdjustment(1,1,1,0,0,0);
            if (CalibrationStoneTable.CalStoneTable.Rows.Count > 0)
            {
                int[] selectedCalStoneArray;
                try
                {
                    selectedCalStoneArray =
                        ((string)AppSettings.Instance.DailyMonitorTargetList).Split(',').Select(s => Int32.Parse(s)).ToArray();
                }
                catch
                {
                    selectedCalStoneArray = new int[0];
                }

                DataRow row = null;

                for (int i = 0; i < selectedCalStoneArray.Length; i++)
                {
                    row = _dtReferenceStoneResults.NewRow();
                    row["ReferenceStone"] = CalibrationStoneTable.CalStoneTable.Rows[selectedCalStoneArray[i]]["Target"];
                    row["Description"] = "Target";
                    row["L"] = CalibrationStoneTable.CalStoneTable.Rows[selectedCalStoneArray[i]]["L"];
                    row["a"] = CalibrationStoneTable.CalStoneTable.Rows[selectedCalStoneArray[i]]["a"];
                    row["b"] = CalibrationStoneTable.CalStoneTable.Rows[selectedCalStoneArray[i]]["b"];
                    row["C"] = CalibrationStoneTable.CalStoneTable.Rows[selectedCalStoneArray[i]]["C"];
                    row["H"] = CalibrationStoneTable.CalStoneTable.Rows[selectedCalStoneArray[i]]["H"];
                    row["MaskL"] = CalibrationStoneTable.CalStoneTable.Rows[selectedCalStoneArray[i]]["MaskL"];
                    row["MaskA"] = CalibrationStoneTable.CalStoneTable.Rows[selectedCalStoneArray[i]]["MaskA"];
                    _dtReferenceStoneResults.Rows.Add(row);

                    row = _dtReferenceStoneResults.NewRow();
                    row["Description"] = "Measure";
                    _dtReferenceStoneResults.Rows.Add(row);

                    row = _dtReferenceStoneResults.NewRow();
                    row["Description"] = "Diff";
                    _dtReferenceStoneResults.Rows.Add(row);

                }

                row = _dtReferenceStoneResults.NewRow();
                _dtReferenceStoneResults.Rows.Add(row);

                row = _dtReferenceStoneResults.NewRow();
                row["ReferenceStone"] = "Average";
                row["Description"] = "Diff";
                _dtReferenceStoneResults.Rows.Add(row);
            }

            //force recalibration
            _dailyMonitorTargetIndex = 0;
            _backgroundImage = null;
            _backgroundImageList = new List<System.Drawing.Bitmap>();
        }

        void Connect()
        {
            if (!IsHashCorrect)
            {
                MessageBox.Show("Boundary table hash mismatch", "Contact Administrator");
                return;
            }

            if ((bool)AppSettings.Instance.AutoOpenClose && !Hemisphere.HemisphereMotorConnected)
            {
                MessageBox.Show("Please wait for the hemisphere to be connected", "Hemisphere not connected");
                return;
            }

            if (_camera != null)
            {
                StopCamera();
                _camera = null;
            }

            Status = "Connecting...";

            _camera = new PtGreyCamera();

            IsReady = false;

            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(ConnectCamera);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(ConnectCameraCompleted);
            bw.RunWorkerAsync();

        }

        void ConnectCamera(object sender, DoWorkEventArgs e)
        {
            e.Result = _camera.Connect();
        }

        void ConnectCameraCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            IsReady = true;

            if ((bool)e.Result == true)
            {
                _camera.StartCapture(_bwImageRetriever);
                //App.LogEntry.AddEntry("Started camera capture");
                GColorEvent evb = new GColorEvent(EventType.Info, "Started camera capture", null);
                DeviceEvent eb = new DeviceEvent(DeviceName, evb);
                EventBus.Instance.PostEvent(eb);
                _keepRetrievingImages = true;
                Status = "Initializing";

#if !DEBUG
                var initWindow = new View.Initialization();
                var initVm = new InitializationViewModel(_camera, this);
                initWindow.DataContext = initVm;
                initWindow.ShowDialog();
#endif
                _camera.DefaultSettings();//turn auto white balance off
                if ((bool)AppSettings.Instance.AutoOpenClose)
                    OpenHemisphereCommand();
                _connected = true;
                Status = "Connected";

                status = UtilsLib.Status.Ready;

                evb = new GColorEvent(EventType.Status, UtilsLib.Status.Ready.ToString(), new DeviceStatus(UtilsLib.Status.Ready));
                DeviceEvent de = new DeviceEvent(DeviceName, evb);
                EventBus.Instance.PostEvent(de);
            }
            else
            {
                //App.LogEntry.AddEntry("Failed to Connect to Camera");
                GColorEvent evb = new GColorEvent(EventType.Error, "Failed to Connect to Camera", null);
                DeviceEvent eb = new DeviceEvent(DeviceName, evb);
                EventBus.Instance.PostEvent(eb);
                Status = "";

                evb = new GColorEvent(EventType.Status, "Disconnected", new DeviceStatus(UtilsLib.Status.DisConnected));
                DeviceEvent de = new DeviceEvent(DeviceName, evb);
                EventBus.Instance.PostEvent(de);
            }

        }

        private void _bwImageRetriever_DoWork(object sender, DoWorkEventArgs e)
        {
            while (_keepRetrievingImages)
            {
                try
                {
                    if (IsReady)
                    {
                        BackgroundWorker worker = (BackgroundWorker)sender;

                        BitmapSource image = _camera.GetImage(e);
                        BitmapSource copy = image.Clone();
                        copy.Freeze();

                        if (_mode == Mode.Regular)
                        {
                            if (_takeSingleMeasurement)
                            {
                                if (_takeImage)
                                {
                                    _takeImage = false;

                                    //take image after motor has stopped
                                    image = _camera.GetImage(e);
                                    copy = image.Clone();
                                    copy.Freeze();

                                    double angle = _rotationAngle * _numberOfRotations;
                                    if (angle == 360)
                                        angle = 0;
                                    _measurementImages.Add(angle, copy);

                                    if ((bool)AppSettings.Instance.CalculateColorAtStep)
                                    {
                                        System.Drawing.Bitmap img_Bmp_diamond = GetBitmap(image);

                                        //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Analyzing Sample Image at angle " + angle)));
                                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Analyzing Sample Image at angle " + angle, null));
                                        EventBus.Instance.PostEvent(eb);

                                        List<System.Drawing.Bitmap> stepImage = new List<System.Drawing.Bitmap>();
                                        stepImage.Add(img_Bmp_diamond);
                                        double L = 0, C = 0, H = 0;
                                        string stepColor = ImageProcessing.GetColor_test2(ref stepImage, ref _backgroundImageList,
                                            ref L, ref C, ref H);

                                        //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Color = " + stepColor + ",L = " + L +",C = " + C + ",H = " + H)));
                                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Color = " + stepColor + ",L = " + L + ",C = " + C + ",H = " + H, null));
                                        EventBus.Instance.PostEvent(eb);
                                    }

                                    if (_numberOfRotations < (int)AppSettings.Instance.NumberOfSteps)
                                        ST5.RotateByAngle(_rotationAngle, (Direction)((int)AppSettings.Instance.RotationDirection));
                                    else
                                    {
                                        IsReady = false;
                                        Status = "Analyzing...";

                                        BackgroundWorker bw = new BackgroundWorker();
                                        bw.DoWork += new DoWorkEventHandler(SaveMeasurementImages);
                                        bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(SaveMeasurementImagesCompleted);
                                        bw.RunWorkerAsync();
                                    }
                                }

                            }
                            else if (_takeContinuousMeasurement)
                            {
                                _measurementImages.Add(_continuousImageCount, copy);

                                if ((bool)AppSettings.Instance.CalculateColorAtStep)
                                {
                                    System.Drawing.Bitmap img_Bmp_diamond = GetBitmap(image);

                                    //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Analyzing Sample Image " + _continuousImageCount)));
                                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Analyzing Sample Image " + _continuousImageCount, null));
                                    EventBus.Instance.PostEvent(eb);

                                    List<System.Drawing.Bitmap> stepImage = new List<System.Drawing.Bitmap>();
                                    stepImage.Add(img_Bmp_diamond);
                                    double L = 0, C = 0, H = 0;
                                    string stepColor = ImageProcessing.GetColor_test2(ref stepImage, ref _backgroundImageList,
                                        ref L, ref C, ref H);

                                    //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Color = " + stepColor + ",L = " + L +",C = " + C + ",H = " + H)));
                                    eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Color = " + stepColor + ",L = " + L + ",C = " + C + ",H = " + H, null));
                                    EventBus.Instance.PostEvent(eb);
                                }

                                if (!MotorBusy) //complete
                                {
                                    _takeContinuousMeasurement = false;
                                    IsReady = false;
                                    Status = "Analyzing...";

                                    BackgroundWorker bw = new BackgroundWorker();
                                    bw.DoWork += new DoWorkEventHandler(SaveMeasurementImages);
                                    bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(SaveMeasurementImagesCompleted);
                                    bw.RunWorkerAsync();
                                }

                                _continuousImageCount += 1;
                            }
                        }
                        else
                        {
                            System.Drawing.Bitmap img_Bmp = GetBitmap(image);
                            System.Drawing.Bitmap img_Bmp_copy = GetBitmap(copy);

                            switch (_mode)
                            {
                                case Mode.ColorCalculation:
                                    {
                                        //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Analyzing Sample Image - calcLab_wholeimage")));
                                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Analyzing Sample Image - calcLab_wholeimage", null));
                                        EventBus.Instance.PostEvent(eb);

                                        double L = 0, a = 0, b = 0;
                                        string text = "";
                                        ImageProcessing.calcLab_wholeimage(ref img_Bmp, ref L, ref a, ref b, false);
                                        text = "ALL L = " + string.Format("{0:00.0}", L) + ", a = " + string.Format("{0:0.00}", a) + ", b = " + string.Format("{0:0.00}", b);

                                        ImageProcessing.calcLab_ROI(ref img_Bmp, ref L, ref a, ref b, false);
                                        text = text + "\nROI L = " + string.Format("{0:00.0}", L) + ", a = " + string.Format("{0:0.00}", a) + ", b = " + string.Format("{0:0.00}", b);

                                        //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry(text, true)));
                                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, text, null));
                                        EventBus.Instance.PostEvent(eb);

                                        //ImageProcessing.calcLab_ROI(ref img_Bmp,ref L, ref a, ref b, false);
                                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                                        //App.LogEntry.AddEntry("ROI L = " + L + ", a = " + a + ", b = " + b,true)));

                                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                                        //App.LogEntry.AddEntry("Blue=" + GetWBBlue().ToString() + " Red=" + GetWBRed().ToString(),true)));

                                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                                        //    App.LogEntry.AddEntry("Camera temp=" + GetCameraTemp().ToString(),true)));

                                        //////////////////////////////
                                        //////////////////////////////

                                        if ((bool)AppSettings.Instance.AutoOpenClose)
                                            OpenHemisphereCommand();

                                        _mode = Mode.Regular;
                                        IsReady = true;
                                    }
                                    break;
                                case Mode.BackgroundCalculation:
                                    {
                                        //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Analyzing Sample Image - calcBGR_wholeimage")));
                                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Analyzing Sample Image - calcBGR_wholeimage", null));
                                        EventBus.Instance.PostEvent(eb);

                                        //double B = 0, G = 0, R = 0;
                                        string text = "";
                                        //ImageProcessing.calcBGR_wholeimage(ref img_Bmp, ref B, ref G, ref R);
                                        //text = "B = " + string.Format("{0:000.0}", B) + ", G = " + string.Format("{0:000.0}", G) + ", R = " + string.Format("{0:000.0}", R);

                                        string strMessage = "";
                                        List<System.Drawing.Bitmap> ImageList = new List<System.Drawing.Bitmap>();
                                        ImageList.Add(img_Bmp);
                                        if (ImageProcessing.check_NO_backgroundDust(ref ImageList, ref strMessage) == false)
                                        {
                                            text = "Check dust / scratch on stage";
                                        }
                                        else
                                        {
                                            text = "Stage condition: OK";
                                        }

                                        if ((bool)AppSettings.Instance.AutoOpenClose)
                                            OpenHemisphereCommand();

                                        _mode = Mode.Regular;
                                        IsReady = true;

                                        //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry(text, true)));
                                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, text, null));
                                        EventBus.Instance.PostEvent(eb);
                                    }
                                    break;
                                case Mode.CalibrationAutoSetting:
                                    {
                                        if (_calibrationStablizeCount++ == 0)
                                        {
                                            //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Calibration Mode : Initializing Calibration Settings...")));
                                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration Mode : Initializing Calibration Settings...", null));
                                            EventBus.Instance.PostEvent(eb);

                                            _camera.InitCalibrationSettings();
                                            Thread.Sleep(5000);
                                        }
                                        else if (_calibrationStablizeCount == 3)
                                        {
                                            //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Calibration Mode : Configuring Camera to Auto...")));
                                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration Mode : Configuring Camera to Auto...", null));
                                            EventBus.Instance.PostEvent(eb);

                                            _camera.ResetSettings();
                                        }
                                        else
                                        {
                                            if (_calibrationStablizeCount < 9)
                                            {
                                                //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Calibration Mode : Auto Stabilizing...")));
                                                DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration Mode : Auto Stabilizing...", null));
                                                EventBus.Instance.PostEvent(eb);

                                                //Thread.Sleep(1000);
                                                Thread.Sleep(200);
                                            }
                                            else
                                            {
                                                _calibrationStablizeCount = 0;
                                                _mode = Mode.CalibrationManualSetting;
                                            }
                                        }
                                    }
                                    break;
                                case Mode.CalibrationCheck:
                                    {
                                        //Get RGB background results
                                        //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Calibration Mode: Checking Calibration...")));
                                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration Mode: Checking Calibration...", null));
                                        EventBus.Instance.PostEvent(eb);

                                        double B = 0, G = 0, R = 0;
                                        ImageProcessing.calcBGR_wholeimage(ref img_Bmp, ref B, ref G, ref R);

                                        //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("B = " + B + ", G = " + G + ", R = " + R)));
                                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "B = " + B + ", G = " + G + ", R = " + R, null));
                                        EventBus.Instance.PostEvent(eb);

                                        if (Math.Abs(R - G) <= (double)AppSettings.Instance.WBConvergence &&
                                                Math.Abs(B - G) <= (double)AppSettings.Instance.WBConvergence)
                                        {
                                            ///////////////////////
                                            //Hiroshi add 5/23/2014
                                            ///////////////////////
                                            _camera.Finish_calibration();   //Shutter Auto off
                                            Thread.Sleep(500);
                                            ///////////////////////


                                            try
                                            {
                                                _backgroundImage = null;
                                                _backgroundImageList = new List<System.Drawing.Bitmap>();
                                                _takingBackgroundImages = true;

                                                if ((int)AppSettings.Instance.NumberOfSteps > 0)
                                                {
                                                    _numberOfRotations = 0;
                                                    _rotationAngle = 360 / (int)AppSettings.Instance.NumberOfSteps;
                                                    ST5.RotateByAngle(_rotationAngle, (Direction)((int)AppSettings.Instance.RotationDirection));
                                                }
                                                else
                                                {
                                                    //rotate 360 degrees and collect as many images as possible
                                                    ST5.RotateByAngle(360, (Direction)((int)AppSettings.Instance.RotationDirection));
                                                }

                                                //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Started collecting background images...", true)));
                                                eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Started collecting background images...", null));
                                                EventBus.Instance.PostEvent(eb);

                                                _mode = Mode.CalibrationCollectBackground;
                                            }
                                            catch (Exception /*exc*/)
                                            {
                                                _backgroundImage = null;
                                                _backgroundImageList = new List<System.Drawing.Bitmap>();
                                                _takingBackgroundImages = false;
                                                _calibrationStablizeCount = 0;
                                                throw new ApplicationException("Error collecting background images, calibration failed!");
                                            }
                                        }
                                        else
                                            _mode = Mode.CalibrationManualSetting;
                                    }
                                    break;
                                case Mode.CalibrationCollectBackground:
                                    {
                                        if ((int)AppSettings.Instance.NumberOfSteps == 0)
                                            _backgroundImageList.Add(img_Bmp_copy);

                                        if (!MotorBusy) //check for complete
                                        {
                                            if ((int)AppSettings.Instance.NumberOfSteps > 0 &&
                                                    ++_numberOfRotations < (int)AppSettings.Instance.NumberOfSteps)
                                            {
                                                _backgroundImageList.Add(img_Bmp_copy);
                                                ST5.RotateByAngle(_rotationAngle, (Direction)((int)AppSettings.Instance.RotationDirection));
                                            }
                                            else
                                            {
                                                if ((int)AppSettings.Instance.NumberOfSteps > 0)
                                                    _backgroundImageList.Add(img_Bmp_copy);

                                                /////////////////////////////////////////////////////
                                                // Hiroshi background check 2/20/2014
                                                /////////////////////////////////////////////////////
                                                string strMessage = "";
                                                String text = "";

                                                if (GlobalVariables.InTestMode ||
                                                    ImageProcessing.check_NO_backgroundDust(ref _backgroundImageList, ref strMessage) == true)
                                                {
                                                    _backgroundImage = image.Clone();
                                                    _backgroundImage.Freeze();
                                                    _mode = Mode.Regular;
                                                    _takingBackgroundImages = false;


                                                    /////////////////////////////////////////////////////
                                                    //Hiroshi add for camera temp monitoring 5/07/2014
                                                    /////////////////////////////////////////////////////
                                                    _cameraTempBG = GetCameraTemp();

                                                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Camera temp=" + _cameraTempBG.ToString())));
                                                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Camera temp=" + _cameraTempBG.ToString(), null));
                                                    EventBus.Instance.PostEvent(eb);

                                                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Blue=" + GetWBBlue().ToString() + " Red=" + GetWBRed().ToString())));
                                                    eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Blue=" + GetWBBlue().ToString() + " Red=" + GetWBRed().ToString(), null));
                                                    EventBus.Instance.PostEvent(eb);

                                                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("BG image count=" + _backgroundImageList.Count.ToString())));
                                                    eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "BG image count=" + _backgroundImageList.Count.ToString(), null));
                                                    EventBus.Instance.PostEvent(eb);

                                                    sw.Reset();
                                                    sw.Start();
                                                    _tempChange = false;

                                                    //Identify the sudden change of background

                                                    if (_firstCalib)
                                                    {
                                                        _shutterTimeBG = GetShutterTime();
                                                        _blue = GetWBBlue();
                                                        _red = GetWBRed();
                                                        _firstCalib = false;
                                                    }
                                                    else
                                                    {

                                                        if (Math.Abs(_shutterTimeBG - GetShutterTime()) > (double)AppSettings.Instance.ShutterTimeDiff ||
                                                            Math.Abs(_blue - GetWBBlue()) > GlobalVariables.LightStableSettings.BlueWBDiff ||
                                                            Math.Abs(_red - GetWBRed()) > GlobalVariables.LightStableSettings.RedWBDiff ||
                                                            (Math.Abs(_blue - GetWBBlue()) >= GlobalVariables.LightStableSettings.BlueWBDiff &&
                                                            Math.Abs(_red - GetWBRed()) >= GlobalVariables.LightStableSettings.RedWBDiff))
                                                        {
                                                            text = "[Warning: check light stability]";
                                                            if (Math.Abs(_shutterTimeBG - GetShutterTime()) > (double)AppSettings.Instance.ShutterTimeDiff)
                                                            {
                                                                text += ": [_shutterTimeBG - GetShutterTime]";
                                                            }
                                                            if (Math.Abs(_blue - GetWBBlue()) > GlobalVariables.LightStableSettings.BlueWBDiff)
                                                            {
                                                                text += ": [_blue - GetWBBlue]";
                                                            }
                                                            if (Math.Abs(_red - GetWBRed()) > GlobalVariables.LightStableSettings.RedWBDiff)
                                                            {
                                                                text += ": [_red - GetWBRed]";
                                                            }
                                                            if (Math.Abs(_blue - GetWBBlue()) >= GlobalVariables.LightStableSettings.BlueWBDiff &&
                                                            Math.Abs(_red - GetWBRed()) >= GlobalVariables.LightStableSettings.RedWBDiff)
                                                            {
                                                                text += ": [_blue - GetWBBlue && _red - GetWBRed]";
                                                            }
                                                            using (var f = File.OpenWrite(_lockOutFileName))
                                                            { }
                                                        }

                                                        _shutterTimeBG = GetShutterTime();
                                                        _blue = GetWBBlue();
                                                        _red = GetWBRed();
                                                    }


                                                    //////////////////////////////////////////////////////

                                                    if (text.Length > 0)
                                                    {
                                                        //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry(text, true)));
                                                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, text, null));
                                                        EventBus.Instance.PostEvent(eb);
                                                    }
                                                    else
                                                    {
                                                        calibrated = true;
                                                        _cameraTempMeasurement = GetCameraTemp();
                                                        //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Calibration Complete!", true)));
                                                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration Complete!", null));
                                                        EventBus.Instance.PostEvent(eb);
                                                    }
                                                    IsReady = true;

                                                    if (((string)AppSettings.Instance.DailyMonitorTargetList).Length > 0 &&
                                                        _dailyMonitorTargetIndex < ((string)AppSettings.Instance.DailyMonitorTargetList).Split(',').Length)
                                                    {
                                                        try
                                                        {
                                                            var targetStones = ((string)AppSettings.Instance.DailyMonitorTargetList).Split(',');
                                                            MeasureIconText = CalibrationStoneTable.CalStoneTable.Rows[Convert.ToInt32(targetStones[_dailyMonitorTargetIndex])]["Target"].ToString();
                                                            MeasureIconImageSource = "..\\Images\\cal_stone.png";
                                                            MeasureIconToolTip = "Measure calibration stone";
                                                        }
                                                        catch
                                                        {
                                                            MeasureIconText = "";
                                                            MeasureIconImageSource = "..\\Images\\measurement.png";
                                                            MeasureIconToolTip = "Measure";
                                                            _dailyMonitorTargetIndex = 0;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        MeasureIconText = "";
                                                        MeasureIconImageSource = "..\\Images\\measurement.png";
                                                        MeasureIconToolTip = "Measure";
                                                    }

                                                    if ((bool)AppSettings.Instance.AutoOpenClose)
                                                        OpenHemisphereCommand();

                                                    _firstCalibration = false;
                                                }
                                                else
                                                {
                                                    _backgroundImage = null;
                                                    _backgroundImageList = new List<System.Drawing.Bitmap>();
                                                    _takingBackgroundImages = false;
                                                    _calibrationStablizeCount = 0;
                                                    _firstCalibration = false;

                                                    throw new ApplicationException("Calibration Failed!  Dust on stage.");
                                                }


                                            }
                                        }

                                    }
                                    break;
                                case Mode.CalibrationManualSetting:
                                    {
                                        if (_calibrationStablizeCount++ == 0)
                                        {
                                            //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Calibration Mode : Configuring Camera calibration settings...")));
                                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration Mode : Configuring Camera calibration settings...", null));
                                            EventBus.Instance.PostEvent(eb);
                                            _camera.DefaultSettings();
                                        }
                                        else
                                        {
                                            if (_calibrationStablizeCount < 50)
                                            {
                                                _whitebalanceStabilizeCount = 0;
                                                _mode = Mode.CalibrationAdjustWhiteBalance;
                                            }
                                            else
                                            {
                                                _backgroundImage = null;
                                                _backgroundImageList = new List<System.Drawing.Bitmap>();
                                                _calibrationStablizeCount = 0;

                                                throw new ApplicationException("Calibration Failed!");
                                            }
                                        }
                                    }
                                    break;
                                case Mode.CalibrationAdjustWhiteBalance:
                                    {
                                        if (_whitebalanceStabilizeCount++ == 0)
                                        {
                                            //Get RGB background results
                                            double B = 0, G = 0, R = 0;
                                            ImageProcessing.calcBGR_wholeimage(ref img_Bmp, ref B, ref G, ref R);

                                            //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("B = " + B + ", G = " + G + ", R = " + R)));
                                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "B = " + B + ", G = " + G + ", R = " + R, null));
                                            EventBus.Instance.PostEvent(eb);
                                            _camera.Calibrate(R, G, B);

                                            //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Delay wait for WB to take effect...")));
                                            eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Delay wait for WB to take effect...", null));
                                            EventBus.Instance.PostEvent(eb);
                                        }
                                        else
                                        {
                                            if (_whitebalanceStabilizeCount < 16)
                                            {
                                                if (_whitebalanceStabilizeCount % 5 == 0)
                                                {
                                                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Delay wait for WB to take effect...")));
                                                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Delay wait for WB to take effect...", null));
                                                    EventBus.Instance.PostEvent(eb);
                                                }
                                                Thread.Sleep(100);
                                            }
                                            else
                                            {
                                                _whitebalanceStabilizeCount = 0;
                                                _mode = Mode.CalibrationCheck;
                                            }
                                        }
                                    }
                                    break;

                            }

                        }

                        if (ObjectDetectOn)
                        {
                            System.Drawing.Bitmap src = GetBitmap(image);
                            int count = -1;
                            double area = -1;
                            System.Drawing.Bitmap dst = ImageProcessingUtility.ObjectDetector(src, out count, out area);
                            BitmapSource dst1 = GetBitmapSource(dst).Clone();
                            dst1.Freeze();
                            CachedCameraImage = dst1;
                            worker.ReportProgress(0, dst1);
                        }
                        else
                        {
                            CachedCameraImage = image;
                            worker.ReportProgress(0, image);
                        }

                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (ApplicationException aex)
                {
                    IsReady = true;
                    MeasurementActive = false;
                    _mode = Mode.Regular;
                    Status = "Connected";

                    if ((bool)AppSettings.Instance.AutoOpenClose)
                        OpenHemisphereCommand();

                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Process error: " + aex.Message, true)));
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Process error: " + aex.Message, null));
                    EventBus.Instance.PostEvent(eb);
                }
                catch (FlyCapture2Managed.FC2Exception fex)
                {
                    IsReady = true;
                    MeasurementActive = false;
                    _mode = Mode.Regular;
                    Status = "Connected";

                    if ((bool)AppSettings.Instance.AutoOpenClose)
                        OpenHemisphereCommand();

                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Camera error: " + fex.Message, true)));
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Camera error: " + fex.Message, null));
                    EventBus.Instance.PostEvent(eb);
                }
                catch (Exception ex)
                {
                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Exception in background worker -> disconnect camera: " + ex.Message, true)));
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Exception in background worker -> disconnect camera: " + ex.Message, null));
                    EventBus.Instance.PostEvent(eb);

                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Exception in background worker: " + ex.ToString())));
                    eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Exception in background worker: " + ex.ToString(), null));
                    EventBus.Instance.PostEvent(eb);
                }
                finally
                {
                    GC.Collect();
                }
            }
        }

        private void _bwImageRetriever_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            CameraImage = (BitmapSource)e.UserState;
            RaiseImageReceivedEvent(new ImageReceived(DeviceName, CameraImage));
        }

        void Calibrate()
        {
            if (File.Exists(_lockOutFileName))
            {
                MessageBox.Show("Light stability problem [Exists(_lockOutFileName)]", "Contact Administrator");
                return;
            }

            if ((bool)AppSettings.Instance.AutoOpenClose)
            {
                if (_buttonPressed)
                    return;
                else
                    _buttonPressed = true;

                BackgroundWorker bw = new BackgroundWorker();
                bw.DoWork += new DoWorkEventHandler(Hemisphere.Close);
                bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(CalibrateWaitAfterFirst);
                bw.RunWorkerAsync();
            }
            else
                CalibrateContinue(null, null);
        }

        void CalibrateWaitAfterFirst(Object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if ((bool)e.Result == false)
                {
                    //throw new Exception("Failed to close hemisphere");
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Failed to close hemisphere", null));
                    EventBus.Instance.PostEvent(eb);
                    _buttonPressed = false;
                }
                else if (_firstCalibration)
                {
                    BackgroundWorker bw = new BackgroundWorker();
                    bw.DoWork += new DoWorkEventHandler(CalibrateSleep);
                    bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(CalibrateContinue);
                    bw.RunWorkerAsync();
                }
                else
                    CalibrateContinue(null, null);
            }
            catch (Exception ex)
            {
                //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry(ex.Message, true)));
                DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, ex.Message, null));
                EventBus.Instance.PostEvent(eb);
                _buttonPressed = false;
            }
        }

        void CalibrateSleep(Object sender, DoWorkEventArgs e)
        {
            Thread.Sleep(5000);
        }

        void CalibrateContinue(Object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                /////////////////////////////////////////////////////
                //Dust monitoring
                /////////////////////////////////////////////////////
                List<System.Drawing.Bitmap> BGImageList = new List<System.Drawing.Bitmap>();
                System.Drawing.Bitmap img_Bmp = GetBitmap(CachedCameraImage);
                BGImageList.Add(img_Bmp);

                string strMessage = "";

                if (!GlobalVariables.InTestMode && ImageProcessing.check_NO_backgroundDust(ref BGImageList, ref strMessage) == false)
                {
                    _backgroundImage = null;
                    _backgroundImageList = new List<System.Drawing.Bitmap>();
                    _takingBackgroundImages = false;
                    _mode = Mode.Regular;
                    _calibrationStablizeCount = 0;

                    if ((bool)AppSettings.Instance.AutoOpenClose)
                        OpenHemisphereCommand();

                    //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry(strMessage, true)));
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, strMessage, null));
                    EventBus.Instance.PostEvent(eb);
                    IsReady = true;
                }
                else
                {
                    //App.LogEntry.AddEntry("Calibration Started...", true);
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration Started...", null));
                    EventBus.Instance.PostEvent(eb);
                    Busy = true;
                    _calibrationStablizeCount = 0;
                    _mode = Mode.CalibrationAutoSetting;

                    GColorEvent evb = new GColorEvent(EventType.Status, UtilsLib.Status.DisConnected.ToString(), new DeviceStatus(UtilsLib.Status.DisConnected));
                    DeviceEvent de = new DeviceEvent(DeviceName, evb);
                    EventBus.Instance.PostEvent(de);
                }
            }
            catch (Exception ex)
            {
                //App.LogEntry.AddEntry("Calibrate Exception : " + ex.Message);
                DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Calibrate Exception : " + ex.Message, null));
                EventBus.Instance.PostEvent(eb);
            }
            finally
            {
                _buttonPressed = false;
            }
        }


        void Measure()
        {
            if (File.Exists(_lockOutFileName))
            {
                MessageBox.Show("Light stability problem  [Exists(_lockOutFileName)]", "Contact Administrator");
                return;
            }

            if ((bool)AppSettings.Instance.AutoOpenClose)
            {
                if (_buttonPressed)
                    return;
                else
                    _buttonPressed = true;

                BackgroundWorker bw = new BackgroundWorker();
                bw.DoWork += new DoWorkEventHandler(Hemisphere.Close);
                bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(MeasureContinue);
                bw.RunWorkerAsync();
            }
            else
                MeasureContinue(null, null);
        }

        void Measure(int id)
        {
            if (File.Exists(_lockOutFileName))
            {
                MessageBox.Show("Light stability problem  [Exists(_lockOutFileName)]", "Contact Administrator");
                return;
            }

            if ((bool)AppSettings.Instance.AutoOpenClose)
            {
                if (_buttonPressed)
                    return;
                else
                    _buttonPressed = true;

                BackgroundWorker bw = new BackgroundWorker();
                bw.DoWork += new DoWorkEventHandler(Hemisphere.Close);
                bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(MeasureContinue);
                bw.RunWorkerAsync(id);
            }
            else
                MeasureContinue(null, null);
        }


        void MeasureContinue(Object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (sender != null)
                {
                    if ((bool)e.Result == false)
                    {
                        //Application.Current.Dispatcher.Invoke((Action)(() =>App.LogEntry.AddEntry("Failed to close hemisphere", true)));
                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Failed to close hemisphere", null));
                        EventBus.Instance.PostEvent(eb);
                        return;
                    }
                }

                if (!ST5.MotorConnected && !ST5.Connect((string)AppSettings.Instance.MotorPort))
                {
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Motor connection error", null));
                    EventBus.Instance.PostEvent(eb);
                    if ((bool)AppSettings.Instance.AutoOpenClose)
                        OpenHemisphereCommand();
                    return;
                }


                if (!GlobalVariables.InTestMode)
                {
                    /////////////////////////////////////////////////////
                    //Temperature monitoring
                    /////////////////////////////////////////////////////
                    _cameraTempMeasurement = GetCameraTemp();
                    if (Math.Abs(_cameraTempMeasurement - _cameraTempBG) > 0.05) _tempChange = true;

                    if (false)
                    {
                        //if (Math.Abs(_cameraTempMeasurement - _cameraTempBG) > 0.15)
                        if (Math.Abs(_cameraTempMeasurement - _cameraTempBG) > Convert.ToDouble(AppSettings.Instance.Temperature))
                        {
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //App.LogEntry.AddEntry("Calibration again " + "Temp_BG=" + _cameraTempBG.ToString() + ", Temp_Measure=" + _cameraTempMeasurement.ToString(), true)));
                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration again " + "Temp_BG=" + _cameraTempBG.ToString() + ", Temp_Measure=" + _cameraTempMeasurement.ToString(), null));
                            EventBus.Instance.PostEvent(eb);

                            _backgroundImage = null;
                            _backgroundImageList = new List<System.Drawing.Bitmap>();
                            _takingBackgroundImages = false;
                            _mode = Mode.Regular;
                            _calibrationStablizeCount = 0;
                            IsReady = true;

                            if ((bool)AppSettings.Instance.AutoOpenClose)
                                OpenHemisphereCommand();

                            return;
                        }

                        //if ((_tempChange == true && sw.ElapsedMilliseconds > 240000) || sw.ElapsedMilliseconds > 600000)
                        if (sw.ElapsedMilliseconds > Convert.ToDouble(AppSettings.Instance.Time) * 1000)
                        {
                            sw.Stop();
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //App.LogEntry.AddEntry("Calibration again " + "time[ms]=" + sw.ElapsedMilliseconds.ToString(), true)));
                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration again " + "time[ms]=" + sw.ElapsedMilliseconds.ToString(), null));
                            EventBus.Instance.PostEvent(eb);

                            _backgroundImage = null;
                            _backgroundImageList = new List<System.Drawing.Bitmap>();
                            _takingBackgroundImages = false;
                            _mode = Mode.Regular;
                            _calibrationStablizeCount = 0;
                            IsReady = true;

                            if ((bool)AppSettings.Instance.AutoOpenClose)
                                OpenHemisphereCommand();

                            return;
                        }
                    }
                    //////////////////////////////////////////////////////

                    /////////////////////////////////////////////////////
                    //Diamond position check
                    /////////////////////////////////////////////////////
                    System.Drawing.Bitmap img_Bmp = GetBitmap(CachedCameraImage);
                    string comment = "";
                    int x, y;
                    double imageWidth = _camera.CropImageWidth == 0 ? _camera.ImageWidth : _camera.CropImageWidth;
                    double imageHeight = _camera.CropImageHeight == 0 ? _camera.ImageHeight : _camera.CropImageHeight;
                    x = (int)(imageWidth * 0.5); // (int)CrossHairHorizontalPixelOffset;
                    y = (int)(imageHeight * 0.5); // (int)CrossHairVerticalPixelOffset;
                    if (!ImageProcessing.check_diamond_position(ref img_Bmp, x, y, ref comment))
                    {
                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //App.LogEntry.AddEntry(comment, true)));
                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, comment, null));
                        EventBus.Instance.PostEvent(eb);

                        if ((bool)AppSettings.Instance.AutoOpenClose)
                            OpenHemisphereCommand();

                        return;
                    }

                    /////////////////////////////////////////////////////


                    /////////////////////////////////////////////////////
                    //Dust monitoring
                    /////////////////////////////////////////////////////
                    //test object detection
                    //var watch = System.Diagnostics.Stopwatch.StartNew();
                    int objectCount = -1;
                    double maskArea = -1;
                    bool noDust = true;
                    if (ImageProcessingUtility.DustDetectOn)
                    {
                        ImageProcessingUtility.ObjectDetector(img_Bmp, out objectCount, out maskArea);
                        if (objectCount == -1)
                        {
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //App.LogEntry.AddEntry("Error processing stone - please contact adminstrator.", true)));
                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Error processing stone - please contact adminstrator.", null));
                            EventBus.Instance.PostEvent(eb);

                            if ((bool)AppSettings.Instance.AutoOpenClose)
                                OpenHemisphereCommand();

                            return;
                        }
                        noDust = ImageProcessing.check_NO_measurementDust(ref img_Bmp, ref comment);
                    }

                    //watch.Stop();
                    if (objectCount > 0 || !noDust)
                    {
                        bool objectDetectState = ObjectDetectOn;
                        ObjectDetectOn = true;

                        var result = MessageBox.Show("There may be dust on the stage.\n\nContinue measurement?",
                            "Multiple objects detected", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                        ObjectDetectOn = objectDetectState;
                        if (result != MessageBoxResult.Yes)
                        {
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //    App.LogEntry.AddEntry("Measurement cancelled by user", true)));
                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Measurement cancelled by user", null));
                            EventBus.Instance.PostEvent(eb);

                            if ((bool)AppSettings.Instance.AutoOpenClose)
                                OpenHemisphereCommand();

                            return;
                        }
                    }
                    ///////////////////////////////////////////////////////////////

                }




                /////////////////////////////////////////////////////

                // create the stone in each measurement with control number
                //_stone = null;
#if !DEBUG
                if (MeasureIconText.Length == 0)
                {
                    var dialogControlNumberWindow = new gColor.View.StoneData();
                    var dialogStoneDataViewModel = new gColor.ViewModel.StoneDataViewModel();
                    dialogControlNumberWindow.DataContext = dialogStoneDataViewModel;
                    bool? dialogResult = dialogControlNumberWindow.ShowDialog();
                    if (dialogResult == true)
                        _stone = dialogStoneDataViewModel.Cassette;
                }
                if (_stone != null || MeasureIconText.Length > 0)
#endif
                {
                    ST5.ResetPosition();

                    _numberOfRotations = 0;
                    _continuousImageCount = 0;
                    _measurementImages = new Dictionary<double, BitmapSource>();

                    if (MeasureIconText.Length == 0)
                    {
                        ImageProcessing.setLabAdjustment(
                            (double)AppSettings.Instance.LConv, (double)AppSettings.Instance.AConv, (double)AppSettings.Instance.BConv,
                            (double)AppSettings.Instance.Lshift,(double) AppSettings.Instance.Ashift, (double)AppSettings.Instance.Bshift);
                    }
                    else
                    {
                        ImageProcessing.setLabAdjustment(1.0, 1.0, 1.0, 0.0, 0.0, 0.0);
                    }

                    if ((int)AppSettings.Instance.NumberOfSteps > 0)
                    {
                        _rotationAngle = 360 / (int)AppSettings.Instance.NumberOfSteps;
                        ST5.RotateByAngle(_rotationAngle, (Direction)((int)AppSettings.Instance.RotationDirection));
                        //App.LogEntry.AddEntry("Measurement Started");
                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Measurement Started", null));
                        EventBus.Instance.PostEvent(eb);
                        _takeImage = false; //will be set after first step
                        _takeSingleMeasurement = true;
                    }
                    else
                    {
                        //rotate 360 degrees and collect as many images as possible
                        ST5.RotateByAngle(360, (Direction)((int)AppSettings.Instance.RotationDirection));
                        //App.LogEntry.AddEntry("Continuous Measurement Started");
                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Continuous Measurement Started", null));
                        EventBus.Instance.PostEvent(eb);
                        _takeContinuousMeasurement = true;
                    }

                    Busy = true;

                    GColorEvent evb = new GColorEvent(EventType.Status, UtilsLib.Status.Busy.ToString(), new DeviceStatus(UtilsLib.Status.Busy));
                    DeviceEvent de = new DeviceEvent(DeviceName, evb);
                    EventBus.Instance.PostEvent(de);
                }
            }
            catch (Exception ex)
            {
                //App.LogEntry.AddEntry("Measure Exception : " + ex.Message);
                DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Measure Exception : " + ex.Message, null));
                EventBus.Instance.PostEvent(eb);
                if ((bool)AppSettings.Instance.AutoOpenClose)
                    OpenHemisphereCommand();
            }
            finally
            {
                _buttonPressed = false;
            }
        }

        void ST5_BusyStateEvent(object sender, EventArgs e)
        {
            MotorBusy = ST5.DriveBusy;
            if (_takeSingleMeasurement)
            {
                if (!ST5.DriveBusy)
                {
                    //completed step
                    _numberOfRotations++;
                    _takeImage = true;
                }
            }

        }

        public System.Drawing.Bitmap GetBitmap(BitmapSource bitmapsource)
        {
            System.Drawing.Bitmap tempBitmap, bitmap;
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapsource));
                enc.Save(outStream);
                tempBitmap = new System.Drawing.Bitmap(outStream);
            }
            bitmap = new System.Drawing.Bitmap(tempBitmap);
            tempBitmap.Dispose();
            return bitmap;
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern Int32 DeleteObject(IntPtr hGDIObj);

        public BitmapSource GetBitmapSource(System.Drawing.Bitmap bmp)
        {
            IntPtr hBitmap = bmp.GetHbitmap();
            System.Windows.Media.Imaging.BitmapSource bs =
                System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                  hBitmap,
                  IntPtr.Zero,
                  System.Windows.Int32Rect.Empty,
                  System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(hBitmap);
            bmp.Dispose();

            return bs;
        }

        private static System.Windows.Media.PixelFormat ConvertPixelFormat(System.Drawing.Imaging.PixelFormat sourceFormat)
        {
            switch (sourceFormat)
            {
                case System.Drawing.Imaging.PixelFormat.Format24bppRgb:
                    return System.Windows.Media.PixelFormats.Bgr24;

                case System.Drawing.Imaging.PixelFormat.Format32bppArgb:
                    return System.Windows.Media.PixelFormats.Bgra32;

                case System.Drawing.Imaging.PixelFormat.Format32bppRgb:
                    return System.Windows.Media.PixelFormats.Bgr32;
            }

            return new System.Windows.Media.PixelFormat();
        }

        void Color()
        {
            if ((bool)AppSettings.Instance.AutoOpenClose)
            {
                BackgroundWorker bw = new BackgroundWorker();
                bw.DoWork += new DoWorkEventHandler(Hemisphere.Close);
                bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(ColorContinue);
                bw.RunWorkerAsync();
            }
            else
                ColorContinue(null, null);

        }
        void ColorContinue(Object sender, RunWorkerCompletedEventArgs e)
        {
            _mode = Mode.ColorCalculation;
        }

        void Background()
        {
            if ((bool)AppSettings.Instance.AutoOpenClose)
            {
                BackgroundWorker bw = new BackgroundWorker();
                bw.DoWork += new DoWorkEventHandler(Hemisphere.Close);
                bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(BackgroundContinue);
                bw.RunWorkerAsync();
            }
            else
                BackgroundContinue(null, null);
        }
        void BackgroundContinue(Object sender, RunWorkerCompletedEventArgs e)
        {
            _mode = Mode.BackgroundCalculation;
        }

        void Cancel()
        {
            if (MeasurementActive)
            {
                MeasurementActive = false;
                _takingBackgroundImages = false;
                _mode = Mode.Regular;
                _calibrationStablizeCount = 0;

                ST5.StopMotor();
                while (MotorBusy)
                    Thread.Sleep(10);

                //force recalibration
                _backgroundImage = null;
                _backgroundImageList = new List<System.Drawing.Bitmap>();
                IsReady = true;

                //Application.Current.Dispatcher.Invoke((Action)(() =>
                //        App.LogEntry.AddEntry("Measurement/Calibration Stopped", true)));
                DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Measurement/Calibration Stopped", null));
                EventBus.Instance.PostEvent(eb);
                if ((bool)AppSettings.Instance.AutoOpenClose)
                    OpenHemisphereCommand();

            }

        }

        void ConnectToMotor(object sender, DoWorkEventArgs e)
        {
            ST5.Connect((string)AppSettings.Instance.MotorPort, 3);
            MotorBusy = ST5.DriveBusy;
        }

        void ConnectToHemisphere(object sender, DoWorkEventArgs e)
        {
            if (!Hemisphere.HemisphereMotorConnected)
            {
                GColorEvent geb = new GColorEvent(EventType.Info, "Initial hemisphere motor", null);
                DeviceEvent det = new DeviceEvent(DeviceName, geb);
                EventBus.Instance.PostEvent(det);
            } else
            {
                GColorEvent geb = new GColorEvent(EventType.Info, "Hemisphere motor connected", null);
                DeviceEvent det = new DeviceEvent(DeviceName, geb);
                EventBus.Instance.PostEvent(det);
            }
        }

        void ContinuousCCW()
        {
            if (!ST5.MotorConnected && !ST5.Connect((string)AppSettings.Instance.MotorPort))
            {
                return;
            }

            ST5.ContinuousMotion(Direction.CCW);
        }

        void StepCCW()
        {
            if ((!ST5.MotorConnected && !ST5.Connect((string)AppSettings.Instance.MotorPort)) ||
                (int)AppSettings.Instance.NumberOfSteps == 0)
            {
                return;
            }

            ST5.RotateByAngle(360 / (int)AppSettings.Instance.NumberOfSteps, Direction.CCW);
        }

        void Stop()
        {
            if (!ST5.MotorConnected)
            {
                return;
            }

            ST5.StopMotor();
        }

        void StepCW()
        {
            if ((!ST5.MotorConnected && !ST5.Connect((string)AppSettings.Instance.MotorPort)) ||
                (int)AppSettings.Instance.NumberOfSteps == 0)
            {
                return;
            }

            ST5.RotateByAngle(360 / (int)AppSettings.Instance.NumberOfSteps, Direction.CW);
        }

        void ContinuousCW()
        {
            if (!ST5.MotorConnected && !ST5.Connect((string)AppSettings.Instance.MotorPort))
            {
                return;
            }

            ST5.ContinuousMotion(Direction.CW);
        }

        void SaveImage(object sender, DoWorkEventArgs e)
        {
            string filePath = (string)e.Argument;

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    string extension = Path.GetExtension(filePath);
                    BitmapEncoder encoder = new JpegBitmapEncoder();
                    if (extension.ToUpper() == ".BMP")
                        encoder = new BmpBitmapEncoder();
                    BitmapSource bs = CachedCameraImage;
                    bs.Freeze();
                    encoder.Frames.Add(BitmapFrame.Create(bs));
                    encoder.Save(fileStream);
                    e.Result = filePath + " saved!";
                }
            }
            catch (Exception ex)
            {
                e.Result = "Save Failed: " + ex.Message;
            }
            finally
            {

            }
        }

        void SaveImageCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            IsReady = true;

            //App.LogEntry.AddEntry((string)e.Result);
            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, (string)e.Result, null));
            EventBus.Instance.PostEvent(eb);

            Status = "Connected";
        }

        void SaveMeasurementImages(object sender, DoWorkEventArgs e)
        {
            try
            {
                string color = String.Empty;

                List<System.Drawing.Bitmap> imageList = new List<System.Drawing.Bitmap>();

                if (_measurementImages != null && _measurementImages.Count > 0)
                {
                    //Application.Current.Dispatcher.Invoke((Action)(() =>
                    //                        App.LogEntry.AddEntry("Performing color analysis...")));
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Performing color analysis...", null));
                    EventBus.Instance.PostEvent(eb);
                    foreach (KeyValuePair<double, BitmapSource> kvp in _measurementImages)
                    {
                        imageList.Add(GetBitmap(kvp.Value));
                    }

                    #region color_calc
                    double L = 0, a = 0, b = 0, C = 0, H = 0;
                    double mask_L = 0, mask_A = 0;
                    string L_description = "", C_description = "", H_description = "",
                        comment1 = "", comment2 = "", comment3 = "";

                    bool colorResult = false;
                    //if (!ImageProcessing.IsImageLightStable(ref imageList, ref _backgroundImageList, _imageDiffLimit))
                    //{
                    //    Application.Current.Dispatcher.Invoke((Action)(() =>
                    //                        App.LogEntry.AddEntry("Check light stable: ImageProcessing.IsImageLightStable return false!")));
                    //    using (var f = File.OpenWrite(_lockOutFileName))
                    //    { }
                    //}
                    //else
                    {
                        colorResult = ImageProcessing.GetColor_description(ref imageList, ref _backgroundImageList,
                           ref L, ref a, ref b, ref C, ref H, ref L_description, ref C_description,
                           ref H_description, ref mask_L, ref mask_A, ref comment1, ref comment2, ref comment3);

                        if (colorResult)
                        {

                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //App.LogEntry.AddEntry("Color = " + C_description + ",L = " + L +
                            //    ",C = " + C + ",H = " + H + ", L_des = " + L_description +
                            //    ", C_des = " + C_description + ", H_des = " + H_description)));
                            string s = "Color = " + C_description + ",L = " + L +
                                ",C = " + C + ",H = " + H + ", L_des = " + L_description +
                                ", C_des = " + C_description + ", H_des = " + H_description;
                            eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, s, null));
                            EventBus.Instance.PostEvent(eb);


                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //    App.LogEntry.AddEntry(comment1)));
                            eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, comment1, null));
                            EventBus.Instance.PostEvent(eb);

                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //App.LogEntry.AddEntry(comment2)));
                            eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, comment2, null));
                            EventBus.Instance.PostEvent(eb);
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //App.LogEntry.AddEntry(comment3)));
                            eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, comment3, null));
                            EventBus.Instance.PostEvent(eb);
                        }
                        else
                        {
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //        App.LogEntry.AddEntry(comment3)));
                            eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, comment3, null));
                            EventBus.Instance.PostEvent(eb);
                        }

                        //store dust images////////////////////////////////////////
                        #region save_dust
                        if (ImageProcessingUtility.DustDetectOn)
                        {
                            _dustImages = new Dictionary<double, BitmapSource>();
                            int div = _measurementImages.Count / 3;
                            if ((int)AppSettings.Instance.NumberOfSteps == 0)
                            {
                                _dustImages.Add(div, _measurementImages[div]);
                                _dustImages.Add(2 * div, _measurementImages[2 * div]);
                            }
                            else
                            {
                                int stepAngle = 360 / (int)AppSettings.Instance.NumberOfSteps;
                                _dustImages.Add(stepAngle * div, _measurementImages[stepAngle * div]);
                                _dustImages.Add(stepAngle * div * 2, _measurementImages[stepAngle * div * 2]);
                            }
                        }
                        #endregion
                        ///////////////////////////////////////////////////////////



                        /////////////////////////////////////////////////////
                        //Hiroshi add for camera temp monitoring 5/07/2014
                        /////////////////////////////////////////////////////
                        #region temp_monitor
                        _cameraTempMeasurement = GetCameraTemp();
                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //App.LogEntry.AddEntry("Camera temp=" + _cameraTempMeasurement.ToString() + " bgtemp=" + _cameraTempBG.ToString())));
                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Camera temp=" + _cameraTempMeasurement.ToString() + " bgtemp=" + _cameraTempBG.ToString(), null));
                        EventBus.Instance.PostEvent(eb);

                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //App.LogEntry.AddEntry("Blue=" + GetWBBlue().ToString() + " Red=" + GetWBRed().ToString())));
                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Blue=" + GetWBBlue().ToString() + " Red=" + GetWBRed().ToString(), null));
                        EventBus.Instance.PostEvent(eb);

                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //App.LogEntry.AddEntry("Image count=" + imageList.Count.ToString())));
                        eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Image count=" + imageList.Count.ToString(), null));
                        EventBus.Instance.PostEvent(eb);

                        if (comment1 != "") comment1 = comment1 + "," + comment3 + ", " + GetCameraTemp().ToString() + ", " + _cameraTempBG.ToString() + ", " + GetShutterTime().ToString() + ", " + GetWBBlue().ToString() + ", " + GetWBRed().ToString();
                        if (comment2 != "") comment2 = comment2 + "," + comment3 + ", " + GetCameraTemp().ToString() + ", " + _cameraTempBG.ToString() + ", " + GetShutterTime().ToString() + ", " + GetWBBlue().ToString() + ", " + GetWBRed().ToString();
                        #endregion
                        //////////////////////////////////////////////////////
                    }
                    #endregion



                    #region set_result
                    if (_stone == null)
                        _stone = new Stone();

                    _stone.C = C;
                    _stone.L = L;
                    _stone.H = H;
                    _stone.A = a;
                    _stone.B = b;
                    _stone.HDesc = H_description;
                    _stone.CDesc = C_description;
                    _stone.LDesc = L_description;
                    _stone.Mask_L = mask_L;
                    _stone.Mask_A = mask_A;
                    _stone.Comment1 = comment1;
                    _stone.Comment2 = comment2;
                    _stone.Comment3 = comment3;
                    _stone.GoodColorResult = colorResult;
                    #endregion

                }

                e.Result = "Measurement Complete!";

                if (color != String.Empty)
                {
                    e.Result += " The calculated color is " + color;
                }


            }
            catch (Exception ex)
            {
                e.Result = "Measurement images save failed: " + ex.Message;
            }
            finally
            {

            }
        }

        //Hiroshi add for status monitoring
        double GetCameraTemp()
        {
            Dictionary<CAMERA_PROPERTY, double> readings = new Dictionary<CAMERA_PROPERTY, double>();
            readings.Add(CAMERA_PROPERTY.Temperature, 0);
            _camera.GetInitializationPropertyValues(readings);
            return readings[CAMERA_PROPERTY.Temperature];
        }

        double GetShutterTime()
        {
            Dictionary<CAMERA_PROPERTY, double> readings = new Dictionary<CAMERA_PROPERTY, double>();
            readings.Add(CAMERA_PROPERTY.Shutter, 0);
            _camera.GetInitializationPropertyValues(readings);
            return readings[CAMERA_PROPERTY.Shutter];
        }

        double GetWBRed()
        {
            Dictionary<CAMERA_PROPERTY, double> readings = new Dictionary<CAMERA_PROPERTY, double>();
            readings.Add(CAMERA_PROPERTY.WhiteBalanceRed, 0);
            _camera.GetInitializationPropertyValues(readings);
            return readings[CAMERA_PROPERTY.WhiteBalanceRed];
        }

        double GetWBBlue()
        {
            Dictionary<CAMERA_PROPERTY, double> readings = new Dictionary<CAMERA_PROPERTY, double>();
            readings.Add(CAMERA_PROPERTY.WhiteBalanceBlue, 0);
            _camera.GetInitializationPropertyValues(readings);
            return readings[CAMERA_PROPERTY.WhiteBalanceBlue];
        }

        void SaveMeasurementImagesTask(Dictionary<double, BitmapSource> measurementImages, List<System.Drawing.Bitmap> bgImageList)
        {
            try
            {
                string dateTimeStamp = DateTime.UtcNow.ToString("yyyy-MM-dd") + "_" + DateTime.UtcNow.ToString("HHmmss");
                string rootDirectory = (string)AppSettings.Instance.MeasurementsFolder + @"\" +
                    (_stone != null ? (_stone.ControlNumber + @"\") : "NoControlNumber\\");

                Directory.CreateDirectory(rootDirectory);

                //Application.Current.Dispatcher.Invoke((Action)(() =>
                //    App.LogEntry.AddEntry("Saving measurement images to " +
                //        rootDirectory)));

                //Status = "Saving Measurement Images...";

                int measurementImageIndex = 0;

                foreach (KeyValuePair<double, BitmapSource> kvp in measurementImages)
                {
                    measurementImageIndex++;

                    string filePath = rootDirectory + "StoneImage"
                        + kvp.Key + "_" + dateTimeStamp + (string)AppSettings.Instance.MeasurementsFileExtension;

                    if ((measurementImageIndex == measurementImages.Count) ||
                            (measurementImageIndex == ((1 * measurementImages.Count) / 3)) ||
                            (measurementImageIndex == ((2 * measurementImages.Count) / 3))
                        )
                    {
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            BitmapEncoder encoder = new JpegBitmapEncoder();
                            if (((string)AppSettings.Instance.MeasurementsFileExtension).ToUpper() == ".BMP")
                                encoder = new BmpBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(kvp.Value));
                            encoder.Save(fileStream);
                        }
                    }
                }

                //save background image[s]
                measurementImageIndex = 0;
                for (int i = 0; i < bgImageList.Count; i++)
                {
                    measurementImageIndex++;

                    string bgFilePath = rootDirectory + "BackgroundImage" + i
                            + "_" + dateTimeStamp + (string)AppSettings.Instance.MeasurementsFileExtension;

                    if ((measurementImageIndex == bgImageList.Count) ||
                            (measurementImageIndex == ((1 * bgImageList.Count) / 3)) ||
                            (measurementImageIndex == ((2 * bgImageList.Count) / 3))
                        )
                    {
                        if (((string)AppSettings.Instance.MeasurementsFileExtension).ToUpper() == ".BMP")
                            bgImageList[i].Save(bgFilePath, System.Drawing.Imaging.ImageFormat.Bmp);
                        else
                            bgImageList[i].Save(bgFilePath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
        }

        void SaveMeasurementImagesCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if ((bool)AppSettings.Instance.AutoOpenClose)
                    OpenHemisphereCommand();

                //Check dust in images///////////////////////////////////////////////////////////
                #region check_dust

                bool dustPresent = true;

                if (_dustImages != null && _dustImages.Count >= 2)
                {
                    int objectCount = -1;
                    double maskArea = -1;
                    string unused = "";

                    System.Drawing.Bitmap[] dBitmap = new System.Drawing.Bitmap[_dustImages.Count];
                    int[] imageNumber = new int[_dustImages.Count];
                    int count = 0;
                    foreach (var kvp in _dustImages)
                    {
                        dBitmap[count] = GetBitmap(kvp.Value);
                        imageNumber[count] = (int)kvp.Key;
                        count++;
                    }

                    var img = ImageProcessingUtility.ObjectDetector(dBitmap[0], out objectCount, out maskArea);
                    bool noDust = ImageProcessing.check_NO_measurementDust(ref dBitmap[0], ref unused);
                    bool userAccepted = false;

                    if ((objectCount != 0) || !noDust)
                    {
                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //{
                        //    var verifyDustDlg = new gColor.View.VerifyDustDialog();
                        //    var verifyDustDlgVM = new VerifyDustDialogViewModel(imageNumber[0].ToString(), GetBitmapSource(img));
                        //    verifyDustDlg.DataContext = verifyDustDlgVM;
                        //    userAccepted = (bool)verifyDustDlg.ShowDialog();
                        //}));

                        // todo dialog
                    }
                    else
                        userAccepted = true;

                    if (userAccepted)
                    {
                        //user checked the image and accepted the image
                        //check second dust image
                        img = ImageProcessingUtility.ObjectDetector(dBitmap[1], out objectCount, out maskArea);
                        noDust = ImageProcessing.check_NO_measurementDust(ref dBitmap[1], ref unused);
                        if ((objectCount != 0) || !noDust)
                        {
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //{
                            //    var verifyDustDlg = new gColor.View.VerifyDustDialog();
                            //    var verifyDustDlgVM = new VerifyDustDialogViewModel(imageNumber[0].ToString(), GetBitmapSource(img));
                            //    verifyDustDlg.DataContext = verifyDustDlgVM;
                            //    userAccepted = (bool)verifyDustDlg.ShowDialog();
                            //}));
                            //todo: dialog
                        }
                        else
                            userAccepted = true;
                    }

                    dustPresent = !userAccepted;
                }
                else
                    dustPresent = false;

                if (dustPresent)
                {
                    //Application.Current.Dispatcher.Invoke((Action)(() =>
                    //    App.LogEntry.AddEntry("User rejected measurement due to dust!", true)));
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "User rejected measurement due to dust!", null));
                    EventBus.Instance.PostEvent(eb);
                }

                #endregion
                /////////////////////////////////////////////////////////////////////////////////


                string deviceName = ImageProcessing.getDevicename();
                string spectrumFileName = deviceName + DateTime.Now.ToString("yyyyMMddhhmmss") + ".csv";

                if (!dustPresent)
                {
                    if (MeasureIconText.Length > 0)
                    {
                        if (!GlobalVariables.InTestMode && (bool)AppSettings.Instance.ExtractCalDataToTextFile &&
                                ((string)AppSettings.Instance.CalDataTextFilePath).Length > 0)
                        {
                            //execute on main thread
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                _stone.ControlNumber = MeasureIconText;
                                //_stone.Save((string)AppSettings.Instance.MetrologyFilePath"), deviceName + "ColorimeterMetrology.csv");
                                _stone.Save((string)AppSettings.Instance.CalDataTextFilePath, spectrumFileName);
                            }));
                        }

                        var targetStone = CalibrationStoneTable.CalStoneTable.Select("Target = '" + MeasureIconText + "'");
                        int index = -1, measureRowIndex = -1;

                        try
                        {
                            var targetStones = ((string)AppSettings.Instance.DailyMonitorTargetList).Split(',');
                            int currentIndex = CalibrationStoneTable.CalStoneTable.Rows.IndexOf(targetStone[0]);
                            index = Array.IndexOf(targetStones, currentIndex.ToString());
                            _dailyMonitorTargetIndex = index + 1;

                            //populate _dtReferenceStoneResults
                            var foundRows = _dtReferenceStoneResults.Select("ReferenceStone = '" + MeasureIconText + "'");
                            measureRowIndex = _dtReferenceStoneResults.Rows.IndexOf(foundRows[0]) + 1;
                            int diffRowIndex = measureRowIndex + 1;

                            _dtReferenceStoneResults.Rows[measureRowIndex]["L"] = Math.Round(_stone.L, 3);
                            _dtReferenceStoneResults.Rows[diffRowIndex]["L"] =
                                Math.Round(Math.Round(_stone.L, 3) - Convert.ToDouble(foundRows[0]["L"]), 3);
                            _dtReferenceStoneResults.Rows[measureRowIndex]["a"] = Math.Round(_stone.A, 3);
                            _dtReferenceStoneResults.Rows[diffRowIndex]["a"] =
                                Math.Round(Math.Round(_stone.A, 3) - Convert.ToDouble(foundRows[0]["a"]), 3);
                            _dtReferenceStoneResults.Rows[measureRowIndex]["b"] = Math.Round(_stone.B, 3);
                            _dtReferenceStoneResults.Rows[diffRowIndex]["b"] =
                                Math.Round(Math.Round(_stone.B, 3) - Convert.ToDouble(foundRows[0]["b"]), 3);
                            _dtReferenceStoneResults.Rows[measureRowIndex]["C"] = Math.Round(_stone.C, 3);
                            _dtReferenceStoneResults.Rows[diffRowIndex]["C"] =
                                Math.Round(Math.Round(_stone.C, 3) - Convert.ToDouble(foundRows[0]["C"]), 3);
                            _dtReferenceStoneResults.Rows[measureRowIndex]["H"] = Math.Round(_stone.H, 3);
                            _dtReferenceStoneResults.Rows[diffRowIndex]["H"] =
                                Math.Round(Math.Round(_stone.H, 3) - Convert.ToDouble(foundRows[0]["H"]), 3);
                            _dtReferenceStoneResults.Rows[measureRowIndex]["MaskL"] = Math.Round(_stone.Mask_L, 3);
                            _dtReferenceStoneResults.Rows[diffRowIndex]["MaskL"] =
                                Math.Round(Math.Round(_stone.Mask_L, 3) - Convert.ToDouble(foundRows[0]["MaskL"]), 3);
                            _dtReferenceStoneResults.Rows[measureRowIndex]["MaskA"] = Math.Round(_stone.Mask_A, 3);
                            _dtReferenceStoneResults.Rows[diffRowIndex]["MaskA"] =
                                Math.Round(Math.Round(_stone.Mask_A, 3) - Convert.ToDouble(foundRows[0]["MaskA"]), 3);

                            if (index < targetStones.Length - 1)
                            {
                                MeasureIconText = CalibrationStoneTable.CalStoneTable.Rows[Convert.ToInt32(targetStones[_dailyMonitorTargetIndex])]["Target"].ToString();
                                MeasureIconImageSource = "..\\Images\\cal_stone.png";
                                MeasureIconToolTip = "Measure calibration stone";
                            }
                            else
                            {
                                if (Convert.ToDouble(_dtReferenceStoneResults.Rows[3]["b"]) - Convert.ToDouble(_dtReferenceStoneResults.Rows[0]["b"]) > 0)
                                {
                                    var averageRow = _dtReferenceStoneResults.Select("ReferenceStone = 'Average'");
                                    var diffRows = _dtReferenceStoneResults.Select("Description = 'Diff'");

                                    double sumL = 0, suma = 0, sumb = 0, sumC = 0, sumH = 0, sumMaskL = 0, sumMaskA = 0;
                                    int count = diffRows.Length - 1;
                                    for (int i = 0; i < count; i++)
                                    {
                                        sumL += Convert.ToDouble(diffRows[i]["L"]);
                                        suma += Convert.ToDouble(diffRows[i]["a"]);
                                        sumb += Convert.ToDouble(diffRows[i]["b"]);
                                        sumC += Convert.ToDouble(diffRows[i]["C"]);
                                        sumH += Convert.ToDouble(diffRows[i]["H"]);
                                        sumMaskL += Convert.ToDouble(diffRows[i]["MaskL"]);
                                        sumMaskA += Convert.ToDouble(diffRows[i]["MaskA"]);
                                    }

                                    averageRow[0]["L"] = Math.Round(sumL / count, 3);
                                    averageRow[0]["a"] = Math.Round(suma / count, 3);
                                    averageRow[0]["b"] = Math.Round(sumb / count, 3);
                                    averageRow[0]["C"] = Math.Round(sumC / count, 3);
                                    averageRow[0]["H"] = Math.Round(sumH / count, 3);
                                    averageRow[0]["MaskL"] = Math.Round(sumMaskL / count, 3);
                                    averageRow[0]["MaskA"] = Math.Round(sumMaskA / count, 3);

                                    //Application.Current.Dispatcher.Invoke((Action)(() =>
                                    //{
                                    //    var dialogOutOfRange = new gColor.View.DailyMonitorOutOfRange();
                                    //    var OutOfRangeDataViewModel = new gColor.ViewModel.DailyMonitorViewModel(_dtReferenceStoneResults);
                                    //    dialogOutOfRange.DataContext = OutOfRangeDataViewModel;
                                    //    dialogOutOfRange.ShowDialog();
                                    //}));
                                    //todo dialog

                                }
                                else
                                {
                                    MessageBox.Show("The order of the reference stones measurement is incorrect.\nPlease check the L a b settings tab", "Error in settings");
                                }

                                _dailyMonitorTargetIndex = 0; //cycle back to first calibration stone
                                MeasureIconText = CalibrationStoneTable.CalStoneTable.Rows[Convert.ToInt32(targetStones[_dailyMonitorTargetIndex])]["Target"].ToString();
                                MeasureIconImageSource = "..\\Images\\cal_stone.png";
                                MeasureIconToolTip = "Measure calibration stone";
                            }
                        }
                        catch (Exception ex)
                        {
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //{
                            //    App.LogEntry.AddEntry(ex.Message);
                            //}));

                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, ex.Message, null));
                            EventBus.Instance.PostEvent(eb);
                        }


                    }
                    else if (!GlobalVariables.InTestMode && _stone != null && (bool)AppSettings.Instance.ExtractToTextFile &&
                            ((string)AppSettings.Instance.TextFilePath).Length > 0)
                    {
                        if (File.Exists(_lockOutFileName))
                        {
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //     App.LogEntry.AddEntry("[Warning: check light stability]: [Exists(_lockOutFileName)]", true)));

                            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "[Warning: check light stability]: [Exists(_lockOutFileName)]", null));
                            EventBus.Instance.PostEvent(eb);
                        }
                        else
                        {
                            //Application.Current.Dispatcher.Invoke((Action)(() =>
                            //{
                            //    var dialogSaveWindow = new gColor.View.SaveData();
                            //    var dialogSaveDataViewModel = new gColor.ViewModel.SaveDataViewModel(_stone);
                            //    dialogSaveWindow.DataContext = dialogSaveDataViewModel;
                            //    bool? dialogResult = dialogSaveWindow.ShowDialog();
                            //    if (dialogResult == true)
                            //    {
                            //        _stone.Save((string)AppSettings.Instance.TextFilePath, spectrumFileName);
                            //        _stone.Save((string)AppSettings.Instance.MetrologyFilePath, deviceName + "ColorimeterMetrology.csv");

                            //        #region save_images
                            //        if ((bool)AppSettings.Instance.SaveMeasuments && ((string)AppSettings.Instance.MeasurementsFolder).Length > 0)
                            //        {
                            //            SaveMeasurementImagesTask(_measurementImages, _backgroundImageList);
                            //        }
                            //        #endregion
                            //    }
                            //}));
                            // todo dialog
                        }
                    }
                }

                IsReady = true;
                MeasurementActive = false;
                Status = "Connected";

                GColorEvent evb = new GColorEvent(EventType.Status, UtilsLib.Status.Ready.ToString(), new DeviceStatus(UtilsLib.Status.Ready));
                DeviceEvent de = new DeviceEvent(DeviceName, evb);
                EventBus.Instance.PostEvent(de);

                //if (!GlobalVariables.InTestMode)
                if(false)
                {
                    /////////////////////////////////////////////////////
                    //Hiroshi add for Re-calibration warning
                    /////////////////////////////////////////////////////
                    if (Math.Abs(_cameraTempMeasurement - _cameraTempBG) > Convert.ToDouble(AppSettings.Instance.Temperature))
                    {
                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //App.LogEntry.AddEntry("Calibration again " + "Temp_BG=" + _cameraTempBG.ToString() + ", Temp_Measure=" + _cameraTempMeasurement.ToString(), true)));
                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Calibration again " + "Temp_BG=" + _cameraTempBG.ToString() + ", Temp_Measure=" + _cameraTempMeasurement.ToString(), null));
                        EventBus.Instance.PostEvent(eb);

                        _backgroundImage = null;
                        _backgroundImageList = new List<System.Drawing.Bitmap>();
                        _takingBackgroundImages = false;
                        _mode = Mode.Regular;
                        _calibrationStablizeCount = 0;
                        IsReady = true;

                        return;
                    }

                    //if ((_tempChange == true && sw.ElapsedMilliseconds > 240000) || sw.ElapsedMilliseconds > 600000)
                    if (sw.ElapsedMilliseconds > (Convert.ToDouble(AppSettings.Instance.Time) * 1000 - 20000))
                    {
                        sw.Stop();
                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //App.LogEntry.AddEntry("Calibration again " + "time[ms]=" + sw.ElapsedMilliseconds.ToString(), true)));
                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Calibration again " + "time[ms]=" + sw.ElapsedMilliseconds.ToString(), null));
                        EventBus.Instance.PostEvent(eb);

                        _backgroundImage = null;
                        _backgroundImageList = new List<System.Drawing.Bitmap>();
                        _takingBackgroundImages = false;
                        _mode = Mode.Regular;
                        _calibrationStablizeCount = 0;
                        IsReady = true;
                        return;
                    }


                    /////////////////////////////////////////////////////
                    //Hiroshi add for Re-calibration warning
                    /////////////////////////////////////////////////////
                    if (Math.Abs(_cameraTempMeasurement - _cameraTempBG) > Convert.ToDouble(AppSettings.Instance.Temperature))
                    {
                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //App.LogEntry.AddEntry("Calibration again " + "Temp_BG=" + _cameraTempBG.ToString() + ", Temp_Measure=" + _cameraTempMeasurement.ToString(), true)));
                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration again " + "Temp_BG=" + _cameraTempBG.ToString() + ", Temp_Measure=" + _cameraTempMeasurement.ToString(), null));
                        EventBus.Instance.PostEvent(eb);

                        _backgroundImage = null;
                        _backgroundImageList = new List<System.Drawing.Bitmap>();
                        _takingBackgroundImages = false;
                        _mode = Mode.Regular;
                        _calibrationStablizeCount = 0;
                        IsReady = true;

                    }

                    //if ((_tempChange == true && sw.ElapsedMilliseconds > 240000) || sw.ElapsedMilliseconds > 600000)
                    if (sw.ElapsedMilliseconds > Convert.ToDouble(AppSettings.Instance.Time) * 1000)
                    {
                        sw.Stop();
                        //Application.Current.Dispatcher.Invoke((Action)(() =>
                        //App.LogEntry.AddEntry("Calibration again " + "time[ms]=" + sw.ElapsedMilliseconds.ToString(), true)));
                        DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Calibration again " + "time[ms]=" + sw.ElapsedMilliseconds.ToString(), null));
                        EventBus.Instance.PostEvent(eb);

                        _backgroundImage = null;
                        _backgroundImageList = new List<System.Drawing.Bitmap>();
                        _takingBackgroundImages = false;
                        _mode = Mode.Regular;
                        _calibrationStablizeCount = 0;
                        IsReady = true;

                    }
                }
            }
            finally
            {
                _measurementImages = null;
            }

            //Application.Current.Dispatcher.Invoke((Action)(() =>
            //        App.LogEntry.AddEntry((string)e.Result)));
            DeviceEvent eba = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, (string)e.Result, null));
            EventBus.Instance.PostEvent(eba);
        }

        void StopCamera()
        {
            _keepRetrievingImages = false;
            //App.LogEntry.AddEntry("Stopped camera capture");
            DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Stopped camera capture", null));
            EventBus.Instance.PostEvent(eb);
            _camera.DisConnect();
            //App.LogEntry.AddEntry("Camera Disconnected");
            eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Info, "Camera Disconnected", null));
            EventBus.Instance.PostEvent(eb);
        }

        ~gColorManager()
        {
            _keepRetrievingImages = false;
            CameraDispose();
        }

        void CameraDispose()
        {
            Hemisphere.Disconnect();
            if (_camera != null)
                _camera.DisConnect();
        }


        public override bool ConnectEx()
        {
            Connect();
            return true;
        }

        public override bool CalibrateEx()
        {
            Calibrate();
            return true;
        }

        public override bool MeasureEx()
        {
            Measure();
            return true;
        }

        public override bool IsDeviceReady()
        {

            return _connected && Hemisphere.IsOpen && !Busy && IsReady;
        }

        public override bool NeedCalibration()
        {
            if(_firstCalibration)
            {
                calibrated = false;
                return true;
            } 

            if (calibrated)
            {
                if (Math.Abs(_cameraTempMeasurement - _cameraTempBG) > Convert.ToDouble(AppSettings.Instance.Temperature))
                {
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Calibration again " + "Temp_BG=" + _cameraTempBG.ToString() + ", Temp_Measure=" + _cameraTempMeasurement.ToString(), null));
                    EventBus.Instance.PostEvent(eb);

                    _backgroundImage = null;
                    _backgroundImageList = new List<System.Drawing.Bitmap>();
                    _takingBackgroundImages = false;
                    _mode = Mode.Regular;
                    _calibrationStablizeCount = 0;
                    IsReady = true;
                    calibrated = false;
                    return true;
                }

                if (sw.ElapsedMilliseconds > (Convert.ToDouble(AppSettings.Instance.Time) * 1000 - 20000))
                {
                    sw.Stop();
                    DeviceEvent eb = new DeviceEvent(DeviceName, new GColorEvent(EventType.Error, "Calibration again " + "time[ms]=" + sw.ElapsedMilliseconds.ToString(), null));
                    EventBus.Instance.PostEvent(eb);

                    _backgroundImage = null;
                    _backgroundImageList = new List<System.Drawing.Bitmap>();
                    _takingBackgroundImages = false;
                    _mode = Mode.Regular;
                    _calibrationStablizeCount = 0;
                    IsReady = true;
                    calibrated = false;
                    return true;
                }
                return false;
            }
            else
            {
                return true;
            }

        }

        public override bool DisconnectEx()
        {
            CameraDispose();
            return true;
        }

        public override bool InitialStone(string ctlNum)
        {
            _stone = new Stone(ctlNum);
            return true;
        }
    }

    public class GlobalVariables
    {
        public static bool IsAdmin = false;
        public static bool InTestMode = false;

        static LightStabilitySettings _lightStabilitySettings = new LightStabilitySettings();
        public static LightStabilitySettings LightStableSettings { get { return _lightStabilitySettings; } }
    }
}
