using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gUVLib.Model
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

                using (var reader = new StreamReader("LShiftingTargets.csv"))
                {
                    int lineNumber = 0;
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        if (lineNumber++ > 0 && line.Trim().Length > 0) //skip first row
                        {
                            var values = line.Split(',');
                            if (values.Length != 4)
                                throw new Exception("Incorrect number of columns");

                            if (values[0].Contains(' '))
                                throw new Exception("Target name cannot have spaces");

                            targets.Add(values);

                        }
                        else
                        {
                            var values = line.Split(',');
                            if (values[0].ToUpper() != "TARGET" ||
                                values[1].ToUpper() != "L_MAX" ||
                                values[2].ToUpper() != "L_MIN" ||
                                values[3].ToUpper() != "CURRENT_STEP")
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
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error reading daily monitor file");
                calStoneData = new string[,] { };
            }

            dtCalStoneTable = new DataTable();

            dtCalStoneTable.Columns.Add("Target");
            dtCalStoneTable.Columns.Add("L_Max");
            dtCalStoneTable.Columns.Add("L_Min");
            dtCalStoneTable.Columns.Add("Current_Step");


            for (int r = 0; r < calStoneData.GetLength(0); r++)
            {
                DataRow row = dtCalStoneTable.NewRow();
                for (int c = 0; c < calStoneData.GetLength(1); c++)
                    row[c] = calStoneData[r, c];

                dtCalStoneTable.Rows.Add(row);
            }

        }

    }
}
