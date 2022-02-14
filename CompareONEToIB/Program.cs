using System.Diagnostics;
using System.Text.RegularExpressions;
using CompareONEToBroker;

namespace CompareOneToIB;

//,Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc
//,"IB1",12/3/2021,285,"244+1lp 2021-10-11 11:37", SPX, Open, Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67

static class Program
{
    internal const string version = "0.0.7";
    internal const string version_date = "2022-02-14";

    static int Main(string[] args)
    {
#if false
        ONE.RunUnitTests();
#endif

        var stopWatch = new Stopwatch();
        stopWatch.Start();

        ONE.program_name = "CompareONEToIB";
        ONE.version = "0.0.7";
        ONE.version_date = "2022-02-14";

        ONE.broker = "IB";
        ONE.broker_directory = @"C:\Users\lel48\OneDrive\Documents\IBExport\";

        ONE.ProcessCommandLine(args); // calls System.Environment.Exit(-1) if bad command line arguments

        (ONE.broker_filename, ONE.broker_filedate) = GetIBFileName(ONE.broker_directory, ONE.broker_filename); // parses tda_filename from command line if it is not null to get date
        if (ONE.one_filename == null || ONE.broker_filename == null)
            return -1;

        Console.WriteLine("\nProcessing ONE file: " + ONE.one_filename);
        Console.WriteLine($"Processing {ONE.broker} file: {ONE.broker_filename}");

        bool rc = ONE.ProcessONEFile(ONE.one_filename);
        if (!rc)
            return -1;

        rc = ProcessIBFile(ONE.broker_filename);
        if (!rc)
            return -1;

        rc = ONE.CompareONEPositionsToBrokerPositions();
        if (!rc)
            return -1;

        stopWatch.Stop();
        Console.WriteLine($"\nElapsed time = {stopWatch.Elapsed}");

        return 0;
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

            files = Directory.GetFiles(directory, filename_pattern, SearchOption.TopDirectoryOnly);
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
                Console.WriteLine($"\n***Error*** No IB files found in {directory} with following filename pattern: [filtered_]portfolio.yyyymmdd.csv");
                return (null, latestDate);
            }

            return (latest_full_filename, latestDate);
        }

        if (!File.Exists(specified_full_filename))
        {
            Console.WriteLine($"\n***Error*** Specified IB file {specified_full_filename} does not exist");
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
            Console.WriteLine($"\n***Error*** Specified IB file does not match following pattern: [filtered_]portfolio.yyyymmdd.csv");
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
                ONE.broker_columns.Add(column_name, i);
        }
        int index_of_last_required_column = 0;
        for (int i = 0; i < required_columns.Length; i++)
        {
            if (!ONE.broker_columns.TryGetValue(required_columns[i], out int colnum))
            {
                Console.WriteLine($"\n***Error*** IB file header must contain column named {required_columns[i]}");
                return false;
            }
            index_of_last_required_column = Math.Max(colnum, index_of_last_required_column);
        }
        ONE.broker_description_col = ONE.broker_columns["Financial Instrument Description"];
        ONE.broker_quantity_col = ONE.broker_columns["Position"];
        ONE.security_type_col = ONE.broker_columns["Security Type"];

        // now process each IB position line
        for (int line_index = 2; line_index < lines.Length; line_index++)
        {
            string line = lines[line_index].Trim();

            // blank line terminates list of positions. Next line must be "Cash Balances"
            if (line.Length == 0)
                break;

            bool rc = ONE.ParseCSVLine(line, out List<string> fields);
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

        if (ONE.brokerPositions.Count == 0)
        {
            Console.WriteLine($"\n***Error*** No positions related to {ONE.master_symbol} in IB file {full_filename}");
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

        bool rc = int.TryParse(fields[ONE.broker_quantity_col], out int quantity);
        if (!rc)
        {
            Console.WriteLine($"***Error*** in IB line {line_index + 1}: invalid Quantity: {fields[ONE.broker_quantity_col]}");
            return -1;
        }
        if (quantity == 0)
            return 1;

        var ibPosition = new Position(isONEPosition: false)
        {
            Quantity = quantity
        };

        string description = fields[ONE.broker_description_col];
        int security_type_col = ONE.broker_columns["Security Type"];
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
                    Console.WriteLine($"***Error*** in IB line {line_index + 1}: invalid option specification: {fields[ONE.broker_description_col]}");
                    return -1;
                }
                if (!description.StartsWith(ONE.master_symbol))
                    irrelevant_position = true; // This IB position is not relevant to this compare. Add to irrelevantIBPositions collection
                break;

            case "FUT":
                //MES      MAR2022,1,USD,4624.50,23122.50,4625.604,-5.52,0.00,No,FUT,23136.14
                ibPosition.Type = SecurityType.Futures;
                rc = ParseFuturesSpec(description, @"(\w+) +(\w+)$", ibPosition);
                if (!rc)
                    return -1;
                if (!ONE.relevant_symbols.ContainsKey(ibPosition.Symbol))
                    irrelevant_position = true; // This IB position is not relevant to this compare. Add to irrelevantIBPositions collection
                break;

            case "STK":
                //SPY,100,USD,463.3319397,46333.19,463.02,31.19,0.00,No,STK,46333.19
                ibPosition.Type = SecurityType.Stock;
                ibPosition.Symbol = fields[0].Trim();
                if (!ONE.relevant_symbols.ContainsKey(ibPosition.Symbol))
                    irrelevant_position = true; // This IB position is not relevant to this compare. Add to irrelevantIBPositions collection
                break;
        }

        if (irrelevant_position && !ONE.irrelevantBrokerPositions.Contains(ibPosition))
        {
            ONE.irrelevantBrokerPositions.Add(ibPosition);
            return 1;
        }

        if (ONE.brokerPositions.Contains(ibPosition))
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
        ONE.brokerPositions.Add(ibPosition);
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
        if (!position.Symbol.StartsWith(ONE.master_symbol))
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
}

