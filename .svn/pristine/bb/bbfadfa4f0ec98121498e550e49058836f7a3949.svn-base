using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace gUVLib.Model
{
    public enum CAMERA_PROPERTY
    {
        Shutter = 0,
        Temperature = 1,
        WhiteBalanceRed = 2,
        WhiteBalanceBlue = 3
    }

    public abstract class Camera
    {
        public double ImageWidth { get; set; }
        public double ImageHeight { get; set; }
        public double CropImageWidth { get; set; }
        public double CropImageHeight { get; set; }
        public uint SerialNumber { get; set; }
        public string FirmwareVersion { get; set; }

        //abstract must be overriden
        public abstract bool Connect();
        public abstract void StartCapture(BackgroundWorker bw);
        public abstract BitmapSource GetImage(DoWorkEventArgs e);
        public abstract void EditCameraSettings();
        public abstract void DisConnect();

        public abstract void InitCalibrationSettings();
        public abstract void ResetSettings();
        public abstract void DefaultSettings();

        //virtual may be overridden
        public virtual void Calibrate(double R, double G, double B) { }
        public virtual void AdjustWhiteBalance(int increment, bool automatic, ref uint oldValue) { }
        public virtual void GetInitializationPropertyValues(Dictionary<CAMERA_PROPERTY, double> properties) { }
        public virtual void InitFluorescenceSettings(int setting) { }
        public virtual void RestoreNormalSettings() { }
        public virtual void BufferFrames(bool onOff) { }
        public virtual double Framerate { get; set; }
        public virtual void RestartCapture() { }

        //virtual Hiroshi
        public virtual void Finish_calibration() { }


    }
}
