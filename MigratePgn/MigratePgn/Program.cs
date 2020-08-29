using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MigratePgn
{
    class Program
    {
        static void Main(string[] args)
        {
            var data = File.ReadAllLines("accumulate.pgn");
            var keep = new List<string>();
            var legacy = File.ReadAllLines("legacy.pgn").ToList();
            for (int i=0; i < data.Length; i+= 4)
            {
                if (data[i+1].Contains(args[0]) || data[i+2].Contains(args[0]))
                {
                    legacy.Add(data[i]);
                    legacy.Add(data[i+1]);
                    legacy.Add(data[i+2]);
                    legacy.Add(data[i+3]);
                } else
                {
                    keep.Add(data[i]);
                    keep.Add(data[i + 1]);
                    keep.Add(data[i + 2]);
                    keep.Add(data[i + 3]);
                }
            }
            File.Copy("accumulate.pgn", "accumulate.backup.pgn", true);
            File.Copy("legacy.pgn", "legacy.backup.pgn", true);
            File.WriteAllLines("accumulate.pgn", keep);
            File.WriteAllLines("legacy.pgn", legacy);
        }
    }
}
