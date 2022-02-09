using System.Diagnostics;
using System.Text.RegularExpressions;
using CompareONEToIB;

namespace CompareOneToIB;

// todo: rename OptionType to SecurityType
enum SecurityType
{
    Put,
    Call,
    Stock,
    Futures,
}

enum TradeStatus
{
    Open,
    Closed
}

// this is a subclass of IComparable so we can properly sort/compare the entries in various Dictionaries
class Position : IComparable<Position>
{
    internal readonly bool IsONEPosition; // yes, I could have used sub-classes...I wanted to keep things simple
    internal string Account = "";
    internal string TradeId = ""; // only set for ONE Positions
    internal string Symbol = "";
    internal SecurityType Type;
    internal DateOnly Expiration;
    internal int Strike = 0;
    internal int Quantity = 0;

    // only used for consolidateONEPositions: id's of ONE Trades that contribute to this consolidated position
    internal HashSet<string> TradeIds = new();

    // only used for broker (IB, TDA, TastyWorks) Positions during reconciliation with ONE positions
    internal int one_quantity = 0;

    internal Position(bool isONEPosition)
    {
        IsONEPosition = isONEPosition;
    }

    // copy constructor
    internal Position(Position other)
    {
        IsONEPosition = other.IsONEPosition;
        Account = other.Account;
        TradeId = other.TradeId; // only set for ONE Positions
        Symbol = other.Symbol;
        Type = other.Type;
        Expiration = other.Expiration;
        Strike = other.Strike;
        Quantity = other.Quantity;
    }

    public int CompareTo(Position? other)
    {
        Debug.Assert(other != null);
        if (other == null)
            return 1;

        bool thisIsOption = Type == SecurityType.Put || Type == SecurityType.Call;
        bool otherIsOption = other.Type == SecurityType.Put || other.Type == SecurityType.Call;
        if (!thisIsOption)
        {
            // this is stock/future

            if (otherIsOption)
                return -1; // this is stock/future, other is option: stocks/futures come before options

            // this and other are both Stocks/Futures: stocks come before futures, then symbol, then, if future, expiration

            if (Type == SecurityType.Stock)
            {
                if (other.Type == SecurityType.Futures)
                    return -1; // stocks come before futures

                // this and other are both stocks...sort by symbol
                return Symbol.CompareTo(other.Symbol);
            }

            // this is futures

            if (other.Type == SecurityType.Stock)
                return 1; // this is futures, other is stock: futures come after stocks

            // this and other are both futures..sort by symbol then expiration
            if (Symbol != other.Symbol)
                return Symbol.CompareTo(other.Symbol);

            return Expiration.CompareTo(other.Expiration);
        }

        // this is an option

        if (!otherIsOption)
            return 1; // other is stock/future; stocks/futures come before options

        // this and other are both options; sort by expiration, then strike, then symbol (like SPX, SPXW), finally type (put/Call)
        if (other.Expiration != this.Expiration)
            return Expiration.CompareTo(other.Expiration);
        else if (other.Strike != Strike)
            return Strike.CompareTo(other.Strike);
        else if (other.Symbol != Symbol)
            return other.Symbol.CompareTo(Symbol);
        else // this 
            return Type.CompareTo(other.Type);
    }

    public override int GetHashCode()
    {
        return Symbol.GetHashCode() ^ Type.GetHashCode() ^ Expiration.GetHashCode() ^ Strike.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj is Position other)
        {
            return other != null && Symbol == other.Symbol && Type == other.Type && Expiration == other.Expiration && Strike == other.Strike;
        }
        return false;
    }
}

//,Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc
//,"IB1",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
class ONETrade
{
    internal string Account = "";
    internal DateOnly Expiration;
    internal string TradeId = "";
    internal string TradeName = "";
    internal TradeStatus Status;
    internal DateTime OpenDt;
    internal DateTime CloseDt;
    internal int Dte;
    internal int Dit;

    // these are consolidated positions for trade: key is (symbol, OptionType, Expiration, Strike)
    // so Dictionary contains no keys with quantity == 0
    internal HashSet<Position> Positions = new();
}

static class Program
{
    internal const string version = "0.0.6";
    internal const string version_date = "2022-02-09";

    internal static string? ib_filename = null;
    internal static string ib_directory = @"C:\Users\lel48\OneDrive\Documents\IBExport\";
    internal static string? one_filename = null;
    internal static string one_directory = @"C:\Users\lel48\OneDrive\Documents\ONEExport\";
    internal static DateOnly ib_filedate;
    internal static DateOnly one_filedate;

    internal static string master_symbol = "SPX";
    internal static string one_account = "";
    internal static int index_of_last_required_column = -1;
    internal static int ib_description_col = -1;
    internal static int ib_quantity_col = -1;
    internal static int security_type_col = -1;

    // ONE uses the main index symbol for positions in the underlying, wheras IB uses an actual stock/futures symbol
    // so...to reconcile these, if ONE has a position, say, of 10 SPX, this could be equivalent to 2 MES contracts,
    // 100 SPY shares, or some combination, like 1 MES contract and 50 SPY shares
    // So...the float is the number of ONE SPX shares that a share of the given item represents. So, { "SPY", 0.1f }
    // means that 1 share of SPY in IB represents 0.1 shares of SPX in ONE
    internal static Dictionary<string, Dictionary<string, float>> associated_symbols = new()
    {
        { "SPX", new Dictionary<string, float> { { "SPY", 0.1f }, { "MES", 5f }, { "ES", 50f } } },
        { "RUT", new Dictionary<string, float> { { "IWM", 0.1f }, { "M2K", 5f }, { "RTY", 50f } } },
        { "NDX", new Dictionary<string, float> { { "QQQ", 0.1f }, { "MNQ", 5f }, { "NQ", 50f } } }
    };
    internal static Dictionary<string, float> relevant_symbols = new(); // set to: associated_symbols[master_symbol];

