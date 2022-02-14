using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CompareONEToBroker;

enum SecurityType
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


    // error (display message, return false) if position already exists
    // programming error if this.Quantity is 0
    public bool Add(int line_index, SortedSet<Position> positions)
    {
        Debug.Assert(this.Quantity != 0);
        if (positions.Contains(this))
        {
            switch (Type)
            {
                case SecurityType.Stock:
                    Console.WriteLine($"***Error*** in {ONE.broker} line {line_index}: duplicate position: {Symbol} {Type}");
                    break;
                case SecurityType.Futures:
                    Console.WriteLine($"***Error*** in {ONE.broker} line {line_index}: duplicate futures contract: {Symbol} {Expiration} ");
                    break;
                default:
                    Console.WriteLine($"***Error*** in {ONE.broker} line {line_index}: duplicate option: {Symbol} {Type} {Expiration} {Strike}");
                    break;
            }
            return false;
        }
        positions.Add(this);
        return true;
    }
}

//,Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc
//,"acctount",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
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

internal static class ONE
{
    internal static string program_name = "";
    internal static string version = "";
    internal static string version_date = "";

    internal static string broker = "";

    internal static string? broker_filename = null;
    internal static string broker_directory = "";
    internal static DateOnly broker_filedate;

    internal static int broker_description_col = -1;
    internal static int broker_quantity_col = -1;
    internal static int security_type_col = -1;

    internal static string? one_filename = null;
    internal static DateOnly one_filedate;
    internal static string one_directory = @"C:\Users\lel48\OneDrive\Documents\ONEExport\";
    internal static string one_account = "";

    internal static string master_symbol = "SPX";
    internal static int index_of_last_required_column = -1;

    internal static readonly Dictionary<string, int> one_trade_columns = new(); // key is column name, value is column index
    internal static readonly Dictionary<string, int> one_position_columns = new(); // key is column name, value is column index
    internal static readonly SortedSet<Position> alreadyExpiredONEPositions = new();

    // note: the ref is readonly, not the contents of the Dictionary
    internal static readonly Dictionary<string, int> broker_columns = new(); // key is column name, value is column index

    static readonly Dictionary<string, ONETrade> oneTrades = new(); // key is trade_id

    // dictionary of ONE trades with key of trade id
    internal static readonly SortedDictionary<string, ONETrade> ONE_trades = new();

    // consolidated ONE positions; key is (symbol, OptionType, Expiration, Strike)
    internal static readonly SortedSet<Position> consolidatedONEPositions = new();

    internal static readonly SortedSet<Position> brokerPositions = new();

    // these Broker positions are not relevant to specified master_symbol, but we want to display them so user can verify
    internal static readonly SortedSet<Position> irrelevantBrokerPositions = new();

    internal static void ProcessCommandLine(string[] args)
    {
        CompareOneToIB.CommandLine.ProcessCommandLineArguments(args); // calls System.Environment.Exit(-1) if bad command line arguments
        ONE.master_symbol = master_symbol.ToUpper();
        ONE.relevant_symbols = associated_symbols[ONE.master_symbol];
        Console.WriteLine($"{program_name} Version {version}, {version_date}. Processing trades for {master_symbol}");
        (ONE.one_filename, one_filedate) = GetONEFileName(one_directory, one_filename); // parses one_filename from command line if it is not null to get date
    }

    // tries to set one_filename from files in one_directory, if it is null on entry
    // parses date from filename and returns it in parameter filedate
    // returns valid full filename or null if invalid filename specified or no valid filename found
    internal static (string?, DateOnly) GetONEFileName(string directory, string? specified_full_filename)
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

    // ONE uses the main index symbol for positions in the underlying, whereas Broker uses an actual stock/futures symbol
    // so...to reconcile these, if ONE has a position, say, of 10 SPX, this could be equivalent to 2 MES contracts,
    // 100 SPY shares, or some combination, like 1 MES contract and 50 SPY shares
    // So...the float is the number of ONE SPX shares that a share of the given item represents. So, { "SPY", 0.1f }
    // means that 1 share of SPY in Broker represents 0.1 shares of SPX in ONE
    readonly internal static Dictionary<string, Dictionary<string, float>> associated_symbols = new()
    {
        { "SPX", new Dictionary<string, float> { { "SPY", 0.1f }, { "MES", 5f }, { "ES", 50f } } },
        { "RUT", new Dictionary<string, float> { { "IWM", 0.1f }, { "M2K", 5f }, { "RTY", 50f } } },
        { "NDX", new Dictionary<string, float> { { "QQQ", 0.1f }, { "MNQ", 5f }, { "NQ", 50f } } }
    };

