using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Exceptions;

namespace TokenTrader.Initialization
{
    class BookFillIsinParams : BaseIsinParams
    {
        public string BasePriceFormula { get; set; }
        public List<string> Predictors { get; set; }
        public string TradeWithPubKey { get; set; }
        public decimal PredictorToBaseApproachingSpeed { get; set; }
        public long? FullSideDealShiftMinsteps { get; set; }
        public decimal BuyPotentialLimitFiat { get; set; }
        public decimal SellPotentialLimitFiat { get; set; }
        public int OrdersNextActionDelayMuMs { get; set; }

        public string MarketMakingModelName { get; set; }
        public MarketMakingModels MarketMakingModel { get; set; }
        public RandomFillParams RandomFill { get; set; }
        public List<Obligation> Obligations { get; set; }
        public decimal BestObligationSpreadTolerancePerc { get; set; }
        public decimal MinObligationSpreadPerc { get; private set; }

        public bool? UseHedge { get; set; }
        public HedgeParams Hedge { get; set; }

        public decimal VolumeOneSideFiat
        {
            get
            {
                if (MarketMakingModel == MarketMakingModels.RandomFill) return RandomFill.VolumeOneSideFiat;
                if (MarketMakingModel == MarketMakingModels.Obligations) return Obligations.Sum(obligation => obligation.VolumeOneSideFiat);
                throw new ConfigErrorsException($"Wrong MarketMakingModel: {MarketMakingModel} in IsinsToTrade.");
            }
        }

        public override void Verify()
        {
            base.Verify();

            if (string.IsNullOrEmpty(BasePriceFormula)) throw new ConfigErrorsException("BasePriceFormula in IsinsToTrade was not set properly.");
            if (Predictors == null || Predictors.Count == 0) throw new ConfigErrorsException("Predictors in IsinsToTrade was not set properly.");
            if (string.IsNullOrEmpty(TradeWithPubKey)) throw new ConfigErrorsException("TradeWithPubKey in IsinsToTrade was not set properly.");            

            if (PredictorToBaseApproachingSpeed <= 0) throw new ConfigErrorsException("PredictorToBaseApproachingSpeed in IsinsToTrade was not set properly.");
            if (FullSideDealShiftMinsteps == null) throw new ConfigErrorsException("FullSideDealShiftMinsteps in IsinsToTrade was not set properly.");
            if (BuyPotentialLimitFiat <= 0) throw new ConfigErrorsException("BuyPotentialLimitFiat in IsinsToTrade was not set properly.");
            if (SellPotentialLimitFiat <= 0) throw new ConfigErrorsException("SellPotentialLimitFiat in IsinsToTrade was not set properly.");
            if (OrdersNextActionDelayMuMs <= 0) throw new ConfigErrorsException("OrdersNextActionDelayMuMs in IsinsToTrade was not set properly.");

            if (string.IsNullOrEmpty(MarketMakingModelName)) throw new ConfigErrorsException("MarketMakingModel in IsinsToTrade was not set properly.");
            if (MarketMakingModelName == "random_fill")
            {
                if (RandomFill == null) throw new ConfigErrorsException("RandomFill in IsinsToTrade was not set properly.");
                RandomFill.Verify();
            }
            else if (MarketMakingModelName == "obligations")
            {
                if (Obligations == null || Obligations.Count == 0) throw new ConfigErrorsException("Obligations in IsinsToTrade was not set properly.");
                foreach (Obligation obligation in Obligations) obligation.Verify();

                if (BestObligationSpreadTolerancePerc <= 0) throw new ConfigErrorsException("BestObligationSpreadTolerancePerc in IsinsToTrade was not set properly.");
            }
            else throw new ConfigErrorsException($"Wrong MarketMakingModel: {MarketMakingModel} in IsinsToTrade.");

            if (UseHedge == null) throw new ConfigErrorsException("UseHedge in IsinsToTrade was not set properly.");
            if (UseHedge.Value)
            {
                if (Hedge == null) throw new ConfigErrorsException("Hedge in IsinsToTrade was not set properly.");
                Hedge.Verify();
            }
        }

        public override void SetDerivativeSettings()
        {
            base.SetDerivativeSettings();

            if (MarketMakingModelName == "random_fill")
            {
                MarketMakingModel = MarketMakingModels.RandomFill;
            }
            else if (MarketMakingModelName == "obligations")
            {
                //на всякий случай, если вдруг ошиблись в конфиге, сортируем по спрэду по возрастанию. чтобы потом ничего не поломалось. 
                Obligations.Sort();
                MarketMakingModel   = MarketMakingModels.Obligations;
                MinObligationSpreadPerc = Obligations.Min(obligation => obligation.SpreadOneSidePerc);
            }
        }
    }
}