    // note: the ref is readonly, not the contents of the Dictionary
    static readonly Dictionary<string, int> ib_columns = new(); // key is column name, value is column index
    static readonly Dictionary<string, int> one_trade_columns = new(); // key is column name, value is column index
    static readonly Dictionary<string, int> one_position_columns = new(); // key is column name, value is column index
    static readonly Dictionary<string, ONETrade> oneTrades = new(); // key is trade_id
    static readonly HashSet<Position> alreadyExpiredONEPositions = new();

    // key is (symbol, OptionType, Expiration, Strike);
    static readonly SortedSet<Position> ibPositions = new();

    // these positions are not relevant to specified master_symbol, but we want to display them so user can verify
    static readonly HashSet<Position> irrelevantIBPositions = new();

    // dictionary of ONE trades with key of trade id
    static readonly SortedDictionary<string, ONETrade> ONE_trades = new();

    // consolidated ONE positions; key is (symbol, OptionType, Expiration, Strike)
    //static readonly SortedDictionary<Position, ConsolidatedQuantity> consolidatedONEPositions = new();
    static readonly SortedSet<Position> consolidatedONEPositions = new();

    static int Main(string[] args)
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();

        // calls System.Environment.Exit(-1) if bad command line arguments
        CommandLine.ProcessCommandLineArguments(args); // might set one_filename, ib_filename
        master_symbol = master_symbol.ToUpper();
        relevant_symbols = associated_symbols[master_symbol];
        Console.WriteLine($"CompareONEToIB Version {version}, {version_date}. Processing trades for {master_symbol}");

        (one_filename, one_filedate) = GetONEFileName(one_directory, one_filename); // parses one_filename from command line if it is not null to get date
        (ib_filename, ib_filedate) = GetIBFileName(ib_directory, ib_filename); // parses ib_filename from command line if it is not null to get date
        if (one_filename == null || ib_filename == null)
            return -1;

        Console.WriteLine("\nProcessing ONE file: " + one_filename);
        Console.WriteLine("Processing IB file: " + ib_filename);

        bool rc = ProcessONEFile(one_filename);
        if (!rc)
            return -1;

        DisplayedIgnoredONEPositions(); // ignored because they expired prior to date in one filename

        DisplayONEPositions();

        rc = ProcessIBFile(ib_filename);
        if (!rc)
            return -1;

        DisplayIBPositions();

        DisplayIrrelevantIBPositions();

        rc = CompareONEPositionsToIBPositions();
        if (!rc)
            return -1;

        Console.WriteLine($"\nSuccess: IB and ONE positions for {master_symbol} are equivalent.");

        stopWatch.Stop();
        Console.WriteLine($"\nElapsed time = {stopWatch.Elapsed}");

