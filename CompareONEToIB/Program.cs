using System;
using System.Collections.Generic;
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
            DateTime latestDate = new(1000, 1, 1);
            string latest_full_filename = "";
            files = Directory.GetFiles(one_directory, '*' + ending, SearchOption.TopDirectoryOnly);
            bool file_found = false;
            foreach(string full_filename in files)
            {
                string filename = Path.GetFileName(full_filename);
                string datestr = filename[..^ending.Length];
                if (DateTime.TryParse(datestr, out DateTime dt))
                {
                    file_found = true;
                    if (dt > latestDate)
                    {
                        latestDate = dt;
                        latest_full_filename = full_filename;
                    }
                }
                else
                {
                    continue;
                }
            }
            if (!file_found)
            {
                Console.WriteLine("***Error*** No valid OptionNet files found");
                return false;
            }

            bool rc = ProcessONEFile(latest_full_filename);
            if (!rc)
                return false;
        }
        else
        {
            return false;
        }

        return true;
    }

    static bool ReadIBData()
    {
        const string filename_pattern = "*.csv"; // file names look like: portfolio.20211208.csv
        const string filename_prefix = "portfolio."; // file names look like: portfolio.20211208.csv
        int filename_prefix_len = filename_prefix.Length; 

        string[] files;
        if (Directory.Exists(ib_directory))
        {
            DateTime latestDate = new(1000, 1, 1);
            string latest_full_filename = "";
            files = Directory.GetFiles(ib_directory, filename_pattern, SearchOption.TopDirectoryOnly);
            bool file_found = false;
            foreach (string full_filename in files)
            {
                string filename = Path.GetFileName(full_filename);
                if (!filename.StartsWith(filename_prefix))
                {
                    Console.WriteLine($"***Warning*** CSV File found in IB directory whose name does not match proper IB portfolio filename: {filename}");
                    continue;
                }
                string datestr = filename[filename_prefix_len..];
                if (datestr.Length != 12) // yyyymmdd.csv
                {
                    Console.WriteLine($"***Warning*** CSV File found in IB directory whose name does not match proper IB portfolio filename: {filename}");
                    continue;
                }
                if (!int.TryParse(datestr[..4], out int year))
                {
                    Console.WriteLine($"***Warning*** CSV File found in IB directory whose name does not match proper IB portfolio filename: {filename}");
                    continue;
                }
                if (!int.TryParse(datestr.AsSpan(4, 2), out int month))
                {
                    Console.WriteLine($"***Warning*** CSV File found in IB directory whose name does not match proper IB portfolio filename: {filename}");
                    continue;
                }
                if (!int.TryParse(datestr.AsSpan(6, 2), out int day))
                {
                    Console.WriteLine($"***Warning*** CSV File found in IB directory whose name does not match proper IB portfolio filename: {filename}");
                    continue;
                }

                file_found = true;
                DateTime dt = new(year, month, day);
                if (dt > latestDate) { 
                    latestDate = dt;
                    latest_full_filename = full_filename;
                }
            }

            if (!file_found)
            {
                Console.WriteLine("***Error*** No valid IB files found");
                return false;
            }

            bool rc = ProcessIBFile(latest_full_filename);
            if (!rc)
                return false;
        }
        else
        {
            return false;
        }

        return true;
    }

    static bool ProcessIBFile(string full_filename)
    {
        using StreamReader sr = new(full_filename);
        Console.WriteLine("Processing IB file: " +  full_filename);

        string? line1 = sr.ReadLine();
        if (line1 == null)
        {
            Console.WriteLine("***Error*** File empty");
            return false;

        }
        line1 = line1.Trim();
        if (line1 != "Portfolio")
        {
            Console.WriteLine("***Error*** First line of IB file must be 'Portfolio'");
            return false;
        }
        return true;
    }

    static bool ProcessONEFile(string full_filename)
    {
        using StreamReader sr = new(full_filename);
        Console.WriteLine("Processing ONE file: " + full_filename);

        string? line1 = sr.ReadLine();
        if (line1 == null)
        {
            Console.WriteLine("***Error*** File empty");
            return false;

        }
        line1 = line1.Trim();
        if (line1 != "ONE Detail Report")
        {
            Console.WriteLine("***Error*** First line of IB file must be 'ONE Detail Report'");
            return false;
        }

        return true;
    }

    static bool CompareONEToIB()
    {
        return true;
    }
}
