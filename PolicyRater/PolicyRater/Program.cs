using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RaterCore;

namespace PolicyRater
{
    class Program
    {
        static List<string> legacyMatches = new List<string> { 
            "weights_run2_70",
            "weights_run3_714" 
        };
        static Scraper scraper = new Scraper(legacyMatches);

        static Accumulator policyAccumulator = new Accumulator(scraper, "PolicyRating", legacyMatches);
        static Accumulator valueAccumulator = new Accumulator(scraper, "ValueRating", legacyMatches);
        static ValueScoreAccumulator valueScoreAccumulator = new ValueScoreAccumulator(scraper, "ValueScoring", legacyMatches);
        static List<RatingModule> modules = new List<RatingModule> { 
            scraper, policyAccumulator, valueAccumulator, valueScoreAccumulator, new Downloader(scraper), new Purger(scraper), 
            new BookMaker(scraper, new List<string> { "gigabook-v3.pgn", "tcec_merged_all_book.pgn", "ICCF_filt_split.pgn" }, new List<double> { 1.0, 0.05, 0.5 }, new List<string> { "weights_run2_72", "weights_run1_6" }, 0.98, 0.15),
            new Player(
                policyAccumulator, 
                "selfplay --openings-pgn=PolicyRating\\openings.pgn --mirror-openings=true --parallelism=32 --visits=1 --backend-opts=cudnn-fp16,gpu=1 --policy-mode-size=32 --tournament-results-file=PolicyRating\\accumulate.pgn --syzygy-paths=<Your6PieceSyzygyPathsHere>",
                new int[] { int.MinValue, -1, 1, -3, 3, -10, 10, -30, 30, -100, 100, -300, 300 }), 
            new Player(
                valueAccumulator,
                "selfplay --openings-pgn=ValueRating\\openings.pgn --mirror-openings=true --visits=1 --backend-opts=cudnn-fp16,gpu=1 --value-mode-size=8 --tournament-results-file=ValueRating\\accumulate.pgn --syzygy-paths=<Your6PieceSyzygyPathsHere>",
                new int[] { int.MinValue, -1, 1}),
            new ValueScorer(valueScoreAccumulator),
            new ValueScorePresenter(
                valueScoreAccumulator,
                "<YourGitHubDirHere>\\vs_gist\\",
                "Value head scores"
                ),
            new Ordo(
                policyAccumulator,
                "<YourGitHubDirHere>\\policy_gist\\",
                "Policy head elo"
                ),
            new Ordo(
                valueAccumulator,
                "<YourGitHubDirHere>\\value_gist\\",
                "Value head elo"
                ),
        };
        static void Main(string[] args)
        {
            Process();
            System.Console.ReadKey();
        }

        static async Task Process()
        {
            try
            {
                while (true)
                {
                    bool somethingHappened = false;
                    foreach (var module in modules)
                    {
                        somethingHappened |= await module.Run();
                    }
                    if (!somethingHappened) { Thread.Sleep(60000); Console.Write("."); }
                }
            }
            catch (Exception e)
            {
                Console.Out.WriteLine(e.ToString());
            }
        }

    }
}