        return 0;
    }

    // tries to set one_filename from files in one_directory, if it is null on entry
    // parses date from filename and returns it in parameter filedate
    // returns valid full filename or null if invalid filename specified or no valid filename found
    static (string?, DateOnly) GetONEFileName(string directory, string? specified_full_filename)
    {
        const string ending = "-ONEDetailReport.csv";
        string[] files;
        DateOnly latestDate = new(1000, 1, 1);
        string latest_full_filename = "";
        string filename, datestr;

        if (specified_full_filename == null)
        {
            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"\n***Error*** Specified ONE directory {directory} does not exist");
                return (null, latestDate);
            }

            bool file_found = false;
            files = Directory.GetFiles(directory, '*' + ending, SearchOption.TopDirectoryOnly);
            foreach (string full_filename in files)
            {
                filename = Path.GetFileName(full_filename); // this is filename portion of full filename
                datestr = filename[..^ending.Length];
                if (DateOnly.TryParse(datestr, out DateOnly dt))
                {
                    file_found = true;
                    if (dt > latestDate)
                    {
                        latestDate = dt;
                        latest_full_filename = full_filename;
                    }
                }
            }

            if (!file_found)
            {
                Console.WriteLine($"\n***Error*** No ONE files found in {one_directory} with following filename pattern: yyyy-mm-dd-ONEDetailReport.csv");
                return (null, latestDate);
            }

            return (latest_full_filename, latestDate);
        }

        if (!File.Exists(specified_full_filename))
        {
            Console.WriteLine($"\n***Error*** Specified ONE file {specified_full_filename} does not exist");
            return (null, latestDate);
        }

        filename = Path.GetFileName(specified_full_filename); // this is filename portion of full filename
        datestr = filename[..^ending.Length];
        if (!DateOnly.TryParse(datestr, out latestDate))
        {
            Console.WriteLine($"\n***Error*** Specified ONE file does not match following pattern: yyyy-mm-dd-ONEDetailReport.csv");
            return (null, latestDate);
        }

        return (specified_full_filename, latestDate);
    }

    static (string?, DateOnly) GetIBFileName(string directory, string? specified_full_filename)
    {
        const string filename_pattern = "*.csv"; // file names look like: portfolio.20211208.csv
        const string portfolio_prefix = "portfolio."; // file names look like: portfolio.20211208.csv
        const string filtered_portfolio_prefix = "filtered_portfolio."; // file names look like: portfolio.20211208.csv
        int filename_prefix1_len = portfolio_prefix.Length;
        int filename_prefix2_len = filtered_portfolio_prefix.Length;
        bool latest_full_filename_is_filtered_portfolio = false;

        string[] files;
        string filename, datestr;
        DateOnly latestDate = new(1000, 1, 1);
        string latest_full_filename = "";

        if (specified_full_filename == null)
        {
            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"\n***Error*** Specified IB directory {directory} does not exist");
                return (null, latestDate);
            }

            files = Directory.GetFiles(ib_directory, filename_pattern, SearchOption.TopDirectoryOnly);
            bool file_found = false;
            foreach (string full_filename in files)
            {
                filename = Path.GetFileName(full_filename);
                if (filename.StartsWith(portfolio_prefix))
                    datestr = filename[filename_prefix1_len..];
                else if (filename.StartsWith(filtered_portfolio_prefix))
                    datestr = filename[filename_prefix2_len..];
                else
                    continue;

                if (datestr.Length != 12) // yyyymmdd.csv
                    continue;
                if (!int.TryParse(datestr[..4], out int year))
                    continue;
                if (!int.TryParse(datestr.AsSpan(4, 2), out int month))
                    continue;
                if (!int.TryParse(datestr.AsSpan(6, 2), out int day))
                    continue;

                file_found = true;
                DateOnly dt = new(year, month, day);
                if (dt > latestDate)
                {
                    latestDate = dt;
                    latest_full_filename = full_filename;
                    latest_full_filename_is_filtered_portfolio = filename.StartsWith(filtered_portfolio_prefix);
                }
                else if (dt == latestDate)
                {
                    // same dates in filenames...must be one file starts with "filtered_portfolio" and the other with just "portfolio"
                    Debug.Assert((latest_full_filename_is_filtered_portfolio && filename.StartsWith(portfolio_prefix)) || (!latest_full_filename_is_filtered_portfolio && filename.StartsWith(filtered_portfolio_prefix)));

                    // choose the one with the latest timestamp
                    DateTime cur_filename_write_date = File.GetLastWriteTime(full_filename);
                    DateTime saved_filename_write_date = File.GetLastWriteTime(latest_full_filename);
                    if (cur_filename_write_date >= saved_filename_write_date)
                    {
                        latestDate = dt;
                        latest_full_filename = full_filename;
                        latest_full_filename_is_filtered_portfolio = filename.StartsWith(filtered_portfolio_prefix);
                    }
                }
            }

            if (!file_found)
            {
                Console.WriteLine($"\n***Error*** No IB files found in {ib_directory} with following filename pattern: [filtered_]portfolio.yyyymmdd.csv");
                return (null, latestDate);
            }

            return (latest_full_filename, latestDate);
        }

        if (!File.Exists(specified_full_filename))
        {
            Console.WriteLine($"\n***Error*** Specified ONE file {specified_full_filename} does not exist");
            return (null, latestDate);
        }

        filename = Path.GetFileName(specified_full_filename); // this is filename portion of full filename
        if (filename.StartsWith(portfolio_prefix))
            datestr = filename[filename_prefix1_len..];
        else if (filename.StartsWith(filtered_portfolio_prefix))
            datestr = filename[filename_prefix2_len..];
        else
        {
            Console.WriteLine($"\n***Error*** Specified IB file does not match following pattern: yyyy-mm-dd-ONEDetailReport.csv");
            return (null, latestDate);
        }

        if (!DateOnly.TryParse(datestr, out latestDate))
        {
            Console.WriteLine($"\n***Error*** Specified ONE file does not match following pattern: [filtered_]portfolio.yyyymmdd.csv");
            return (null, latestDate);
        }

        return (specified_full_filename, latestDate);

    }

    //Portfolio
    //Financial Instrument Description, Position, Currency, Market Price, Market Value, Average Price, Unrealized P&L, Realized P&L, Liquidate Last, Security Type, Delta Dollars
    //SPX APR2022 4300 P [SPXW  220429P04300000 100],2,USD,119.5072021,23901.44,123.5542635,-809.41,0.00,No,OPT,-246454.66
    static bool ProcessIBFile(string full_filename)
    {
        string[] lines = File.ReadAllLines(full_filename);
        if (lines.Length < 3)
        {
            Console.WriteLine("\n***Error*** IB File must contain at least 3 lines");
            return false;
        }

        string line1 = lines[0].Trim();
        if (line1 != "Portfolio")
        {
            Console.WriteLine("***\nError*** First line of IB file must be 'Portfolio'");
            return false;
        }

        // check for required columns and get index of last required column
        string[] required_columns = { "Financial Instrument Description", "Position", "Security Type" };
        line1 = lines[1].Trim();
        string[] column_names = line1.Split(',');
        for (int i = 0; i < column_names.Length; i++)
        {
            string column_name = column_names[i].Trim();
            if (column_name.Length > 0)
                ib_columns.Add(column_name, i);
        }
        int index_of_last_required_column = 0;
        for (int i = 0; i < required_columns.Length; i++)
        {
            if (!ib_columns.TryGetValue(required_columns[i], out int colnum))
            {
                Console.WriteLine($"\n***Error*** IB file header must contain column named {required_columns[i]}");
                return false;
            }
            index_of_last_required_column = Math.Max(colnum, index_of_last_required_column);
        }
        ib_description_col = ib_columns["Financial Instrument Description"];
        ib_quantity_col = ib_columns["Position"];
        security_type_col = ib_columns["Security Type"];

        // now process each IB position line
        for (int line_index = 2; line_index < lines.Length; line_index++)
        {
            string line = lines[line_index].Trim();

            // blank line terminates list of positions. Next line must be "Cash Balances"
            if (line.Length == 0)
                break;

            bool rc = ParseCSVLine(line, out List<string> fields);
            if (!rc)
                return false;
            Debug.Assert(fields.Count > 0);

            if (fields.Count < index_of_last_required_column + 1)
            {
                Console.WriteLine($"\n***Error*** IB position line {line_index + 1} must have {index_of_last_required_column + 1} fields, not {fields.Count} fields");
                return false;
            }

            int irc = ParseIBPositionLine(line_index, fields); // adds positions to ibPositions
            if (irc != 0)
            {
                // if irc == -1, error parsing line, irc == +1, irrelevant symbol - ignore line
                if (irc < 0)
                    return false;
            }
        }

        if (ibPositions.Count == 0)
        {
            Console.WriteLine($"\n***Error*** No positions related to {master_symbol} in IB file {full_filename}");
            return false;
        }

        return true;
    }

    //Financial Instrument Description, Position, Currency, Market Price, Market Value, Average Price, Unrealized P&L, Realized P&L, Liquidate Last, Security Type, Delta Dollars
    //SPX APR2022 4300 P [SPXW  220429P04300000 100],2,USD,119.5072021,23901.44,123.5542635,-809.41,0.00,No,OPT,-246454.66
    //SPY,100,USD,463.3319397,46333.19,463.02,31.19,0.00,No,STK,46333.19
    //MES MAR2022,1, USD,4624.50,23122.50,4625.604,-5.52,0.00, No, FUT,23136.14

    // returns 0 if line was parsed successfully, -1 if there was an error, 1 if line parsed ok, but is for symbol not relevant to this analysis
    static int ParseIBPositionLine(int line_index, List<string> fields)
    {

        bool rc = int.TryParse(fields[ib_quantity_col], out int quantity);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in IB line {line_index + 1}: invalid Quantity: {fields[ib_quantity_col]}");
            return -1;
        }
        if (quantity == 0)
            return 1;

        var ibPosition = new Position(isONEPosition: false)
        {
            Quantity = quantity
        };

        string description = fields[ib_description_col];
        int security_type_col = ib_columns["Security Type"];
        string security_type_str = fields[security_type_col].Trim();
        bool irrelevant_position = false;
        switch (security_type_str)
        {//
            case "OPT":
                //SPX    APR2022 4025 P [SPXW  220429P04025000 100],-4,USD,48.6488838,-19459.55,74.0574865,10163.44,0.00,No,OPT,235456.06
                //rc = ParseOptionSpec(description, @".*\[(\w+) +(.+) \w+\]$", out ibPosition.symbol, out ibPosition.securityType, out ibPosition.expiration, out ibPosition.strike);
                rc = ParseOptionSpec(description, @".*\[(\w+) +(.+) \w+\]$", ibPosition);
                if (!rc)
                {
                    Console.WriteLine($"***Error*** in IB line {line_index + 1}: invalid option specification: {fields[ib_description_col]}");
                    return -1;
                }
                if (!description.StartsWith(master_symbol))
                    irrelevant_position = true; // This IB position is not relevant to this compare. Add to irrelevantIBPositions collection
                break;

            case "FUT":
                //MES      MAR2022,1,USD,4624.50,23122.50,4625.604,-5.52,0.00,No,FUT,23136.14
                ibPosition.Type = SecurityType.Futures;
                rc = ParseFuturesSpec(description, @"(\w+) +(\w+)$", ibPosition);
                if (!rc)
                    return -1;
                if (!relevant_symbols.ContainsKey(ibPosition.Symbol))
                    irrelevant_position = true; // This IB position is not relevant to this compare. Add to irrelevantIBPositions collection
                break;

            case "STK":
                //SPY,100,USD,463.3319397,46333.19,463.02,31.19,0.00,No,STK,46333.19
                ibPosition.Type = SecurityType.Stock;
                ibPosition.Symbol = fields[0].Trim();
                if (!relevant_symbols.ContainsKey(ibPosition.Symbol))
                    irrelevant_position = true; // This IB position is not relevant to this compare. Add to irrelevantIBPositions collection
                break;
        }

        if (irrelevant_position && !irrelevantIBPositions.Contains(ibPosition))
        {
            irrelevantIBPositions.Add(ibPosition);
            return 1;
        }

        if (ibPositions.Contains(ibPosition))
        {
            if (ibPosition.Type == SecurityType.Put || ibPosition.Type == SecurityType.Call)
            {
                Console.WriteLine($"***Error*** in IB line {line_index + 1}: duplicate expiration/strike ({ibPosition.Symbol} {ibPosition.Type} {ibPosition.Expiration},{ibPosition.Strike})");
                return -1;
            }
            else
            {
                if (ibPosition.Type == SecurityType.Futures)
                    Console.WriteLine($"***Error*** in IB line {line_index + 1}: duplicate futures entry ({ibPosition.Symbol} {ibPosition.Expiration})");
                else
                    Console.WriteLine($"***Error*** in IB line {line_index + 1}: duplicate stock entry ({ibPosition.Symbol})");
                return -1;
            }
        }

        Debug.Assert(ibPosition.Symbol != "");
        ibPositions.Add(ibPosition);
        return 0;
    }

    //MES      MAR2022,1,USD,4624.50,23122.50,4625.604,-5.52,0.00,No,FUT,23136.14
    static bool ParseFuturesSpec(string field, string regex, Position ibPosition)
    {
        MatchCollection mc = Regex.Matches(field, regex);
        if (mc.Count > 1)
            return false;
        Match match0 = mc[0];
        if (match0.Groups.Count != 3)
            return false;
        ibPosition.Symbol = match0.Groups[1].Value;
        string expiration_string = match0.Groups[2].Value;
        bool rc = DateOnly.TryParse(expiration_string, out DateOnly expiration); // day of expiration will be incorrect (it will be 1)
        if (rc)
            ibPosition.Expiration = expiration;
        return rc;
    }

    // SPX APR2022 4300 P[SPXW  220429P04300000 100]
    static bool ParseOptionSpec(string field, string regex, Position position)
    {
        position.Symbol = "";
        position.Type = SecurityType.Put;
        position.Expiration = new();
        position.Strike = 0;

        MatchCollection mc = Regex.Matches(field, regex);
        if (mc.Count < 1)
            return false;
        Match match0 = mc[0];
        if (match0.Groups.Count != 3)
            return false;

        position.Symbol = match0.Groups[1].Value.Trim();
        if (!position.Symbol.StartsWith(master_symbol))
            return false;
        string option_code = match0.Groups[2].Value;
        int year = int.Parse(option_code[0..2]) + 2000;
        int month = int.Parse(option_code[2..4]);
        int day = int.Parse(option_code[4..6]);
        position.Expiration = new(year, month, day);
        position.Type = (option_code[6] == 'P') ? SecurityType.Put : SecurityType.Call;
        position.Strike = int.Parse(option_code[7..12]);
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
    //,,"IB1",296,11/12/2021 11:02:02 AM,Buy,1,SPX,,Stock,SPX Stock, SPX,4660.05,0.005
    static bool ProcessONEFile(string full_filename)
    {
        string[] lines = File.ReadAllLines(full_filename);
        if (lines.Length < 9)
        {
            Console.WriteLine("\n***Error*** ONE File must contain at least 9 lines");
            return false;
        }

        string? line1 = lines[0].Trim();
        if (line1 != "ONE Detail Report")
        {
            Console.WriteLine($"\n***Error*** First line of ONE file must be 'ONE Detail Report', not: {line1}");
            return false;
        }

        line1 = lines[1].Trim();
        if (line1.Length != 0)
        {
            Console.WriteLine($"\n***Error*** Second line of ONE must be blank, not: {line1}");
            return false;
        }

        line1 = lines[2].Trim();
        if (!line1.StartsWith("Date/Time:"))
        {
            Console.WriteLine($"\n***Error*** Third line of ONE file must start with 'Date/Time:', not: {line1}");
            return false;
        }

        line1 = lines[3].Trim();
        if (!line1.StartsWith("Filter: [Account]"))
        {
            Console.WriteLine($"\n***Error*** Fourth line of ONE file must start with 'Filter: [Account]', not: {line1}");
            return false;
        }

        line1 = lines[4].Trim();
        if (!line1.StartsWith("Grouping: Account"))
        {
            Console.WriteLine($"\n***Error*** Fifth line of ONE file must start with 'Grouping: Account', not: {line1}");
            return false;
        }

        line1 = lines[5].Trim();
        if (line1.Length != 0)
        {
            Console.WriteLine($"\n***Error*** Sixth line of ONE must be blank, not: {line1}");
            return false;
        }

        // check for required trade line columns (in trade header) and get index of last required trade line column
        string[] trade_required_columns = { "Account", "Expiration", "TradeId", "Underlying", "Status", "OpenDate", "CloseDate", "DaysToExpiration", "DaysInTrade" };
        line1 = lines[6].Trim();
        string[] trade_column_names = line1.Split(',');
        for (int i = 0; i < trade_column_names.Length; i++)
        {
            string column_name = trade_column_names[i].Trim();
            if (column_name.Length > 0)
                one_trade_columns.Add(column_name, i);
        }
        int index_of_last_required_trade_column = 0;
        for (int i = 0; i < trade_required_columns.Length; i++)
        {
            if (!one_trade_columns.TryGetValue(trade_required_columns[i], out int colnum))
            {
                Console.WriteLine($"\n***Error*** ONE trade line header must contain column named {trade_required_columns[i]}");
                return false;
            }
            index_of_last_required_trade_column = Math.Max(colnum, index_of_last_required_trade_column);
        }

        // check for required position line columns (in position header) and get index of last required position line column
        string[] position_required_columns = { "Account", "TradeId", "Date", "Transaction", "Qty", "Symbol", "Expiry", "Type", "Description", "Underlying" };
        line1 = lines[7].Trim();
        string[] position_column_names = line1.Split(',');
        for (int i = 0; i < position_column_names.Length; i++)
        {
            string column_name = position_column_names[i].Trim();
            if (column_name.Length > 0)
                one_position_columns.Add(column_name, i);
        }
        int index_of_last_required_position_column = 0;
        for (int i = 0; i < position_required_columns.Length; i++)
        {
            if (!one_position_columns.TryGetValue(position_required_columns[i], out int colnum))
            {
                Console.WriteLine($"\n***Error*** ONE position line header must contain column named {position_required_columns[i]}");
                return false;
            }
            index_of_last_required_position_column = Math.Max(colnum, index_of_last_required_position_column);
        }

        // account appears here in line 9, in the Trade lines, and in the Position lines. They must all match, but one_account is set here
        one_account = lines[8].Trim();
        if (one_account.Length == 0)
        {
            Console.WriteLine($"\n***Error*** Ninth line of ONE must be ONE account name, not blank");
            return false;
        }

        // parse Trade and Position lines
        ONETrade? curOneTrade = null;
        bool skip_current_trade = false;
        for (int line_index = 9; line_index < lines.Length; line_index++)
        {
            string line = lines[line_index].Trim();

            // trades (except for the first one) are separated by blanks
            if (line.Length == 0)
            {
                curOneTrade = null;
                skip_current_trade = false;
                continue;
            }

            if (skip_current_trade)
                continue;

            bool rc = ParseCSVLine(line, out List<string> fields);
            if (!rc)
                return false;
            // fields[0] must be blank; but, I don't check here yet

            string account1 = fields[1].Trim();
            if (account1.Length != 0)
            {
                // this is trade line
                if (curOneTrade != null)
                {
                    // do whatever final stuff we need to do when we've parsed all position lines for trade
                }

                if (fields.Count < index_of_last_required_trade_column + 1)
                {
                    Console.WriteLine($"\n***Error*** ONE trade line {line_index + 1} must have at least {index_of_last_required_trade_column + 1} fields, not {fields.Count} fields");
                    return false;
                }

                // if this set of positions is not for symbol (master_symbol) we are analyzing...ignore trade (all lines until we encounter blank line) 
                if (master_symbol != fields[one_trade_columns["Underlying"]])
                {
                    skip_current_trade = true;
                    continue;
                }

                // skip trades whose trade name starts with a minus ('-')
                string tradeName = fields[one_trade_columns["TradeName"]];
                if (tradeName.StartsWith('-'))
                {
                    skip_current_trade = true;
                    continue;
                }

                // start new trade - save it in trades Dictionary
                curOneTrade = ParseONETradeLine(line_index, fields);
                if (curOneTrade == null) 
                    return false;

                ONE_trades.Add(curOneTrade.TradeId, curOneTrade);
                continue;
            }

            // this is position line
            if (curOneTrade == null)
            {
                Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} comes before Trade line.");
                return false;
            }

            if (fields.Count < index_of_last_required_position_column + 1)
            {
                Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} must have at least {index_of_last_required_position_column + 1} fields, not {fields.Count} fields");
                return false;
            }

            Position? position = ParseONEPositionLine(line_index, fields, curOneTrade.TradeId);
            if (position == null)
                return false;
            Debug.Assert(position.Type != SecurityType.Futures);
            Debug.Assert(position.Quantity != 0);

            // within trade, we consolidate individual trades to obtain an overall current position
            bool exists = curOneTrade.Positions.TryGetValue(position, out Position? existing_position);
            if (existing_position != null)
            {
                Debug.Assert(exists);
                existing_position.Quantity += position.Quantity;

                // remove position if it now has 0 quantity
                if (existing_position.Quantity == 0)
                    curOneTrade.Positions.Remove(existing_position);
            }
            else
                curOneTrade.Positions.Add(position);
        }

        if (ONE_trades.Count == 0)
        {
            Console.WriteLine($"\n***Error*** No trades in ONE file {full_filename}");
            return false;
        }

        DisplayONETrades();

        RemoveClosedONETrades();

        CreateConsolidateOnePositions();

        return true;
    }

    // go through each ONE trade in ONE_trades and create a set of unique consolidated ONE positions
    // in other words, two different trades could both have a Put with the same expiration and strike...
    // we just want to know the total quantity of this put across all trades
    // right now, this could create ONE positions with 0 quantity if trades "step on strikes"
    static void CreateConsolidateOnePositions()
    {
        foreach (ONETrade oneTrade in ONE_trades.Values)
        {
            foreach (Position onePosition in oneTrade.Positions)
            {
                Debug.Assert(onePosition.Quantity != 0);
                consolidatedONEPositions.TryGetValue(onePosition, out Position? consolidatedPosition);
                if (consolidatedPosition != null)
                {
                    consolidatedPosition.Quantity += onePosition.Quantity; // result could be 0 quantity
                    consolidatedPosition.TradeIds.Add(oneTrade.TradeId);
                }
                else
                {
                    consolidatedPosition = new(onePosition);
                    consolidatedPosition.TradeIds.Add(oneTrade.TradeId);
                    consolidatedONEPositions.Add(consolidatedPosition);
                }
            }
        }

        // now go through consolidated positions and remove any positions that expired prior to date in ONE filename
        // ignore any ONE option positions that expire prior to date in ONE filename
        foreach (Position onePosition in consolidatedONEPositions)
        {
            if (onePosition.Type != SecurityType.Stock && onePosition.Expiration < one_filedate)
                alreadyExpiredONEPositions.Add(onePosition);
        }
        foreach (Position onePosition in alreadyExpiredONEPositions)
            consolidatedONEPositions.Remove(onePosition);
    }

    static void DisplayONETrades()
    {
        Console.WriteLine("\nONE Trades:");
        foreach (ONETrade one_trade in ONE_trades.Values)
        {
            if (one_trade.Status == TradeStatus.Closed)
            {
                if (one_trade.Positions.Count != 0)
                {
                    Console.WriteLine($"\n***Error*** Trade {one_trade.TradeId} is closed, but contains positions:");
                }
                else
                {
                    Console.WriteLine($"\nTrade {one_trade.TradeId}: Closed. No positions");
                    continue;
                }
            }
            else
                Console.WriteLine($"\nTrade {one_trade.TradeId}:");

            if (one_trade.Positions.Count == 0)
            {
                Debug.Assert(one_trade.Status == TradeStatus.Open);
                Console.WriteLine($"***Error*** Trade Open but no net positions.");
                continue;
            }

            foreach (Position position in one_trade.Positions)
                DisplayONEPosition(position);
        }
    }
    
    // ignored because they expired prior to date in one filename
    static void DisplayedIgnoredONEPositions()
    {
        if (alreadyExpiredONEPositions.Count > 0)
        {
            Console.WriteLine("\n***Warning*** the following position(s) in ONE have already expired (based on the date in the ONE filename):");
            foreach (Position position in alreadyExpiredONEPositions)
                DisplayONEPosition(position);
        }
        Console.WriteLine();
    }

    // remove closed trades from ONE_trades
    static void RemoveClosedONETrades()
    {
        List<string> closedTradeIds = new();
        foreach (ONETrade one_trade in ONE_trades.Values)
        {
            if (one_trade.Status == TradeStatus.Closed)
                closedTradeIds.Add(one_trade.TradeId);
        }
        foreach (string id in closedTradeIds)
            ONE_trades.Remove(id);
    }

    // ",Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc"
    //,"IB1",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
    // we don't parse Margin,Comms,PnL,PnLperc
    static ONETrade? ParseONETradeLine(int line_index, List<string> fields)
    {
        Debug.Assert(master_symbol == fields[one_trade_columns["Underlying"]]);

        ONETrade oneTrade = new();

        oneTrade.Account = fields[one_trade_columns["Account"]];
        if (one_account != oneTrade.Account)
        {
            Console.WriteLine($"\n***Error*** In ONE trade line {line_index + 1}, account field: {oneTrade.Account} is not the same as line 9 of file: {one_account}");
            return null;
        }

        int field_index = one_trade_columns["Expiration"];
        if (!DateOnly.TryParse(fields[field_index], out oneTrade.Expiration))
        {
            Console.WriteLine($"\n***Error*** ONE trade line {line_index + 1} has invalid date field: {fields[field_index]}");
            return null;
        }

        oneTrade.TradeId = fields[one_trade_columns["TradeId"]];
        if (oneTrade.TradeId.Length == 0)
        {
            Console.WriteLine($"\n***Error*** ONE trade line {line_index + 1} has empty trade id field");
            return null;
        }

        oneTrade.TradeName = fields[one_trade_columns["TradeName"]];

        string status = fields[one_trade_columns["Status"]];
        if (status == "Open")
            oneTrade.Status = TradeStatus.Open;
        else if (status == "Closed")
            oneTrade.Status = TradeStatus.Closed;
        else
        {
            Console.WriteLine($"\n***Error*** ONE trade line {line_index + 1} has invalid trade status field: {status}");
            return null;
        }

        string open_dt = fields[one_trade_columns["OpenDate"]];
        if (!DateTime.TryParse(open_dt, out oneTrade.OpenDt))
        {
            Console.WriteLine($"\n***Error*** ONE trade line {line_index + 1} has invalid date field: {open_dt}");
            return null;
        }

        if (oneTrade.Status == TradeStatus.Closed)
        {
            string close_dt = fields[one_trade_columns["CloseDate"]];
            if (!DateTime.TryParse(close_dt, out oneTrade.CloseDt))
            {
                Console.WriteLine($"\n***Error*** ONE trade line {line_index + 1} has invalid date field: {close_dt}");
                return null;
            }
        }

        string dte = fields[one_trade_columns["DaysToExpiration"]];
        if (!int.TryParse(dte, out oneTrade.Dte))
        {
            Console.WriteLine($"\n***Error*** ONE trade line {line_index + 1} has invalid dte field: {dte}");
            return null;
        }

        string dit = fields[one_trade_columns["DaysInTrade"]];
        if (!int.TryParse(dit, out oneTrade.Dit))
        {
            Console.WriteLine($"\n***Error*** ONE trade line {line_index + 1} has invalid dit field: {dit}");
            return null;
        }

        if (oneTrades.ContainsKey(oneTrade.TradeId))
        {
            Console.WriteLine($"\n***Error*** in ONE trade line {line_index + 1} in ONE file: duplicate trade id: {oneTrade.TradeId}");
            return null;
        }
        oneTrades.Add(oneTrade.TradeId, oneTrade);

        return oneTrade;
    }

    //,,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission
    //,,"IB1",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
    //,,"IB1",294,11/1/2021 12:24:57 PM,Buy,2,SPX,,Stock,SPX Stock, SPX,4609.8,0.01
    // note there is no Futures position in ONE...a Futures position is represented as Stock
    static Position? ParseONEPositionLine(int line_index, List<string> fields, string trade_id)
    {
        string account = fields[one_position_columns["Account"]];
        if (account != one_account)
        {
            Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has account: {account} that is different from trade account: {one_account}");
            return null;
        }

        string tid = fields[one_position_columns["TradeId"]];
        if (tid != trade_id)
        {
            Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has trade id: {tid} that is different from trade id in trade line: {trade_id}");
            return null;
        }

        Position position = new(isONEPosition: true);
        position.Account = one_account;
        position.TradeId = trade_id;
#if false
        string open_dt = fields[one_position_columns["Date"]];
        if (!DateTime.TryParse(open_dt, out position.open_dt))
        {
            Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has invalid open date field: {open_dt}");
            return null;
        }
#endif
        string transaction = fields[one_position_columns["Transaction"]];
        int quantity_sign;
        switch (transaction)
        {
            case "Buy":
                quantity_sign = 1; break;
            case "Sell":
                quantity_sign = -1; break;
            default:
                Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has invalid transaction type (must be Buy or Sell): {transaction}");
                return null;
        }

        string qty = fields[one_position_columns["Qty"]];
        if (!int.TryParse(qty, out int quantity) || quantity <= 0)
        {
            Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has invalid quantity field: {qty}");
            return null;
        }
        position.Quantity = quantity * quantity_sign;

        string type = fields[one_position_columns["Type"]];
        string symbol = fields[one_position_columns["Symbol"]];
        if (type == "Put" || type == "Call")
        {
            bool rc = ParseOptionSpec(symbol, @"(\w+) +(.+)$", position);
            if (!rc)
                return null;

            // confirm by parsing Expiry field
            string exp = fields[one_position_columns["Expiry"]];
            if (DateOnly.TryParse(exp, out DateOnly expiry))
            {
                if (position.Expiration.CompareTo(expiry) != 0)
                {
                    if (expiry.AddDays(1) == position.Expiration)
                        position.Expiration = expiry;
                    else
                    {
                        Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has discrepency between date in Symbol field {position.Expiration} and date in Expiry field {expiry}");
                        return null;
                    }
                }
            }
        }
        else if (type == "Stock")
        {
            position.Symbol = symbol;
            position.Type = SecurityType.Stock;
            position.Expiration = new DateOnly(1, 1, 1);
            position.Strike = 0;
        }
        else
        {
            Console.WriteLine($"\n***Error*** ONE position line{line_index + 1} has invalid type field (Must be Put, Call, or Stock): {type}");
            return null;
        }
#if false
        string open_price = fields[one_position_columns["Price"]];
        if (!float.TryParse(open_price, out position.open_price))
        {
            Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has invalid price field: {open_price}");
            return null;
        }
#endif
        return position;
    }

    static bool CompareONEPositionsToIBPositions()
    {
        // verify that ONE Index position (if any) matches IB Stock, Futures positons
        bool rc = VerifyStockPositions();

        // go through each consolidated ONE option position (whose quantity is != 0) and find it's associated IB Position
        foreach (Position onePosition in consolidatedONEPositions)
        {
            if (onePosition.Quantity == 0)
                continue;

            Debug.Assert(onePosition.Type != SecurityType.Futures);

            // if ONE position is Stock ignore it...already checked in call to VerifyStockPositions();
            if (onePosition.Type == SecurityType.Stock)
                continue;

            if (!ibPositions.TryGetValue(onePosition, out Position? ib_position))
            {
                Console.WriteLine($"\n***Error*** ONE has a {onePosition.Type} position in trade(s) {string.Join(",", onePosition.TradeIds)}, with no matching position in IB:");
                Console.WriteLine($"{onePosition.Symbol}\t{onePosition.Type}\tquantity: {onePosition.Quantity}\texpiration: {onePosition.Expiration}\tstrike: {onePosition.Strike}");
                rc = false;
                continue;
            }

            if (onePosition.Quantity != ib_position.Quantity)
            {
                Console.WriteLine($"\n***Error*** ONE has a {onePosition.Type} position in trade(s) {string.Join(",", onePosition.TradeIds)}, whose quantity ({onePosition.Quantity}) does not match IB quantity ({ib_position.Quantity}):");
                Console.WriteLine($"{onePosition.Symbol}\t{onePosition.Type}\tquantity: {onePosition.Quantity}\texpiration: {onePosition.Expiration}\tstrike: {onePosition.Strike}");
                rc = false;
            }

            // save one position reference in ib position
            ib_position.TradeIds = onePosition.TradeIds;

            // add one_position quantity to accounted_for_quantity...this will be checked later
            ib_position.one_quantity += onePosition.Quantity;
        }

        // ok...we've gone through all the ONE option positions, and tried to find associated IB positions. But...
        // there could still be IB option positions that have no corresponding ONE position
        // loop through all IB option positions, find associated ONE positions (if they don't exist, display error)
        foreach (Position position in ibPositions)
        {
            // ignore stock/futures positions...they've already been checked in VerifyStockPositions()
            if (position.Type == SecurityType.Stock || position.Type == SecurityType.Futures)
                continue;

            if (position.one_quantity != position.Quantity)
            {
                if (position.one_quantity == 0)
                {
                    Console.WriteLine($"\n***Error*** IB has a {position.Type} position with no matching position in ONE");
                    DisplayIBPosition(position);
                    rc = false;
                }
            }
        }

        return rc;
    }

    // make sure that any Index position in ONE is matched by stock/futures positionin IB and vice versa
    static bool VerifyStockPositions()
    {
        // get ONE consolidated Index position (these are not option positions...ONE actually models a position in the main Index)
        // note that net ONE Index position could be 0 even if individual ONE trades have non-zero Index positions
        // for the purposes of this program, we set the Type of a ONE Index position as Stock to differentiate it from the normal options position in the INdex
        List<Position> one_index_positions = consolidatedONEPositions.Where(s => s.Type == SecurityType.Stock).ToList();
        Debug.Assert(one_index_positions.Count <= 1, "***Program Error*** VerifyStockPositions: more than 1 Index position in consolidatedOnePositions");
        int one_quantity = 0;
        HashSet<string> one_trade_ids = new();
        if (one_index_positions.Count == 1)
        {
            Position one_position = one_index_positions[0];
            Debug.Assert(one_position.Quantity != 0);
            one_quantity = one_position.Quantity;
            one_trade_ids = one_position.TradeIds;
        }

        // get IB stock/futures positions. In reality, stock and futures positions at IB are used to satisfy Index positions in ONE
        // note that net IB position could be 0 even if non-zero stock/futures positions exist in IB
        List<Position> ib_stock_or_futures_positions = ibPositions.Where(s => s.Type == SecurityType.Stock || s.Type == SecurityType.Futures).ToList();
        float ib_stock_or_futures_quantity = 0f;
        foreach (Position ib_position in ib_stock_or_futures_positions)
        {
            Dictionary<string, float> possible_ib_symbols = associated_symbols[master_symbol];
            Debug.Assert(possible_ib_symbols.ContainsKey(ib_position.Symbol));
            float multiplier = possible_ib_symbols[ib_position.Symbol];
            float quantity = ib_position.Quantity;
            ib_stock_or_futures_quantity += multiplier * quantity;
        }

        if (one_quantity == ib_stock_or_futures_quantity)
            return true;

        // at this point, ONE's net Index position does not match IB's equivalent net stock/futures position. 
        // note that either position could be 0
        if (ib_stock_or_futures_quantity == 0)
        {
            Debug.Assert(one_quantity != 0);
            Debug.Assert(one_trade_ids.Count > 0);
            Console.WriteLine($"\n***Error*** ONE has an index position in {master_symbol} of {one_quantity} shares, in trade(s) {string.Join(",", one_trade_ids)}, while IB has no matching positions");
            return false;
        }

        if (one_quantity == 0)
        {
            Debug.Assert(ib_stock_or_futures_quantity != 0);
            Console.WriteLine($"\n***Error*** IB has stock/futures positions of {ib_stock_or_futures_quantity} equivalent {master_symbol} shares, while ONE has no matching positions");
            // todo: list IB positions
            return false;
        }

        // at this point, both ONE and IB have index positions...just not same quantity

        Debug.Assert(ib_stock_or_futures_positions.Count == 1);
        Debug.Assert(one_quantity != 0);
        Debug.Assert(ib_stock_or_futures_quantity != one_quantity);
        Console.WriteLine($"\n***Error*** ONE has an index position in {master_symbol} of {one_quantity} shares, in trade(s) {string.Join(",", one_trade_ids)}, while IB has {ib_stock_or_futures_quantity} equivalent {master_symbol} shares");
        return false;
    }

    const char delimiter = ',';
    static bool ParseCSVLine(string line, out List<string> fields)
    {
        fields = new();
        int state = 0;
        int start = 0;
        char c;
        for (int i = 0; i < line.Length; i++)
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
                        line = line[..i] + line[(i + 1)..];
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

    static void DisplayONEPositions()
    {
        Console.WriteLine($"\nConsolidated ONE Positions for {master_symbol}:");
        foreach (Position position in consolidatedONEPositions)
            DisplayONEPosition(position);

        Console.WriteLine();
    }

    static void DisplayONEPosition(Position position)
    {
        Debug.Assert(position.Quantity != 0);

        switch (position.Type)
        {
            case SecurityType.Stock:
                Console.WriteLine($"{position.Symbol}\tIndex\tquantity: {position.Quantity}\ttrade(s): {string.Join(",", position.TradeIds)}");
                break;
            case SecurityType.Call:
            case SecurityType.Put:
                if (position.TradeIds.Count == 0)
                    Console.WriteLine($"{position.Symbol}\t{position.Type}\tquantity: {position.Quantity}\texpiration: {position.Expiration}\tstrike: {position.Strike}");
                else
                    Console.WriteLine($"{position.Symbol}\t{position.Type}\tquantity: {position.Quantity}\texpiration: {position.Expiration}\tstrike: {position.Strike}\ttrade(s): {string.Join(",", position.TradeIds)}");
                break;
            default:
                Debug.Assert(false);
                break;
        }
    }

    static void DisplayIBPositions()
    {
        Console.WriteLine($"IB Positions related to {master_symbol}:");
        foreach (Position position in ibPositions)
            DisplayIBPosition(position);
    }

    static void DisplayIBPosition(Position position)
    {
        if (position.Quantity == 0)
            return;

        switch (position.Type)
        {
            case SecurityType.Stock:
                Console.WriteLine($"{position.Symbol}\t{position.Type}\tquantity: {position.Quantity}");
                break;
            case SecurityType.Futures:
                Console.WriteLine($"{position.Symbol}\t{position.Type}\tquantity: {position.Quantity}\texpiration: {position.Expiration}");
                break;
            case SecurityType.Call:
            case SecurityType.Put:
                Console.WriteLine($"{position.Symbol}\t{position.Type}\tquantity: {position.Quantity}\texpiration: {position.Expiration}\tstrike: {position.Strike}");
                break;
        }
    }

    static void DisplayIrrelevantIBPositions()
    {
        if (irrelevantIBPositions.Count > 0)
        {
            Console.WriteLine($"\nIB Positions **NOT** related to {master_symbol}:");
            foreach (Position position in irrelevantIBPositions)
                DisplayIBPosition(position);
        }
    }
}

