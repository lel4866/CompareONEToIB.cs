using System;
using System.Diagnostics;
using System.IO;

static class Program
{
    public const string version = "0.0.1";
    public const string version_date = "2021-12-08";

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
        return true;
    }

    static bool ReadIBData()
    {
        return true;
    }

    static bool CompareONEToIB()
    {
        return true;
    }

}
