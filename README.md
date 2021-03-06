# CompareONEToIB.cs V0.0.7

This program compares exported data from OptioneNet Explorer (ONE) to that from Interactive Brokers(IB) to make
sure that the option positions actually held in IB are the ones that are beng modeled by ONE.

The program currently supports portfolios that contain index options for SPX, RUT, and NDX, stock positions in SPY, IWM, and QQQ used
for hedging those option positions, and futures positions in ES, MES, RTY, M2K, NQ, and MNQ used to hedge those option positions. However
analysis can only been done on one class of positions at a time as specified by the command line --symbol parameter.

Since you may have positions at IB that are irrelevant to your options trades, this program attempts to disregard such positions. So, for instance,
if you are analyzing SPX positions (the default unless you specify --symbol in the command line), positions in other options, as well as
positions in other stocks or futures **except SPY, /ES, or /MES** will be ignored. In this case, it is assumed that
positions in SPY, /ES, or /MES are used to hedge SPX option positions. If you are using positions in those instruments for purposes other
than hedging SPX positions, this program could generate incorrect error messages (or not generate error messages when there were errors). One workaround
is to edit the exported IB file to remove positions that you do not want analyzed. This program will display positions that it ignores in a section under
the displayed IB positions it is analyzing. The same logic is applied when analyzing RUT or NDX option positions. 

**This program has not been thoroughly tested and likely has bugs. In addition, the file formats used by IB and ONE
can change without notice, which could cause the program to crash or produce erroneous results. Use at your own risk!**

## What's New
Sometimes you have trades in a ONE account that aren't real. You just modeled something to see what it looked like. You can instruct
this program to ignore those trades by starting the ONE Trade Name with a minus ('-').

The program will now ignore ONE trades made with an underlying instrument that is not the instrument specified in the command line
(or, if not specified in the command line, SPX). So, for instance, if you have made trades in /ES in ONE, but the underlying is
specified as SPX, the program will ignore those trades. Note that this IS NOT the case for Interactive Brokers positions. If the
specified underlying is SPX, IB positions in /ES will be treated as hedge trades and matched up against underlying position in
the SPX index in ONE.

## Command Line
This program is run from the command line, so you must open a terminal and either have this program on your PATH or change
the current working directory to the one containing this program.

There are no required command line arguments if you are analyzing SPX positions and use the recommended directory structure. That structure assumes
there are two directories, ONEExport and IBExport in the same folder as this executable, that contain the files exported by ONE and IB respectively.

**--symbol** specifies the index whose options will be checked. spx, rut, and ndx are currently supported. This
also determines the symbols for the stock/futures positions that will be taken into account. The default is spx (case insensitive).

**--onedir** specifies a directory where your ONE files are saved. It defaults to a directory named ONEExport in the same
directory as this executable.

**--onefile** can be used instead of onedir. It specifies the name (including path if necessary) of the ONE file to be processed.

**--ibdir** specifies a directory where your IB files are saved. It defaults to a directory named IBExport in the same
directory as this executable.

**--ibfile** can be used instead of --ibdir. It specifies the name (including path if necessary) of the IB file to be processed.

If you use a directory (--onedir, --ibdir) instead of a file, the program will use the latest file in the directory whose name matches 
the proper pattern (yyyy-mm-dd-ONEDetailReport.csv for ONE, and portfolio.yyyymmdd.csv or filtered_portfolio.yyyymmdd.csv for IB). 

There are two optional command line arguments:

**--version** just displays the version of the program.

**--help** displays a short summary of the command line arguments.

There are short names for each of the commands: -s, -od, -id, -of, -if, -v, and -h.

Sample command lines (from Windows Command Prompt):
```
CompareONEToIB.exe
CompareONEToIB.exe -s spx -id C:\Users\username\IBExport -od C:\Users\username\ONEExport > output.txt
CompareONEToIB.exe --symbol spx --ibdir C:\Users\username\IBExport --onedir C:\Users\username\ONEExport
```

