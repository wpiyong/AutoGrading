using FlyCapture2Managed;
using FlyCapture2Managed.Gui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using UtilsLib;

namespace gUVLib.Model
{
    public struct PtGreyCameraImage
    {
        public ManagedImage raw;
        public ManagedImage converted;
        public ManagedCamera cam;
    }

    public class PtGreyCamera : Camera
    {
        CameraControlDialog m_ctldlg;
        ManagedCamera camera;
        ManagedPGRGuid prgGUID;

        SafeQueue<BitmapSource> imagesQueue;
        AutoResetEvent stopCameraEvent;
        volatile bool _imageCaptureThreadRunning;
        volatile bool _dropFrames = true;

        double _normalShutterTime;

        public PtGreyCamera()
        {
            string guid = AppSettings.Instance.GUID;
            string[] values = guid.Split(',');
            if (values.Length == 4)
            {
                prgGUID = new ManagedPGRGuid();
                prgGUID.value0 = uint.Parse(values[0]);
                prgGUID.value1 = uint.Parse(values[1]);
                prgGUID.value2 = uint.Parse(values[2]);
                prgGUID.value3 = uint.Parse(values[3]);
            }
        }

        public override double Framerate
        {
            get
            {
                return GetPropertyValue(PropertyType.FrameRate, true);
            }
            set
            {
                SetProprtyAutomaticSetting(PropertyType.FrameRate, false);
                SetAbsolutePropertyValue(PropertyType.FrameRate, (float)value);
            }
        }

        public override bool Connect()
        {
            bool result = false;
            try
            {
                if (prgGUID != null)
                {
                    camera = new ManagedCamera();
                    m_ctldlg = new CameraControlDialog();
                    camera.Connect(prgGUID);

                    CameraInfo ci = camera.GetCameraInfo();
                    SerialNumber = ci.serialNumber;
                    FirmwareVersion = ci.firmwareVersion;

                    stopCameraEvent = new AutoResetEvent(false);
                    _imageCaptureThreadRunning = false;

                    //initialise settings
                    InitializeSettings();
                    InitializeSettingsWB();

                    result = true;
                }
                else
                {
                    CameraSelectionDialog m_selDlg = new CameraSelectionDialog();
                    if (m_selDlg.ShowModal())
                    {
                        ManagedPGRGuid[] guids = m_selDlg.GetSelectedCameraGuids();
                        if (guids.Length == 0)
                        {
                            //MessageBox.Show("Please select a camera", "No camera selected");
                            return false;
                        }

                        camera = new ManagedCamera();
                        m_ctldlg = new CameraControlDialog();
                        camera.Connect(guids[0]);

                        CameraInfo ci = camera.GetCameraInfo();
                        SerialNumber = ci.serialNumber;
                        FirmwareVersion = ci.firmwareVersion;

                        stopCameraEvent = new AutoResetEvent(false);
                        _imageCaptureThreadRunning = false;

                        //initialise settings
                        InitializeSettings();
                        InitializeSettingsWB();

                        result = true;
                    }
                }
            }
            catch (Exception ex)
            {
                //App.LogEntry.AddEntry("Failed to Connect to Point Grey Camera : " + ex.Message);
                result = false;
            }

            return result;
        }

        public override void StartCapture(BackgroundWorker bw)
        {
            ManagedImage m_image = new ManagedImage();
            ManagedImage m_converted = new ManagedImage();

            StartImageCaptureThread();

            PtGreyCameraImage helper = new PtGreyCameraImage();
            helper.converted = m_converted;
            helper.raw = m_image;
            helper.cam = camera;

            bw.RunWorkerAsync(helper);
        }

        void StartImageCaptureThread()
        {
            StopImageCaptureThread();

            imagesQueue = new SafeQueue<BitmapSource>();
            Task.Run(() => QueueImages(camera));

            while (!_imageCaptureThreadRunning)
                Thread.Sleep(20);
        }

        void StopImageCaptureThread()
        {
            if (!_imageCaptureThreadRunning)
                return;

            stopCameraEvent.Set();
            while (_imageCaptureThreadRunning)
                Thread.Sleep(100);

            Thread.Sleep(200);//time for camera to stop streaming
        }

        public override void RestartCapture()
        {
            if (!_imageCaptureThreadRunning)
                StartImageCaptureThread();
        }

