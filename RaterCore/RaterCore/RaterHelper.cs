using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RaterCore
{

    public interface RatingModule
    {
        // Returns true if progress was made.
        Task<bool> Run();
    }
    public class Scraper : RatingModule
    {
        public class Candidate
        {
            public string FileName;
            public string DownloadUrl;
            public bool Special;
            public bool Normal;
            //public List<string> Completed = new List<string>();
            public bool MayGetUsed;
        }
        public List<string> legacyMatches;
        public Scraper(List<string> legacyMatches)
        {
            this.legacyMatches = legacyMatches;
        }

        public List<Candidate> candidates;
        public Dictionary<string, Candidate> filenameLookup;
        public async Task Scrape(string threshold)
        {
            Console.Out.WriteLine("Scraping network list");
            candidates = new List<Candidate>();
            filenameLookup = new Dictionary<string, Candidate>();
            // Fake candidate with no download url for baselineing.
            Candidate baselineCandidate = new Candidate
            {
                FileName = "baseline.pb.gz",
                MayGetUsed = true
            };
            candidates.Add(baselineCandidate);
            filenameLookup[baselineCandidate.FileName] = baselineCandidate;
            foreach (var external in Directory.GetFiles(".", "external_*.pb.gz"))
            {
                Candidate externalCandidate = new Candidate
                {
                    FileName = external,
                    Special = true,
                    MayGetUsed = true
                };
                candidates.Add(externalCandidate);
                filenameLookup[externalCandidate.FileName] = externalCandidate;
            }

            var content = await RaterHelper.Retrieve();
            if (content == null) throw new Exception("Retrieve failed");
            var contentLines = content.Split('\n');

            foreach (var line in contentLines)
            {
                if (!line.Contains("href=")) continue;
                if (!line.Contains("download=")) continue;

                var extra = line.Substring(line.IndexOf("href=\"") + 6);
                extra = extra.Substring(0, extra.IndexOf("\""));
                var fileName = line.Substring(line.IndexOf("download=\"") + 10);
                fileName = fileName.Substring(0, fileName.IndexOf("\""));
                bool legacy = false;
                foreach(var check in legacyMatches)
                {
                    if (fileName.Contains(check)) legacy = true;
                }
                if (legacy) continue;
                Candidate c = new Candidate
                {
                    FileName = fileName,
                    DownloadUrl = "http://ipv4.mooskagh.com" + extra,
                    Normal = true
                };
                candidates.Add(c);
                filenameLookup[fileName] = c;
                // Don't do anything older than this.
                if (fileName == threshold)
                {
                    break;
                }
            }
            Console.Out.WriteLine("Done scraping network list");
        }
        public async Task<bool> Run()
        {
            if (lastScrape < DateTime.Now.Subtract(TimeSpan.FromMinutes(20)))
            {
                Console.Out.WriteLine();
                await Scrape("weights_run1_64250.pb.gz");
                lastScrape = DateTime.Now;
                return true;
            }
            foreach (var candidate in candidates)
            {
                if (candidate.Normal) candidate.MayGetUsed = false;
            }
            return false;
        }
        DateTime lastScrape = DateTime.MinValue;
    }
    public class Downloader : RatingModule
    {
        Scraper scraper;
        public Downloader(Scraper scraper)
        {
            this.scraper = scraper;
        }
        public async Task<bool> Run()
        {
            bool downloaded = false;
            foreach (var candidate in scraper.candidates)
            {
                if (!File.Exists(candidate.FileName) && !string.IsNullOrEmpty(candidate.DownloadUrl))
                {
                    Console.Out.WriteLine($"Downloader: {candidate.FileName}");
                    await RaterHelper.Download(candidate.DownloadUrl, candidate.FileName);
                    downloaded = true;
                }
            }
            return downloaded;
        }
    }
    public class Purger : RatingModule
    {
        public Purger(Scraper scraper)
        {

        }
        public async Task<bool> Run()
        {
            return false;
        }
    }
    public class Accumulator : RatingModule
    {
        public string activeDir;
        public Scraper scraper;
        public Dictionary<string, List<string>> completed;
        public bool dirty = false;
        public bool needsOrdo = false;
        public bool needsOrdoLegacy = false;
        public List<string> legacyMatches;
        public List<string> legacyMatchesToDo;
        public Accumulator(Scraper scraper, string activeDir, List<string> legacyMatches)
        {
            this.scraper = scraper;
            this.activeDir = activeDir;
            this.legacyMatches = legacyMatches;
        }
        public bool Done(string a, string b)
        {
            List<string> others;
            if (!completed.TryGetValue(a, out others)) {
                return false;
            }
            return others.Contains(b);
        }
        public void MarkComplete(string first, string second)
        {
            if (scraper.filenameLookup.ContainsKey(first))
            {
                List<string> others;
                if (!completed.TryGetValue(first, out others))
                {
                    others = new List<string>();
                    completed[first] = others;
                }
                if (!others.Contains(second)) others.Add(second);
            }
            if (scraper.filenameLookup.ContainsKey(second))
            {
                List<string> others;
                if (!completed.TryGetValue(second, out others))
                {
                    others = new List<string>();
                    completed[second] = others;
                }
                if (!others.Contains(first)) others.Add(first);
            }
            dirty = true;
            needsOrdo = true;
        }
        private void ReadAccumulatePgn()
        {
            Console.Out.WriteLine($"Processing Accumulate - {activeDir}");
            completed = new Dictionary<string, List<string>>();
            legacyMatchesToDo = new List<string>();
            needsOrdoLegacy = false;
            var lines = File.ReadAllLines(Path.Combine(activeDir, "accumulate.pgn"));
            string first = null;
            string second = null;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (line.Contains("[White \""))
                {
                    first = line.Substring("[White \"".Length);
                    first = first.Substring(0, first.IndexOf('"'));
                }
                if (line.Contains("[Black \""))
                {
                    second = line.Substring("[Black \"".Length);
                    second = second.Substring(0, second.IndexOf('"'));
                }
                if (line.Contains("[Results \""))
                {
                    if (first != null && second != null)
                    {
                        foreach (var check in legacyMatches)
                        {
                            if (first.Contains(check) || second.Contains(check))
                            {
                                needsOrdoLegacy = true;
                                if (!legacyMatchesToDo.Contains(check)) legacyMatchesToDo.Add(check);
                            }
                        }
                        MarkComplete(first, second);
                    }
                    first = null;
                    second = null;
                }
            }
            Console.Out.WriteLine("Done Processing Accumulate");
        }
        public async Task<bool> Run()
        {
            bool origNeedOrdo = needsOrdo;
            ReadAccumulatePgn();
            needsOrdo = origNeedOrdo;
            dirty = false;
            return false;
        }
    }
    public class ValueScoreAccumulator : RatingModule
    {
        public string activeDir;
        public Scraper scraper;
        public HashSet<string> completed;
        public List<string> legacyMatches;
        public ValueScoreAccumulator(Scraper scraper, string activeDir, List<string> legacyMatches)
        {
            this.scraper = scraper;
            this.activeDir = activeDir;
            this.legacyMatches = legacyMatches;
        }
        public bool Done(string a)
        {
            return completed.Contains(a);
        }
        public void MarkComplete(string a)
        {
            if (scraper.filenameLookup.ContainsKey(a))
            {
                completed.Add(a);
            }
        }
        private void ReadAccumulateScores()
        {
            Console.Out.WriteLine($"Processing Value Score Accumulate - {activeDir}");
            completed = new HashSet<string>();
            var lines = File.ReadAllLines(Path.Combine(activeDir, "scores.txt"));
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                string name = line.Split(' ')[0];
                completed.Add(name);
            }
            Console.Out.WriteLine("Done Processing Value Score Accumulation");
        }
        public async Task<bool> Run()
        {
            ReadAccumulateScores();
            return false;
        }
    }
    public class Player : RatingModule
    {
        Accumulator accumulator;
        string args;
        int[] offsets;
        public Player(Accumulator accumulator, string args, int[] offsets)
        {
            this.accumulator = accumulator;
            this.args = args;
            this.offsets = offsets;
        }
        private async Task<bool> DoLatestNotDone()
        {
            foreach (var candidate in accumulator.scraper.candidates)
            {
                if (candidate.FileName == "baseline.pb.gz") continue;
                var cur = candidate.FileName;
                if (!File.Exists(cur))
                {
                    Console.Out.WriteLine("Missing file: {0}", cur);
                    continue;
                    /*
                    if (candidate.Special) throw new InvalidOperationException("Special candidate that doesn't exist on disk.");
                    Console.Out.WriteLine($"Downloading {cur} {candidate.DownloadUrl}");
                    await Download(candidate.DownloadUrl, cur);
                    */
                }

                if (candidate.Special)
                {
                    var externalPossibles = new[] { "baseline.pb.gz", ".\\external_J13B.2-188.pb.gz", ".\\external_384x30-t60-3010.pb.gz", ".\\external_384x30-3972-swa-20000.pb.gz" };
                    foreach (var possibleName in externalPossibles)
                    {
                        if (possibleName == cur) continue;
                        if (!accumulator.Done(cur, possibleName) && File.Exists(possibleName))
                        {
                            Console.Out.WriteLine($"{accumulator.activeDir} {cur} v {possibleName}");
                            Process p = new Process();
                            p.StartInfo.FileName = Path.Combine(accumulator.activeDir, "lc0-0.27-dev-fast.exe");
                            p.StartInfo.Arguments =
                                $"{args} --player1.weights={cur} --player2.weights={possibleName}";
                            p.StartInfo.CreateNoWindow = true;
                            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                            p.Start();
                            p.WaitForExit();
                            accumulator.MarkComplete(cur, possibleName);
                            return true;
                        }
                    }
                    continue;
                }
                var basename = cur.Split('.')[0];
                var netParts = basename.Split('_');
                var netNum = int.Parse(netParts[2]);
                bool didSomething = false;
                foreach (int offset in offsets)
                {
                    var possibleName = (offset == int.MinValue) ? "baseline.pb.gz" : string.Join("_", netParts[0], netParts[1], netNum + offset) + ".pb.gz";
                    if (!accumulator.Done(cur, possibleName) && File.Exists(possibleName))
                    {
                        Console.Out.WriteLine($"{accumulator.activeDir} {cur} v {possibleName}");
                        Process p = new Process();
                        p.StartInfo.FileName = Path.Combine(accumulator.activeDir, "lc0-0.27-dev-fast.exe");
                        p.StartInfo.Arguments =
                            $"{args} --player1.weights={cur} --player2.weights={possibleName}";
                        p.StartInfo.CreateNoWindow = true;
                        p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        p.Start();
                        p.WaitForExit();
                        accumulator.MarkComplete(cur, possibleName);
                        didSomething = true;
                    }
                }
                if (didSomething)
                {
                    return true;
                }
            }
            return false;
        }
        public async Task<bool> Run()
        {
            return await DoLatestNotDone();
        }
    }
    public class Ordo : RatingModule
    {
        Accumulator accumulator;
        string dir;
        string fileName;
        DateTime lastOrdo = DateTime.MinValue;
        public Ordo(Accumulator accumulator, string dir, string fileName)
        {
            this.accumulator = accumulator;
            this.dir = dir;
            this.fileName = fileName;
        }
        void DoLegacyOrdo()
        {
            Console.Out.WriteLine($"Updating LegacyOrdo - {accumulator.activeDir}");
            Process p = null;
            foreach (var check in accumulator.legacyMatchesToDo) {
                p = new Process();
                p.StartInfo.FileName = "MigratePgn.exe";
                p.StartInfo.Arguments = check;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                p.StartInfo.WorkingDirectory = accumulator.activeDir;
                p.Start();
                p.WaitForExit();
            }
            p = new Process();
            p.StartInfo.FileName = "Ordo.exe";
            p.StartInfo.Arguments = "-Q -N 0 -D -W -n8 -s100 -G -V -U \"0,1,2,3,4,5,6,7,8,9,10\" -j old_stats.txt -o old_rating.txt -p legacy.pgn";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.WorkingDirectory = accumulator.activeDir;
            p.Start();
            p.PriorityClass = ProcessPriorityClass.Idle;
            p.WaitForExit();
        }
        void DoOrdo()
        {
            Console.Out.WriteLine($"Updating Ordo - {accumulator.activeDir}");
            Process p = new Process();
            p.StartInfo.FileName = "Ordo.exe";
            p.StartInfo.Arguments = "-Q -N 0 -D -W -n8 -s100 -G -V -U \"0,1,2,3,4,5,6,7,8,9,10\" -j stats.txt -o rating.txt -p accumulate.pgn";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.WorkingDirectory = accumulator.activeDir;
            p.Start();
            p.PriorityClass = ProcessPriorityClass.Idle;
            p.WaitForExit();
            RaterHelper.MergeRatings(Path.Combine(accumulator.activeDir, "old_rating.txt"), Path.Combine(accumulator.activeDir, "rating.txt"));
            File.Copy(Path.Combine(accumulator.activeDir, "rating.txt"), Path.Combine(dir, fileName), true);
            p = new Process();
            p.StartInfo.FileName = "C:\\Program Files\\Git\\cmd\\git.exe";
            p.StartInfo.Arguments = "commit -am 'AutoUpdate'";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.WorkingDirectory = dir;
            p.Start();
            p.WaitForExit();
            p = new Process();
            p.StartInfo.FileName = "C:\\Program Files\\Git\\cmd\\git.exe";
            p.StartInfo.Arguments = "push";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.WorkingDirectory = dir;
            p.Start();
            p.WaitForExit();
            Console.Out.WriteLine("Ordo Done.");
        }
        public async Task<bool> Run()
        {
            if (accumulator.dirty) return false;
            if (lastOrdo < DateTime.Now.Subtract(TimeSpan.FromMinutes(60)))
            {
                if (accumulator.needsOrdoLegacy)
                {
                    accumulator.needsOrdo = true;
                    DoLegacyOrdo();
                    accumulator.needsOrdoLegacy = false;
                }
                if (lastOrdo == DateTime.MinValue || accumulator.needsOrdo)
                {
                    DoOrdo();
                    lastOrdo = DateTime.Now;
                    accumulator.needsOrdo = false;
                    return true;
                }
            }
            return false;
        }

    }

    public class BookMaker: RatingModule
    {
        Random r;
        Scraper scraper;
        List<string> inputBooks;
        List<string> runsToBookMake;
        List<double> bookProbs;
        double cutoff;
        DateTime lastBookMake = DateTime.MinValue;
        double extrastartposfrac;
        public BookMaker(Scraper scraper, List<string> inputBooks, List<double> bookProbs, List<string> runsToBookMake, double cutoff, double extrastartposfrac)
        {
            r = new Random();
            this.scraper = scraper;
            this.inputBooks = inputBooks;
            this.bookProbs = bookProbs;
            this.runsToBookMake = runsToBookMake;
            this.cutoff = cutoff;
            this.extrastartposfrac = extrastartposfrac;
        }

        List<List<string>> Clump(string[] lines)
        {
            var result = new List<List<string>>();
            var cur = new List<string>();
            bool movesStarted = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("[")) cur.Add(line);
                else if (string.IsNullOrEmpty(line))
                {
                    cur.Add(line);
                    if (movesStarted)
                    {
                        result.Add(cur);
                        cur = new List<string>();
                        movesStarted = false;
                    }
                } else
                {
                    movesStarted = true;
                    cur.Add(line);
                }
            }
            if (movesStarted)
            {
                result.Add(cur);
            }
            return result;
        }
        void DoBookMake()
        {
            foreach (var run in runsToBookMake)
            {
                Console.Out.WriteLine($"BookGenerating: {run}");
                string newestFileForRun = null;
                foreach (var candidate in scraper.candidates)
                {
                    if (candidate.FileName.Contains(run))
                    {
                        newestFileForRun = candidate.FileName;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(newestFileForRun))
                {
                    Console.Out.WriteLine($"No net for run {run}");
                    continue;
                }
                Console.Out.WriteLine($"Creating book for {newestFileForRun}");
                List<string> finalOutput = new List<string>();
                int skipped = 0;
                int book_idx = 0;
                int total_included = 0;
                foreach (var book in inputBooks)
                {
                    string[] baseLines = File.ReadAllLines($"BookGenerating\\{book}");
                    var clumps = Clump(baseLines);
                    Process p = new Process();
                    p.StartInfo.FileName = "BookGenerating\\lc0_0.27_dev_fast.exe";
                    p.StartInfo.Arguments = $"selfplay --openings-pgn=BookGenerating\\{book} --weights={newestFileForRun} --backend-opts=cudnn-fp16,gpu=1";
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    p.Start();
                    while (!p.StandardOutput.EndOfStream)
                    {
                        var line = p.StandardOutput.ReadLine();
                        if (line.Contains("Opening:") && !line.Contains("decided"))
                        {
                            var parts = line.Split(' ');
                            int index = int.Parse(parts[1]);
                            double q = double.Parse(parts[3]);
                            double d = double.Parse(parts[5]);
                            double w = (q + 1.0 - d) / 2.0;
                            double l = w - q;
                            if (w > cutoff || l > cutoff || d > cutoff) { skipped++; continue; }
                            double confidence = Math.Max(Math.Max(w, d), l);
                            // f(0.33)=0, f(1.0)=1, f'(0.33)=0
                            // x'=(x-0.33) g(0)=0, g(0.66)=1, g'(0)=0 - g(x')=x'*x'*9/4
                            // f(x)=9/4*x*x - 9/4*2/3*x + 9/4*1/9
                            // 1/4 - 3/2*x + 9/4*x*x
                            // 1.0-f(x) = 3/4 + 3/2*x - 9/4*x*x
                            if (r.NextDouble() > 3.0/4.0 + 3.0/2.0*confidence - 9.0/4.0*confidence*confidence) { skipped++; continue; }
                            if (r.NextDouble() > bookProbs[book_idx]) continue;
                            finalOutput.AddRange(clumps[index]);
                            total_included++;
                        }
                    }
                    p.WaitForExit();
                    book_idx++;
                }
                int to_add_startpos = (int)(total_included * extrastartposfrac / (1.0 - extrastartposfrac));
                for (int i=0; i < to_add_startpos; i++)
                {
                    finalOutput.Add("1. *");
                    finalOutput.Add("");
                }
                Console.Out.WriteLine($"Excluded {skipped} opening(s) based on the nets eval.");
                File.WriteAllLines($"BookGenerating\\{run}_unified.pgn", finalOutput.ToArray());
            }
        }
        public async Task<bool> Run()
        {
            if (lastBookMake < DateTime.Now.Subtract(TimeSpan.FromHours(24)))
            {
                DoBookMake();
                lastBookMake = DateTime.Now;
                return true;
            }
            return false;
        }

    }

    public class ValueScorer : RatingModule
    {
        ValueScoreAccumulator accumulator;
        public ValueScorer(ValueScoreAccumulator accumulator)
        {
            this.accumulator = accumulator;
        }
        private bool DoLatestNotDone()
        {
            foreach (var candidate in accumulator.scraper.candidates)
            {
                if (candidate.FileName == "baseline.pb.gz") continue;
                var cur = candidate.FileName;
                if (!File.Exists(cur))
                {
                    Console.Out.WriteLine("Missing file: {0}", cur);
                    continue;
                }

                if (!accumulator.Done(cur))
                {
                    string[] baseline = File.ReadAllLines(Path.Combine(accumulator.activeDir, "evals.txt"));
                    Console.Out.WriteLine($"{accumulator.activeDir} {cur}");
                    Process p = new Process();
                    p.StartInfo.FileName = Path.Combine(accumulator.activeDir, "lc0_0.27_dev_fast.exe");
                    p.StartInfo.Arguments =
                        $"selfplay --openings-pgn=BookGenerating\\gigabook-v3.pgn --weights={cur} --backend-opts=cudnn-fp16,gpu=1";
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    p.Start();
                    int bIdx = 0;
                    double qMSE = 0.0;
                    double dMSE = 0.0;
                    double kld = 0.0;
                    while (!p.StandardOutput.EndOfStream)
                    {
                        var line = p.StandardOutput.ReadLine();
                        if (line.Contains("Opening:") && !line.Contains("decided"))
                        {
                            var parts = line.Split(' ');
                            int index = int.Parse(parts[1]);
                            if (index != bIdx) Console.Out.WriteLine("Misalignment!");
                            double q = double.Parse(parts[3]);
                            double d = double.Parse(parts[5]);
                            // If d is exact but q is non-zero, probably a rounding issue.
                            if (d == 1.0)
                            {
                                d = d - Math.Abs(q);
                            }
                            double w = (q + 1.0 - d) / 2.0;
                            double l = w - q;
                            string[] parts2 = baseline[bIdx].Split(' ');
                            double qb = double.Parse(parts2[0]);
                            double db = double.Parse(parts2[1]);
                            if (db == 1.0)
                            {
                                db = db - Math.Abs(qb);
                            }
                            double wb = (qb + 1.0 - db) / 2.0;
                            double lb = wb - qb;
                            qMSE += Math.Pow(qb - q, 2.0);
                            dMSE += Math.Pow(db - d, 2.0);
                            bool ok = !double.IsNaN(kld);
                            if (wb > 0.0 && w > 0.0)
                            {
                                kld += wb * Math.Log(wb / w);
                            }
                            if (db > 0.0 && d > 0.0)
                            {
                                kld += db * Math.Log(db / d);
                            }
                            if (lb > 0.0 && l > 0.0)
                            {
                                kld += lb * Math.Log(lb / l);
                            }
                            if (double.IsNaN(kld) && ok)
                            {
                                Console.Out.WriteLine($"{q}, {w}, {l}, {d} {qb}, {wb}, {lb}, {db}");
                            }
                            bIdx++;
                        }
                    }
                    p.WaitForExit();
                    if (p.ExitCode == 0)
                    {
                        File.AppendAllText(Path.Combine(accumulator.activeDir, "scores.txt"), $"{cur} {qMSE} {dMSE} {kld}" + Environment.NewLine);
                        accumulator.MarkComplete(cur);
                        return true;
                    } else
                    {
                        Console.Out.WriteLine("Value head scorer exited without success.");
                        return false;
                    }
                }
            }
            return false;
        }

        public async Task<bool> Run()
        {
            return DoLatestNotDone();
        }

    }
    public class ValueScorePresenter : RatingModule
    {
        ValueScoreAccumulator accumulator;
        string dir;
        string fileName;
        DateTime lastPresent = DateTime.MinValue;
        public ValueScorePresenter(ValueScoreAccumulator accumulator, string dir, string fileName)
        {
            this.accumulator = accumulator;
            this.dir = dir;
            this.fileName = fileName;
        }
        void DoVSPresent()
        {
            Console.Out.WriteLine($"Presenting - {accumulator.activeDir}");
            string[] lines = File.ReadAllLines(Path.Combine(accumulator.activeDir, "scores.txt"));
            List<KeyValuePair<double, string>> toSort = new List<KeyValuePair<double, string>>();
            foreach (var line in lines)
            {
                string[] parts = line.Split(' ');
                double qScore = double.Parse(parts[1]);
                double dScore = double.Parse(parts[2]);
                double kldScore = double.Parse(parts[3]);
                toSort.Add(new KeyValuePair<double, string>(qScore, $"{parts[0],-45} {qScore,10:0} {dScore,10:0} {kldScore,10:0}"));
            }
            toSort.Sort((a,b)=>a.Key.CompareTo(b.Key));
            File.WriteAllLines(Path.Combine(accumulator.activeDir, "presented_scores.txt"), toSort.Select(a=>a.Value));
            File.Copy(Path.Combine(accumulator.activeDir, "presented_scores.txt"), Path.Combine(dir, fileName), true);
            Process p = new Process();
            p.StartInfo.FileName = "C:\\Program Files\\Git\\cmd\\git.exe";
            p.StartInfo.Arguments = "commit -am 'AutoUpdate'";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.WorkingDirectory = dir;
            p.Start();
            p.WaitForExit();
            p = new Process();
            p.StartInfo.FileName = "C:\\Program Files\\Git\\cmd\\git.exe";
            p.StartInfo.Arguments = "push";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.WorkingDirectory = dir;
            p.Start();
            p.WaitForExit();
            Console.Out.WriteLine("Presenting Done.");
        }
        public async Task<bool> Run()
        {
            if (lastPresent < DateTime.Now.Subtract(TimeSpan.FromMinutes(60)))
            {
                DoVSPresent();
                lastPresent = DateTime.Now;
                return true;
            }
            return false;
        }

    }

    public class RaterHelper
    {

        static RaterHelper()
        {
            httpClient.Timeout = TimeSpan.FromMinutes(5);
        }
        public static async Task Download(string url, string file)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    using (var dataStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(file, FileMode.CreateNew))
                    {
                        await dataStream.CopyToAsync(fileStream);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    if (File.Exists(file)) File.Delete(file);
                    Console.Out.WriteLine(ex.ToString());
                    Thread.Sleep(60000);
                    if (i == 9) throw;
                }
            }
        }


        readonly static HttpClient httpClient = new HttpClient();

        public static async Task<string> Retrieve()
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    return await httpClient.GetStringAsync("http://ipv4.mooskagh.com/networks");
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine(ex.ToString());
                    Thread.Sleep(60000);
                }
            }
            return null;
        }


        private struct RatingDetail
        {
            public int origIndex;
            public string name;
            public double rating;
            public double error;
            public double points;
            public double played;
            public double cfs;
            public double w;
            public double d;
            public double l;
        }

        private static RatingDetail ConvertToDetail(string line)
        {
            //   # PLAYER                                     :  RATING ERROR     POINTS PLAYED(%)  CFS(%)        W D        L D(%)
            //   1 .\external_384x30-t60-3180.pb.gz           :    2614     16     1217.5      2000  60.9      83      977     481      542  24.1
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return new RatingDetail {
                origIndex = int.Parse(parts[0]),
                name = parts[1],
                rating = double.Parse(parts[3]),
                error = double.Parse(parts[4]),
                points = double.Parse(parts[5]),
                played = double.Parse(parts[6]),
                cfs = -1.0,
                w = double.Parse(parts[9]),
                d = double.Parse(parts[10]),
                l = double.Parse(parts[11]),
            };
        }

        private static RatingDetail MergeDetails(IEnumerable<RatingDetail> details)
        {
            // There should be at most 2.
            var all = details.ToArray();
            if (all.Length > 2) throw new ArgumentException("Shouldn't have more than 2");
            if (all.Length == 1) return all[0];
            Console.Out.WriteLine($"Merging {all[0].name}");

            return new RatingDetail
            {
                origIndex = Math.Min(all[0].origIndex, all[1].origIndex),
                name = all[0].name,
                rating = all[0].rating,
                error = Math.Min(all[0].error, all[1].error),
                points = all[0].points + all[1].points,
                played = all[0].played + all[1].played,
                cfs = -1,
                w = all[0].w + all[1].w,
                d = all[0].d + all[1].d,
                l = all[0].l + all[1].l,
            };
        }

        private static string DetailToString(RatingDetail detail)
        {
            StringBuilder result = new StringBuilder();
            result.AppendFormat("{0,4} {1,-42} : {2,7} {3,6} {4,10:#.0} {5,9} {6,5:#.0} ------- {8,8} {9,7} {10,8} {11,5:#.0}", 
                detail.origIndex, detail.name, detail.rating, detail.error, detail.points, detail.played, detail.points*100/detail.played, detail.cfs, detail.w, detail.d, detail.l, detail.d*100/(detail.w+detail.d+detail.l));
            return result.ToString();
        }

        private static bool ShouldIgnore(string a)
        {
            if (string.IsNullOrEmpty(a)) return true;
            if (a.StartsWith("White")) return true;
            if (a.StartsWith("Draw")) return true;
            return false;
        }

        public static void MergeRatings(string oldFile, string newFile)
        {
            if (!File.Exists(oldFile)) return;
            string[] oldlines = File.ReadAllLines(oldFile);
            string[] newLines = File.ReadAllLines(newFile);
            var oldDetails = oldlines.Skip(2).Where(a=>!ShouldIgnore(a)).Select(ConvertToDetail).ToArray();
            var newDetails = newLines.Skip(2).Where(a => !ShouldIgnore(a)).Select(ConvertToDetail).ToArray();
            var oldBaseline = oldDetails.Single(a => a.name == "baseline.pb.gz");
            var newBaseline = newDetails.Single(a => a.name == "baseline.pb.gz");
            for (int i=0; i < newDetails.Length; i++)
            {
                newDetails[i].rating += oldBaseline.rating - newBaseline.rating;
            }
            var mergedDetails = oldDetails.Concat(newDetails).GroupBy(a => a.name).Select(MergeDetails).OrderByDescending(a => a.rating).ThenBy(a => a.origIndex).ToArray();
            for(int i=0; i < mergedDetails.Length;i++)
            {
                mergedDetails[i].origIndex = i + 1;
            }
            File.WriteAllLines(newFile, newLines.Take(2).Concat(mergedDetails.Select(DetailToString)));
        }
    }
}
