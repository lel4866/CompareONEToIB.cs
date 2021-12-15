using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

enum OptionType
{
    Put,
    Call,
    Stock,
    Futures
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
    //public float total_commission;
    //public float pnl;

    // these are consolidated positions for trade: key is (symbol, OptionType, Expiration, Strike); value is quantity
    public Dictionary<(string, OptionType, DateOnly, int), int> positions = new();
}

//,,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission
//,,"IB1",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
class ONEPosition
{
    public string account = "";
    public string trade_id = "";
    public OptionType optionType;
    public DateTime open_dt;
    public string symbol = ""; // SPX, SPXW, etc
    public int strike;
    public DateOnly expiration;
    public int quantity; // positive==buy, negative==sell
    public float open_price;
}

//Financial Instrument Description, Position, Currency, Market Price, Market Value, Average Price, Unrealized P&L, Realized P&L, Liquidate Last, Security Type, Delta Dollars
//SPX APR2022 4300 P [SPXW  220429P04300000 100],2,USD,119.5072021,23901.44,123.5542635,-809.41,0.00,No,OPT,-246454.66
class IBPosition
{
    public OptionType optionType;
    public string symbol = ""; // SPX, SPXW, etc
    public int strike = 0;
    public DateOnly expiration = new();
    public int quantity;
    //public float averagePrice; // average entry price
    //public float marketPrice; // current market price
    //public float unrealizedPnL;
    //public float realizedPnL;

    // used only during reconciliation with ONE positions
    public int one_quantity = 0;
    public List<string> oneTrades = new();
}

static class Program
{
    public const string version = "0.0.1";
    public const string version_date = "2021-12-08";
    public const string ib_directory = @"C:\Users\lel48\OneDrive\Documents\IBExport\";
    public const string one_directory = @"C:\Users\lel48\OneDrive\Documents\ONEExport\";

    static string one_account = "";

    static Dictionary<string, int> ib_columns = new(); // key is column name, value is column index
    static Dictionary<string, int> one_columns = new(); // key is column name, value is column index
    static Dictionary<string, ONETrade> oneTrades = new(); // key is trade_id

    // consolidated ONE positions; key is (OptionType, Expiration, Strike); value is (quantity, List<string>); string is trade id 
    static Dictionary<(string, OptionType, DateOnly, int), (int, List<string>)> consolidatedOnePositions = new();

    // key is (symbol, OptionType, Expiration, Strike); value is quantity
    static Dictionary<(string, OptionType, DateOnly, int), IBPosition> ibPositions = new();

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
        Console.WriteLine($"\nElapsed time = {stopWatch.Elapsed}");

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
    //Financial Instrument Description, Position, Currency, Market Price, Market Value, Average Price, Unrealized P&L, Realized P&L, Liquidate Last, Security Type, Delta Dollars
    //SPX APR2022 4300 P [SPXW  220429P04300000 100],2,USD,119.5072021,23901.44,123.5542635,-809.41,0.00,No,OPT,-246454.66
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

        // check for required columns and get index of last required column
        string[] ib_required_columns = { "Financial Instrument Description", "Position", "Security Type" };
        line1 = lines[1].Trim();
        string[] column_names = line1.Split(',');
        for (int i = 0; i < column_names.Length; i++)
        {
            string xx = column_names[i];
            ib_columns.Add(column_names[i].Trim(), i);
        }

        int index_of_last_required_column = 0;
        for (int i=0; i<ib_required_columns.Length; i++)
        {
            if (!ib_columns.TryGetValue(ib_required_columns[i], out int colnum))
            {
                Console.WriteLine($"***Error*** IB file must contain column named {ib_required_columns[i]}");
                return false;
            }
            index_of_last_required_column = Math.Max(colnum, index_of_last_required_column);
        }