        void QueueImages(ManagedCamera cam)
        {
            ManagedImage managedImage = new ManagedImage();
            ManagedImage managedImageConverted = new ManagedImage();
            Debug.WriteLine("Camera capture starting");

            try
            {
                camera.StartCapture();
                _imageCaptureThreadRunning = true;

                while (true)
                {
                    try
                    {
                        cam.RetrieveBuffer(managedImage);
                        managedImage.ConvertToBitmapSource(managedImageConverted);
                        BitmapSource image = managedImageConverted.bitmapsource.Clone();
                        image.Freeze();

                        if (!imagesQueue.TryEnqueue(image, 200, _dropFrames))
                        {
                            Debug.WriteLine("Failed to queue");
                        }

                    }
                    catch (FC2Exception fex)
                    {
                        if (fex.Type != ErrorType.Timeout && fex.Type != ErrorType.ImageConsistencyError)
                            throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                        throw;
                    }

                    if (stopCameraEvent.WaitOne(50))
                        break;

                }

                camera.StopCapture();

                Debug.WriteLine("Camera capture stopped");
            }
            catch (Exception ex)
            {
                //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Camera capture thread exception:" + ex.Message)));
                DeviceEvent e = new DeviceEvent("AccuFluo", new GUVEvent(EventType.Error, "Camera capture thread exception:" + ex.Message, null));
                EventBus.Instance.PostEvent(e);
            }
            finally
            {
                _imageCaptureThreadRunning = false;
            }
        }

        public override BitmapSource GetImage(DoWorkEventArgs e)
        {
            BitmapSource source = null;
            BitmapSource deQueueSource = null;

            int timer = 0;
            while (!imagesQueue.TryDequeue(out deQueueSource, 100))
            {
                Thread.Sleep(100);
                if (timer++ > 150)
                    throw new ApplicationException("Timed out waiting for image");
            }

            if (deQueueSource == null)
                throw new ApplicationException("Bad queue data");


            if ((AppSettings.Instance.CropHeight == 0 && AppSettings.Instance.CropWidth == 0) ||
                 (AppSettings.Instance.CropHeight + AppSettings.Instance.CropTop > deQueueSource.Height) ||
                 (AppSettings.Instance.CropWidth  + AppSettings.Instance.CropLeft > deQueueSource.Width)
                )
            {
                source = deQueueSource;
                CropImageWidth = 0;
                CropImageHeight = 0;
            }
            else
            {
                source = new CroppedBitmap(deQueueSource,
                    new System.Windows.Int32Rect((int)AppSettings.Instance.CropLeft,
                                                    (int)AppSettings.Instance.CropTop,
                                                    (int)(AppSettings.Instance.CropWidth == 0 ?
                                                            deQueueSource.Width : AppSettings.Instance.CropWidth),
                                                    (int)(AppSettings.Instance.CropHeight == 0 ?
                                                            deQueueSource.Height : AppSettings.Instance.CropHeight)));
                CropImageWidth = source.Width;
                CropImageHeight = source.Height;
            }
            source.Freeze();
            ImageWidth = deQueueSource.Width;
            ImageHeight = deQueueSource.Height;

            return source;
        }