Sample command lines (from Windows Power Shell):
```
./CompareONEToIB.exe
./CompareONEToIB.exe -s spx -id C:\Users\username\IBExport -od C:\Users\username\ONEExport > output.txt
./CompareONEToIB.exe --symbol spx --ibdir C:\Users\username\IBExport --onedir C:\Users\username\ONEExport
```

Why use directories instead of the specifying the actual files? So you can just save newer files to the 
directory without changing the command line arguments. The program automatically selects 
the files with the latest dates embedded in the filenames (It does not check the actual OS time stamp). 

## Exporting the IB data

The IB data is exported by running IB's Trader WorkStation, opening the Account Window, going to the File menu
and selecting Export Portfolio. When the Export Portfolio/Save dialog pops up, click the Configure Export Settings...
button and when the Trader Workstation Configuration dialog pops up, make sure that Advanced Contract display is checked
and  Include "Exchange" column, Include Account" column, and Include Account Number" column are unchecked. Depending on
whether or not you checked "Show zero positions row" in the Portfolio section of the Account window, the exported file
name will start with "portfolio" or "filtered_portfolio". When this program is searching for the IB file to process, it
accepts either, and if two files exist with the same date in the filename (yyyymmdd), where one starts with "portfolio" and
the other starts with "filtered_portfolio", the file with the lastest modified time stamp will be used. This program displays
the names of the files selected for processing. **You must verify that the ones chosen are the ones you want!**

### This is what the IB data looks like:

```
Portfolio
Financial Instrument Description,Position,Currency,Market Price,Market Value,Average Price,Unrealized P&L,Realized P&L,Liquidate Last,Security Type,Delta Dollars
SPX    APR2022 4300 P [SPXW  220429P04300000 100],2,USD,119.5072021,23901.44,123.5542635,-809.41,0.00,No,OPT,-246454.66
SPX    APR2022 4000 P [SPXW  220429P04000000 100],-4,USD,75.819664,-30327.87,82.2374865,2567.13,0.00,No,OPT,305432.86
SPX    APR2022 3250 P [SPXW  220429P03250000 100],2,USD,25.7420445,5148.41,27.5892635,-369.44,0.00,No,OPT,-47068.39
SPX    APR2022 4325 P [SPX   220414P04325000 100],2,USD,111.5892257,22317.85,109.4212135,433.60,0.00,No,OPT,-248012.63
```

## Exporting the ONE data

The ONE data is exported by opening ONE, clicking on Reports, then on the Reports window, clicking on the little filter icon on the Account dropdown
and selecting the account that holds the trades you want to compare with, then clicking the Export button and saving the file.
**Make sure that the Report Type dropdown is set to Detail.**

### This is what the ONE data looks like:

```
ONE Detail Report

Date/Time: 12/8/2021 08:28:42
Filter: [Account] = 'IB1'
Grouping: Account

,Account,Expiration,TradeId,TradeName,Underlying,Status,TradeType,OpenDate,CloseDate,DaysToExpiration,DaysInTrade,Margin,Comms,PnL,PnLperc
,,Account,TradeId,Date,Transaction,Qty,Symbol,Expiry,Type,Description,Underlying,Price,Commission
IB1 
,"IB1",12/3/2021,285,"244+1lp 2021-10-11 11:37",SPX,Open,Custom,10/11/2021 11:37 AM,,53,58,158973.30,46.46,13780.74,8.67
,,"IB1",285,10/11/2021 11:37:32 AM,Buy,2,SPX   220319P04025000,3/18/2022,Put,SPX Mar22 4025 Put,SPX,113.92,2.28
,,"IB1",285,10/11/2021 11:37:32 AM,Buy,4,SPX   220319P02725000,3/18/2022,Put,SPX Mar22 2725 Put,SPX,12.8,4.56
,,"IB1",285,10/11/2021 11:37:32 AM,Sell,4,SPX   220319P03725000,3/18/2022,Put,SPX Mar22 3725 Put,SPX,68.77,4.56
,,"IB1",285,10/11/2021 3:58:48 PM,Buy,1,SPXW  211204P03000000,12/3/2021,Put,SPX Dec21 3000 Put,SPX,2.7,1.5
```

