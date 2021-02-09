﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UtilsLib;
using gColorLib;
using ViewModelLib;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Collections.ObjectModel;
using AutoGrading.Model;
using System.Net.Sockets;
using System.ComponentModel;
using System.Windows.Input;
using gUVLib;
using DplLib;
using FTIRLib;

namespace AutoGrading.ViewModel
{
    enum DeviceType
    {
        DPL = 0,
        FTIR,
        ColorMeter,
        AccuFluo,
        //FTIR,
        URobot
    };

    enum URAction
    {
        PickUp = 0,
        DropOff
    }

    class ProcessStatus
    {
        public bool pickedUp;
        public bool readyCV;
        public bool stoneOnCV;
        public bool removedCV;
        public bool dropped;
        public bool robot_ready;
        public bool error;
        public ManualResetEvent processEvent;
        public int index;
        public string controlNum;
        public ProcessStatus(int index)
        {
            this.index = index;
        }
        public ProcessStatus(int index, string ctlNum)
        {
            this.index = index;
            controlNum = ctlNum;
        }
        public ProcessStatus(int index, ManualResetEvent pe)
        {
            this.index = index;
            processEvent = pe;
        }
    }

    class NewProcessStatus
    {
        public bool pickedUp;
        public bool droppedOff;
        public bool dropped;
        public bool error;
        public ManualResetEvent processEvent;
        public string controlNum;
        public string stageID;
        public NewProcessStatus(string stageID, string ctlNum)
        {
            this.stageID = stageID;
            controlNum = ctlNum;
        }
    }

    class URHostStatus
    {
        public bool poweringon;
        public bool idle;
        public bool loadingprogram;
        public bool brakereleasing;
        public bool running;
        public bool startingprogram;
        public bool poweringoff;
        public bool poweroff;
        public bool shuttingdown;
        public bool halted;
    }

    class CommandQueue
    {
        public readonly object aLock = new object();
        static public AutoResetEvent commandEvent = new AutoResetEvent(false);
        private Queue<Tuple<string, long>> _queue;
        private string ProcID;

        public CommandQueue(string id)
        {
            _queue = new Queue<Tuple<string, long>>();
            ProcID = id;
        }

        public void Enqueue(Tuple<string, long> v)
        {
            // TODO: exception handling for _queue.Length reaches Max value.
            lock (aLock)
            {
                _queue.Enqueue(v);
                commandEvent.Set();
            }
        }

        public Tuple<string, long> Dequeue()
        {
            // TODO: exception handling for _queue is empty
            lock (aLock)
            {
                if (Length > 0)
                {
                    return _queue.Dequeue();
                }
                else
                {
                    return null;
                }
            }
        }

        public Tuple<string, long> Peek()
        {
            lock (aLock)
            {
                if(Length > 0)
                {
                    return _queue.Peek();
                }
                else
                {
                    return null;
                }
            }
        }

        public void Clear()
        {
            _queue.Clear();
        }

        private int Length
        { get { return _queue.Count; } }
    }

    class WorkArg
    {
        public ProcessStatus procStatus;
        public string threadName;
        public ManualResetEvent processEvent;
        public bool EvEnd;
        public CommandQueue commandQueue;
        public bool procBusy;

        public WorkArg(ProcessStatus procStatus, string threadName, ManualResetEvent processEvent)
        {
            this.procStatus = procStatus;
            this.threadName = threadName;
            this.processEvent = processEvent;
            this.EvEnd = false;

            commandQueue = new CommandQueue(threadName);
            procBusy = false;
        }
    }

    class NewWorkArg
    {
        public NewProcessStatus procStatus;
        public string threadName;
        public ManualResetEvent processEvent;
        public bool EvEnd;
        public CommandQueue commandQueue;
        public bool uRBusy;
        public bool procBusy;

        public NewWorkArg(NewProcessStatus procStatus, string threadName, ManualResetEvent processEvent)
        {
            this.procStatus = procStatus;
            this.threadName = threadName;
            this.processEvent = processEvent;
            this.EvEnd = false;

            commandQueue = new CommandQueue(threadName);
            uRBusy = false;
            procBusy = false;
        }
    }

    class WorkQueue
    {
        public readonly object aLock = new object();
        public AutoResetEvent queueEvent = new AutoResetEvent(false);

        private Queue<NewProcessStatus> _queue;

        public WorkQueue()
        {
            _queue = new Queue<NewProcessStatus>();
        }

        public void Enqueue(NewProcessStatus v)
        {
            // TODO: exception handling for _queue.Length reaches Max value.
            lock (aLock)
            {
                _queue.Enqueue(v);
                queueEvent.Set();
            }
        }

        public NewProcessStatus Dequeue()
        {
            // TODO: exception handling for _queue is empty
            lock (aLock)
            {
                if (Length > 0)
                {
                    return _queue.Dequeue();
                } else
                {
                    return null;
                }
            }
        }

        public void Clear()
        {
            lock (aLock)
            {
                _queue.Clear();
            }
        }

        private int Length
        { get { return _queue.Count; } }
    }

    class ThreadPool
    {
        ControlViewModel instance = null;
        Thread[] threads = null;
        WorkQueue workQueue = null;

        //public WorkArg[] WorkArgs = null;
        public NewWorkArg[] WorkArgs = null;
        public int NumWorkers;

        public ThreadPool(ControlViewModel instance, WorkQueue workQueue, int num)
        {
            this.instance = instance;
            NumWorkers = num;
            threads = new Thread[NumWorkers];
            //WorkArgs = new WorkArg[NumWorkers];
            WorkArgs = new NewWorkArg[NumWorkers];

            this.workQueue = workQueue;
            for (int i = 0; i < NumWorkers; ++i)
            {
                threads[i] = new Thread(new ParameterizedThreadStart(instance.StartWorker));
                threads[i].Name = (i+1).ToString();

                WorkArgs[i] = new NewWorkArg(null, threads[i].Name, new ManualResetEvent(false));
            }
        }

        public void Start()
        {
            for (int i = 0; i < NumWorkers; ++i)
            {
                threads[i].Start(WorkArgs[i]);
            }
        }

        public void Join()
        {
            for (int i = 0; i < NumWorkers; ++i)
            {
                threads[i].Join();
            }
        }

        public void End()
        {
            for (int i = 0; i < NumWorkers; ++i)
            {
                WorkArgs[i].EvEnd = true;
                workQueue.queueEvent.Set();
                Thread.Sleep(20);
            }
        }
    }

    public class ControlViewModel : ViewModelLib.ViewModelBase
    {
        public LogEntryViewModel LogEntryVM { get { return App.LogEntry; } }

        public RelayCommand CommandConnectAll { get; set; }
        public RelayCommand CommandStageSettings { get; set; }
        public RelayCommand CommandCalibrateAll { get; set; }
        public RelayCommand CommandMeasureAll { get; set; }
        public RelayCommand CommandStartProcess { get; set; }
        public RelayCommand CommandTestUR { get; set; }
        public RelayCommand CommandOpenHemisphere { get; set; }
        public RelayCommand CommandCloseHemisphere { get; set; }
        public RelayCommand CommandURResume { get; set; }
        public RelayCommand CommandCalibrate { get; set; }

        ManualResetEvent ProcessEvent = new ManualResetEvent(false);
        ManualResetEvent UREvent = new ManualResetEvent(false);
        
        static int processing = 0;
        static int count = 0;
        static int NumWorkers = 2;
        WorkQueue workQueue = null;
        ThreadPool threadPool = null;
        bool usingThreadPool = true;

        Thread urThread = null;
        URHostStatus urHostStatus = null;

        Thread urCommandThread = null;
        bool urCommandThreadStop = false;

        List<ProcessStatus> processStatusList = new List<ProcessStatus>();
        List<DeviceStatus> deviceStatusList = new List<DeviceStatus>();

        bool testing = false;