        // now process each IB position line
        for (int line_index = 2; line_index < lines.Length; line_index++)
        {
            bool rc = ParseCSVLine(lines[line_index], out List<string> fields);
            if (!rc)
                return false;

            // blank line terminates list of positions. Next line must be "Cash Balances"
            if (fields.Count == 0)
                break;

            if (fields.Count < index_of_last_required_column+1)
            {
                Console.WriteLine($"***Error*** IB position line #{line_index + 1} must have {index_of_last_required_column + 1} fields, not {fields.Count} fields");
                return false;
            }

            rc = ParseIBPositionLine(line_index, fields);
            if (!rc)
                return false;
        }

        return true;
    }

    //Financial Instrument Description, Position, Currency, Market Price, Market Value, Average Price, Unrealized P&L, Realized P&L, Liquidate Last, Security Type, Delta Dollars
    //SPX APR2022 4300 P [SPXW  220429P04300000 100],2,USD,119.5072021,23901.44,123.5542635,-809.41,0.00,No,OPT,-246454.66
    //SPY,100,USD,463.3319397,46333.19,463.02,31.19,0.00,No,STK,46333.19
    //MES MAR2022,1, USD,4624.50,23122.50,4625.604,-5.52,0.00, No, FUT,23136.14
    static bool ParseIBPositionLine(int line_index, List<string> fields)
    {
        IBPosition ibPosition = new();

        int quantity_col = ib_columns["Position"];
        bool rc = int.TryParse(fields[quantity_col], out ibPosition.quantity);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Position: {fields[quantity_col]}");
            return false;
        }
#if false
        rc = float.TryParse(fields[3], out ibPosition.marketPrice);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Market Price: {fields[3]}");
            return false;
        }

        rc = float.TryParse(fields[5], out ibPosition.averagePrice);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Average Price: {fields[5]}");
            return false;
        }

        rc = float.TryParse(fields[6], out ibPosition.unrealizedPnL);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Unrealized P&L: {fields[6]}");
            return false;
        }

        rc = float.TryParse(fields[7], out ibPosition.realizedPnL);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid Realized P&L: {fields[7]}");
            return false;
        }
