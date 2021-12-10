using System;
using System.Diagnostics;
using System.IO;

static class Program
{
    public const string version = "0.0.1";
    public const string version_date = "2021-12-08";
    public const string ib_directory = @"C:\Users\lel48\OneDrive\Documents\IBExport\";
    public const string one_directory = @"C:\Users\lel48\OneDrive\Documents\ONEExport\";



    static int Main(string[] args)
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();

        bool rc = ProcessCommandLineArguments(args); // calls System.Environment.Exit(-1) if bad command line arguments
        if (!rc)
            return -1;

        rc = ReadONEData();
        if (!rc)
            return -1;

        rc = ReadIBData();
        if (!rc)
            return -1;

        rc = CompareONEToIB();
        if (!rc)
            return -1;

        stopWatch.Stop();
        Console.WriteLine($"Elapsed time = {stopWatch.Elapsed}");

        return 0;
    }

    static bool ProcessCommandLineArguments(string[] args)
    {
        return true;
    }

    static bool ReadONEData()
    {
        const string ending = "-ONEDetailReport.csv";
        string[] files;
        if (Directory.Exists(one_directory))
        {
            files = Directory.GetFiles(one_directory, '*' + ending, SearchOption.TopDirectoryOnly);
            bool file_found = false;
            foreach(string full_fn in files)
            {
                string filename = Path.GetFileName(full_fn);
                string datestr = filename.Substring(0, filename.Length - ending.Length);
                if (DateTime.TryParse(datestr, out DateTime dateValue))
                {
                    file_found = true;
                }
                else
                {
                    continue;
                }
            }
            if (!file_found)
            {
                Console.WriteLine("***Error*** No OptionNet files found");
                return false;
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    static bool ReadIBData()
    {
        const string filename_pattern = "*.csv";
        string[] files;
        if (Directory.Exists(one_directory))
        {
            files = Directory.GetFiles(one_directory, filename_pattern, SearchOption.TopDirectoryOnly);
            bool file_found = false;
            foreach (string full_fn in files)
            {
                string filename = Path.GetFileName(full_fn);
                string datestr = filename.Substring("portfolio.".Length, filename_pattern.Length - "portfolio..csv".Length);
                if (DateTime.TryParse(datestr, out DateTime dateValue))
                {
                    file_found = true;
                }
                else
                {
                    continue;
                }
            }
            if (!file_found)
            {
                Console.WriteLine("***Error*** No OptionNet files found");
                return false;
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    static bool CompareONEToIB()
    {
        return true;
    }

}
