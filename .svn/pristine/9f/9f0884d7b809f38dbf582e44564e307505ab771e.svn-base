using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FTIRLib.Model
{
    public class AppSettings
    {
        public string HemisphereIPAddress { get; set; }
        public int HemispherePort { get; set; }

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
            settingsFileName = currentDirectory + @"\FTIRSettings.config";
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
