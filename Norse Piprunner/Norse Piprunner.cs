using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class NorsePiprunner : Robot
    {
        [Parameter("Buy", DefaultValue = true)]
        public bool Buy { get; set; }

        [Parameter("Sell", DefaultValue = true)]
        public bool Sell { get; set; }

        [Parameter("Pip Step", DefaultValue = 10, MinValue = 1)]
        public int PipStep { get; set; }

        [Parameter("First Volume", DefaultValue = 1000, MinValue = 1000, Step = 1000)]
        public int FirstVolume { get; set; }

        [Parameter("Max Spread", DefaultValue = 3.0)]
        public double MaxSpread { get; set; }

        [Parameter("Average TP", DefaultValue = 3, MinValue = 1)]
        public int AverageTP { get; set; }

        [Parameter("Volume Exponent", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0)]
        public double VolumeExponent { get; set; }

        private string Label = "piprunner";
        private Position position;
        private DateTime buyOpenTime;
        private DateTime sellOpenTime;
        private int orderStatus;
        private double currentSpread;
        private bool initial_start = true;
        private bool cStop = false;

        protected override void OnStart()
        {
        }
        protected override void OnTick()
        {
            currentSpread = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
            if (activeDirectionCount(TradeType.Buy) > 0)
                TrailBuySL(AverageEntryPrice(TradeType.Buy), AverageTP);
            if (activeDirectionCount(TradeType.Sell) > 0)
                TrailSellSL(AverageEntryPrice(TradeType.Sell), AverageTP);
            if (MaxSpread >= currentSpread && !cStop)
                SimpleLogic();
            DrawDescisionLines();
        }
        protected override void OnError(Error error)
        {
            if (error.Code == ErrorCode.NoMoney)
            {
                cStop = true;
                Print("openning stopped because: not enough money");
            }
        }
        protected override void OnBar()
        {
            RefreshData();
        }
        protected override void OnStop()
        {
            ChartObjects.RemoveAllObjects();
        }
        private void SimpleLogic()
        {
            if (initial_start)
            {
                //Entry Signal previous bar larger than the one before
                if (Buy && activeDirectionCount(TradeType.Buy) == 0 && MarketSeries.Close.Last(1) > MarketSeries.Close.Last(2))
                {
                    orderStatus = OrderSend(TradeType.Buy, Volumizer(FirstVolume));
                    if (orderStatus > 0)
                        buyOpenTime = MarketSeries.OpenTime.Last(0);
                    else
                        Print("First BUY openning error at: ", Symbol.Ask, "Error Type: ", LastResult.Error);
                }
                //Entry signal  previous bar smaller than the one before
                if (Sell && activeDirectionCount(TradeType.Sell) == 0 && MarketSeries.Close.Last(2) > MarketSeries.Close.Last(1))
                {
                    orderStatus = OrderSend(TradeType.Sell, Volumizer(FirstVolume));
                    if (orderStatus > 0)
                        sellOpenTime = MarketSeries.OpenTime.Last(0);
                    else
                        Print("First SELL openning error at: ", Symbol.Bid, "Error Type: ", LastResult.Error);
                }
            }
            Gridernize();
        }


        //Create the Grid sistem based on the PipStep
        private void Gridernize()
        {
            if (activeDirectionCount(TradeType.Buy) > 0)
            {
                if (Math.Round(Symbol.Ask, Symbol.Digits) < Math.Round(GetHighestBuyEntry(TradeType.Buy) - PipStep * Symbol.PipSize, Symbol.Digits) && buyOpenTime != MarketSeries.OpenTime.Last(0))
                {
                    long b_lotS = NextLotSize(TradeType.Buy);
                    orderStatus = OrderSend(TradeType.Buy, Volumizer(b_lotS));
                    if (orderStatus > 0)
                        buyOpenTime = MarketSeries.OpenTime.Last(0);
                    else
                        Print("Next BUY openning error at: ", Symbol.Ask, "Error Type: ", LastResult.Error);
                }
            }
            if (activeDirectionCount(TradeType.Sell) > 0)
            {
                if (Math.Round(Symbol.Bid, Symbol.Digits) > Math.Round(GetLowestSellEntry(TradeType.Sell) + PipStep * Symbol.PipSize, Symbol.Digits) && sellOpenTime != MarketSeries.OpenTime.Last(0))
                {
                    long s_lotS = NextLotSize(TradeType.Sell);
                    orderStatus = OrderSend(TradeType.Sell, Volumizer(s_lotS));
                    if (orderStatus > 0)
                        sellOpenTime = MarketSeries.OpenTime.Last(0);
                    else
                        Print("Next SELL openning error at: ", Symbol.Bid, "Error Type: ", LastResult.Error);
                }
            }
        }
        private int OrderSend(TradeType TrdTp, long iVol)
        {
            int orderStatus = 0;
            if (iVol > 0)
            {
                TradeResult result = ExecuteMarketOrder(TrdTp, Symbol, iVol, Label, 0, 0, 0, "smart_grid");

                if (result.IsSuccessful)
                {
                    Print(TrdTp, "Opened at: ", result.Position.EntryPrice);
                    orderStatus = 1;
                }
                else
                    Print(TrdTp, "Openning Error: ", result.Error);
            }
            else
                Print("Volume calculation error: Calculated Volume is: ", iVol);
            return orderStatus;
        }


        //Trail the stoploss position for a BUY Order
        private void TrailBuySL(double price, int tp)
        {
            foreach (var position in Positions)
            {
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TradeType.Buy)
                    {
                        double? new_tp = Math.Round(price + tp * Symbol.PipSize, Symbol.Digits);
                        if (position.TakeProfit != new_tp)
                            ModifyPosition(position, position.StopLoss, new_tp);
                    }
                }
            }
        }


        //Trail the stoploss position for a SELL Order
        private void TrailSellSL(double price, int tp)
        {
            foreach (var position in Positions)
            {
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TradeType.Sell)
                    {
                        double? new_tp = Math.Round(price - tp * Symbol.PipSize, Symbol.Digits);
                        if (position.TakeProfit != new_tp)
                            ModifyPosition(position, position.StopLoss, new_tp);
                    }
                }
            }
        }

        //Draw the Action lines to illustrate the trades
        private void DrawDescisionLines()
        {
            if (activeDirectionCount(TradeType.Buy) > 1)
            {
                double y = AverageEntryPrice(TradeType.Buy);
                ChartObjects.DrawHorizontalLine("bpoint", y, Colors.Yellow, 2, LineStyle.Dots);
            }
            else
                ChartObjects.RemoveObject("bpoint");
            if (activeDirectionCount(TradeType.Sell) > 1)
            {
                double z = AverageEntryPrice(TradeType.Sell);
                ChartObjects.DrawHorizontalLine("spoint", z, Colors.HotPink, 2, LineStyle.Dots);
            }
            else
                ChartObjects.RemoveObject("spoint");
            ChartObjects.DrawText("pan", botText(), StaticPosition.TopLeft, Colors.Tomato);
        }

        //Text to be printed on Screen
        private string botText()
        {
            string printString = "";
            string BPos = "";
            string SPos = "";
            string spread = "";
            string BTA = "";
            string STA = "";
            double CBPOS = 0;
            double CSPOS = 0;

            CBPOS = activeDirectionCount(TradeType.Buy);
            CSPOS = activeDirectionCount(TradeType.Sell);
            spread = "\nSpread = " + Math.Round(currentSpread, 1);
            if (CBPOS > 0)
                BPos = "\nBuy Positions = " + activeDirectionCount(TradeType.Buy);
            if (CSPOS > 0)
                SPos = "\nSell Positions = " + activeDirectionCount(TradeType.Sell);
            if (activeDirectionCount(TradeType.Buy) > 0)
            {
                double abta = Math.Round((AverageEntryPrice(TradeType.Buy) - Symbol.Bid) / Symbol.PipSize, 1);
                BTA = "\nBuy Target Away = " + abta;
            }
            if (activeDirectionCount(TradeType.Sell) > 0)
            {
                double asta = Math.Round((Symbol.Ask - AverageEntryPrice(TradeType.Sell)) / Symbol.PipSize, 1);
                STA = "\nSell Target Away = " + asta;
            }
            if (currentSpread > MaxSpread)
                printString = "MAX SPREAD EXCEED";
            else
                printString = "Foxy Grid" + BPos + spread + SPos + BTA + STA;
            return (printString);
        }

        //Return the active positions of this bot
        private int ActiveLabelCount()
        {
            int ASide = 0;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                    ASide++;
            }
            return ASide;
        }

        //Return the position count of trades of specific type (BUY/SELL)
        private int activeDirectionCount(TradeType TrdTp)
        {
            int TSide = 0;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TrdTp)
                        TSide++;
                }
            }
            return TSide;
        }

        //The Avarage EtryPrice for all positions of a specific type (SELL/BUY)
        private double AverageEntryPrice(TradeType TrdTp)
        {
            double Result = 0;
            double AveragePrice = 0;
            long Count = 0;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TrdTp)
                    {
                        AveragePrice += position.EntryPrice * position.Volume;
                        Count += position.Volume;
                    }
                }
            }
            if (AveragePrice > 0 && Count > 0)
                Result = Math.Round(AveragePrice / Count, Symbol.Digits);
            return Result;
        }


        private double GetHighestBuyEntry(TradeType TrdTp)
        {
            double GetHighestBuyEntry = 0;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TrdTp)
                    {
                        if (GetHighestBuyEntry == 0)
                        {
                            GetHighestBuyEntry = position.EntryPrice;
                            continue;
                        }
                        if (position.EntryPrice < GetHighestBuyEntry)
                            GetHighestBuyEntry = position.EntryPrice;
                    }
                }
            }
            return GetHighestBuyEntry;
        }


        private double GetLowestSellEntry(TradeType TrdTp)
        {
            double GetLowestSellEntry = 0;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TrdTp)
                    {
                        if (GetLowestSellEntry == 0)
                        {
                            GetLowestSellEntry = position.EntryPrice;
                            continue;
                        }
                        if (position.EntryPrice > GetLowestSellEntry)
                            GetLowestSellEntry = position.EntryPrice;
                    }
                }
            }
            return GetLowestSellEntry;
        }


        private double LastEntry(TradeType TrdTp)
        {
            double LastEntryPrice = 0;
            int APositionID = 0;
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TrdTp)
                    {
                        if (APositionID == 0 || APositionID > position.Id)
                        {
                            LastEntryPrice = position.EntryPrice;
                            APositionID = position.Id;
                        }
                    }
                }
            }
            return LastEntryPrice;
        }


        private long LastVolume(TradeType TrdTp)
        {
            long LastVolumeTraded = 0;
            int APositionID = 0;
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TrdTp)
                    {
                        if (APositionID == 0 || APositionID > position.Id)
                        {
                            LastVolumeTraded = position.Volume;
                            APositionID = position.Id;
                        }
                    }
                }
            }
            return LastVolumeTraded;
        }


        private long clt(TradeType TrdTp)
        {
            long Result = 0;
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TrdTp)
                        Result += position.Volume;
                }
            }
            return Result;
        }


        private int GridCount(TradeType TrdTp1, TradeType TrdTp2)
        {
            double LastEntryPrice = LastEntry(TrdTp2);
            int APositionID = 0;
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                position = Positions[i];
                if (position.Label == Label && position.SymbolCode == Symbol.Code)
                {
                    if (position.TradeType == TrdTp1 && TrdTp1 == TradeType.Buy)
                    {
                        if (Math.Round(position.EntryPrice, Symbol.Digits) <= Math.Round(LastEntryPrice, Symbol.Digits))
                            APositionID++;
                    }
                    if (position.TradeType == TrdTp1 && TrdTp1 == TradeType.Sell)
                    {
                        if (Math.Round(position.EntryPrice, Symbol.Digits) >= Math.Round(LastEntryPrice, Symbol.Digits))
                            APositionID++;
                    }
                }
            }
            return APositionID;
        }


        private long NextLotSize(TradeType TrdRp)
        {
            int current_Volume = GridCount(TrdRp, TrdRp);
            long last_Volume = LastVolume(TrdRp);
            long next_Volume = Symbol.NormalizeVolume(last_Volume * Math.Pow(VolumeExponent, current_Volume));
            return next_Volume;
        }


        private long Volumizer(long vol)
        {
            long volmin = Symbol.VolumeMin;
            long volmax = Symbol.VolumeMax;
            long voltemp = vol;

            if (voltemp < volmin)
                voltemp = volmin;
            if (voltemp > volmax)
                voltemp = volmax;
            return voltemp;
        }
    }
}
