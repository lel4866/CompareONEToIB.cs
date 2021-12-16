using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompareOneToIB;

namespace CompareONEToIB;

internal static class CommandLine
{
    internal static void ProcessCommandLineArguments(string[] args)
    {
        string? arg_name = null;

        foreach (string arg in args)
        {
            if (arg_name == null)
            {
                switch (arg)
                {
                    case "-v":
                    case "--version":
                        Console.WriteLine("CompareONEToIB version " + Program.version);
                        break;
                    case "-s":
                    case "--symbol":
                        arg_name = "symbol";
                        break;
                    case "-h":
                    case "--help":
                        Console.WriteLine(Program.version);
                        Console.WriteLine("Compare OptionnetExplorer positions with Interactive Brokers positions");
                        Console.WriteLine("Command line arguments:");
                        Console.WriteLine("    --version, -v : display version number");
                        Console.WriteLine("    --symbol, -s  : primary option index symbol; currently, SPX, RUT, or NDX);
                        break;

                    default:
                        Console.WriteLine("Invalid command line argument: " + arg + ". Program exiting.");
                        System.Environment.Exit(-1);
                        break;
                }
            }
            else
            {
                switch (arg_name)
                {
                    case "symbol":
                        if (!Program.associate_symbols.ContainsKey(arg))
                        {
                            Console.WriteLine("Unknown symbol: " + arg + ". Program exiting.");
                            System.Environment.Exit(-1);
                        }
                        Program.master_symbol = arg;
                        break;
                }
                arg_name = null;
            }
        }
    }
}