        public ControlViewModel()
        {
            EventBus.Instance.Register(this);

            Locations = new ObservableCollection<string>() { "Pick-up1", "DPL", "ColorMeter", "AccuFluo", "FTIR", "Pick-up2" };
            Location = Locations[0];

            Actions = new ObservableCollection<string>() { "Pick Up", "Drop Off" };
            Action = Actions[0];

            ProcIDs = new ObservableCollection<string>() { "1", "2" };
            ProcID = ProcIDs[0];

            DeviceManagers = new ObservableCollection<DeviceManager>();
            for(int i = 0; i <= (int)DeviceType.URobot; i++)
            {
                if (((DeviceType)i).ToString() == "ColorMeter")
                {
                    gColorManager manager = new gColorManager("ColorMeter", App.Settings.DeviceList.Contains("ColorMeter"));
                    DeviceManagers.Add(manager);
                    manager.AddImageReceivedEventSubscriber(ImageReceivedHandler);
                }
                if (((DeviceType)i).ToString() == "URobot")
                {
                    URobotManager manager = new URobotManager("URobot", App.Settings.DeviceList.Contains("URobot"));
                    DeviceManagers.Add(manager);
                    manager.AddImageReceivedEventSubscriber(ImageReceivedHandler);
                    //manager.AddURHostConnectedEventSubscriber(URHostConnectedHandler);
                }
                if (((DeviceType)i).ToString() == "AccuFluo")
                {
                    gUVManager manager = new gUVManager("AccuFluo", App.Settings.DeviceList.Contains("AccuFluo"));
                    DeviceManagers.Add(manager);
                    manager.AddImageReceivedEventSubscriber(ImageReceivedHandler);
                }
                if (((DeviceType)i).ToString() == "DPL")
                {
                    DplManager manager = new DplManager("DPL", App.Settings.DeviceList.Contains("DPL"));
                    DeviceManagers.Add(manager);
                    //manager.AddImageReceivedEventSubscriber(ImageReceivedHandler);
                }
                if (((DeviceType)i).ToString() == "FTIR")
                {
                    FTIRManager manager = new FTIRManager("FTIR", App.Settings.DeviceList.Contains("FTIR"));
                    DeviceManagers.Add(manager);
                    //manager.AddImageReceivedEventSubscriber(ImageReceivedHandler);
                }
            }

            for(int i = 0; i < (int)DeviceType.URobot; i++)
            {
                DeviceStatus ds = new DeviceStatus(Status.Ready);
                deviceStatusList.Add(ds);
            }

            CommandConnectAll = new RelayCommand(param => ConnectAll());
            CommandStageSettings = new RelayCommand(param => StageSettings());
            CommandCalibrateAll = new RelayCommand(param => CalibrateAll());
            CommandMeasureAll = new RelayCommand(param => MeasureAll());
            CommandStartProcess = new RelayCommand(param => StartProcess());
            CommandTestUR = new RelayCommand(param => TestUR());
            CommandOpenHemisphere = new RelayCommand(param => OpenHemisphere());
            CommandCloseHemisphere = new RelayCommand(param => CloseHemisphere());
            CommandURResume = new RelayCommand(param => URResume());
            CommandCalibrate = new RelayCommand(param => Calibrate());

            if (usingThreadPool)
            {
                workQueue = new WorkQueue();
                threadPool = new ThreadPool(this, workQueue, NumWorkers);
                threadPool.Start();
            }

            urCommandThread = StartURCommandThread();
        }

        #region properties
        public ObservableCollection<DeviceManager> DeviceManagers { get; set; }

        public ObservableCollection<string> Locations { get; }
        public ObservableCollection<string> Actions { get; }
        public ObservableCollection<string> ProcIDs { get; }

        string _procID;
        public string ProcID
        {
            get { return _procID; }
            set
            {
                if (_procID == value)
                {
                    return;
                }
                _procID = value;
                OnPropertyChanged("ProcID");
            }
        }

        string _action;
        public string Action
        {
            get { return _action; }
            set
            {
                if(_action == value)
                {
                    return;
                }
                _action = value;
                OnPropertyChanged("Action");
            }
        }

        string _location;
        public string Location
        {
            get { return _location; }
            set
            {
                if(_location == value)
                {
                    return;
                }
                _location = value;
                OnPropertyChanged("Location");
            }
        }

        BitmapSource _gColorImage;
        public BitmapSource GColorImage
        {
            get { return _gColorImage; }
            set
            {
                _gColorImage = value;
                OnPropertyChanged("GColorImage");
            }
        }

        BitmapSource _gUVImage;
        public BitmapSource GUVImage
        {
            get { return _gUVImage; }
            set
            {
                _gUVImage = value;
                OnPropertyChanged("GUVImage");
            }
        }

        BitmapSource _uRobotImage;
        public BitmapSource URobotImage
        {
            get { return _uRobotImage; }
            set
            {
                _uRobotImage = value;
                OnPropertyChanged("URobotImage");
            }
        }

        object _selectedDevice;
        public object SelectedDevice
        {
            get { return _selectedDevice; }
            set
            {
                _selectedDevice = value;
                OnPropertyChanged("SelectedDevice");
            }
        }

        bool _mainLight = false;
        public bool MainLight
        {
            get { return _mainLight; }
            set
            {
                if(_mainLight == value)
                {
                    return;
                }
                _mainLight = value;
                toggleLight(_mainLight);
                OnPropertyChanged("MainLight");
            }
        }

        #endregion

        void toggleLight(bool on)
        {
            gUVManager uvManager = (gUVManager)DeviceManagers[2];
            uvManager.MainLight = on;

            //gColorManager colorManager = (gColorManager)DeviceManagers[1];
            //colorManager.MainLight = on;
        }

        public void ConnectingDevices()
        {

        }

        void ConnectAll()
        {
            for(int i = 0; i < DeviceManagers.Count; i++)
            {
                DeviceManager dm = DeviceManagers[i];
                if (Array.IndexOf(App.Settings.Devices, dm.DeviceName) < 0)
                {
                    continue;
                }
                dm.ConnectEx();
            }
        }

        void StageSettings()
        {

        }

        void Calibrate()
        {
            if(SelectedDevice != null)
            {
                DeviceManager dm = (DeviceManager)SelectedDevice;
                dm.CalibrateEx();
            }
        }

        void CalibrateAll()
        {
            for (int i = 0; i < DeviceManagers.Count; i++)
            {
                DeviceManager dm = DeviceManagers[i];
                if (Array.IndexOf(App.Settings.Devices, dm.DeviceName) < 0)
                {
                    continue;
                }
                dm.CalibrateEx();
            }
        }

        void MeasureAll()
        {
            for (int i = 0; i < DeviceManagers.Count - 1; i++)
            {
                processing++;
                ProcessEvent.Reset();
                DeviceManager dm = DeviceManagers[i];
                if (Array.IndexOf(App.Settings.Devices, dm.DeviceName) < 0)
                {
                    continue;
                }
                dm.InitialStone(i.ToString());
                dm.MeasureEx();

                // measurement test for accufluor, colormeter
                string stageID = "1";
                //ProcessStatus v = new ProcessStatus(count);
                // adding control number for stone save function
                NewProcessStatus v = new NewProcessStatus(stageID, "1");
                //workQueue.Enqueue(v);
            }
        }

        void TestUR()
        {
            testing = true;

            string id = ProcID;
            string location = "1";
            if(Location == "Pick-up1")
            {
                location = "1";
            }
            else if (Location == "DPL")
            {
                location = "2";
            }
            else if (Location == "ColorMeter")
            {
                location = "3";
            }
            else if (Location == "AccuFluo")
            {
                location = "4";
            }
            else if (Location == "FTIR")
            {
                location = "5";
            }
            else if (Location == "Pick-up2")
            {
                location = "6";
            }

            string op = "0";
            if(Action == "Drop Off")
            {
                op = "1";
            }

            string command = "(" + id + "," + location + "," + op + ")";

            DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
            if (!robot.Send(command))
            {
                MessageBox.Show("Failed sending command: " + command, "Error");
            }
        }

