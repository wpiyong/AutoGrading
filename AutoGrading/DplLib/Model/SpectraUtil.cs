using Microsoft.Research.DynamicDataDisplay.DataSources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using DPLAnalyzer;

namespace DplLib.Model
{
    public class SpectraUtil
    {
        //MainViewModel _mainVM;

        List<ObservableDataSource<Point>> _spectra;
        public List<ObservableDataSource<Point>> Spectra
        {
            get { return _spectra; }
            set
            {
                _spectra = value;
                //OnPropertyChanged("Spectra");
            }

        }


        public SpectraUtil()
        {

        }


        public void CreateSpectra(int count)
        {
            var spectra = new List<ObservableDataSource<Point>>();

            for (int i = 0; i < count; i++)
            {
                var source = new ObservableDataSource<Point>();
                //source.SetXYMapping(p => p);
                //source.Collection.Add(new Point(0, 100));
                //source.Collection.Add(new Point(400, 100));
                //source.Collection.Add(new Point(1000, 400));
                spectra.Add(source);
            }

            Spectra = spectra.ToList();
        }


        public void UpdateSpectra(double[] wl, double[][] countsList)
        {
            for (int i = 0; i < Spectra.Count; i++)
            {
                var source = Spectra[i];

                List<Point> pts = new List<Point>();
                for (int j = 0; j < countsList[i].Length; j++)
                {
                    Point p = new Point(wl[j], countsList[i][j]);
                    pts.Add(p);
                }

                source.Collection.Clear();
                source.AppendMany(pts);

            }
        }

        public DPL_ANALYZER_RESULT AnalyzeSpectra(List<double> intTimes, double[] wl, double[][] countsList)
        {
            var dpl = new Analyzer(wl, intTimes, countsList, 0);
            bool? diamondDetected; int diamondPeakCount;
            bool? _468Detected, _737Detected;
            double integrationTimeUsed;
            return dpl.TestCVD(out diamondDetected, out diamondPeakCount,
                                out _468Detected, out _737Detected, out integrationTimeUsed);
        }

        public DPL_ANALYZER_RESULT AnalyzeSpectra(List<double> intTimes, double[] wl, double[][] countsList,
            out bool? diamondDetected, out int diamondPeakCount,
            out bool? _468Detected, out bool? _737Detected, out double integrationTimeUsed)
        {
            var dpl = new Analyzer(wl, intTimes, countsList, (int)AppSettings.Instance.MetrologyIntegrationTimeIndex);

            return dpl.TestCVD(out diamondDetected, out diamondPeakCount,
                                out _468Detected, out _737Detected, out integrationTimeUsed);
        }



        public void ClearSpectra()
        {
            if (Spectra == null)
                return;

            for (int i = 0; i < Spectra.Count; i++)
            {
                Spectra[i].Collection.Clear();
            }
        }


        public bool SaveSpectra(string controlNumber, bool pass_result,
            double[] wls, double[][] countsList, List<string> integrationTimes)
        {
            bool res = false;

            try
            {
                var controlNumberPad = controlNumber.PadRight(9, '0');
                var rootDir = AppSettings.Instance.PassSaveFolder + @"\" +
                        controlNumberPad.Substring(0, 3) + @"\" +
                        controlNumberPad.Substring(3, 3) + @"\" +
                        controlNumberPad.Substring(6, 3);
                if (!pass_result)
                {
                    rootDir = AppSettings.Instance.ReferSaveFolder;
                }
                System.IO.Directory.CreateDirectory(rootDir);

                for (int i = 0; i < countsList.Length; i++)
                {
                    var intTimeString = integrationTimes[i].ToString();
                    var filePath = rootDir + @"\" + controlNumber + "_405_" + intTimeString + "ms.spc";
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);

                    if (!SPCHelper.SaveToSPC(wls,
                               countsList[i],
                               filePath, "Wavelength (nm)", "Counts"))
                        throw new Exception("Could not save data");
                }

                res = true;
            }
            catch (Exception ex)
            {

            }

            return res;
        }



        //public void OnFileDrop(string[] filepaths)
        //{
        //    var spcFileCount = filepaths.Count(p => Path.GetExtension(p).ToUpper() == ".SPC");
        //    if (spcFileCount != filepaths.Length || spcFileCount > 5)
        //        return;

        //    try
        //    {
        //        //order them
        //        var mss = filepaths.Select(f => System.IO.Path.GetFileNameWithoutExtension(f).Split('_').Last()).ToList();
        //        List<double> intTimes = mss.Select(f => Convert.ToDouble(f.TrimEnd(new char[] { 'm', 's' }))).ToList();

        //        var sorted = intTimes
        //            .Select((x, i) => new KeyValuePair<double, int>(x, i))
        //            .OrderBy(x => x.Key)
        //            .ToList();

        //        var sortedIntTimes = sorted.Select(x => x.Key).ToList();
        //        var sortedIntTimeIndexes = sorted.Select(x => x.Value).ToList();

        //        double[][] countsList = new double[filepaths.Length][];
        //        List<double> wl = new List<double>();
        //        List<double> counts = new List<double>();

        //        foreach (int i in sortedIntTimeIndexes)
        //        {
        //            if (!OpenSPC(filepaths[i], out wl, out counts))
        //            {
        //                MessageBox.Show("Could not open file");
        //                return;
        //            }

        //            countsList[i] = counts.ToArray();
        //        }

        //        ClearSpectra();
        //        CreateSpectra(filepaths.Length);

        //        UpdateSpectra(wl.ToArray(), countsList);
        //        var result = AnalyzeSpectra(sortedIntTimes, wl.ToArray(), countsList);
        //        if (result == DPLAnalyzer.DPL_ANALYZER_RESULT.NO_CVD_DETECTED)
        //        {
        //            _mainVM.LegendContent = "PASS";
        //            _mainVM.LegendDescription = "[No CVD features detected]";
        //        }
        //        else
        //        {
        //            _mainVM.LegendContent = "REFER";
        //            _mainVM.LegendDescription = "";
        //            if (result == DPLAnalyzer.DPL_ANALYZER_RESULT.ERROR)
        //            {
        //                _mainVM.LegendDescription = "[Check sample position]";
        //            }
        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show(ex.Message, "Error opening files");
        //        return;
        //    }


        //}

        bool OpenSPC(string fileName, out List<double> wl, out List<double> counts)
        {
            wl = new List<double>();
            counts = new List<double>();

            try
            {
                string xAxisLabel = "", yAxisLabel = "", notes = "";
                double[] wls = new double[0];
                double[] ints = new double[0];
                if (SPCHelper.OpenSPC(fileName, ref wls, ref ints, ref xAxisLabel, ref yAxisLabel, ref notes))
                {
                    wl = wls.ToList();
                    counts = ints.ToList();
                    return true;
                }
            }
            catch
            {

            }

            return false;
        }

    }
}
