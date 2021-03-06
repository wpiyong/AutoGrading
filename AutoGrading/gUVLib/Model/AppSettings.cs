﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace gUVLib.Model
{
    public class AppSettings
    {
        // guv.exe.config
        public string GUID { get; set; }
        public int NumberOfSteps { get; set; }
        public int RotationDirection { get; set; }
        public string MotorPort { get; set; }
        public double MotorVelocity { get; set; }
        public double MotorContinuousVelocity { get; set; }
        public bool SaveMeasuments { get; set; }
        public string MeasurementsFolder { get; set; }
        public string MeasurementsFileExtension { get; set; }
        public int MotorStepsPerRev { get; set; }
        public double Saturation { get; set; }
        public double Gamma { get; set; }
        public double WBConvergence { get; set; }
        public bool CalculateColorAtStep { get; set; }
        public double FrameRate { get; set; }
        public int CropTop { get; set; }
        public int CropLeft { get; set; }
        public int CropWidth { get; set; }
        public int CropHeight { get; set; }
        public double Hue { get; set; }
        public int WBIncrement { get; set; }
        public double CrossHairVerticalOffsetPercent { get; set; }
        public double CrossHairHorizontalOffsetPercent { get; set; }
        public int CrossHairBrush { get; set; }
        public bool ExtractToTextFile { get; set; }
        public string TextFilePath { get; set; }
        public bool WBInitialize { get; set; }
        public int WBInitializeRed { get; set; }
        public int WBInitializeBlue { get; set; }
        public int Time { get; set; }
        public double Temperature { get; set; }
        public double ShutterTime { get; set; }
        public double ShutterTimeDiff { get; set; }
        public double ADiff { get; set; }
        public double BDiff { get; set; }
        public bool ExtractCalDataToTextFile { get; set; }
        public string CalDataTextFilePath { get; set; }
        public string DailyMonitorTargetList { get; set; }
        public double LConv { get; set; }
        public double AConv { get; set; }
        public double BConv { get; set; }
        public double Lshift { get; set; }
        public double Ashift { get; set; }
        public double Bshift { get; set; }
        public bool AutoOpenClose { get; set; }
        public double CameraTempDiff { get; set; }
        public string BoundaryHash { get; set; }
        public string MetrologyFilePath { get; set; }

        // spectrumSettings.config
        public string RootUrl { get; set; }

        // fluorescencesettings.config
        public bool FluorescenceMeasure { get; set; }
        public double Gain { get; set; }
        public double FShutterTime { get; set; }
        public double FShutterTimeDiff { get; set; }
        public uint FluorescenceSetCurrent { get; set; }
        public double LowGain { get; set; }
        public double LowFShutterTime { get; set; }
        public double FLThreshold { get; set; }
        public bool ExtractFluorescenceDataToTextFile { get; set; }
        public string FluorescenceTextFilePath { get; set; }
        public uint MainLightSetCurrent { get; set; }
        public int UVWarningThreshold { get; set; }
        public int UVWarningThresholdHigh { get; set; }
        public bool EnableWBAdjustment { get; set; }
        public bool ShowMultiColorComment { get; set; }
        public double MultiColorThresholdPercent { get; set; }
        public string ST5IPAddress { get; set; }
        public int ST5Port { get; set; }
        public string HemisphereIPAddress { get; set; }
        public int HemispherePort { get; set; }

        public Endpoints endpoints;
        public bool IsAdmin = false;
        public bool InTestMode = false;

        private static AppSettings instance = null;
        private static readonly object _lock = new object();
        XDocument _settings;
        string settingsFileName = "";

        public static AppSettings Instance
        {
            get
            {
                lock (_lock)
                {
                    if (instance == null)
                    {
                        instance = new AppSettings();
                    }
                    return instance;
                }
            }
        }

        AppSettings()
        {
            string currentDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            settingsFileName = currentDirectory + @"\gUVSettings.config";
            Load();
        }

        void Load()
        {
            bool result = false;
            try
            {
                _settings = XDocument.Load(settingsFileName);
                foreach (var prop in this.GetType().GetProperties())
                {
                    prop.SetValue(this, Convert.ChangeType(Get(prop.Name), prop.PropertyType,
                        System.Globalization.CultureInfo.InvariantCulture));

                }

                result = true;
            }
            catch (Exception ex)
            {
                result = false;
            }
        }

        public object Get(string name)
        {
            object res = null;

            var field = _settings.Descendants("setting")
                                    .Where(x => (string)x.Attribute("name") == name)
                                    .FirstOrDefault();

            if (field != null)
            {
                res = field.Element("value").Value;
            }
            else
                throw new Exception("Property not found in AppSettings");

            return res;
        }

        public void Set(string name, object value)
        {
            var field = _settings.Descendants("setting")
                                    .Where(x => (string)x.Attribute("name") == name)
                                    .FirstOrDefault();

            if (field != null)
            {
                field.Element("value").Value = value.ToString();
            }
            else
                throw new Exception("Property not found in AppSettings");
        }
    }
}