        void OpenHemisphere()
        {
            try
            {
                //gColorLib.Model.Hemisphere.Open(null, null);
                FTIRLib.Model.Drift.Open(null, null);
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        void CloseHemisphere()
        {
            try
            {
                //gColorLib.Model.Hemisphere.Close(null, null);
                FTIRLib.Model.Drift.Close(null, null);
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        void URResume()
        {
            if (!urHostStatus.halted)
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Start resuming UR from halt..."));

            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(URResumeEx);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(URResumeCompleted);
            bw.RunWorkerAsync();
        }

        void URResumeEx(object sender, DoWorkEventArgs e)
        {
            Result r = new Result(false, "", null);
            
            int timeout = 2 * 60 * 1000;
            DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
            UREvent.Reset();
            string msg = "play";
            Thread.Sleep(500);
            try
            {
                if (!robot.Send(msg, true))
                {
                    r.message = "Error: failed in sending play";
                }
                else
                {
                    if (!UREvent.WaitOne(timeout))
                    {
                        r.message = "Error: URHost play timeout";
                    }
                    else
                    {
                        if (!urHostStatus.startingprogram)
                        {
                            r.message = "Error: did not receive starting program response";
                        }
                        else
                        {
                            r.success = true;
                        }
                    }
                }
                e.Result = r;
            } catch(Exception ex)
            {
                r.message = ex.Message;
                e.Result = r;
            }
        }

        void URResumeCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Result r = (Result)e.Result;
            if (r.success)
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("UR resumed from halt"));
                urHostStatus.halted = false;
            } else
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to resume UR from halt - " + r.message));
            }
        }

        void StartProcess()
        {
            string controlNum = "";
            string location = "";
            var dialogControlNumberWindow = new View.StoneData();
            var dialogViewModel = new StoneDataViewModel();
            dialogControlNumberWindow.DataContext = dialogViewModel;
            bool? dialogResult = dialogControlNumberWindow.ShowDialog();
            if (dialogResult == true)
            {
                controlNum = dialogViewModel.ControlNumber;
                location = dialogViewModel.Location;
            }
            else
                return;

            if (usingThreadPool)
            {
                processing++;
                testing = false;

                string stageID = location;
                //ProcessStatus v = new ProcessStatus(count);
                // adding control number for stone save function
                NewProcessStatus v = new NewProcessStatus(stageID, controlNum);
                workQueue.Enqueue(v);
            }
            else
            {
                testing = false;
                processing++;
                count++;
                ProcessStatus processStatus = new ProcessStatus(count, ProcessEvent);
                processStatusList.Add(processStatus);

                BackgroundWorker bw = new BackgroundWorker();
                bw.DoWork += new DoWorkEventHandler(StartProcessEx);
                bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(ProcessCompleted);
                bw.RunWorkerAsync(processStatus);
            }
        }

