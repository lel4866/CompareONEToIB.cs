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
        bool exit = false;

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
                    case "-id":
                    case "--ibdir":
                        arg_name = "ibdir";
                        break;
                    case "-od":
                    case "--onedir":
                        arg_name = "onedir";
                        break;
                    case "-h":
                    case "--help":
                        Console.WriteLine(Program.version);
                        Console.WriteLine("Compare OptionnetExplorer positions with Interactive Brokers positions");
                        Console.WriteLine("Program will compare positions in the latest file in each of the specified directories");
                        Console.WriteLine("\nCommand line arguments:");
                        Console.WriteLine("    --version, -v : display version number");
                        Console.WriteLine("    --symbol, -s  : specify primary option index symbol; currently, SPX, RUT, or NDX");
                        Console.WriteLine("    --onedir, -od  : specify directory that contains files exported from ONE (of form: yyyy-mm-dd-ONEDetailReport.csv)");
                        Console.WriteLine("    --ibdir, -id  : specify directory that contains files exported from IB (of form: portfolio.yyyymmdd.csv)");
                        break;

                    default:
                        Console.WriteLine("Invalid command line argument: " + arg + ". Program exiting.");
                        exit = true;
                        break;
                }
            }
            else
            {
                switch (arg_name)
                {
                    case "symbol":
                        if (!Program.associated_symbols.ContainsKey(arg))
                        {
                            Console.WriteLine("Unknown symbol: " + arg + ". Program exiting.");
                            System.Environment.Exit(-1);
                        }
                        Program.master_symbol = arg;
                        break;

                    case "ibdir":
                        if (!Directory.Exists(arg))
                        {
                            Console.WriteLine("IB Direcotry: " + arg + "does not exist. Program exiting.");
                            exit = true;
                        }
                        Program.ib_directory = arg;
                        break;

                    case "onedir":
                        if (!Directory.Exists(arg))
                        {
                            Console.WriteLine("ONE Direcotry: " + arg + "does not exist. Program exiting.");
                            exit = true;
                        }
                        Program.one_directory = arg;
                        break;
                }
                arg_name = null;
            }
        }

        if (exit)
            System.Environment.Exit(-1);
    }
}
