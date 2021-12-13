# CompareONEToIB.cs

This program compares exported data from OptioneNet Explorer (ONE) to that from Interactive Brokers(IB) to make
sure that the option positions actually held in IB are the ones that are beng modeled by ONE.

The IB data is exported by running IB's Trader WorkStation, opening the Account Window, going to the File menu
and selecting Export Portfolio. When the Export Portfolio/Save dialog pops up, click the Configure Export Settings...
button and when the Trader Workstation Configuration dialog pops up, make sure that Advanced Contract display is checked
and  Include "Exchange" column, Include "Account" column, Include "Account" column, and Include "Account Number" column
are unchecked

This is what the data looks like:

```
Portfolio
Financial Instrument Description,Position,Currency,Market Price,Market Value,Average Price,Unrealized P&L,Realized P&L,Liquidate Last,Security Type,Delta Dollars
SPX    APR2022 4300 P [SPXW  220429P04300000 100],2,USD,119.5072021,23901.44,123.5542635,-809.41,0.00,No,OPT,-246454.66
SPX    APR2022 4000 P [SPXW  220429P04000000 100],-4,USD,75.819664,-30327.87,82.2374865,2567.13,0.00,No,OPT,305432.86
SPX    APR2022 3250 P [SPXW  220429P03250000 100],2,USD,25.7420445,5148.41,27.5892635,-369.44,0.00,No,OPT,-47068.39
SPX    APR2022 4325 P [SPX   220414P04325000 100],2,USD,111.5892257,22317.85,109.4212135,433.60,0.00,No,OPT,-248012.63
```

The ONE data is exported by opening ONE, clicking on Reports, then on the Reports window, clicking on the little filter icon on the Account dropdown
and selecting the account that holds the trades you want to compare with, then clicking the Export button and saving the file.
**Make sure that the Report Type dropdown is set to Detail.**

This is what the data looks like:

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


