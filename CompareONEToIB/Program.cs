using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

enum OptionType
{
    Put,
    Call
}

enum TradeStatus
{
    Open,
    Closed
}

//,Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc
//,"IB1",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
class ONETrade
{
    public string account = "";
    public string trade_id = "";
    public string trade_name = "";
    public string underlying = "";
    public TradeStatus status;
    public DateTime open_dt;
    public DateTime close_dt;
    public int dte;
    public int dit;
    public float total_commission;
    public float pnl;
    public List<ONEPosition> positions = new();
}

//,,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission
//,,"IB1",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
class ONEPosition
{
    public string account = "";
    public string trade_id = "";
    public DateTime open_dt;
    public int quantity; // positive==buy, negative==sell
    public OptionType optionType;
    public int strike;
    public DateOnly expiration;
    public float open_price;
    public float commission;
}

//Account, Financial Instrument Description, Exchange, Position, Currency, Market Price,Market Value, Average Price,Unrealized P&L,Realized P&L,Liquidate Last, Security Type,Delta Dollars
//UXXXXXXX,SPX APR2022 4300 P[SPXW  220429P04300000 100],CBOE,2,USD,123.0286484,24605.73,123.5542635,-105.12,0.00,No,OPT,-246551.12
class IBPosition
{
    public string account = "";
    public int quantity;
    public float marketPrice;
    public float averagePrice; // average entry price
    public float unrealizedPnL;
    public float realizedPnL;
}

static class Program
{
    public const string version = "0.0.1";
    public const string version_date = "2021-12-08";
    public const string ib_directory = @"C:\Users\lel48\OneDrive\Documents\IBExport\";
    public const string one_directory = @"C:\Users\lel48\OneDrive\Documents\ONEExport\";

    static string one_account = "";
    static string ib_account = "";

    static Dictionary<string, ONETrade> oneTrades = new(); // key is trade_id
    static Dictionary<(DateTime, int), int> ibPositions = new(); // key is (Expiration, Strike); value is quantity

    static int Main(string[] args)
    {
#if false
        string line = "\"ab\"\"c\"";
        int i = 4;
        string line1 = line[..i];
        string line2 = line[(i+1)..];
        string line3 = line1 + line2;
        string line = "\"ab\"\"c\"";
        bool rc1 = parseCVSLine(line, out List<string> fields);
#endif
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

        string ib_header1 = "Account,Financial Instrument Description,Exchange,Position,Currency,Market Price,Market Value,Average Price,Unrealized P&L,Realized P&L,Liquidate Last,Security Type,Delta Dollars";
        line1 = lines[1].Trim();
        if (line1 != ib_header1)
        {
            Console.WriteLine("***Error*** First line of IB file must start with: Account,Financial Instrument Description,Exchange,Position,...");
            return false;
        }

        for (int line_index = 2; line_index < lines.Length; line_index++)
        {
            bool rc = parseCSVLine(lines[line_index], out List<string> fields);
            if (!rc)
                return false;

            // blank line terminates list of positions. Next line must be "Cash Balances"
            if (fields.Count == 0)
                break;

            rc = ParseIBPositionLine(line_index, fields);
            if (!rc)
                return false;
        }

        return true;
    }

    //Account, Financial Instrument Description, Exchange, Position, Currency, Market Price,Market Value, Average Price,Unrealized P&L,Realized P&L,Liquidate Last, Security Type,Delta Dollars
    //UXXXXXXX,SPX APR2022 4300 P[SPXW  220429P04300000 100],CBOE,2,USD,123.0286484,24605.73,123.5542635,-105.12,0.00,No,OPT,-246551.12
    static bool ParseIBPositionLine(int line_index, List<string> fields)
    {
        if (fields.Count != 13)
        {
            Console.WriteLine($"***Error*** IB Position line #{line_index + 1} must have 12 fields, not {fields.Count} fields");
            return false;
        }

        ib_account = fields[0].Trim();
        if (ib_account.Length == 0)
        {
            Console.WriteLine($"***Error*** Account id (first field) in IB line #{line_index + 1} is blank");
            return false;
        }

        // ignore positions which are not CBOE SPX Options
        if (fields[2].Trim() != "CBOE" || fields[11].Trim() != "OPT" || !fields[1].StartsWith("SPX"))
        {
            Console.WriteLine($"Ignoring line #{line_index + 1} in IB file: not an SPX option");
            return true;
        }

        IBPosition ibPosition = new();
        ibPosition.account = ib_account;

        bool rc = int.TryParse(fields[3], out ibPosition.quantity);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Position: {fields[3]}");
            return false;
        }

