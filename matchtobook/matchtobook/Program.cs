using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace matchtobook
{
    class Program
    {
        static void Main(string[] args)
        {
            var input = File.ReadAllLines(args[0]);
            var output = new List<string>();
            var currentFen = "";
            var currentGame = new List<string>();
            var started = false;
            foreach (var line in input)
            {
                if (line.StartsWith("[FEN "))
                {
                    currentFen = line;
                }
                if (line.StartsWith("["))
                {
                    if (started)
                    {
                        Finalize(ref currentFen, ref started, currentGame, output);
                    }
                    continue;
                }
                started = true;
                var movetext = line;
                while (movetext.Contains("{"))
                {
                    int start = movetext.IndexOf("{");
                    int end = movetext.IndexOf("}", start);
                    movetext = movetext.Substring(0, start) + movetext.Substring(end + 1);
                }
                if (movetext.Contains(";"))
                {
                    movetext = movetext.Substring(0, movetext.IndexOf(";"));
                }
                if (string.IsNullOrWhiteSpace(movetext)) continue;
                var parts = movetext.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var move = part;
                    while (move.Contains("."))
                    {
                        move = move.Substring(move.IndexOf(".") + 1);
                    }
                    if (string.IsNullOrWhiteSpace(move)) continue;
                    if (move == "1/2-1/2" || move == "*" || move == "1-0" || move == "0-1") continue;
                    currentGame.Add(move);
                }
            }
            if (started)
            {
                Finalize(ref currentFen, ref started, currentGame, output);
            }
            File.AppendAllLines("Book.pgn", output);
        }

        private static void Finalize(ref string currentFen, ref bool started, List<string> currentGame, List<string> output)
        {
            for (int i=0; i < currentGame.Count; i++)
            {
                if (!string.IsNullOrEmpty(currentFen)) output.Add(currentFen);
                output.Add("[Result \"*\"]");
                output.Add("");
                StringBuilder moves = new StringBuilder();
                for (int j=0; j < i; j++) {
                    if (j %2 == 0)
                    {
                        moves.Append($"{j/2+1}.");
                    }
                    moves.Append(currentGame[j]);
                    moves.Append(" ");
                }
                moves.Append("*");
                output.Add(moves.ToString());
                output.Add("");
            }
            currentGame.Clear();
            started = false;
            currentFen = "";
        }
    }
}