        public override void DisConnect()
        {
            try
            {
                if (camera != null)
                {
                    StopImageCaptureThread();
                    camera.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PtGrey Disconnect : " + ex.Message);
            }
            finally
            {
                camera = null;
            }
        }

        public override void EditCameraSettings()
        {
            bool restartCapture = _imageCaptureThreadRunning;

            try
            {
                StopImageCaptureThread();

                if (m_ctldlg.IsVisible())
                {
                    m_ctldlg.Disconnect();
                    m_ctldlg.Hide();
                }
                else
                {
                    m_ctldlg.Connect(camera);
                    m_ctldlg.Show();
                }
            }
            finally
            {
                if (restartCapture)
                    StartImageCaptureThread();
            }
        }


        void SetCameraVideoModeAndFrameRate()
        {
            bool restartCapture = _imageCaptureThreadRunning;
            StopImageCaptureThread();

            try
            {
                if (FirmwareVersion[0] == '2')
                {
                    const Mode k_fmt7Mode = Mode.Mode1;
                    const PixelFormat k_fmt7PixelFormat = PixelFormat.PixelFormatRgb8;

                    Format7ImageSettings fmt7ImageSettings = new Format7ImageSettings();
                    fmt7ImageSettings.mode = k_fmt7Mode;
                    fmt7ImageSettings.offsetX = 80;// 560;
                    fmt7ImageSettings.offsetY = 60;// 420;
                    fmt7ImageSettings.width = 800;
                    fmt7ImageSettings.height = 600;
                    fmt7ImageSettings.pixelFormat = k_fmt7PixelFormat;

                    // Validate the settings to make sure that they are valid
                    bool settingsValid = false;
                    Format7PacketInfo fmt7PacketInfo = camera.ValidateFormat7Settings(
                        fmt7ImageSettings,
                        ref settingsValid);

                    if (settingsValid != true)
                    {
                        // Settings are not valid
                        throw new Exception("Invalid resolution settings");
                    }

                    //Format7ImageSettings fmt7CamImageSettings = new Format7ImageSettings();
                    //uint currPacketSize = 0;
                    //float percentage = 0.0f;

                    //camera.GetFormat7Configuration(fmt7CamImageSettings, ref currPacketSize, ref percentage);

                    ////bool supported = false;
                    ////Format7Info fmt7Info = camera.GetFormat7Info(k_fmt7Mode, ref supported);
                    //if (fmt7ImageSettings.pixelFormat == fmt7CamImageSettings.pixelFormat
                    //    && fmt7ImageSettings.mode == fmt7CamImageSettings.mode)
                    //{
                    //    if (currPacketSize >= fmt7PacketInfo.maxBytesPerPacket)
                    //        currPacketSize = fmt7PacketInfo.maxBytesPerPacket;
                    //}
                    //else
                    //    currPacketSize = fmt7PacketInfo.maxBytesPerPacket;

                    // Set the settings to the camera
                    camera.SetFormat7Configuration(
                       fmt7ImageSettings,
                       6256);//8648);
                }
                else
                {
                    camera.SetVideoModeAndFrameRate(VideoMode.VideoMode800x600Rgb, FrameRate.FrameRate30);
                }
            }
            catch (FC2Exception /*ex*/)
            {
                throw;
            }

            if (restartCapture)
                StartImageCaptureThread();
        }


        void SetAbsolutePropertyValue(PropertyType property, float newValue)
        {
            CameraProperty camProp = camera.GetProperty(property);
            CameraPropertyInfo propInfo = camera.GetPropertyInfo(property);

            if (!camProp.autoManualMode && propInfo.manualSupported && propInfo.absValSupported)
            {
                float difference = camProp.absValue - newValue;
                if (difference != 0)
                {
                    // The brightness abs register sometimes starts drifting
                    // due to a rounding error between the camera and the
                    // actual value being held by the adjustment. To prevent
                    // this, only apply the change to the camera if the
                    // difference is greater than a specified amount.

                    // Check if the difference is greater than 0.005f. 
                    if (property != PropertyType.Brightness ||
                        Math.Abs(difference) > 0.005f)
                    {
                        camProp.absControl = true;
                        camProp.absValue = newValue;
                        camera.SetProperty(camProp);
                    }
                }
            }
            else
            {
                throw new ApplicationException("Trying to set a property that cannot be adjusted");
            }
        }


        public float GetPropertyValue(PropertyType property, bool absolute, bool valueB = false)
        {
            CameraProperty camProp = camera.GetProperty(property);

            return (absolute ? camProp.absValue : (!valueB ? camProp.valueA : camProp.valueB));
        }


        public override void AdjustWhiteBalance(int increment, bool valueB, ref uint oldValue)
        {
            PropertyType property = PropertyType.WhiteBalance;

            CameraProperty camProp = camera.GetProperty(property);
            CameraPropertyInfo propInfo = camera.GetPropertyInfo(property);

            camProp.absControl = false;
            if (!valueB)
            {
                oldValue = camProp.valueA;

                if (increment > 0)
                    camProp.valueA = camProp.valueA + (uint)increment;
                else
                    camProp.valueA = camProp.valueA - (uint)(-1 * increment);
            }
            else
            {
                oldValue = camProp.valueB;

                if (increment > 0)
                    camProp.valueB = camProp.valueB + (uint)increment;
                else
                    camProp.valueB = camProp.valueB - (uint)(-1 * increment);
            }
            camera.SetProperty(camProp);
        }

        uint SetWhiteBalance(int newValue, bool valueB)
        {
            uint oldValue;
            PropertyType property = PropertyType.WhiteBalance;

            CameraProperty camProp = camera.GetProperty(property);
            CameraPropertyInfo propInfo = camera.GetPropertyInfo(property);

            camProp.absControl = false;
            if (!valueB)//red
            {
                oldValue = camProp.valueA;
                camProp.valueA = (uint)newValue;
            }
            else
            {
                oldValue = camProp.valueB;
                camProp.valueB = (uint)newValue;
            }
            camera.SetProperty(camProp);
            return oldValue;

        }


        void SetProprtyAutomaticSetting(PropertyType property, bool automatic)
        {
            CameraProperty camProp = camera.GetProperty(property);
            if (camProp.autoManualMode != automatic)
            {
                camProp.autoManualMode = automatic;
                camera.SetProperty(camProp);
            }
        }

        void SetProprtyEnabledSetting(PropertyType property, bool enabled)
        {
            CameraProperty camProp = camera.GetProperty(property);
            camProp.onOff = enabled;
            camera.SetProperty(camProp);
        }

        void SetProprtyOnePush(PropertyType property, bool onePush)
        {
            CameraProperty camProp = camera.GetProperty(property);

            camProp.onePush = onePush;
            camera.SetProperty(camProp);
        }


        public override void GetInitializationPropertyValues(Dictionary<CAMERA_PROPERTY, double> properties)
        {
            foreach (var key in properties.Keys.ToList())
            {
                switch (key)
                {
                    case CAMERA_PROPERTY.Shutter:
                        properties[key] = Math.Round(GetPropertyValue(PropertyType.Shutter, true), 3);
                        break;
                    case CAMERA_PROPERTY.Temperature:
                        properties[key] = (GetPropertyValue(PropertyType.Temperature, false) / 10.0) - 273.15;
                        break;
                    case CAMERA_PROPERTY.WhiteBalanceRed:
                        properties[key] = GetPropertyValue(PropertyType.WhiteBalance, false, false);
                        break;
                    case CAMERA_PROPERTY.WhiteBalanceBlue:
                        properties[key] = GetPropertyValue(PropertyType.WhiteBalance, false, true);
                        break;
                }
            }
        }

        void InitializeSettings()
        {
            bool restartCapture = _imageCaptureThreadRunning;
            StopImageCaptureThread();

            //first reset to default settings
            camera.RestoreFromMemoryChannel(0);

            SetProprtyAutomaticSetting(PropertyType.FrameRate, false);
            SetCameraVideoModeAndFrameRate();
            SetProprtyAutomaticSetting(PropertyType.Shutter, false);
            SetAbsolutePropertyValue(PropertyType.Shutter, (float)17.0);
            SetProprtyAutomaticSetting(FlyCapture2Managed.PropertyType.WhiteBalance, false);
            SetProprtyAutomaticSetting(PropertyType.Gain, true);
            SetProprtyOnePush(PropertyType.WhiteBalance, true);
            Thread.Sleep(2000);


            SetProprtyAutomaticSetting(PropertyType.Saturation, false);
            SetProprtyAutomaticSetting(PropertyType.Hue, false);
            SetProprtyAutomaticSetting(PropertyType.Gamma, false);

            SetAbsolutePropertyValue(PropertyType.Saturation, (float)AppSettings.Instance.Saturation);
            SetAbsolutePropertyValue(PropertyType.Gamma, (float)AppSettings.Instance.Gamma);

            SetProprtyEnabledSetting(PropertyType.Hue, true);
            SetAbsolutePropertyValue(PropertyType.Hue, (float)AppSettings.Instance.Hue);

            FC2Config config = new FC2Config();
            config = camera.GetConfiguration();
            config.grabTimeout = 200;
            camera.SetConfiguration(config);

            if (restartCapture)
            {
                StartImageCaptureThread();
            }

        }


        public override void BufferFrames(bool onOff)
        {
            bool restartCapture = _imageCaptureThreadRunning;
            StopImageCaptureThread();

            //EmbeddedImageInfo embeddedInfo = new EmbeddedImageInfo();
            //embeddedInfo = camera.GetEmbeddedImageInfo();

            FC2Config config = new FC2Config();
            config = camera.GetConfiguration();

            //TriggerMode triggerMode = camera.GetTriggerMode();

            if (onOff)
            {
                if (!Hemisphere.EnableDisableTrigger(false)) //disable timer trigger
                    throw new ApplicationException("Could not disable external timer trigger");

                //triggerMode.onOff = true;
                //triggerMode.mode = 0;
                //triggerMode.parameter = 0;
                //triggerMode.polarity = 1; //rising edge

                config.grabMode = GrabMode.BufferFrames;
                config.numBuffers = 88;

                //embeddedInfo.frameCounter.onOff = true;

                //camera.SetTriggerMode(triggerMode);
                camera.SetConfiguration(config);
                //camera.SetEmbeddedImageInfo(embeddedInfo);

                //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Camera buffer frames on")));
                DeviceEvent e = new DeviceEvent("AccuFluo", new GUVEvent(EventType.Info, "Camera buffer frames on", null));
                EventBus.Instance.PostEvent(e);
            }
            else
            {
                config.grabMode = GrabMode.DropFrames;
                //embeddedInfo.frameCounter.onOff = false;

                ///triggerMode.onOff = false;

                //camera.SetEmbeddedImageInfo(embeddedInfo);
                camera.SetConfiguration(config);
                //camera.SetTriggerMode(triggerMode);

                if (!Hemisphere.EnableDisableTrigger(true))
                    throw new ApplicationException("Could not enable external timer trigger");

                //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Camera buffer frames off")));
                DeviceEvent e = new DeviceEvent("AccuFluo", new GUVEvent(EventType.Info, "Camera buffer frames off", null));
                EventBus.Instance.PostEvent(e);
            }

            if (!imagesQueue.TryClear(1000))
                throw new ApplicationException("Could not empty queue");

            _dropFrames = !onOff;

            if (restartCapture)
            {
                StartImageCaptureThread();
            }

        }


        void InitializeSettingsWB()
        {
            if (AppSettings.Instance.WBInitialize)
            {
                SetWhiteBalance(AppSettings.Instance.WBInitializeRed, false);
                SetWhiteBalance(AppSettings.Instance.WBInitializeBlue, true);
            }
        }

        public override void InitCalibrationSettings()
        {
            InitializeSettings();
        }

        public override void ResetSettings()
        {
            //SetCameraVideoModeAndFrameRate();
            InitializeSettingsWB();

        }

        //Hiroshi debug
        static bool _firstCalib = true;

        public override void DefaultSettings()
        {
            SetProprtyAutomaticSetting(FlyCapture2Managed.PropertyType.Sharpness, false);
            SetProprtyAutomaticSetting(FlyCapture2Managed.PropertyType.Saturation, false);
            SetProprtyAutomaticSetting(FlyCapture2Managed.PropertyType.Hue, false);

            SetAbsolutePropertyValue(FlyCapture2Managed.PropertyType.Saturation,
                (float)AppSettings.Instance.Saturation);
            SetAbsolutePropertyValue(FlyCapture2Managed.PropertyType.Hue,
                (float)AppSettings.Instance.Hue);

            // Hiroshi add 2014/4/24 Adjust the Blue and Red gain for initial stage of R>G and B<G 
            if (_firstCalib == true)
            {
                //uint oldValue = 0;
                //AdjustWhiteBalance(-2, false, ref oldValue); // -2 for red
                //AdjustWhiteBalance(-2, true, ref oldValue); // -2 for blue
                _firstCalib = false;
            }

        }

        public override void Calibrate(double R, double G, double B)
        {
            uint oldValue = 0;

            if (R - G > (double)AppSettings.Instance.WBConvergence)
            {
                AdjustWhiteBalance(-1, false, ref oldValue);
                //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Calibration Mode : Decremented WB Red from " + oldValue)));
                DeviceEvent e = new DeviceEvent("AccuFluo", new GUVEvent(EventType.Info, "Calibration Mode : Decremented WB Red from " + oldValue, null));
                EventBus.Instance.PostEvent(e);
            }
            else if (G - R > (double)AppSettings.Instance.WBConvergence)
            {
                AdjustWhiteBalance(1, false, ref oldValue);
                //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Calibration Mode : Incremented WB Red from " + oldValue)));
                DeviceEvent e = new DeviceEvent("AccuFluo", new GUVEvent(EventType.Info, "Calibration Mode : Incremented WB Red from " + oldValue, null));
                EventBus.Instance.PostEvent(e);
            }

            if (B - G > (double)AppSettings.Instance.WBConvergence)
            {
                AdjustWhiteBalance(-1, true, ref oldValue);
                //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Calibration Mode : Decremented WB Blue from " + oldValue)));
                DeviceEvent e = new DeviceEvent("AccuFluo", new GUVEvent(EventType.Info, "Calibration Mode : Decremented WB Blue from " + oldValue, null));
                EventBus.Instance.PostEvent(e);
            }
            else if (G - B > (double)AppSettings.Instance.WBConvergence)
            {
                AdjustWhiteBalance(1, true, ref oldValue);
                //Application.Current.Dispatcher.Invoke((Action)(() => App.LogEntry.AddEntry("Calibration Mode : Incremented WB Blue from " + oldValue)));
                DeviceEvent e = new DeviceEvent("AccuFluo", new GUVEvent(EventType.Info, "Calibration Mode : Incremented WB Blue from " + oldValue, null));
                EventBus.Instance.PostEvent(e);
            }

        }

        //Hiroshi add for debug
        public override void Finish_calibration()
        {
            _normalShutterTime = GetPropertyValue(PropertyType.Shutter, true);

            TriggerMode triggerMode = camera.GetTriggerMode();
            triggerMode.onOff = true;
            triggerMode.mode = 0;
            triggerMode.parameter = 0;
            triggerMode.polarity = 1; //rising edge
            camera.SetTriggerMode(triggerMode);
        }

        public override void InitFluorescenceSettings(int set)
        {
            float shutter = 0, gain = 0;

            switch (set)
            {
                case 0:
                    shutter = (float)AppSettings.Instance.FShutterTime;
                    gain = (float)AppSettings.Instance.Gain;
                    break;
                case 1:
                    shutter = (float)AppSettings.Instance.LowFShutterTime;
                    gain = (float)AppSettings.Instance.LowGain;
                    break;
                default:
                    return;
            }

            SetProprtyAutomaticSetting(FlyCapture2Managed.PropertyType.Shutter, false);
            SetAbsolutePropertyValue(FlyCapture2Managed.PropertyType.Shutter,
                shutter);

            SetProprtyAutomaticSetting(FlyCapture2Managed.PropertyType.Gain, false);
            SetAbsolutePropertyValue(FlyCapture2Managed.PropertyType.Gain,
                gain);


        }

        public override void RestoreNormalSettings()
        {

            SetProprtyAutomaticSetting(FlyCapture2Managed.PropertyType.Shutter, false);
            SetAbsolutePropertyValue(FlyCapture2Managed.PropertyType.Shutter,
                (float)_normalShutterTime);

            SetProprtyAutomaticSetting(FlyCapture2Managed.PropertyType.Gain, true);
        }

        //public bool ReadRegister(uint addr, out uint value)
        //{
        //    value = 0;

        //    try
        //    {
        //        value = camera.ReadRegister(addr);
        //        return true;
        //    }
        //    catch
        //    {
        //        return false;
        //    }
        //}

        //public bool WriteRegister(uint addr, uint value)
        //{
        //    try
        //    {
        //        uint temp = 0;
        //        if (ReadRegister(addr, out  temp))
        //        {
        //            if (temp != value)
        //                camera.WriteRegister(addr, value);

        //            return true;
        //        }
        //        return false;
        //    }
        //    catch
        //    {
        //        return false;
        //    }
        //}
    }