        rc = float.TryParse(fields[5], out ibPosition.marketPrice);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Market Price: {fields[5]}");
            return false;
        }

        rc = float.TryParse(fields[7], out ibPosition.averagePrice);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Average Price: {fields[7]}");
            return false;
        }

        rc = float.TryParse(fields[8], out ibPosition.unrealizedPnL);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Unrealized P&L: {fields[8]}");
            return false;
        }

        rc = float.TryParse(fields[9], out ibPosition.realizedPnL);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Realized P&L: {fields[9]}");
            return false;
        }

        rc = ParseOptionSpec(fields[1], out OptionType type, out DateTime expiration, out int strike);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid option specification: {fields[1]}");
            return false;
        } 

        var key = (expiration, strike);
        if (ibPositions.ContainsKey(key))
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: duplicate expiration/strike ({expiration.Date},{strike})");
            return false;
        }
        ibPositions.Add((expiration, strike), ibPosition.quantity);
        return true;
    }

    // SPX APR2022 4300 P[SPXW  220429P04300000 100]
    static bool ParseOptionSpec(string field, out OptionType type, out DateTime expiration, out int strike)
    {
        type = OptionType.Put;
        expiration = new();
        strike = 0;

        MatchCollection mc = Regex.Matches(field, @".*\[(\w+) +(.*) \w+\]$");
        if (mc.Count > 1)
            return false;
        Match match0 = mc[0];
        if (match0.Groups.Count != 3)
            return false;

        string option_class = match0.Groups[1].Value;
        string option_code = match0.Groups[2].Value;
        int year = int.Parse(option_code[0..2]) + 2000;
        int month = int.Parse(option_code[2..4]);
        int day = int.Parse(option_code[4..6]);
        expiration = new(year, month, day);
        type = (option_code[6] == 'P') ? OptionType.Put : OptionType.Call;
        strike = int.Parse(option_code[7..12]);
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
        ONETrade? curOneTrade = null;
        for (int line_index = 9; line_index < lines.Length; line_index++)
        {
            // fields[0] must be blank;
            // if fields[1] is blank, this is a position line, otherwise it is a trade line
            string line = lines[line_index].Trim();

            // trades (except for the first one) are separated by blanks
            if (line.Length == 0)
            {
                curOneTrade = null;
                continue;
            }
            bool rc = parseCSVLine(line, out List<string> fields);

            if (fields.Count < 14)
            {
                Console.WriteLine($"***Error*** ONE Trade/Position line #{line_index + 1} must have at least 14 fields, not {fields.Count} fields");
                return false;
            }

            string account1 = fields[1].Trim();
            if (account1.Length != 0) {
                if (curOneTrade != null)
                {
                    // do whatever when we've parsed all position lines for trade
                }

                // start new trade
                curOneTrade = ParseONETradeLine(line_index, fields);
                if (curOneTrade == null)
                    return false;
                continue;
            }
            else
            {
                if (curOneTrade == null)
                {
                    Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} comes before Trade line.");
                    return false;
                }

                ONEPosition? position = ParseONEPositionLine(line_index, fields);
                if (position == null)
                    return false;
                curOneTrade.positions.Add(position);
                continue;
            }
        }

        return true;
    }

    // ",Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc"
    //,"IB1",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
    // we don't parse Margin,Comms,PnL,PnLperc
    static ONETrade? ParseONETradeLine(int line_index, List<string> fields) {
        if (fields.Count != 16)
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} must have 16 fields, not {fields.Count} fields");
            return null;
        }

        ONETrade oneTrade = new();

        oneTrade.account = fields[1];
        if (one_account != oneTrade.account)
        {
            Console.WriteLine($"***Error*** In ONE Trade line #{line_index + 1}, account field: {oneTrade.account} is not the same as line 9 of file: {one_account}");
            return null;
        }

        if (!DateTime.TryParse(fields[2], out DateTime dummy_dt))
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid date field: {fields[2]}");
            return null;
        }

        oneTrade.trade_id = fields[3];
        if (oneTrade.trade_id.Length == 0)
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has empty trade id field");
            return null;
        }

        oneTrade.trade_name = fields[4];
        oneTrade.underlying = fields[5];

        if (fields[6] == "Open")
            oneTrade.status = TradeStatus.Open;
        else if (fields[6] == "Closed")
            oneTrade.status = TradeStatus.Closed;
        else
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid trade status field: {fields[6]}");
            return null;
        }

        if (!DateTime.TryParse(fields[8], out oneTrade.open_dt))
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid date field: {fields[8]}");
            return null;
        }

        if (oneTrade.status == TradeStatus.Closed)
        {
            if (!DateTime.TryParse(fields[9], out oneTrade.close_dt))
            {
                Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid date field: {fields[9]}");
                return null;
            }
        }

        if (!int.TryParse(fields[10], out oneTrade.dte))
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid dte field: {fields[10]}");
            return null;
        }

        if (!int.TryParse(fields[11], out oneTrade.dit))
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid dit field: {fields[11]}");
            return null;
        }

        if (oneTrades.ContainsKey(oneTrade.trade_id))
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in ONE file: duplicate trade id: {oneTrade.trade_id}");
            return null;
        }
        oneTrades.Add(oneTrade.trade_id, oneTrade);

        return oneTrade;
    }

    //,,"IB1",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
    static ONEPosition? ParseONEPositionLine(int line_index, List<string> fields)
    {
        if (fields.Count != 14)
        {
            Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} must have 14 fields, not {fields.Count} fields");
            return null;
        }

        ONEPosition position = new();
        return position;
    }

    static bool CompareONEPositionsToIBPositions()
    {
        return true;
    }

    const char delimiter = ',';
    static bool parseCSVLine(string line, out List<string> fields)
    {
        fields = new();
        int state = 0;
        int start = 0;
        char c;
        for (int i=0; i<line.Length; i++)
        {
            c = line[i];
            switch (state)
            {
                case 0: // start of field; quote, delimiter, or other
                    switch (c)
                    {
                        case delimiter: // first char is delimiter...field is empty
                            fields.Add("");
                            break;
                        case '"': // field starts with quote
                            start = i + 1;
                            state = 2;
                            break;
                        default: // field starts with non-quote
                            start = i;
                            state = 1;
                            break;
                    }
                    break;

                case 1: // looking for end of field that didn't start with quote (interior quotes ignored)
                    if (c == delimiter)
                    {
                        fields.Add(line.Substring(start, i - start).Trim());
                        state = 0;
                    }
                    break;

                case 2: // looking for end of field that started with quote; if this is quote, could be start of double quote or end of field
                    if (c == '"')
                        state = 3;
                    break;

                case 3: // looking for end of field that started with quote; prior char was quote (that didn't start field)...if this is quote, it's a double quote, else better be delimiter to end field
                    if (c == '"')
                    {
                        // double quote...throw away first one
                        line = line[..i] + line[(i+1)..];
                        i--;
                        state = 2;
                    }
                    else
                    {
                        if (c != delimiter)
                            return false; // malformed field
                        fields.Add(line.Substring(start, i - start - 1).Trim());
                        state = 0;
                    }
                    break;

                default:
                    Debug.Assert(false);
                    break;
            }

        }
        // process last field
        switch (state)
        {
            case 0: // must be blank line
                Debug.Assert(line.Length == 0);
                break;

            case 1: // field started with non-quote...standard end
                fields.Add(line[start..].Trim());
                break;

            case 2: // field started with quote, but didn't end with quote...error
                return false;

            case 3: // field ended with quote
                string dbg = line[start..^1];
                fields.Add(line[start..^1].Trim());
                break;

            default:
                Debug.Assert(false);
                return false;
        }

        return true;
    }
}