    internal static Dictionary<string, float> relevant_symbols = new(); // set to: associated_symbols[master_symbol];

    //ONE Detail Report
    //
    //Date/Time: 12/8/2021 08:28:42
    //Filter: [Account] = 'account'
    //Grouping: Account
    //
    //,Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc
    //,,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission
    //account
    //,"account",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
    //,,"account",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
    //,,"account",285,10/11/2021 11:37:32 AM,Buy,4,SPX   220319P02725000,3/18/2022,Put,SPX Mar22 2725 Put,SPX,12.8,4.56
    //,,"account",296,11/12/2021 11:02:02 AM,Buy,1,SPX,,Stock,SPX Stock, SPX,4660.05,0.005
    internal static bool ProcessONEFile(string full_filename)
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

                // skip ONE trades whose trade name starts with a minus ('-'): this is the way the user tells this program to skio trades
                string tradeName = fields[one_trade_columns["TradeName"]];
                if (tradeName.StartsWith('-'))
                {
                    skip_current_trade = true;
                    continue;
                }

                // skip ONE trades not in master symbol
                if (master_symbol != fields[one_trade_columns["Underlying"]])
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

        DisplayedIgnoredONEPositions();

        DisplayONEPositions();

        return true;
    }

    // note: this also removes trades from ONE_trades which are closed
    internal static void DisplayONETrades()
    {
        Console.WriteLine("\nONE Trades:");
        foreach (ONETrade one_trade in ONE_trades.Values)
        {
            if (one_trade.Status == TradeStatus.Closed)
            {
                if (one_trade.Positions.Count != 0)
                {
                    Console.WriteLine($"\n***Error*** Trade #{one_trade.TradeId} {one_trade.TradeName} is closed, but contains positions:");
                }
                else
                {
                    Console.WriteLine($"\nTrade #{one_trade.TradeId} {one_trade.TradeName}: Closed. No positions");
                    continue;
                }
            }
            else
                Console.WriteLine($"\nTrade #{one_trade.TradeId} {one_trade.TradeName}:");

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

    // remove closed trades from ONE_trades
    internal static void RemoveClosedONETrades()
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
    //,"account",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
    // we don't parse Margin,Comms,PnL,PnLperc
    internal static ONETrade? ParseONETradeLine(int line_index, List<string> fields)
    {
        ONETrade oneTrade = new();

        oneTrade.Account = fields[ONE.one_trade_columns["Account"]];
        if (one_account != oneTrade.Account)
        {
            Console.WriteLine($"\n***Error*** In ONE Trade line #{line_index + 1}, account field: {oneTrade.Account} is not the same as line 9 of file: {one_account}");
            return null;
        }

        int field_index = ONE.one_trade_columns["Expiration"];
        if (!DateOnly.TryParse(fields[field_index], out oneTrade.Expiration))
        {
            Console.WriteLine($"\n***Error*** ONE Trade line #{line_index + 1} has invalid date field: {fields[field_index]}");
            return null;
        }

        oneTrade.TradeId = fields[ONE.one_trade_columns["TradeId"]];
        if (oneTrade.TradeId.Length == 0)
        {
            Console.WriteLine($"\n***Error*** ONE Trade line #{line_index + 1} has empty trade id field");
            return null;
        }

        oneTrade.TradeName = fields[ONE.one_trade_columns["TradeName"]];

        string status = fields[ONE.one_trade_columns["Status"]];
        if (status == "Open")
            oneTrade.Status = TradeStatus.Open;
        else if (status == "Closed")
            oneTrade.Status = TradeStatus.Closed;
        else
        {
            Console.WriteLine($"\n***Error*** ONE Trade line #{line_index + 1} has invalid trade status field: {status}");
            return null;
        }

        string open_dt = fields[ONE.one_trade_columns["OpenDate"]];
        if (!DateTime.TryParse(open_dt, out oneTrade.OpenDt))
        {
            Console.WriteLine($"\n***Error*** ONE Trade line #{line_index + 1} has invalid date field: {open_dt}");
            return null;
        }

        if (oneTrade.Status == TradeStatus.Closed)
        {
            string close_dt = fields[ONE.one_trade_columns["CloseDate"]];
            if (!DateTime.TryParse(close_dt, out oneTrade.CloseDt))
            {
                Console.WriteLine($"\n***Error*** ONE Trade line #{line_index + 1} has invalid date field: {close_dt}");
                return null;
            }
        }

        string dte = fields[ONE.one_trade_columns["DaysToExpiration"]];
        if (!int.TryParse(dte, out oneTrade.Dte))
        {
            Console.WriteLine($"\n***Error*** ONE Trade line #{line_index + 1} has invalid dte field: {dte}");
            return null;
        }

        string dit = fields[ONE.one_trade_columns["DaysInTrade"]];
        if (!int.TryParse(dit, out oneTrade.Dit))
        {
            Console.WriteLine($"\n***Error*** ONE Trade line #{line_index + 1} has invalid dit field: {dit}");
            return null;
        }

        if (oneTrades.ContainsKey(oneTrade.TradeId))
        {
            Console.WriteLine($"\n***Error*** in #{line_index + 1} in ONE file: duplicate trade id: {oneTrade.TradeId}");
            return null;
        }
        oneTrades.Add(oneTrade.TradeId, oneTrade);

        return oneTrade;
    }

    //,,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission
    //,,"account",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
    //,,"account",294,11/1/2021 12:24:57 PM,Buy,2,SPX,,Stock,SPX Stock, SPX,4609.8,0.01
    // note there is no Futures position in ONE...a Futures position is represented as Stock
    internal static Position? ParseONEPositionLine(int line_index, List<string> fields, string trade_id)
    {
        string account = fields[ONE.one_position_columns["Account"]];
        if (account != one_account)
        {
            Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has account: {account} that is different from trade account: {one_account}");
            return null;
        }

        string tid = fields[ONE.one_position_columns["TradeId"]];
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
        string transaction = fields[ONE.one_position_columns["Transaction"]];
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

        string qty = fields[ONE.one_position_columns["Qty"]];
        if (!int.TryParse(qty, out int quantity) || quantity <= 0)
        {
            Console.WriteLine($"\n***Error*** ONE position line {line_index + 1} has invalid quantity field: {qty}");
            return null;
        }
        position.Quantity = quantity * quantity_sign;

        string type = fields[ONE.one_position_columns["Type"]];
        string symbol = fields[ONE.one_position_columns["Symbol"]];
        if (type == "Put" || type == "Call")
        {
            if (symbol[0] == '/')
            {
                int aa = 1;
            }
            bool rc = ParseOptionSpec(symbol, @"(\w+) +(.+)$", position);
            if (!rc)
                return null;

            // confirm by parsing Expiry field
            string exp = fields[ONE.one_position_columns["Expiry"]];
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

    // go through each ONE trade in ONE_trades and create a consolidated ONE position
    // right now, this could create ONE positions with 0 quantity if trades "step on strikes"
    internal static void CreateConsolidateOnePositions()
    {
        foreach (ONETrade oneTrade in ONE.ONE_trades.Values)
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
                ONE.alreadyExpiredONEPositions.Add(onePosition);
        }
        foreach (Position onePosition in ONE.alreadyExpiredONEPositions)
            consolidatedONEPositions.Remove(onePosition);
    }

    // ignored because they expired prior to date in one filename
    internal static void DisplayedIgnoredONEPositions()
    {
        if (alreadyExpiredONEPositions.Count > 0)
        {
            Console.WriteLine("\n***Warning*** the following position(s) in ONE have already expired (based on the date in the ONE filename):");
            foreach (Position position in alreadyExpiredONEPositions)
                DisplayONEPosition(position);
        }
        Console.WriteLine();
    }

    internal static void DisplayONEPositions()
    {
        Console.WriteLine($"\nConsolidated ONE Positions for {master_symbol}:");
        foreach (Position position in ONE.consolidatedONEPositions)
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

    const char delimiter = ',';
    internal static bool ParseCSVLine(string line, out List<string> fields)
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
            case 0: // must be blank line or trailing comma
                if (line.Length > 0)
                    fields.Add(""); // add empty last field
                break;

            case 1: // field started with non-quote...standard end
                fields.Add(line[start..].Trim());
                break;

            case 2: // field started with quote, but didn't end with quote...error
                return false;

            case 3: // field ended with quote
                fields.Add(line[start..^1].Trim());
                break;

            default:
                Debug.Assert(false);
                return false;
        }

        return true;
    }

    internal static void DisplayBrokerPositions(SortedSet<Position> brokerPositions)
    {
        Console.WriteLine($"{ONE.broker} Positions related to {ONE.master_symbol}:");
        foreach (Position position in brokerPositions)
            DisplayBrokerPosition(position);
    }


    // make sure that any Index position in ONE is matched by stock/futures positionin broker and vice versa
    static bool VerifyStockPositions()
    {
        // get ONE consolidated Index position (these are not option positions...ONE actually models a position in the main Index)
        // note that net ONE Index position could be 0 even if individual ONE trades have non-zero Index positions
        // for the purposes of this program, we set the Type of a ONE Index position as Stock to differentiate it from the normal options position in the Index
        List<Position> one_index_positions = ONE.consolidatedONEPositions.Where(s => s.Type == SecurityType.Stock).ToList();
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

        // get Broker stock/futures positions. In reality, stock and futures positions at Broker are used to satisfy Index positions in ONE
        // note that net Broker position could be 0 even if stock/futures positions exist in Broker
        List<Position> broker_stock_or_futures_positions = brokerPositions.Where(s => s.Type == SecurityType.Stock || s.Type == SecurityType.Futures).ToList();
        float broker_stock_or_futures_quantity = 0f;
        foreach (Position broker_position in broker_stock_or_futures_positions)
        {
            Dictionary<string, float> possible_broker_symbols = ONE.associated_symbols[ONE.master_symbol];
            Debug.Assert(possible_broker_symbols.ContainsKey(broker_position.Symbol));
            float multiplier = possible_broker_symbols[broker_position.Symbol];
            float quantity = broker_position.Quantity;
            broker_stock_or_futures_quantity += multiplier * quantity;
        }

        if (one_quantity == broker_stock_or_futures_quantity)
            return true;

        // at this point, ONE's net Index position does not match broker's net stock/futures position. 
        // note that either position could be 0
        if (broker_stock_or_futures_quantity == 0)
        {
            Debug.Assert(one_quantity != 0);
            Debug.Assert(one_trade_ids.Count > 0);
            Console.WriteLine($"\n***Error*** ONE has an index position in {ONE.master_symbol} of {one_quantity} shares, in trade(s) {string.Join(",", one_trade_ids)}, while {broker} has no matching positions");
            return false;
        }

        if (one_quantity == 0)
        {
            Debug.Assert(broker_stock_or_futures_quantity > 0);
            Console.WriteLine($"\n***Error*** {ONE.broker} has stock/futures positions of {broker_stock_or_futures_quantity} equivalent {ONE.master_symbol} shares, while ONE has no matching positions");
            return false;
        }

        // at this point, both ONE and Broker have index positions...just not same quantity

        Debug.Assert(broker_stock_or_futures_positions.Count == 1);
        Debug.Assert(one_quantity != 0);
        Debug.Assert(broker_stock_or_futures_quantity != one_quantity);
        Console.WriteLine($"\n***Error*** ONE has an index position in {ONE.master_symbol} of {one_quantity} shares, in trade(s) {string.Join(",", one_trade_ids)}, while {broker} has {broker_stock_or_futures_quantity} equivalent {ONE.master_symbol} shares");
        return false;
    }

    internal static bool CompareONEPositionsToBrokerPositions()
    {
        // verify that ONE Index position (if any) matches Broker Stock, Futures positons
        bool rc = VerifyStockPositions();

        // go through each consolidated ONE option position (whose quantity is != 0) and find it's associated Broker Position
        foreach (Position onePosition in ONE.consolidatedONEPositions)
        {
            if (onePosition.Quantity == 0)
                continue;

            Debug.Assert(onePosition.Type != SecurityType.Futures);

            // if ONE position is Stock ignore it...already checked in call to VerifyStockPositions();
            if (onePosition.Type == SecurityType.Stock)
                continue;

            if (!brokerPositions.TryGetValue(onePosition, out Position? broker_position))
            {
                Console.WriteLine($"\n***Error*** ONE has a {onePosition.Type} position in trade(s) {string.Join(",", onePosition.TradeIds)}, with no matching position in {ONE.broker}:");
                Console.WriteLine($"{onePosition.Symbol}\t{onePosition.Type}\tquantity: {onePosition.Quantity}\texpiration: {onePosition.Expiration}\tstrike: {onePosition.Strike}");
                rc = false;
                continue;
            }

            if (onePosition.Quantity != broker_position.Quantity)
            {
                Console.WriteLine($"\n***Error*** ONE has a {onePosition.Type} position in trade(s) {string.Join(",", onePosition.TradeIds)}, whose quantity ({onePosition.Quantity}) does not match {ONE.broker} quantity ({broker_position.Quantity}):");
                Console.WriteLine($"{onePosition.Symbol}\t{onePosition.Type}\tquantity: {onePosition.Quantity}\texpiration: {onePosition.Expiration}\tstrike: {onePosition.Strike}");
                rc = false;
            }

            // save one position reference in Broker position
            broker_position.TradeIds = onePosition.TradeIds;

            // add one_position quantity to accounted_for_quantity...this will be checked later
            broker_position.one_quantity += onePosition.Quantity;
        }

        // ok...we've gone through all the ONE option positions, and tried to find associated Broker positions. But...
        // there could still be Broker option positions that have no corresponding ONE position
        // loop through all Broker option positions, find associated ONE positions (if they don't exist, display error)
        foreach (Position position in brokerPositions)
        {
            // ignore stock/futures positions...they've already been checked in VerifyStockPositions()
            if (position.Type == SecurityType.Stock || position.Type == SecurityType.Futures)
                continue;

            if (position.one_quantity != position.Quantity)
            {
                if (position.one_quantity == 0)
                {
                    Console.WriteLine($"\n***Error*** {ONE.broker} has a {position.Type} position with no matching position in ONE");
                    DisplayBrokerPosition(position);
                    rc = false;
                }
            }
        }

        if (rc)
            Console.WriteLine($"\nSuccess: {broker} and ONE positions for {ONE.master_symbol} are equivalent.");

        return rc;
    }

    static void DisplayBrokerPosition(Position position)
    {
        if (position.Quantity == 0)
            return;

        switch (position.Type)
        {
            case SecurityType.Stock:
                //Console.WriteLine($"{position.symbol} {position.optionType}: quantity = {position.quantity}");
                Console.WriteLine($"{position.Symbol}\t{position.Type}\tquantity: {position.Quantity}");
                break;
            case SecurityType.Futures:
                //Console.WriteLine($"{position.symbol} {position.optionType}: expiration = {position.expiration}, quantity = {position.quantity}");
                Console.WriteLine($"{position.Symbol}\t{position.Type}\tquantity: {position.Quantity}\texpiration: {position.Expiration}");
                break;
            case SecurityType.Call:
            case SecurityType.Put:
                //Console.WriteLine($"{position.symbol} {position.optionType}: expiration = {position.expiration}, strike = {position.strike}, quantity = {position.quantity}");
                Console.WriteLine($"{position.Symbol}\t{position.Type}\tquantity: {position.Quantity}\texpiration: {position.Expiration}\tstrike: {position.Strike}");
                break;
        }
    }

    internal static void DisplayIrrelevantBrokerPositions()
    {
        if (irrelevantBrokerPositions.Count > 0)
        {
            Console.WriteLine($"\n{ONE.broker} Positions **NOT** related to {ONE.master_symbol}:");
            foreach (Position position in irrelevantBrokerPositions)
                DisplayBrokerPosition(position);
        }
    }

    internal static void RunUnitTests()
    {
        List<string> parseCSVTestFields;
        bool ParseCSVLineRC;
        // tests for ParseCSVLine
        ParseCSVLineRC = ParseCSVLine(",", out parseCSVTestFields);
        Debug.Assert(ParseCSVLineRC && parseCSVTestFields.Count == 2 && parseCSVTestFields[0] == "");
        parseCSVTestFields.Clear();
        ParseCSVLineRC = ParseCSVLine(",,", out parseCSVTestFields); // trailing comma ignored
        Debug.Assert(ParseCSVLineRC && parseCSVTestFields.Count == 3 && parseCSVTestFields[0] == "" && parseCSVTestFields[1] == "");
        parseCSVTestFields.Clear();
        ParseCSVLineRC = ParseCSVLine("1,", out parseCSVTestFields); // trailing comma ignored
        Debug.Assert(ParseCSVLineRC && parseCSVTestFields.Count == 2 && parseCSVTestFields[0] == "1");
        ParseCSVLineRC = ParseCSVLine("1,2", out parseCSVTestFields); // trailing comma ignored
        Debug.Assert(ParseCSVLineRC && parseCSVTestFields.Count == 2 && parseCSVTestFields[1] == "2");
        //int test1 = 1;
    }
}