#endif
        int description_col = ib_columns["Financial Instrument Description"];
        string description = fields[description_col];

        int security_type_col = ib_columns["Security Type"];
        string security_type = fields[security_type_col].Trim();
        switch (security_type)
        {
            case "OPT":
                rc = ParseOptionSpec(description, @".*\[(\w+) +(.+) \w+\]$", out ibPosition.symbol, out ibPosition.optionType, out ibPosition.expiration, out ibPosition.strike);
                if (!rc)
                {
                    Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: invalid option specification: {fields[description_col]}");
                    return false;
                }

                break;

            case "FUT":
                //MES      MAR2022,1,USD,4624.50,23122.50,4625.604,-5.52,0.00,No,FUT,23136.14
                ibPosition.optionType = OptionType.Futures;
                rc = ParseFuturesSpec(description, @"(\w+) +(\w+)$", out ibPosition.symbol, out ibPosition.expiration);
                break;

            case "STK":
                //SPY,100,USD,463.3319397,46333.19,463.02,31.19,0.00,No,STK,46333.19
                ibPosition.optionType = OptionType.Stock;
                ibPosition.symbol = fields[0].Trim();
                break;
        }

        var key = (ibPosition.symbol, ibPosition.optionType, ibPosition.expiration, ibPosition.strike);
        if (ibPositions.ContainsKey(key))
        {
            if (ibPosition.optionType == OptionType.Put || ibPosition.optionType == OptionType.Call) {
                Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: duplicate expiration/strike ({ibPosition.symbol} {ibPosition.optionType} {ibPosition.expiration},{ibPosition.strike})");
                return false;
            }
            else
            {
                if (ibPosition.optionType == OptionType.Futures) 
                    Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: duplicate futures entry ({ibPosition.symbol} {ibPosition.expiration})");
                else
                    Console.WriteLine($"***Error*** in #{line_index + 1} in IB file: duplicate stock entry ({ibPosition.symbol})");
                return false;
            }
        }
        ibPositions.Add((ibPosition.symbol, ibPosition.optionType, ibPosition.expiration, ibPosition.strike), ibPosition);
        return true;
    }

    //MES      MAR2022,1,USD,4624.50,23122.50,4625.604,-5.52,0.00,No,FUT,23136.14
    static bool ParseFuturesSpec(string field, string regex, out string symbol, out DateOnly expiration)
    {
        symbol = "";
        expiration = new();

        MatchCollection mc = Regex.Matches(field, regex);
        if (mc.Count > 1)
            return false;
        Match match0 = mc[0];
        if (match0.Groups.Count != 3)
            return false;
        symbol = match0.Groups[1].Value;
        string expiration_string = match0.Groups[2].Value;
        bool rc = DateOnly.TryParse(expiration_string, out expiration); // day of expiration will be incorrect (it will be 1)
        return rc;
    }

    // SPX APR2022 4300 P[SPXW  220429P04300000 100]
    static bool ParseOptionSpec(string field, string regex, out string symbol, out OptionType type, out DateOnly expiration, out int strike)
    {
        symbol = "";
        type = OptionType.Put;
        expiration = new();
        strike = 0;

        MatchCollection mc = Regex.Matches(field, regex);
        if (mc.Count > 1)
            return false;
        Match match0 = mc[0];
        if (match0.Groups.Count != 3)
            return false;

        symbol = match0.Groups[1].Value.Trim();
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
            bool rc = ParseCSVLine(line, out List<string> fields);

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

                ONEPosition? position = ParseONEPositionLine(line_index, fields, curOneTrade.trade_id);
                if (position == null)
                    return false;

                // now add option position to consolidated positions dictionary for trade; remove existing position if quantity now 0
                var key = (position.symbol, position.optionType, position.expiration, position.strike);
                if (curOneTrade.positions.ContainsKey(key))
                {
                    curOneTrade.positions[key] += position.quantity;
                    if (curOneTrade.positions[key] == 0)
                        curOneTrade.positions.Remove(key);
                }
                else
                {
                    Debug.Assert(position.quantity != 0);
                    curOneTrade.positions.Add(key, position.quantity);
                }

                // now add option position to global consolidated positions dictionary; remove existing position if quantity now 0
                //static Dictionary<(string, OptionType, DateOnly, int), (int, List<string>)> onePositions = new();
                if (consolidatedOnePositions.ContainsKey(key))
                {
                    (int quantity, List<string> trades) = consolidatedOnePositions[key];
                    int new_quantity = quantity + position.quantity;
                    if (new_quantity != 0)
                        consolidatedOnePositions[key] = (new_quantity, trades);
                    else
                        consolidatedOnePositions.Remove(key);
                }
                else
                {
                    Debug.Assert(position.quantity != 0);
                    List<string> trade_ids = new();
                    trade_ids.Add(curOneTrade.trade_id);
                    consolidatedOnePositions.Add(key, (position.quantity, trade_ids));
                }
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

    //,,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission
    //,,"IB1",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
    //,,"IB1",294,11/1/2021 12:24:57 PM,Buy,2,SPX,,Stock,SPX Stock, SPX,4609.8,0.01
    // note there is no Futures position in ONE...a Futures position is represented as Stock
    static ONEPosition? ParseONEPositionLine(int line_index, List<string> fields, string trade_id)
    {
        if (fields.Count != 14)
        {
            Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} must have 14 fields, not {fields.Count} fields");
            return null;
        }

        if (fields[2] != one_account)
        {
            Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} has account: {fields[2]} that is different from trade account: {one_account}");
            return null;
        }


        if (fields[3] != trade_id)
        {
            Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} has trade id: {fields[3]} that is different from trade id in trade line: {trade_id}");
            return null;
        }

        ONEPosition position = new();
        position.account = one_account;
        position.trade_id = trade_id;

        if (!DateTime.TryParse(fields[4], out position.open_dt))
        {
            Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} has invalid open date field: {fields[4]}");
            return null;
        }

        int quantity_sign = 0;
        if (fields[5] == "Buy")
            quantity_sign = 1;
        else if (fields[5] == "Sell")
            quantity_sign = -1;
        else
        {
            Console.WriteLine($"***Error*** ONE Position line #{line_index + 1} has invalid transaction type (must be Buy or Sell): {fields[5]}");
            return null;
        }

        if (!int.TryParse(fields[6], out position.quantity))
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid quantity field: {fields[6]}");
            return null;
        }
        position.quantity *= quantity_sign;

        if (fields[9] == "Put" || fields[9] == "Call")
        {
            bool rc = ParseOptionSpec(fields[7], @"(\w+) +(.+)$", out position.symbol, out position.optionType, out position.expiration, out position.strike);
            if (!rc)
                return null;

            // confirm by parsing Expiry field
            if (DateOnly.TryParse(fields[8], out DateOnly expiry))
            {
                if (position.expiration.CompareTo(expiry) != 0)
                {
                    if (expiry.AddDays(1) == position.expiration)
                        position.expiration = expiry;
                    else
                    {
                        Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has discrepency between date in Symbol field {position.expiration} and date in Expiry field {expiry}");
                        return null;
                    }
                }
            }
        }
        else if (fields[9] == "Stock")
        {
            position.symbol = fields[7];
            position.optionType = OptionType.Stock;
            position.expiration = new DateOnly(1, 1, 1);
            position.strike = 0;
        }
        else
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid type field (Must be Put, Call, or Stock): {fields[9]}");
            return null;
        }

        if (!float.TryParse(fields[12], out position.open_price))
        {
            Console.WriteLine($"***Error*** ONE Trade line #{line_index + 1} has invalid price field: {fields[12]}");
            return null;
        }

        return position;
    }

    static bool CompareONEPositionsToIBPositions()
    {
        // display ONE positions
        DisplayONEPositions();

        // display IB positions
        DisplayIBPositions();

        // go through each ONE position and find it's associated IB Position
        //static Dictionary<(string, OptionType, DateOnly, int), (int, List<string>)> onePositions = new();
        // ((string symbol, OptionType type, DateOnly expiration, int strike), (int quantity, List<string> trades))
        foreach (((string symbol, OptionType type, DateOnly expiration, int strike), (int one_quantity, List<string> one_trade_ids)) in consolidatedOnePositions)
        {
            if (!ibPositions.TryGetValue((symbol, type, expiration, strike), out IBPosition? ib_position))
            {
                switch (type)
                {
                    case OptionType.Stock:
                        if (one_trade_ids.Count == 1)
                            Console.WriteLine($"***Error*** ONE has a stock position in {symbol} of {one_quantity} shares, in trade {one_trade_ids[0]}, with no matching position in IB");
                        else
                        {
                            Console.WriteLine($"***Error*** ONE has a stock position in {symbol} of {one_quantity} shares, with no matching position in IB in the following ONE trades:");
                            foreach (string one_trade_id in one_trade_ids)
                            {
                                ONETrade one_trade = oneTrades[one_trade_id];
                                int missing_quantity = one_trade.positions[(symbol, OptionType.Stock, new DateOnly(1, 1, 1), 0)];
                                Console.WriteLine($"            ONE trade {one_trade_id} has {missing_quantity} shares");
                            }
                        }
                        break;

                    case OptionType.Futures:
                        if (one_trade_ids.Count == 1)
                            Console.WriteLine($"***Error*** ONE has a futures position in {symbol} {expiration} of {one_quantity} contracts, in trade {one_trade_ids[0]}, with no matching position in IB");
                        else
                        {
                            Console.WriteLine($"***Error*** ONE has a futures position in {symbol} {expiration} of {one_quantity} contracts, with no matching position in IB in the following ONE trades:");
                            foreach (string one_trade_id in one_trade_ids)
                            {
                                ONETrade one_trade = oneTrades[one_trade_id];
                                int missing_quantity = one_trade.positions[(symbol, OptionType.Futures, expiration, 0)];
                                Console.WriteLine($"            ONE trade {one_trade_id} has {missing_quantity} contracts");
                            }
                        }
                        break;

                    case OptionType.Call:
                    case OptionType.Put:
                        if (one_trade_ids.Count == 1)
                            Console.WriteLine($"***Error*** ONE has an option position in {symbol} {type} {expiration} {strike} of {one_quantity} contracts, in trade {one_trade_ids[0]}, with no matching position in IB:");
                        else
                        {
                            Console.WriteLine($"***Error*** ONE has an option position in {symbol} {type} {expiration} {strike} of {one_quantity} contracts, with no matching position in IB in the following ONE trades:");
                            foreach (string one_trade_id in one_trade_ids)
                            {
                                ONETrade one_trade = oneTrades[one_trade_id];
                                int missing_quantity = one_trade.positions[(symbol, OptionType.Futures, expiration, strike)];
                                Console.WriteLine($"            ONE trade {one_trade_id} has {missing_quantity} contracts");
                            }
                        }
                        break;
                }
                continue;
            }

            // save one position reference in ib position
            ib_position.oneTrades = one_trade_ids;

            // add one_position quantity to accounted_for_quantity...this will be checked later
            ib_position.one_quantity += one_quantity;
            //}
        }

        // now make sure each IB position has proper associated one position
        foreach (IBPosition position in ibPositions.Values)
        {
            if (position.one_quantity != position.quantity)
            {
                if (position.one_quantity == 0)
                {
                    Console.WriteLine($"***Error*** IB has a {position.optionType} position with no matching position in ONE");
                    DisplayIBPosition(position);
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine($"***Error*** IB quantity {position.quantity} does not match total ONE quantity {position.one_quantity} for IB position:");
                    DisplayIBPosition(position);
                    Console.WriteLine();
                }
            }
        }
        return true;
    }

    const char delimiter = ',';
    static bool ParseCSVLine(string line, out List<string> fields)
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
                        fields.Add(line[start..i].Trim());
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

    //static Dictionary<(string, OptionType, DateOnly, int), (int, List<string>)> consolidatedOnePositions = new();
    static void DisplayONEPositions()
    {
        Console.WriteLine("\nONE Positions:");
        foreach (((string symbol, OptionType optionType, DateOnly expiration, int strike), (int quantity, List<string> trades)) in consolidatedOnePositions)
        {
            switch (optionType)
            {
                case OptionType.Stock:
                    Console.WriteLine($"{symbol} {optionType}: quantity = {quantity}");
                    break;
                case OptionType.Call:
                case OptionType.Put:
                    Console.WriteLine($"{symbol} {optionType}: expiration = {expiration}, strike = {strike}, quantity = {quantity}");
                    break;
            }
        }
        Console.WriteLine();
    }

    static void DisplayIBPositions()
    {
        Console.WriteLine("\nIB Positions:");
        foreach (IBPosition position in ibPositions.Values)
            DisplayIBPosition(position);
        Console.WriteLine();
    }

    static void DisplayIBPosition(IBPosition position)
    {
        switch (position.optionType)
        {
            case OptionType.Stock:
                Console.WriteLine($"{position.symbol} {position.optionType}: quantity = {position.quantity}");
                break;
            case OptionType.Futures:
                Console.WriteLine($"{position.symbol} {position.optionType}: expiration = {position.expiration}, quantity = {position.quantity}");
                break;
            case OptionType.Call:
            case OptionType.Put:
                Console.WriteLine($"{position.symbol} {position.optionType}: expiration = {position.expiration}, strike = {position.strike}, quantity = {position.quantity}");
                break;
        }
    }
}