    class SafeQueue<T>
    {
        // A queue that is protected by Monitor.
        private Queue<T> m_inputQueue = new Queue<T>();

        // Try to add an element to the queue: Add the element to the queue 
        // only if the lock becomes available during the specified time
        // interval.
        public bool TryEnqueue(T qValue, int waitTime, bool dropFrames)
        {
            bool res = false;

            // Request the lock.
            if (Monitor.TryEnter(m_inputQueue, waitTime))
            {
                try
                {
                    if (dropFrames)
                    {
                        m_inputQueue.Clear();
                        m_inputQueue.Enqueue(qValue);
                        res = true;
                    }
                    else if (m_inputQueue.Count < 100)
                    {
                        m_inputQueue.Enqueue(qValue);
                        res = true;
                    }
                }
                finally
                {
                    // Ensure that the lock is released.
                    Monitor.Exit(m_inputQueue);
                }

            }

            return res;
        }

        // Lock the queue and dequeue an element.
        public bool TryDequeue(out T retval, int waitTime)
        {
            bool res = false;
            retval = (T)(object)null;

            // Request the lock, and block until it is obtained.
            if (Monitor.TryEnter(m_inputQueue, waitTime))
            {
                try
                {
                    if (m_inputQueue.Count > 0)
                    {
                        // When the lock is obtained, dequeue an element.
                        retval = m_inputQueue.Dequeue();
                        res = true;
                    }

                }
                finally
                {
                    // Ensure that the lock is released.
                    Monitor.Exit(m_inputQueue);
                }

            }

            return res;

        }


        public bool TryClear(int waitTime)
        {
            bool res = false;

            // Request the lock, and block until it is obtained.
            if (Monitor.TryEnter(m_inputQueue, waitTime))
            {
                try
                {
                    m_inputQueue.Clear();
                    res = true;
                }
                finally
                {
                    // Ensure that the lock is released.
                    Monitor.Exit(m_inputQueue);
                }

            }

            return res;

        }
    }
}
