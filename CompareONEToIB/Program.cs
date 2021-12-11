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

    static string one_account = "";
    static string ib_account = "";

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

        rc = CompareONEPositionsToIBPositions();
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

    //Portfolio
    //Account, Financial Instrument Description, Exchange, Position, Currency, Market Price,Market Value, Average Price,Unrealized P&L,Realized P&L,Liquidate Last, Security Type,Delta Dollars
    //UXXXXXXX,SPX APR2022 4300 P[SPXW  220429P04300000 100],CBOE,2,USD,123.0286484,24605.73,123.5542635,-105.12,0.00,No,OPT,-246551.12
    //UXXXXXXX,SPX APR2022 4000 P[SPXW  220429P04000000 100],CBOE,-4,USD,79.0655136,-31626.21,82.2374865,1268.79,0.00,No,OPT,309447.06
    static bool ProcessIBFile(string full_filename)
    {
        Console.WriteLine("Processing IB file: " +  full_filename);
        string[] lines = File.ReadAllLines(full_filename);
        if (lines.Length < 3)
        {
            Console.WriteLine("***Error*** IB File must contain at least 3 lines");
            return false;
        }

        string line1 = lines[0].Trim();
        if (line1 != "Portfolio")
        {
            Console.WriteLine("***Error*** First line of IB file must be 'Portfolio'");
            return false;
        }

        string ib_header1 = "Account,Financial Instrument Description,Exchange,Position,Currency,Market Price, Market Value,Average Price, Unrealized P & L,Realized P&L,Liquidate Last, Security Type,Delta Dollars";
        line1 = lines[1].Trim();
        if (line1 != ib_header1)
        {
            Console.WriteLine("***Error*** First line of IB file must start with: Account,Financial Instrument Description,Exchange,Position,...");
            return false;
        }

        for (int line_index = 2; line_index < lines.Length; line_index++)
        {
            string[] fields = lines[line_index].Split(',');
            ib_account = fields[0].Trim();
            if (ib_account.Length == 0)
            {
                Console.WriteLine($"***Error*** Account id (first field) in IB line #{line_index + 1} is blank");
                return false;
            }
        }

        return true;
    }

    //ONE Detail Report
    //
    //Date/Time: 12/8/2021 08:28:42
    //Filter: [Account] = 'IB1'
    //Grouping: Account
    //
    //,Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc
    //,,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission
    //IB1
    //,"IB1",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
    //,,"IB1",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
    //,,"IB1",285,10/11/2021 11:37:32 AM,Buy,4,SPX   220319P02725000,3/18/2022,Put,SPX Mar22 2725 Put,SPX,12.8,4.56

    static bool ProcessONEFile(string full_filename)
    {
        Console.WriteLine("Processing ONE file: " + full_filename);
        string[] lines = File.ReadAllLines(full_filename);
        if (lines.Length < 9)
        {
            Console.WriteLine("***Error*** ONE File must contain at least 9 lines");
            return false;
        }

        string? line1 = lines[0].Trim();
        if (line1 != "ONE Detail Report")
        {
            Console.WriteLine($"***Error*** First line of ONE file must be 'ONE Detail Report', not: {line1}");
            return false;
        }

        line1 = lines[1].Trim();
        if (line1.Length != 0)
        {
            Console.WriteLine($"***Error*** Second line of ONE must be blank, not: {line1}");
            return false;
        }

        line1 = lines[2].Trim();
        if (!line1.StartsWith("Date/Time:"))
        {
            Console.WriteLine($"***Error*** Third line of ONE file must start with 'Date/Time:', not: {line1}");
            return false;
        }

        line1 = lines[3].Trim();
        if (!line1.StartsWith("Filter: [Account]"))
        {
            Console.WriteLine($"***Error*** Fourth line of ONE file must start with 'Filter: [Account]', not: {line1}");
            return false;
        }

        line1 = lines[4].Trim();
        if (!line1.StartsWith("Grouping: Account"))
        {
            Console.WriteLine($"***Error*** Fifth line of ONE file must start with 'Grouping: Account', not: {line1}");
            return false;
        }

        line1 = lines[5].Trim();
        if (line1.Length != 0)
        {
            Console.WriteLine($"***Error*** Sixth line of ONE must be blank, not: {line1}");
            return false;
        }

        line1 = lines[6].Trim();
        string one_trade_header = ",Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc";
        if (line1 != one_trade_header)
        {
            Console.WriteLine($"***Error*** Seventh line of ONE file (Trade Header) must start with ',Account,Expiration,TradeId,TradeName,Underlying...', not: {line1}");
            return false;
        }

        line1 = lines[7].Trim();
        string one_position_header = ",,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission";
        if (line1 != one_position_header)
        {
            Console.WriteLine($"***Error*** Eighth line of ONE file (Position Header) must start with ',,Account,TradeId,Date,Transaction,Qty,Symbol...', not: {line1}");
            return false;
        }

        // account appears here in line 9, in the Trade lines, and in the Position lines. They must all match
        one_account = lines[8].Trim();
        if (one_account.Length == 0)
        {
            Console.WriteLine($"***Error*** Ninth line of ONE must be ONE account name, not blank");
            return false;
        }

        // parse Trade and Position lines
        bool existing_trade = false;
        for (int line_index = 9; line_index < lines.Length; line_index++)
        {
            // fields[0] must be blank;
            // if fields[1] is blank, this is a position line, otherwise it is a trade line
            string line = lines[line_index].Trim();
            string[] fields = line.Split(',');
            if (fields.Length < 14)
            {
                Console.WriteLine($"***Error*** ONE Trade/Position line #{line_index + 1} must have at least 14 fields, not {fields.Length} fields");
                return false;
            }

            bool rc;
            string account1 = fields[1].Trim();
            if (account1.Length != 0) {
                if (existing_trade)
                {
                    // do whatever when we've parsed all position lines for trade
                }

                // start new trade
                rc = ParseONETradeLine(line_index, fields);
                if (!rc)
                    return false;
                existing_trade = true;
                continue;
            }
            else
            {
                if (!existing_trade)
                {
                    Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} comes before Trade line.");
                    return false;
                }

                rc = ParseONEPositionLine(line_index, fields);
                if (!rc)
                    return false;
                continue;
            }
        }

        return true;
    }

    static bool ParseONETradeLine(int line_index, string[] fields) {
        if (fields.Length != 16)
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} must have 16 fields, not {fields.Length} fields");
            return false;
        }

        if (!RemoveQuotes(line_index+1, "Account", fields[1].Trim(), out string account1)) 
                return false;

        if (one_account != account1)
        {
            Console.WriteLine($"***Error*** In ONE Trade line #{line_index + 1}, account field: {account1} is not the same as line 9 of file: {one_account}");
            return false;
        }

        string trade_date_string = fields[2].Trim();
        if (!DateTime.TryParse(trade_date_string, out DateTime trade_dt))
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid date field: {trade_date_string}");
            return false;
        }

        string trade_id = fields[3].Trim();
        if (trade_id.Length == 0)
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has empty trade id field");
            return false;
        }

        if (!RemoveQuotes(line_index + 1, "trade name", fields[4], out string trade_name))
            return false;

        return true;
    }

    static bool ParseONEPositionLine(int line_index, string[] fields)
    {
        if (fields.Length != 14)
        {
            Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} must have 14 fields, not {fields.Length} fields");
            return false;
        }

        return true;
    }


    static bool CompareONEPositionsToIBPositions()
    {
        return true;
    }

    // remove quotes that surround a filed in a csv file...ok if quotes don't exist
    static bool RemoveQuotes(int lineno, string field_name, string field, out string stripped_field)
    {
        stripped_field = field.Trim();
        if (stripped_field.Length == 0)
        {
            Console.WriteLine($"***Error*** In line {lineno}, field {field_name}, field is blank");
            return false;
        }

        if (stripped_field.Length == 1 || stripped_field[0] != '"' || field[^1] != '"')
                return true;

        stripped_field = field[1..^2].Trim();
        if (stripped_field.Length == 0)
        {
            Console.WriteLine($"***Error*** In line {lineno}, field {field_name}: after stripping quotes, field is blank");
            return false;
        }

        return true;
    }
}
