using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gColorLib.Model
{
    public static class CalibrationStoneTable
    {
        static string[,] calStoneData = new string[,]
        {
            
        };


        static DataTable dtCalStoneTable;
        public static DataTable CalStoneTable
        {
            get { return dtCalStoneTable; }
        }


        static CalibrationStoneTable()
        {
            try
            {
                List<string[]> targets = new List<string[]>();

                using (var reader = new StreamReader("LabShiftingTargets.csv"))
                {
                    int lineNumber = 0;
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        if (lineNumber++ > 0) //skip first row
                        {
                            var values = line.Split(',');
                            if (values.Length != 8)
                                throw new Exception("Incorrect number of columns");

                            if (values[0].Contains(' '))
                                throw new Exception("Target name cannot have spaces");

                            targets.Add(values);
                            
                        }
                        else
                        {
                            var values = line.Split(',');
                            if (values[0].ToUpper() != "TARGET" ||
                                values[1].ToUpper() != "L" ||
                                values[2].ToUpper() != "A" ||
                                values[3].ToUpper() != "B" ||
                                values[4].ToUpper() != "C" ||
                                values[5].ToUpper() != "H" ||
                                values[6].ToUpper() != "MASK_L" ||
                                values[7].ToUpper() != "MASK_A")
                                throw new Exception("Bad daily monitor format");
                        }
                    }
                }

                
                if (targets.Count > 0)
                {
                    calStoneData = new string[targets.Count, targets[0].Length];
                    for (int i = 0; i < targets.Count; i++)
                    {
                        for (int j = 0; j < targets[i].Length; j++)
                            calStoneData[i, j] = targets[i][j];
                    }
                }
            }
            catch(Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error reading daily monitor file");
                UtilsLib.EventBus.Instance.PostEvent(ex.Message);
                calStoneData = new string[,] { };
            }

            dtCalStoneTable = new DataTable();

            dtCalStoneTable.Columns.Add("Target");
            dtCalStoneTable.Columns.Add("L");
            dtCalStoneTable.Columns.Add("a");
            dtCalStoneTable.Columns.Add("b");
            dtCalStoneTable.Columns.Add("C");
            dtCalStoneTable.Columns.Add("H");
            dtCalStoneTable.Columns.Add("MaskL");
            dtCalStoneTable.Columns.Add("MaskA");

            for (int r = 0; r < calStoneData.GetLength(0); r++)
            {
                DataRow row = dtCalStoneTable.NewRow();
                for (int c = 0; c < calStoneData.GetLength(1); c++)
                    row[c] = calStoneData[r,c];

                dtCalStoneTable.Rows.Add(row);
            }

        }

    }
}
