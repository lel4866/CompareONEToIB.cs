# CompareONEToIB.cs

This program compares exported data from OptioneNet Explorer (ONE) to that from Interactibe Brokers(IB) to make
sure that the option positions actually held in IB are the ones that are beng modeled by ONE.

The IB data is exported by running IB's Trader WorkStation, openinh the Account Window, goong to the File menu
and selecting Export Portfolio. THis is what the data looks like:

```
Portfolio
Account,Financial Instrument Description,Exchange,Position,Currency,Market Price,Market Value,Average Price,Unrealized P&L,Realized P&L,Liquidate Last,Security Type,Delta Dollars
UXXXXXXX,SPX    APR2022 4300 P [SPXW  220429P04300000 100],CBOE,2,USD,123.0286484,24605.73,123.5542635,-105.12,0.00,No,OPT,-246551.12
UXXXXXXX,SPX    APR2022 4000 P [SPXW  220429P04000000 100],CBOE,-4,USD,79.0655136,-31626.21,82.2374865,1268.79,0.00,No,OPT,309447.06
UXXXXXXX,SPX    APR2022 3250 P [SPXW  220429P03250000 100],CBOE,2,USD,27.4843445,5496.87,27.5892635,-20.98,0.00,No,OPT,-48976.59
UXXXXXXX,SPX    APR2022 4325 P [SPX   220414P04325000 100],CBOE,2,USD,115.3174286,23063.49,109.4212135,1179.24,0.00,No,OPT,-248383.25
UXXXXXXX,SPX    APR2022 4200 P [SPX   220414P04200000 100],CBOE,2,USD,95.0227967,19004.56,118.2114235,-4637.73,0.00,No,OPT,-202399.81
```

The ONE data is exported by opening ONE, clicking on Reports, then on the Reports window, clicking on the little filter icon on the Account dropdown
and selecting the account that holds the trades you want to compare with, than clicking the Export button and saving the file. This is what the data looks like:

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