## This is sample output: 

```
>.\CompareONEToIB.exe -s spx -id C:\Users\username\IBExport -od C:\Users\username\ONEExport > output.txt

CompareONEToIB Version 0.0.2, 2021-12-17. Processing trades for SPX

Processing ONE file: C:\Users\lel48\OneDrive\Documents\ONEExport\2021-12-14-ONEDetailReport.csv
Processing IB file: C:\Users\lel48\OneDrive\Documents\IBExport\portfolio.20211213.csv

ONE Trades:

Trade 284:
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 3100
SPX  Put     quantity: -4   expiration: 3/18/2022    strike: 3625
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 3825

Trade 285:
SPX  Put     quantity: 4    expiration: 3/18/2022    strike: 2725
SPX  Put     quantity: -4   expiration: 3/18/2022    strike: 3775
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 4025
SPXW Put     quantity: 1    expiration: 4/29/2022    strike: 3250

Trade 287:
SPX  Index   quantity: -1
SPX  Put     quantity: 4    expiration: 4/14/2022    strike: 2775
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 3800
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4075

Trade 288:
SPX  Put     quantity: 2    expiration: 1/21/2022    strike: 2450
SPXW Put     quantity: 2    expiration: 3/31/2022    strike: 3175
SPXW Put     quantity: -2   expiration: 3/31/2022    strike: 3825
SPXW Put     quantity: -2   expiration: 3/31/2022    strike: 3850
SPXW Put     quantity: 2    expiration: 3/31/2022    strike: 4100

Trade 294: Closed. No positions

Trade 296:
SPXW Put     quantity: 1    expiration: 1/31/2022    strike: 2800
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 3150
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 3900
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4200

Trade 297:
SPXW Put     quantity: 2    expiration: 1/31/2022    strike: 2600
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 3500
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 4050
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4325

Trade 298:
SPX  Put     quantity: 2    expiration: 2/18/2022    strike: 2350
SPXW Put     quantity: 2    expiration: 4/29/2022    strike: 3400
SPXW Put     quantity: -4   expiration: 4/29/2022    strike: 4000
SPXW Put     quantity: 2    expiration: 4/29/2022    strike: 4300

Trade 301:
SPX  Index   quantity: 5
SPXW Put     quantity: 1    expiration: 1/14/2022    strike: 2700
SPX  Put     quantity: 1    expiration: 1/21/2022    strike: 2850
SPXW Put     quantity: 1    expiration: 1/28/2022    strike: 3000
SPXW Put     quantity: 1    expiration: 1/31/2022    strike: 2750

Trade 302: Closed. No positions

Trade 303: Closed. No positions

Trade 304: Closed. No positions

Consolidated ONE Positions for SPX:
SPX  Index   quantity: 4    trade(s): 287,301
SPXW Put     quantity: 1    expiration: 1/14/2022    strike: 2700   trade(s): 301
SPX  Put     quantity: 2    expiration: 1/21/2022    strike: 2450   trade(s): 288
SPX  Put     quantity: 1    expiration: 1/21/2022    strike: 2850   trade(s): 301
SPXW Put     quantity: 1    expiration: 1/28/2022    strike: 3000   trade(s): 301
SPXW Put     quantity: 2    expiration: 1/31/2022    strike: 2600   trade(s): 297
SPXW Put     quantity: 1    expiration: 1/31/2022    strike: 2750   trade(s): 301
SPXW Put     quantity: 1    expiration: 1/31/2022    strike: 2800   trade(s): 296
SPX  Put     quantity: 2    expiration: 2/18/2022    strike: 2350   trade(s): 298
SPX  Put     quantity: 4    expiration: 3/18/2022    strike: 2725   trade(s): 285
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 3100   trade(s): 284
SPX  Put     quantity: -4   expiration: 3/18/2022    strike: 3625   trade(s): 284
SPX  Put     quantity: -4   expiration: 3/18/2022    strike: 3775   trade(s): 285
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 3825   trade(s): 284
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 4025   trade(s): 285
SPXW Put     quantity: 2    expiration: 3/31/2022    strike: 3175   trade(s): 288
SPXW Put     quantity: -2   expiration: 3/31/2022    strike: 3825   trade(s): 288
SPXW Put     quantity: -2   expiration: 3/31/2022    strike: 3850   trade(s): 288
SPXW Put     quantity: 2    expiration: 3/31/2022    strike: 4100   trade(s): 288
SPX  Put     quantity: 4    expiration: 4/14/2022    strike: 2775   trade(s): 287
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 3150   trade(s): 296
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 3500   trade(s): 297
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 3800   trade(s): 287
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 3900   trade(s): 296
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 4050   trade(s): 297
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4075   trade(s): 287
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4200   trade(s): 296
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4325   trade(s): 297
SPXW Put     quantity: 1    expiration: 4/29/2022    strike: 3250   trade(s): 285
SPXW Put     quantity: 2    expiration: 4/29/2022    strike: 3400   trade(s): 298
SPXW Put     quantity: -4   expiration: 4/29/2022    strike: 4000   trade(s): 298
SPXW Put     quantity: 2    expiration: 4/29/2022    strike: 4300   trade(s): 298

IB Positions related to SPX:
SPY  Stock   quantity: 100
MES  Futures quantity: 1    expiration: 3/1/2022
SPXW Put     quantity: 1    expiration: 1/14/2022    strike: 2700
SPX  Put     quantity: 2    expiration: 1/21/2022    strike: 2450
SPX  Put     quantity: 1    expiration: 1/21/2022    strike: 2850
SPXW Put     quantity: 1    expiration: 1/28/2022    strike: 3000
SPXW Put     quantity: 2    expiration: 1/31/2022    strike: 2600
SPXW Put     quantity: 1    expiration: 1/31/2022    strike: 2750
SPXW Put     quantity: 1    expiration: 1/31/2022    strike: 2800
SPX  Put     quantity: 2    expiration: 2/18/2022    strike: 2350
SPX  Put     quantity: 4    expiration: 3/18/2022    strike: 2725
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 3100
SPX  Put     quantity: -4   expiration: 3/18/2022    strike: 3625
SPX  Put     quantity: -4   expiration: 3/18/2022    strike: 3775
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 3825
SPX  Put     quantity: 2    expiration: 3/18/2022    strike: 4025
SPXW Put     quantity: 2    expiration: 3/31/2022    strike: 3175
SPXW Put     quantity: -2   expiration: 3/31/2022    strike: 3825
SPXW Put     quantity: -2   expiration: 3/31/2022    strike: 3850
SPXW Put     quantity: 2    expiration: 3/31/2022    strike: 4100
SPX  Put     quantity: 4    expiration: 4/14/2022    strike: 2775
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 3150
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 3350
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 3800
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 3900
SPX  Put     quantity: -4   expiration: 4/14/2022    strike: 4050
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4075
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4200
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 4325
SPXW Put     quantity: 2    expiration: 4/29/2022    strike: 3250
SPXW Put     quantity: -4   expiration: 4/29/2022    strike: 4000
SPXW Put     quantity: 2    expiration: 4/29/2022    strike: 4300

***Error*** ONE has an index position in SPX of 4 shares, in trade(s) 287,301, while IB has 15 equivalent SPX shares

***Error*** ONE has a Put position in trade(s) 297, with no matching position in IB:
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 3500

***Error*** ONE has a Put position in trade(s) 285, whose quantity (1) does not match IB quantity (2):
SPXW Put     quantity: 1    expiration: 4/29/2022    strike: 3250

***Error*** ONE has a Put position in trade(s) 298, with no matching position in IB:
SPXW Put     quantity: 2    expiration: 4/29/2022    strike: 3400

***Error*** IB has a Put position with no matching position in ONE
SPX  Put     quantity: 2    expiration: 4/14/2022    strike: 3350
```
