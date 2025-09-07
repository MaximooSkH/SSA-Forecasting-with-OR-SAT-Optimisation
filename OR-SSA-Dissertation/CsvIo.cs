using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OR_SSA_Dissertation
{
    public static class CsvIo
    {
        public static double[] LoadColumn(string path, int col)
        {
            var lines = File.ReadAllLines(path);
            var list = new List<double>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var t = line.Split(',', ';', '\t');
                if (col < 0 || col >= t.Length) continue;
                if (double.TryParse(t[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
            }
            if (list.Count == 0) throw new Exception("No numeric data parsed from CSV.");
            return list.ToArray();
        }

        public static void SaveArray(string path, double[] arr)
        {
            using (var sw = new StreamWriter(path))
                for (int i = 0; i < arr.Length; i++)
                    sw.WriteLine(arr[i].ToString(CultureInfo.InvariantCulture));
        }

        public static void SaveMatrix(string path, double[,] mat)
        {
            int n = mat.GetLength(0), m = mat.GetLength(1);
            using (var sw = new StreamWriter(path))
            {
                for (int i = 0; i < n; i++)
                {
                    var row = new string[m];
                    for (int j = 0; j < m; j++)
                        row[j] = mat[i, j].ToString(CultureInfo.InvariantCulture);
                    sw.WriteLine(string.Join(",", row));
                }
            }
        }
    }
}