        void StartProcessEx(object sender, DoWorkEventArgs e) 
        {
            ProcessStatus procStatus = (ProcessStatus)e.Argument;
            DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
            DeviceManager dm = null;
            e.Result = true;

            int timeout = 60 * 1000; // 3 minutes
            string msg;
            for (int i = 0; i < (int)DeviceType.URobot; i++)
            {
                dm = DeviceManagers[i];

                // 0. prepare stone
                {
                    procStatus.processEvent.Reset();
                    msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "pick up stone");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    if (!robot.Send("pickup1"))
                    {
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending pickup1"));
                        e.Result = false;
                        return;
                    }
                    if (!procStatus.processEvent.WaitOne(timeout))
                    {
                        msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "pickup1 timeout");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        e.Result = false;
                        return;
                    }
                    else
                    {
                        // todo: check pickedUp
                        if (!procStatus.pickedUp)
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "did not receive pickedUp status");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            e.Result = false;
                            return;
                        }
                    }
                }

                if (procStatus.dropped)
                {
                    msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    e.Result = false;
                    return;
                }

                // 1. move stone
                {
                    procStatus.processEvent.Reset();
                    msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "move stone");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    if (!robot.Send("ack")) // change to move later
                    {
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to move stone"));
                        e.Result = false;
                        return;
                    }
                    if (!procStatus.processEvent.WaitOne(timeout))
                    {
                        msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "readyCV timeout");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        e.Result = false;
                        return;
                    }
                    else
                    {
                        //Thread.Sleep(50);
                        //todo: check readyCV
                        if (procStatus.readyCV)
                        {
                            if (!robot.Send("ack"))
                            {
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to readyCV"));
                                e.Result = false;
                                return;
                            }
                            
                        } else
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "did not receive readyCV status");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            e.Result = false;
                            return;
                        }
                    }
                }

                if (procStatus.dropped)
                {
                    msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    e.Result = false;
                    return;
                }

                // 1.5 drop stone
                {
                    procStatus.processEvent.Reset();
                    if (dm.IsDeviceReady())
                    {
                        Thread.Sleep(2000);
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "load stone");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        if (!robot.Send("loadCV"))
                        {
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to send loadCV"));
                            e.Result = false;
                            return;
                        }
                    }
                    else
                    {
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "not ready");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        e.Result = false;
                        return;
                    }
                    if (!procStatus.processEvent.WaitOne(timeout))
                    {
                        msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "load stone timeout");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        e.Result = false;
                        return;
                    }
                    else
                    {
                        // todo: check stoneOnCV
                        if (procStatus.stoneOnCV)
                        {
                            if (!robot.Send("ack"))
                            {
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to stoneOnCV"));
                                e.Result = false;
                                return;
                            }
                        } else
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "did not receive stoneOnCV status");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            e.Result = false;
                            return;
                        }
                    }
                }

                if (procStatus.dropped)
                {
                    msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    e.Result = false;
                    return;
                }

                // 2. calibrate device
                {
                    procStatus.processEvent.Reset();
                    if (dm.NeedCalibration())
                    {
                        // pick up stone
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "pick up stone");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        if (!robot.Send("repeatCV"))
                        {
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to send repeatCV"));
                            e.Result = false;
                            return;
                        }
                        if (!procStatus.processEvent.WaitOne(timeout))
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "calibrating pick up stone timeout");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            e.Result = false;
                            return;
                        }
                        else
                        {
                            // todo: check removedCV
                            if (!robot.Send("ack"))
                            {
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to removedCV"));
                                e.Result = false;
                                return;
                            }
                        }

                        // calibrate device
                        do
                        {
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "calibrating");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            procStatus.processEvent.Reset();
                            dm.CalibrateEx();
                            procStatus.processEvent.WaitOne();
                            Thread.Sleep(5000);
                        } while (dm.NeedCalibration());

                        // put back stone
                        if (dm.IsDeviceReady())
                        {
                            procStatus.processEvent.Reset();
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "put stone");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            if (!robot.Send("loadCV"))
                            {
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to send loadCV again"));
                                e.Result = false;
                                return;
                            }
                            if (!procStatus.processEvent.WaitOne(timeout))
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "put stone timeout");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                e.Result = false;
                                return;
                            }
                            else
                            {
                                // todo: check stoneOnCV
                                if (!robot.Send("ack"))
                                {
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to stoneOnCV"));
                                    e.Result = false;
                                    return;
                                }
                            }
                        }
                        else
                        {
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "not ready");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            e.Result = false;
                            return;
                        }
                    }
                }

                if (procStatus.dropped)
                {
                    msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    e.Result = false;
                    return;
                }

                // 3. measure stone
                {
                    msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "measuring");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    procStatus.processEvent.Reset();
                    dm.MeasureEx();
                    if (!procStatus.processEvent.WaitOne(timeout))
                    {
                        msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "measuring timeout");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        e.Result = false;
                        return;
                    }
                }
            }

            Thread.Sleep(5000);
            // 4. finished process
            if (dm.IsDeviceReady())
            {
                msg = string.Format("{0}", "finish process");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));

                procStatus.processEvent.Reset();
                if (!robot.Send("doneCV"))
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("failed to send doneCV"));
                    e.Result = false;
                    return;
                }
                if (!procStatus.processEvent.WaitOne(timeout))
                {
                    msg = string.Format("Error: {0}, {1}", ((DeviceType)((int)DeviceType.URobot - 1)).ToString(), "put back stone timeout");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    e.Result = false;
                    return;
                }
                else
                {
                    // todo: check removedCV
                    if (procStatus.removedCV)
                    {
                        if (!robot.Send("ack"))
                        {
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to stoneOnCV"));
                            e.Result = false;
                            return;
                        }
                    }
                    else
                    {
                        msg = string.Format("Error: {0}", "did not receive removedCV status");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        e.Result = false;
                        return;
                    }
                }
            }
            else
            {
                msg = string.Format("{0}", "device not ready for pickup the stone to finish the process");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                e.Result = false;
                return;
            }

            if (procStatus.dropped)
            {
                msg = string.Format("Error: {0}", "stone dropped");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                e.Result = false;
                return;
            }
        }

        void ProcessCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            processing--;
            Application.Current.Dispatcher.BeginInvoke(new Action(CommandManager.InvalidateRequerySuggested));
            if ((bool)e.Result == true)
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process completed"));
            } else
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
            }
        }

        public void StartWorker(object o)
        {
            NewWorkArg workArg = (NewWorkArg)o;
            while (true)
            {
                // dequeue to get the job
                NewProcessStatus v = workQueue.Dequeue();
                workArg.procStatus = v;
                // Process a task.
                if (v != null)
                {
                    // todo: check first device is not busy to accept the new stone
                    while(DeviceManagers[0].status == UtilsLib.Status.Busy)
                    {
                        Thread.Sleep(2000);
                    }
                    // process the job
                    Console.WriteLine("Process worker: " + workArg.threadName);
                    workArg.procBusy = true;
                    StartNewProcessEx(workArg);
                    //StartNewProcessEx(workArg);
                }
                else
                {
                    workArg.procBusy = false;
                    workQueue.queueEvent.WaitOne();
                }

                // Ending condition:
                if (workArg.EvEnd)
                {
                    break;
                }
            }
        }

        void StartNewProcessEx(NewWorkArg workArg)
        {
            DeviceManager dm = null;

            NewProcessStatus procStatus = workArg.procStatus;
            ManualResetEvent processEvent = workArg.processEvent;
            CommandQueue commandQueue = workArg.commandQueue;
            commandQueue.Clear();
            string processID = workArg.threadName;
            string stageID = procStatus.stageID;

            string location = null;
            string op = null;
            string command = null;
            long currentTime;

            int timeout = 5 * 60 * 1000; // 5 minutes
            string msg;

            try
            {
                // 0. check if the first device status is ready (not busy)
                {
                    for (int i = 0; i < (int)DeviceType.URobot; i++)
                    {
                        dm = DeviceManagers[i];
                        if (Array.IndexOf(App.Settings.Devices, dm.DeviceName) < 0)
                        {
                            continue;
                        } else
                        {
                            break;
                        }
                    }
                    while (dm.status == Status.Busy)
                    {
                        msg = string.Format("{0} is busy for Process: {1}, please wait.", dm.DeviceName, processID);
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        Thread.Sleep(3000);
                    }
                }

                // 1. pick up from stage
                {
                    msg = string.Format("{0}, {1}", "Pick up stone from stage ", stageID);
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    processEvent.Reset();
                    location = stageID;
                    op = ((int)URAction.PickUp).ToString();
                    command = processID + "," + location + "," + op;
                    currentTime = (long)(DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds;
                    commandQueue.Enqueue(new Tuple<string, long>(command, currentTime));

                    if (!processEvent.WaitOne(timeout))
                    {
                        msg = string.Format("Error: {0}", command + " timeout");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                        throw new Exception(msg); // todo remove comments in production
                    }
                    else
                    {
                        if (procStatus.dropped)
                        {
                            msg = string.Format("Error: {0}", command + " stone dropped");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }

                        // todo: check pickedUp
                        if (!procStatus.pickedUp)
                        {
                            msg = string.Format("Error: {0}", command + " did not receive pickedUp status");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                    }
                }

                // 2. go through each device to do the measurement
                for (int i = 0; i < (int)DeviceType.URobot; i++)
                {
                    procStatus.pickedUp = false;
                    procStatus.droppedOff = false;
                    procStatus.dropped = false;

                    //location = (i + 2).ToString();

                    dm = DeviceManagers[i];
                    if(Array.IndexOf(App.Settings.Devices, dm.DeviceName) < 0)
                    {
                        continue;
                    }
                    int index = Array.IndexOf(Locations.ToArray(), dm.DeviceName);
                    location = (index + 1).ToString();
                    DeviceStatus ds = deviceStatusList[i];

                    // need to change when thread numbers are more than 2
                    while (ds.status == Status.Busy)
                    {
                        Thread.Sleep(2000);
                    }

                    ds.status = Status.Busy;
                    ds.procID = processID;

                    dm.status = Status.Busy;

                    dm.InitialStone(procStatus.controlNum);

                    // check if need calibration
                    processEvent.Reset();
                    if (dm.NeedCalibration())
                    {
                        // calibrate device
                        do
                        {
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "calibrating");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            processEvent.Reset();
                            dm.CalibrateEx();
                            processEvent.WaitOne();
                            Thread.Sleep(5000);
                        } while (dm.NeedCalibration());
                    }

                    // drop the stone in device
                    int count = 100;
                    do
                    {
                        if (dm.IsDeviceReady())
                        {
                            break;
                        }
                        else
                        {
                            count--;
                            Thread.Sleep(300);
                        }
                    } while (count > 0);

                    if (dm.IsDeviceReady())
                    {
                        processEvent.Reset();
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "put stone");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));

                        op = ((int)URAction.DropOff).ToString();
                        command = processID + "," + location + "," + op;
                        currentTime = (long)(DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds;
                        commandQueue.Enqueue(new Tuple<string, long>(command, currentTime));

                        if (!processEvent.WaitOne(timeout))
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "put stone timeout");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                        else
                        {
                            if (procStatus.dropped)
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }

                            if (!procStatus.droppedOff)
                            {
                                msg = string.Format("Error: {0}", command + " did not receive droppedOff status");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                        }
                    }
                    else
                    {
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "not ready");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                        throw new Exception(msg);
                    }

                    // measure stone
                    {
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "measuring");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        processEvent.Reset();
                        dm.MeasureEx();
                        if (!processEvent.WaitOne(timeout))
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "measuring timeout");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                        else
                        {
                            if (procStatus.dropped)
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                            // todo: add measure complete flag 
                            //Thread.Sleep(5000);
                        }
                    }

                    // check if the next device is busy
                    {
                        //if( 2 == ((int)DeviceType.URobot - i))
                        if(i+1 < (int)DeviceType.URobot)
                        {
                            while(DeviceManagers[i+1].status == UtilsLib.Status.Busy)
                            {
                                msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "wait for next device to be ready");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Thread.Sleep(2000);
                            }
                        }
                    }

                    // remove stone from device
                    {
                        Thread.Sleep(1000);
                        count = 100;
                        do
                        {
                            if (dm.IsDeviceReady())
                            {
                                break;
                            }
                            else
                            {
                                count--;
                                Thread.Sleep(300);
                            }
                        } while (count > 0);

                        if (dm.IsDeviceReady())
                        {
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "measurement finished");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));

                            processEvent.Reset();

                            op = ((int)URAction.PickUp).ToString();
                            command = processID + "," + location + "," + op;
                            currentTime = (long)(DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds;
                            commandQueue.Enqueue(new Tuple<string, long>(command, currentTime));

                            if (!processEvent.WaitOne(timeout))
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone pickup timeout");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                            else
                            {
                                if (procStatus.dropped)
                                {
                                    msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                    throw new Exception(msg);
                                }

                                if (!procStatus.pickedUp)
                                {
                                    msg = string.Format("Error: {0}", command + " did not receive pickedUp status");
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                    throw new Exception(msg);
                                }
                            }
                        }
                        else
                        {
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), " not ready to pickup the stone to finish the measurement");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                    }

                    ds.status = Status.Ready;
                    ds.procID = null;
                    dm.status = Status.Ready;
                }

                // 3. finished process, put stone back to stage
                {
                    msg = string.Format("{0}, {1}", "Drop off stone to stage ", stageID);
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    processEvent.Reset();
                    location = stageID;
                    op = ((int)URAction.DropOff).ToString();
                    command = processID + "," + location + "," + op;
                    currentTime = (long)(DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds;
                    commandQueue.Enqueue(new Tuple<string, long>(command, currentTime));

                    if (!processEvent.WaitOne(timeout))
                    {
                        msg = string.Format("Error: {0}", command + " timeout");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                        throw new Exception(msg);
                    }
                    else
                    {
                        if (procStatus.dropped)
                        {
                            msg = string.Format("Error: {0}", command + " stone dropped");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }

                        // todo: check pickedUp
                        if (!procStatus.droppedOff)
                        {
                            msg = string.Format("Error: {0}", command + " did not receive pickedUp status");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                    }
                }

                // finally
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process completed"));
                workArg.procBusy = false;
                processing--;
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed: " + ex.Message));
                workArg.procBusy = false;
                processing--;
            }
        }

        void StartProcessEx(WorkArg workArg)
        {
            DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
            DeviceManager dm = null;

            ProcessStatus procStatus = workArg.procStatus;
            ManualResetEvent processEvent = workArg.processEvent;
            string processID = workArg.threadName;

            int timeout = 60 * 1000; // 3 minutes
            string msg;
            for (int i = 0; i < (int)DeviceType.URobot; i++)
            {
                dm = DeviceManagers[i];
                if(Array.IndexOf(App.Settings.Devices, dm.DeviceName) < 0)
                {
                    continue;
                }
                DeviceStatus ds = deviceStatusList[i];
                
                // need to change when thread numbers are more than 2
                while (ds.status == Status.Busy)
                {
                    Thread.Sleep(2000);
                }

                ds.status = Status.Busy;
                ds.procID = processID;

                dm.InitialStone(procStatus.controlNum);

                try
                {
                    // 0. prepare stone
                    {
                        processEvent.Reset();
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "pick up stone");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        if (!robot.Send("pickup1"))
                        {
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending pickup1"));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception("Error: failed in sending pickup1");
                        }
                        if (!processEvent.WaitOne(timeout))
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "pickup1 timeout");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                        else
                        {
                            // todo: check pickedUp
                            if (!procStatus.pickedUp)
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "did not receive pickedUp status");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                        }
                    }

                    if (procStatus.dropped)
                    {
                        msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                        throw new Exception(msg);
                    }

                    // 1. move stone
                    {
                        processEvent.Reset();
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "move stone");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        if (!robot.Send("ack")) // change to move later
                        {
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to move stone"));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception("Error: failed to ack to move stone");
                        }
                        if (!processEvent.WaitOne(timeout))
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "readyCV timeout");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                        else
                        {
                            //Thread.Sleep(50);
                            //todo: check readyCV
                            if (procStatus.readyCV)
                            {
                                if (!robot.Send("ack"))
                                {
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to readyCV"));
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                    throw new Exception("Error: failed to ack to readyCV");
                                }

                            }
                            else
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "did not receive readyCV status");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                        }
                    }

                    if (procStatus.dropped)
                    {
                        msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                        throw new Exception(msg);
                    }

                    // 1.5 drop stone
                    {
                        processEvent.Reset();
                        if (dm.IsDeviceReady())
                        {
                            Thread.Sleep(2000);
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "load stone");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            if (!robot.Send("loadCV"))
                            {
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to send loadCV"));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception("Error: failed to send loadCV");
                            }
                        }
                        else
                        {
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "not ready");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                        if (!processEvent.WaitOne(timeout))
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "load stone timeout");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                        else
                        {
                            if (procStatus.stoneOnCV)
                            {
                                if (!robot.Send("ack"))
                                {
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to stoneOnCV"));
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                    throw new Exception("Error: failed to ack to stoneOnCV");
                                }
                            }
                            else
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "did not receive stoneOnCV status");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                        }
                    }

                    if (procStatus.dropped)
                    {
                        msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                        throw new Exception(msg);
                    }

                    // 2. calibrate device
                    {
                        processEvent.Reset();
                        if (dm.NeedCalibration())
                        {
                            // pick up stone
                            msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "pick up stone");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            if (!robot.Send("repeatCV"))
                            {
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to send repeatCV"));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception("Error: failed to send repeatCV");
                            }
                            if (!processEvent.WaitOne(timeout))
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "calibrating pick up stone timeout");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                            else
                            {
                                if (procStatus.removedCV)
                                {
                                    if (!robot.Send("ack"))
                                    {
                                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to removedCV"));
                                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                        throw new Exception("Error: failed to ack to removedCV");
                                    }
                                }
                                else
                                { 
                                    msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "did not receive removedCV status");
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                    throw new Exception(msg);
                                }
                            }

                            // calibrate device
                            do
                            {
                                msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "calibrating");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                processEvent.Reset();
                                dm.CalibrateEx();
                                processEvent.WaitOne();
                                Thread.Sleep(5000);
                            } while (dm.NeedCalibration());

                            // put back stone
                            if (dm.IsDeviceReady())
                            {
                                processEvent.Reset();
                                msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "put stone");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                if (!robot.Send("loadCV"))
                                {
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to send loadCV again"));
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                    throw new Exception("Error: failed to send loadCV again");
                                }
                                if (!processEvent.WaitOne(timeout))
                                {
                                    msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "put stone timeout");
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                    throw new Exception(msg);
                                }
                                else
                                {
                                    // todo: check stoneOnCV
                                    if (!robot.Send("ack"))
                                    {
                                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to stoneOnCV"));
                                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                        throw new Exception("Error: failed to ack to stoneOnCV");
                                    }
                                }
                            }
                            else
                            {
                                msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "not ready");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                        }
                    }

                    if (procStatus.dropped)
                    {
                        msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "stone dropped");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                        throw new Exception(msg);
                    }

                    // 3. measure stone
                    {
                        msg = string.Format("{0}, {1}", ((DeviceType)i).ToString(), "measuring");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        processEvent.Reset();
                        dm.MeasureEx();
                        if (!processEvent.WaitOne(timeout))
                        {
                            msg = string.Format("Error: {0}, {1}", ((DeviceType)i).ToString(), "measuring timeout");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                    }

                    // 3.5 remove stone from device
                    {
                        Thread.Sleep(5000);
                        if (dm.IsDeviceReady())
                        {
                            msg = string.Format("{0}", "finish measurement");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));

                            processEvent.Reset();
                            if (!robot.Send("doneCV"))
                            {
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to send doneCV"));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception("Error: failed to send doneCV");
                            }
                            if (!processEvent.WaitOne(timeout))
                            {
                                msg = string.Format("Error: {0}, {1}", ((DeviceType)((int)DeviceType.URobot - 1)).ToString(), "put back stone timeout");
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                                throw new Exception(msg);
                            }
                        }
                        else
                        {
                            msg = string.Format("{0}", "device not ready for pickup the stone to finish the process");
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                            throw new Exception(msg);
                        }
                    }

                    ds.status = Status.Ready;
                    ds.procID = null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    ds.status = Status.Ready;
                    ds.procID = null;
                    return;
                }
            }
  
            // 4. finished process
            {
                // put stone back to initial stage
                // todo: check removedCV
                if (procStatus.removedCV)
                {
                    if (!robot.Send("ack"))
                    {
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed to ack to stoneOnCV"));
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                        return;
                    }
                }
                else
                {
                    msg = string.Format("Error: {0}", "did not receive removedCV status");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                    return;
                }
            }
   

            if (procStatus.dropped)
            {
                msg = string.Format("Error: {0}", "stone dropped");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process failed"));
                return;
            }

            // finally
            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Process completed"));

        }

        void ImageReceivedHandler(ImageReceived image)
        {
            if(image.device == DeviceType.ColorMeter.ToString())
            {
                GColorImage = image.image;
            } else if (image.device == DeviceType.URobot.ToString())
            {
                URobotImage = image.image;
            } else if (image.device == DeviceType.AccuFluo.ToString())
            {
                GUVImage = image.image;
            }
        }

        void URHostConnectedHandler()
        {
            urThread = InitialURThread();
        }

        public void OnEvent(DeviceEvent e)
        {
            // event process
            if (e.device == DeviceType.ColorMeter.ToString())
            {
                ProcessGColorEvent((GColorEvent)e.result);
            }
            else if (e.device == DeviceType.URobot.ToString())
            {
                ProcessURobotEventNew((URobotEvent)e.result);
            }
            else if (e.device == DeviceType.AccuFluo.ToString())
            {
                ProcessGUVEvent((GUVEvent)e.result);
            }
            else if (e.device == DeviceType.DPL.ToString())
            {
                ProcessDPLEvent((DPLEvent)e.result);
            }
            else if(e.device == DeviceType.FTIR.ToString())
            {
                ProcessFTIREvent((FTIREvent)e.result);
            }
        }

        void urReconnect()
        {
            urThread.Abort();
            urThread.Join();
            urThread = null;
            DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
            robot.ReconnectEx();
        }

        void ProcessURobotEventNew(URobotEvent ure)
        {
            if (ure.id != null && ure.id == "Client")
            {
                if (ure.eventType == EventType.Status)
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Robot host: " + ure.message));
                    if (ure.message.ToLower().Contains("connected: universal robots dashboard server"))
                    {
                        // start the UR Host power on initialization
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("URobot: " + "Initial robot host"));

                        Thread.Sleep(5000);
                        URHostConnectedHandler();
                    }
                    else if (ure.message == "Disconnected")
                    {
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("command is not allowed due to safety reasons"))
                    {
                        urReconnect();
                        //UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("powering on"))
                    {
                        urHostStatus.poweringon = true;
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("idle"))
                    {
                        urHostStatus.idle = true;
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("loading program"))
                    {
                        urHostStatus.loadingprogram = true;
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("brake releasing"))
                    {
                        urHostStatus.brakereleasing = true;
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("running"))
                    {
                        urHostStatus.running = true;
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("starting program"))
                    {
                        urHostStatus.startingprogram = true;
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("powering off"))
                    {
                        urHostStatus.poweringoff = true;
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("power_off"))
                    {
                        urHostStatus.poweroff = true;
                        UREvent.Set();
                    }
                    else if (ure.message.ToLower().Contains("shutting down"))
                    {
                        urHostStatus.shuttingdown = true;
                        UREvent.Set();
                    }
                }
                else if (ure.eventType == EventType.Error)
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("URobot Host: " + ure.message));
                }
            }
            else
            {
                NewProcessStatus procStatus = null;
                string id = null;
                string location = null;
                string op = null;
                if (usingThreadPool)
                {
                    if (processing>0)
                    {
                        if (ure.message == "checkStone" || ure.message == "halted")
                        {

                        }
                        else
                        {
                            string ss = ure.message.TrimStart('[');
                            string cs = ss.TrimEnd(']');
                            string[] cc = cs.Split(',');
                            id = cc[1];
                            location = cc[2];
                            op = cc[3];
                            procStatus = threadPool.WorkArgs[int.Parse(id) - 1].procStatus;
                            procStatus.processEvent = threadPool.WorkArgs[int.Parse(id) - 1].processEvent;
                        }
                    }
                }
                else
                {
                    if (processing > 0)
                    {
                        //procStatus = processStatusList[count - 1];
                    }
                }

                if (ure.eventType == EventType.Result)
                {
                    if (ure.message == "Command")
                    {
                        URobotResult uRes = (URobotResult)ure.result;
                        if (uRes.Command == "Measure")
                        {
                            DeviceManagers[(int)DeviceType.ColorMeter].MeasureEx();
                        }
                        else if (uRes.Command == "Calibrate")
                        {
                            DeviceManagers[(int)DeviceType.ColorMeter].CalibrateEx();
                        }
                        else
                        {
                            Console.WriteLine("ProcessURobotEvent: Unknow command");
                            return;
                        }
                    }
                    else if (ure.message == "Status")
                    {
                        URobotResult uRes = (URobotResult)ure.result;
                        if (uRes.Command == "Connected")
                        {
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("URobot: " + "Robot connected"));
                        }
                        else if (uRes.Command == "Disconnected")
                        {
                            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("URobot: " + "Robot disconnected"));
                        }
                    }
                }
                else if (ure.eventType == EventType.Error)
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("URobot: " + ure.message));
                    if (processing > 0)
                    {
                        procStatus.error = true;
                        procStatus.processEvent.Set();
                    }
                }
                else if (ure.eventType == EventType.Status)
                {
                    if (processing > 0)
                    {
                        if (op == ((int)URAction.PickUp).ToString())
                        {
                            //if (!procStatus.pickedUp)
                            {
                                procStatus.pickedUp = true;
                                procStatus.processEvent.Set();
                            }
                        }
                        else if (op == ((int)URAction.DropOff).ToString())
                        {
                            procStatus.droppedOff = true;
                            procStatus.processEvent.Set();
                        }
                        else if (ure.message.Contains("checkStone"))
                        {
                            System.Windows.Forms.DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Continue?", "Check Stone", System.Windows.Forms.MessageBoxButtons.YesNo);
                            if (dialogResult == System.Windows.Forms.DialogResult.Yes)
                            {
                                //do something
                                DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
                                robot.Send("continue");
                            }
                            else if (dialogResult == System.Windows.Forms.DialogResult.No)
                            {
                                //do something else
                                DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
                                robot.Send("halt");
                            }
                        }
                        else if (ure.message.Contains("halted"))
                        {
                            // set each process status to dropped, and trigger the event for each process to go into dropped process
                            // clear unprocessed job queue
                            // clear unprocessed command queue
                            workQueue.Clear();

                            for(int i = 0; i < threadPool.WorkArgs.Length; i++)
                            {
                                NewProcessStatus newProcStatus = threadPool.WorkArgs[i].procStatus;
                                newProcStatus.processEvent = threadPool.WorkArgs[i].processEvent;
                                newProcStatus.dropped = true;

                                CommandQueue commandQueue = threadPool.WorkArgs[i].commandQueue;
                                commandQueue.Clear();

                                newProcStatus.processEvent.Set();

                            }

                            urHostStatus.startingprogram = false;
                            urHostStatus.halted = true;
                        }
                    }
                    else
                    {
                        if (ure.message == "robot_ready")
                        {
                            Console.WriteLine("Robot ready!!!");
                        }
                        else if (ure.message.Contains("checkStone"))
                        {
                            System.Windows.Forms.DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Continue?", "Check Stone", System.Windows.Forms.MessageBoxButtons.YesNo);
                            if (dialogResult == System.Windows.Forms.DialogResult.Yes)
                            {
                                //do something
                                DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
                                robot.Send("continue");
                            }
                            else if (dialogResult == System.Windows.Forms.DialogResult.No)
                            {
                                //do something else
                                DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
                                robot.Send("halt");
                            }
                        }
                        else if (ure.message.Contains("halted"))
                        {
                            workQueue.Clear();

                            for (int i = 0; i < threadPool.WorkArgs.Length; i++)
                            {
                                NewProcessStatus newProcStatus = threadPool.WorkArgs[i].procStatus;
                                newProcStatus.processEvent = threadPool.WorkArgs[i].processEvent;
                                newProcStatus.dropped = true;
                                newProcStatus.processEvent.Set();
                            }

                            urHostStatus.startingprogram = false;
                            urHostStatus.halted = true;
                        }
                    }
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("URobot: " + ure.message));
                }
            }
        }

        void ProcessGColorEvent(GColorEvent e)
        {
            NewProcessStatus procStatus = null;
            if (testing)
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("ColorMeter: " + e.message));
                return;
            }
            if (usingThreadPool)
            {
                if (processing > 0)
                {
                    int index = int.Parse(deviceStatusList[(int)DeviceType.ColorMeter].procID) - 1;
                    procStatus = threadPool.WorkArgs[index].procStatus;
                    procStatus.processEvent = threadPool.WorkArgs[index].processEvent;
                }
            }
            else
            {
                if (processing > 0)
                {
                    //procStatus = processStatusList[count - 1];
                }
            }

            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("ColorMeter: " + e.message));

            if (e.eventType == EventType.Info)
            {
                if (processing > 0)
                {
                    if (e.message == "Calibration Complete!")
                    {
                        procStatus.processEvent.Set();
                    } else if (e.message.Contains("Measurement Complete!"))
                    {
                        procStatus.processEvent.Set();
                    }
                }
            } else if (e.eventType == EventType.Error)
            {
                if (processing > 0)
                {
                    if(e.message.Contains("Check dust on stage"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    } else if(e.message.Contains("Brightness is low"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.Contains("Failed to close hemisphere"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    }
                    else if(e.message.ToLower().Contains("calibration failed"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    } else if(e.message.ToLower().Contains("check light stability"))
                    {
                        MessageBox.Show("check light stability", "Warning");
                        procStatus.processEvent.Set();
                    }else if(e.message.ToLower().Contains("motor connection error"))
                    {
                        MessageBox.Show("Motor connection error", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if(e.message.ToLower().Contains("error processing stone"))
                    {
                        MessageBox.Show("Error processing stone", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if(e.message.ToLower().Contains("measurement cancelled by user"))
                    {
                        MessageBox.Show("Measurement cancelled", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if(e.message.ToLower().Contains("measure exception"))
                    {
                        MessageBox.Show("Measure Exception", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if(e.message.ToLower().Contains("check diamond position"))
                    {
                        MessageBox.Show("Check diamond position", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if(e.message.ToLower().Contains("image processing error"))
                    {
                        MessageBox.Show("Image processing error", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if(e.message.ToLower().Contains("measure again: process failure"))
                    {
                        MessageBox.Show("Measure again: process failure", "Error");
                        procStatus.processEvent.Set();
                    }
                }
            } else if (e.eventType == EventType.Status)
            {

            } else if(e.eventType == EventType.Result)
            {

            }

        }

        void ProcessGUVEvent(GUVEvent e)
        {
            NewProcessStatus procStatus = null;
            if (testing)
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Accufluo: " + e.message));
                return;
            }
            if (usingThreadPool)
            {
                if (processing > 0)
                {
                    int index = int.Parse(deviceStatusList[(int)DeviceType.AccuFluo].procID) - 1;
                    //int index = 0;
                    procStatus = threadPool.WorkArgs[index].procStatus;
                    procStatus.processEvent = threadPool.WorkArgs[index].processEvent;
                }
            }
            else
            {
                if (processing > 0)
                {
                    //procStatus = processStatusList[count - 1];
                }
            }

            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("AccuFluo: " + e.message));

            if (e.eventType == EventType.Info)
            {
                if (processing > 0)
                {
                    if (e.message == "Calibration Complete!")
                    {
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.Contains("Measurement Complete!"))
                    {
                        procStatus.processEvent.Set();
                    }
                }
            }
            else if (e.eventType == EventType.Error)
            {
                if (processing > 0)
                {
                    if (e.message.Contains("Check dust on stage"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.Contains("Brightness is low"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.Contains("Failed to close hemisphere"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("calibration failed"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("check light stability"))
                    {
                        MessageBox.Show("check light stability", "Warning");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("motor connection error"))
                    {
                        MessageBox.Show("Motor connection error", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("error processing stone"))
                    {
                        MessageBox.Show("Error processing stone", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("measurement cancelled by user"))
                    {
                        MessageBox.Show("Measurement cancelled", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("measure exception"))
                    {
                        MessageBox.Show("Measure Exception", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("check diamond position"))
                    {
                        MessageBox.Show("Check diamond position", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("image processing error"))
                    {
                        MessageBox.Show("Image processing error", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("measure again: process failure"))
                    {
                        MessageBox.Show("Measure again: process failure", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if(e.message.ToLower().Contains("uv intensity low"))
                    {
                        MessageBox.Show("UV Intensity low", "Error");
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.ToLower().Contains("uv intensity high"))
                    {
                        MessageBox.Show("UV Intensity high", "Error");
                        procStatus.processEvent.Set();
                    }
                }
            }
            else if (e.eventType == EventType.Status)
            {

            }
            else if (e.eventType == EventType.Result)
            {

            }
        }

        void ProcessDPLEvent(DPLEvent e)
        {
            NewProcessStatus procStatus = null;
            if (testing)
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("DPL: " + e.message));
                return;
            }
            if (usingThreadPool)
            {
                if (processing > 0)
                {
                    int index = int.Parse(deviceStatusList[(int)DeviceType.DPL].procID) - 1;
                    procStatus = threadPool.WorkArgs[index].procStatus;
                    procStatus.processEvent = threadPool.WorkArgs[index].processEvent;
                }
            }
            else
            {
                if (processing > 0)
                {
                    //procStatus = processStatusList[count - 1];
                }
            }

            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("DPL: " + e.message));

            if (e.eventType == EventType.Info)
            {
                if (processing > 0)
                {
                    if (e.message == "Calibration Complete!")
                    {
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.Contains("Measurement Complete"))
                    {
                        procStatus.processEvent.Set();
                    }
                }
            }
            else if (e.eventType == EventType.Error)
            {
                if (processing > 0)
                {
                    if (e.message.Contains("Error during measurement"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    }
                    
                }
            }
            else if (e.eventType == EventType.Status)
            {

            }
            else if (e.eventType == EventType.Result)
            {

            }
        }

        void ProcessFTIREvent(FTIREvent e)
        {
            NewProcessStatus procStatus = null;
            if (testing)
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("FTIR: " + e.message));
                return;
            }
            if (usingThreadPool)
            {
                if (processing > 0)
                {
                    int index = int.Parse(deviceStatusList[(int)DeviceType.FTIR].procID) - 1;
                    procStatus = threadPool.WorkArgs[index].procStatus;
                    procStatus.processEvent = threadPool.WorkArgs[index].processEvent;
                }
            }
            else
            {
                if (processing > 0)
                {
                    //procStatus = processStatusList[count - 1];
                }
            }

            Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("FTIR: " + e.message));

            if (e.eventType == EventType.Info)
            {
                if (processing > 0)
                {
                    if (e.message == "Calibration Completed")
                    {
                        procStatus.processEvent.Set();
                    }
                    else if (e.message.Contains("Measurement completed"))
                    {
                        procStatus.processEvent.Set();
                    }
                }
            }
            else if (e.eventType == EventType.Error)
            {
                if (processing > 0)
                {
                    if (e.message.Contains("Measurement failed"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    } else if (e.message.Contains("Calibration failed"))
                    {
                        MessageBox.Show(e.message, "Error");
                        procStatus.processEvent.Set();
                    }

                }
            }
            else if (e.eventType == EventType.Status)
            {

            }
            else if (e.eventType == EventType.Result)
            {

            }
        }

        void StartUR()
        {
            int timeout = 2 * 60 * 1000;
            string msg;

            DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];

            if (false)
            {
                UREvent.Reset();
                msg = "robotmode";
                if (!robot.Send(msg, true))
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending robotmode"));
                    return;
                }
                if (!UREvent.WaitOne(timeout))
                {
                    msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "robotmode timeout");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
                else
                {
                    if (!urHostStatus.poweroff)
                    {
                        msg = string.Format("Error: {0}", "did not receive poweroff response");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        return;
                    }
                }
            }
            // 1. power on
            UREvent.Reset();
            msg = "power on";
            if(!robot.Send(msg, true))
            {
                //Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending power on"));
                return;
            }
            if (!UREvent.WaitOne(timeout))
            {
                msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "power on timeout");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                return;
            }
            else
            {
                if (!urHostStatus.poweringon)
                {
                    msg = string.Format("Error: {0}", "did not receive powering on response");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
            }

            // 2. robotmode
            if (false)
            {
                UREvent.Reset();
                msg = "robotmode";
                if (!robot.Send(msg, true))
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending robotmode"));
                    return;
                }
                if (!UREvent.WaitOne(timeout))
                {
                    msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "robotmode timeout");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
                else
                {
                    if (!urHostStatus.idle)
                    {
                        msg = string.Format("Error: {0}", "did not receive idle response");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        return;
                    }
                }
            }

            //3. load diamond.urp
            Thread.Sleep(5000);
            UREvent.Reset();
            msg = "load \"Diamond.urp\"";
            if (!robot.Send(msg, true))
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending load\"diamond.urp\""));
                return;
            }
            if (!UREvent.WaitOne(timeout))
            {
                msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "load diamond.urp timeout");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                return;
            }
            else
            {
                if (!urHostStatus.loadingprogram)
                {
                    msg = string.Format("Error: {0}", "did not receive loading program response");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
            }

            // 4. brake release
            Thread.Sleep(5000);
            UREvent.Reset();
            msg = "brake release";
            if (!robot.Send(msg, true))
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending brake release"));
                return;
            }
            if (!UREvent.WaitOne(timeout))
            {
                msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "brake release timeout");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                return;
            }
            else
            {
                if (!urHostStatus.brakereleasing)
                {
                    msg = string.Format("Error: {0}", "did not receive brake release response");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
            }

            //5. robotmode
            if(false)
            {
                UREvent.Reset();
                msg = "robotmode";
                if (!robot.Send(msg, true))
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending robotmode"));
                    return;
                }
                if (!UREvent.WaitOne(timeout))
                {
                    msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "robotmode timeout");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
                else
                {
                    if (!urHostStatus.running)
                    {
                        msg = string.Format("Error: {0}", "did not receive running response");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        return;
                    }
                }
            }

            // 6. play
            Thread.Sleep(5000);
            UREvent.Reset();
            msg = "play";
            if (!robot.Send(msg, true))
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending play"));
                return;
            }
            if (!UREvent.WaitOne(timeout))
            {
                msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "play timeout");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                return;
            }
            else
            {
                if (!urHostStatus.startingprogram)
                {
                    msg = string.Format("Error: {0}", "did not receive starting program response");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
            }
        }

        public Thread InitialURThread()
        {
            urHostStatus = new URHostStatus();
            var t = new Thread(StartUR);
            t.Start();
            return t;
        }

        void StartURCommand()
        {
            DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];
            while (true)
            {
                string v = null;

                Tuple<string, long> command = null;
                bool procBusy = false;
                int index = -1;
                for (int i = 0; i < threadPool.NumWorkers; i++)
                {
                    if (threadPool.WorkArgs[i].uRBusy)
                    {
                        procBusy = true;
                        command = threadPool.WorkArgs[i].commandQueue.Dequeue();
                        index = i;
                        break;
                    }
                }

                if (procBusy)
                {
                    if(command == null)
                    {
                        // todo: change to null later using event to trigger the thread running
                        v = null;
                    } else
                    {
                        v = command.Item1;
                    }
                }
                else
                {
                    // dequeue to get the command
                    for (int i = 0; i < threadPool.NumWorkers; i++)
                    {
                        Tuple<string, long> item = threadPool.WorkArgs[i].commandQueue.Peek();
                        if(item != null)
                        {
                            if(command == null)
                            {
                                command = item;
                                index = i;
                            }
                            else
                            {
                                if(item.Item2 < command.Item2)
                                {
                                    command = item;
                                    index = i;
                                }
                            }
                        }
                    }

                    if(command != null)
                    {
                        command = threadPool.WorkArgs[index].commandQueue.Dequeue();
                        v = command.Item1;
                    }
                    else
                    {
                        v = null;
                    }
                }

                // Process a task.
                if (v != null)
                {
                    // process the job
                    Console.WriteLine("Send command to UR: " + v);
                    
                    // processID + "," + location + "," + op;
                    string[] ss = v.Split(',');

                    // check if command is pickup from pickup1 and pickup2 check next device status 
                    // if status busy, re
                    DeviceManager dm = null;
                    if ((ss[1] == "1" || ss[1] == "6") && ss[2] == "0")
                    {
                        for (int i = 0; i < (int)DeviceType.URobot; i++)
                        {
                            dm = DeviceManagers[i];
                            if (Array.IndexOf(App.Settings.Devices, dm.DeviceName) < 0)
                            {
                                dm = null;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    if (dm != null && dm.status == Status.Busy)
                    {
                        // change the command timestamp
                        long currentTime = (long)(DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds;
                        // requeue the command with new timestamp
                        threadPool.WorkArgs[index].commandQueue.Enqueue(new Tuple<string, long>(v, currentTime));
                    }
                    else
                    {
                        // add command check the command action if it is pickup, set the uRBusy to true, otherwise, set to false
                        if (ss[2] == "0")
                        {
                            threadPool.WorkArgs[index].uRBusy = true;
                        }
                        else
                        {
                            threadPool.WorkArgs[index].uRBusy = false;
                        }

                        robot.Send("(" + v + ")");
                    }
                    Thread.Sleep(500);
                }
                else
                {
                    if (procBusy)
                    {
                        Thread.Sleep(500);
                    }
                    else
                    {
                        CommandQueue.commandEvent.WaitOne();
                    }
                }

                // Ending condition:
                if (urCommandThreadStop)
                {
                    break;
                }
            }
        }

        public Thread StartURCommandThread()
        {
            var t = new Thread(StartURCommand);
            t.Start();
            return t;
        }

        void StopUR()
        {
            int timeout = 2 * 1000;
            string msg;

            DeviceManager robot = DeviceManagers[(int)DeviceType.URobot];

            // 1. power off
            if (false)
            {
                UREvent.Reset();
                msg = "power off";
                if (!robot.Send(msg, true))
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending power off"));
                    return;
                }
                if (!UREvent.WaitOne(timeout))
                {
                    msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "power off timeout");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
                else
                {
                    if (!urHostStatus.poweringoff)
                    {
                        msg = string.Format("Error: {0}", "did not receive powering off response");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        return;
                    }
                }
            }
            //2. robotmode
            if (false)
            {
                UREvent.Reset();
                msg = "robotmode";
                if (!robot.Send(msg, true))
                {
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending robotmode"));
                    return;
                }
                if (!UREvent.WaitOne(timeout))
                {
                    msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "robotmode timeout");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
                else
                {
                    if (!urHostStatus.poweroff)
                    {
                        msg = string.Format("Error: {0}", "did not receive power off response");
                        Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                        return;
                    }
                }
            }

            // 3. shutdown
            //Thread.Sleep(5000);
            UREvent.Reset();
            msg = "shutdown";
            if (!robot.Send(msg, true))
            {
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry("Error: failed in sending shutdown"));
                return;
            }
            if (!UREvent.WaitOne(timeout))
            {
                msg = string.Format("Error: {0}, {1}", DeviceType.URobot.ToString(), "shutdown timeout");
                Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                return;
            }
            else
            {
                if (!urHostStatus.shuttingdown)
                {
                    msg = string.Format("Error: {0}", "did not receive shuttingdown response");
                    Application.Current.Dispatcher.Invoke(() => LogEntryVM.AddEntry(msg));
                    return;
                }
            }
        }

        #region dispose
        private bool _disposed = false;

        protected override void OnDispose()
        {
            Dispose(true);

            processing = 0;

            for (int i = 0; i < DeviceManagers.Count; i++)
            {
                DeviceManager dm = DeviceManagers[i];
                if(dm.DeviceName == "URobot")
                {
                    StopUR();
                }
                dm.DisconnectEx();
            }

            if (usingThreadPool)
            {
                threadPool.End();
                threadPool.Join();
            }

            urCommandThreadStop = true;

            UREvent.Set();
            if (urThread != null)
            {
                urThread.Join();
            }

            CommandQueue.commandEvent.Set();
            urCommandThread.Join();

            GC.SuppressFinalize(this);
        }

        void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    //
                }
                _disposed = true;
            }
        }
        #endregion
    }
}
