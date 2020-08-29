using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lotsofeval
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> output = new List<string>();
            bool expectingResult = false;
            string prevLine = "";
            string prevPrevLine = "";
            foreach (var line in File.ReadLines("details.log"))
            {
                if (line.Contains("position startpos"))
                {
                    if (expectingResult) throw new Exception("Missing result.");
                    expectingResult = true;
                }
                if (line.Contains("Opponent moves"))
                {
                    if (!expectingResult) throw new Exception("Multiple results with no position.");
                    double d, q;
                    if (!ExtractDQ(prevPrevLine, out d, out q))
                    {
                        ExtractDQ(prevLine, out d, out q);
                    }
                    output.Add($"{q} {d}");
                    expectingResult = false;
                }
                if (line.Contains("bestmove") && expectingResult)
                {
                    double d, q;
                    if (!ExtractDQ(prevPrevLine, out d, out q))
                    {
                        ExtractDQ(prevLine, out d, out q);
                    }
                    output.Add($"{q} {d}");
                    expectingResult = false;
                }
                prevPrevLine = prevLine;
                prevLine = line;
            }
            File.WriteAllLines("evals.txt", output);
            /*
            string[] todo = File.ReadAllLines("gigabookucis.txt");
            Process p = new Process();
            p.StartInfo.FileName = "lc0.exe";
            p.StartInfo.Arguments = $"--logfile=details.log";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.Start();
            for (int i=0; i < todo.Length; i++)
            {
                if (todo[i].Contains("Opening:") && !todo[i].Contains("determined"))
                {
                    i++;
                    p.StandardInput.WriteLine("ucinewgame");
                    string moves = "";
                    if (!string.IsNullOrEmpty(todo[i]))
                        moves = " moves " + todo[i];
                    if (i % 200 == 1)
                    {
                        Console.Out.WriteLine($"{i / 2}");
                    }
                    p.StandardInput.WriteLine("position startpos" + moves);
                    p.StandardInput.WriteLine("go nodes 1000");
                    while (!p.StandardOutput.EndOfStream)
                    {
                        var line = p.StandardOutput.ReadLine();
                        if (line.Contains("bestmove")) break;
                    }
                }
            }
            p.WaitForExit();
            */
        }

        private static bool ExtractDQ(string prevPrevLine, out double d, out double q)
        {
            d = 0;
            q = 0;
            int dStart = prevPrevLine.IndexOf("D: ") + 3;
            int dEnd = prevPrevLine.IndexOf(")", dStart);
            if (prevPrevLine.Substring(dStart, dEnd - dStart).Contains("--")) return false;
            d = double.Parse(prevPrevLine.Substring(dStart, dEnd - dStart));
            int qStart = prevPrevLine.IndexOf("Q: ") + 3;
            int qEnd = prevPrevLine.IndexOf(")", qStart);
            q = double.Parse(prevPrevLine.Substring(qStart, qEnd - qStart));
            return true;
        }
    }
}
