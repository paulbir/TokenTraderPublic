using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BinanceConnector;
using BitfinexConnector;
using BitMEXConnector;
using BitstampConnector;
using CGCXConnector;
using CoinFlexConnector;
using CREXConnector;
using DeribitConnector;
using DummyConnector;
using DutyFlyConnector;
using FineryConnector;
using Flee.PublicTypes;
using GlobitexConnector;
using HitBTCConnector;
using IDaxConnector;
using KucoinConnector;
using Newtonsoft.Json.Linq;
using OceanConnector;
using QryptosConnector;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedTools;
using SharedTools.Interfaces;
using SimpleInjector;
using SimpleInjector.Diagnostics;
using TmexConnector;
using TokenTrader.Interfaces;
using TokenTrader.TradeModels;
using TokenTrader.TradeModels.BookFill;
using WoortonConnector;
using WoortonV2Connector;
using XenaConnector;

namespace TokenTrader.Initialization
{
    static class Initializer
    {
        public static Container               Container;
        public static CancellationTokenSource ParentTokenSource { get; } = new CancellationTokenSource();

        public static void Register()
        {
            //IConfigurationRoot config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: true).Build();
            //string tradeModel = config.GetValue<string>("TradeModel");
            //config.Bind(settings, options => options.BindNonPublicProperties = true);

            string       settingsPath    = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            string       appSettingsFile = File.ReadAllText(settingsPath);
            JObject      settingsObj     = JObject.Parse(appSettingsFile);
            string       tradeModel      = (string)settingsObj.SelectToken("TradeModel");
            BaseSettings settings        = CreateSettingsOfTradeModelType(tradeModel, settingsObj);

            settings.Verify();
            settings.SetDerivativeSettings();

            Container = new Container();

            //loggerGlob = CreateLogger(settings);
            Container.RegisterInstance(CreateLogger(settings));
            Container.RegisterInstance<ICancellationTokenProvider>(new ConcreteCancellationTokenProvider(ParentTokenSource));
            RegisterIdGenerator();
            Container.RegisterSingleton<IUdpSender, UdpSender>();
            Container.RegisterSingleton<IUdpReceiver, UdpReceiver>();
            Container.RegisterInstance(CreateTradeConnectorContext(settings));
            Container.RegisterInstance(CreateTradeModelContext(settings));
            RegisterTradeModel(settings, Container);
            RegisterTradeConnectors(settings, Container);
            ConditionalRegister(settings, Container, tradeModel);

            Container.Verify();
        }

        static BaseSettings CreateSettingsOfTradeModelType(string tradeModel, JObject settingsObj)
        {
            switch (tradeModel)
            {
                case "SimultaneousTrades": return settingsObj.ToObject<SimultaneousTradesSettingsContainer>();
                case "BookFill":           return settingsObj.ToObject<BookFillSettingsContainer>();
                default:                   throw new ConfigErrorsException($"Unknown trade model type {tradeModel}");
            }
        }

        static void RegisterIdGenerator()
        {
            Registration registration = Lifestyle.Transient.CreateRegistration(typeof(IdGeneratorOneBased), Container);
            Container.AddRegistration(typeof(IIdGenerator), registration);
            registration.SuppressDiagnosticWarning(DiagnosticType.LifestyleMismatch, "won't verify otherwise");
        }

        static ILogger CreateLogger(BaseSettings settings)
        {
            string logOutput = settings.LogOutput;

            if (settings.CreateNewLogWithDate.Value && settings.LogOutput != "console") logOutput = DateTime.Now.ToString("yyyy-MM-dd_") + logOutput;

            return new Logger(logOutput, settings.AppendLog.Value);
        }

        static ITradeConnectorContext CreateTradeConnectorContext(BaseSettings settings)
        {
            ITradeModelSettings tradeModelSettings;
            switch (settings.TradeModel)
            {
                case "SimultaneousTrades":
                    tradeModelSettings = ((SimultaneousTradesSettingsContainer)settings).TradeModelSettings;
                    break;
                case "BookFill":
                    tradeModelSettings = ((BookFillSettingsContainer)settings).TradeModelSettings;
                    break;
                default: throw new ConfigErrorsException($"Unknown trade model type {settings.TradeModel}");
            }

            return new TradeConnectorContext(tradeModelSettings.IsMarginMarket.Value);
        }

        static ITradeModelContext CreateTradeModelContext(BaseSettings settings)
        {
            switch (settings.TradeModel)
            {
                case "SimultaneousTrades": return CreateSimultaneousTradesContext(settings);

                case "BookFill": return CreateBookFillContext(settings);

                default: throw new ConfigErrorsException($"Unknown trade model type {settings.TradeModel}");
            }
        }

        static ITradeModelContext CreateSimultaneousTradesContext(BaseSettings settings)
        {
            SimultaneousTradesSettings simultaneousTradesSettings             = ((SimultaneousTradesSettingsContainer)settings).TradeModelSettings;
            var                        dataConnectorContexts                  = new Dictionary<string, DataConnectorContext>();
            var                        conversionToFiatIsinByTradeIsin        = new Dictionary<string, (string isin, string exchange)>();
            var                        additionalIsinsToGetFromTradeConnector = new HashSet<string>();

            foreach (SimultaneousTradesIsinParams isinToTrade in simultaneousTradesSettings.IsinsToTrade)
            {
                (string dataConnectorName, string dataIsin) = SplitDataIsin(isinToTrade.Isin, isinToTrade.ConvertToFiatIsin);
                AddDataIsinAndConnector(settings.TradeConnector, dataConnectorContexts, additionalIsinsToGetFromTradeConnector, dataConnectorName, dataIsin);
                conversionToFiatIsinByTradeIsin.Add(isinToTrade.Isin, (dataIsin, dataConnectorName));
            }

            return new SimultaneousTradesContext(settings.TradeConnectorsSettings,
                                                 dataConnectorContexts.Values.ToList(),
                                                 conversionToFiatIsinByTradeIsin,
                                                 simultaneousTradesSettings.IsinsToTrade.ToDictionary(keySelector: isinToTrade => isinToTrade.Isin,
                                                                                                      elementSelector: isinToTrade => isinToTrade),
                                                 additionalIsinsToGetFromTradeConnector.ToList(),
                                                 simultaneousTradesSettings.MaxSpreadToChooseMidPricePerc,
                                                 simultaneousTradesSettings.MinBasePriceOffsetFromBestMinsteps,
                                                 simultaneousTradesSettings.MaxTradeDelaySec,
                                                 holdBuyOrder: simultaneousTradesSettings.HoldBuyOrder.Value,
                                                 holdSellOrder: simultaneousTradesSettings.HoldSellOrder.Value,
                                                 safeBuySideMode: simultaneousTradesSettings.SafeBuySideMode.Value,
                                                 useVWAP: simultaneousTradesSettings.UseVWAP.Value,
                                                 enableSlowDelayMode: simultaneousTradesSettings.EnableSlowDelayMode.Value,
                                                 enableHighVolumeMode: simultaneousTradesSettings.EnableHighVolumeMode.Value,
                                                 baseQtySigmaFrac: simultaneousTradesSettings.BaseQtySigmaFrac,
                                                 slowDownOnNarrowSpread: simultaneousTradesSettings.SlowDownOnNarrowSpread.Value,
                                                 simultaneousTradesSettings.MaxSpreadForReadyPricesPerc,
                                                 simultaneousTradesSettings.IsMarginMarket.Value,
                                                 simultaneousTradesSettings.NoBookCrossCheckVariables,
                                                 simultaneousTradesSettings.StopOnStuckBookTimeoutSec);
        }

        static ITradeModelContext CreateBookFillContext(BaseSettings settings)
        {
            var              bookFillSettingsContainer = (BookFillSettingsContainer)settings;
            BookFillSettings bookFillSettings          = bookFillSettingsContainer.TradeModelSettings;

            var tradeConnectorIsins             = new HashSet<string>();
            var hedgeConnectorIsins             = new HashSet<string>();
            var dataConnectorContexts           = new Dictionary<string, DataConnectorContext>();
            var conversionToFiatIsinByTradeIsin = new Dictionary<string, (string isin, string exchange)>();
            var baseFormulasByVariable          = new Dictionary<string, List<FormulaContext>>();
            var predictorFormulasByVariable     = new Dictionary<string, List<FormulaContext>>();
            var baseFormulaByIsinToTrade        = new Dictionary<string, FormulaContext>();
            var predictorFormulasByIsinToTrade  = new Dictionary<string, List<FormulaContext>>();
            var numberOnlyVariables             = new HashSet<string>();
            var nextActionDelayByVariable       = new Dictionary<string, int>();

            ThrowOnConnectorsAndSettingsMismatch(bookFillSettings.IsinsToTrade.Select(isinToTrade => isinToTrade.TradeWithPubKey),
                                                 settings.TradeConnectorsSettings.Select(connectorSettings => connectorSettings.PubKey),
                                                 "Trade");

            ThrowOnConnectorsAndSettingsMismatch(bookFillSettings.IsinsToTrade.Where(isinToTrade => isinToTrade.UseHedge.Value).
                                                                  Select(isinToTrade => isinToTrade.Hedge.HedgeWithPubKey),
                                                 bookFillSettingsContainer.HedgeConnectorsSettings?.Select(connectorSettings => connectorSettings.PubKey),
                                                 "Hedge");

            foreach (BookFillIsinParams isinToTrade in bookFillSettings.IsinsToTrade)
            {
                if (isinToTrade.UseHedge.Value) hedgeConnectorIsins.Add(isinToTrade.Hedge.HedgeWithIsin);

                (string dataConnectorName, string dataIsin) = SplitDataIsin(isinToTrade.Isin, isinToTrade.ConvertToFiatIsin);
                AddDataIsinAndConnector(settings.TradeConnector,
                                        dataConnectorContexts,
                                        tradeConnectorIsins,
                                        dataConnectorName,
                                        dataIsin,
                                        bookFillSettingsContainer.HedgeConnector,
                                        hedgeConnectorIsins);
                conversionToFiatIsinByTradeIsin.Add(isinToTrade.Isin, (dataIsin, dataConnectorName));

                //заполняем исины с для торгового коннектора и коннекторов данных. создаём соответствие формул входящим в них переменным.
                //всё это для базовой формулы
                FormulaContext baseFormulaContext = FillContextsFromFormula(settings.TradeConnector,
                                                                            isinToTrade.Isin,
                                                                            isinToTrade.BasePriceFormula,
                                                                            bookFillSettings.VariableSubstRealMap,
                                                                            isinToTrade.OrdersNextActionDelayMuMs,
                                                                            dataConnectorContexts,
                                                                            tradeConnectorIsins,
                                                                            baseFormulasByVariable,
                                                                            numberOnlyVariables,
                                                                            nextActionDelayByVariable,
                                                                            bookFillSettingsContainer.HedgeConnector,
                                                                            hedgeConnectorIsins);
                baseFormulaByIsinToTrade.Add(isinToTrade.Isin, baseFormulaContext);

                var predictorFormulas = new List<FormulaContext>();

                //заполняем исины с для торгового коннектора и коннекторов данных. создаём соответствие формул входящим в них переменным.
                //всё это для каждой формулы предиктора
                foreach (string predictor in isinToTrade.Predictors)
                {
                    FormulaContext predictorFormulaContext = FillContextsFromFormula(settings.TradeConnector,
                                                                                     isinToTrade.Isin,
                                                                                     predictor,
                                                                                     bookFillSettings.VariableSubstRealMap,
                                                                                     isinToTrade.OrdersNextActionDelayMuMs,
                                                                                     dataConnectorContexts,
                                                                                     tradeConnectorIsins,
                                                                                     predictorFormulasByVariable,
                                                                                     numberOnlyVariables,
                                                                                     nextActionDelayByVariable,
                                                                                     bookFillSettingsContainer.HedgeConnector,
                                                                                     hedgeConnectorIsins);

                    predictorFormulas.Add(predictorFormulaContext);
                }

                predictorFormulasByIsinToTrade.Add(isinToTrade.Isin, predictorFormulas);
            }

            //var test = new HashSet<FormulaContext>(baseFormulasByVariable.Values.SelectMany(context => context));
            //test.UnionWith(predictorFormulasByVariable.Values.SelectMany(context => context));
            return new BookFillContext(settings.TradeConnectorsSettings,
                                       bookFillSettingsContainer.HedgeConnectorsSettings,
                                       dataConnectorContexts.Values.ToList(),
                                       conversionToFiatIsinByTradeIsin,
                                       bookFillSettings.IsinsToTrade.ToDictionary(keySelector: isinToTrade => isinToTrade.Isin,
                                                                                  elementSelector: isinToTrade => isinToTrade),
                                       tradeConnectorIsins.ToList(),
                                       hedgeConnectorIsins.ToList(),
                                       baseFormulasByVariable,
                                       predictorFormulasByVariable,
                                       baseFormulaByIsinToTrade,
                                       predictorFormulasByIsinToTrade,
                                       numberOnlyVariables,
                                       nextActionDelayByVariable,
                                       bookFillSettings.VariableSubstRealMap,
                                       bookFillSettings.MaxSpreadForReadyPricesPerc,
                                       bookFillSettings.IsMarginMarket.Value,
                                       bookFillSettings.UseUdp.Value,
                                       bookFillSettings.UDPListenPort,
                                       bookFillSettings.UDPSendPort,
                                       bookFillSettings.CheckPricesMatch.Value,
                                       bookFillSettings.InstanceName,
                                       bookFillSettings.NoBookCrossCheckVariables,
                                       bookFillSettings.StopOnStuckBookTimeoutSec);

            void ThrowOnConnectorsAndSettingsMismatch(IEnumerable<string> settingsPubKeys, IEnumerable<string> connectorsPubKeys, string type)
            {
                HashSet<string> settingsPubKeysSet   = settingsPubKeys.ToHashSet();
                HashSet<string> connectorsPubKeysSet = settingsPubKeys.ToHashSet();

                //чтобы не было в исинах ключей, которых нет в сеттингах коннекторов. и не было сеттингов коннекторов, которыми никто 
                // не хочет пользоваться.
                if (!settingsPubKeysSet.SetEquals(connectorsPubKeysSet))
                    throw new ConfigErrorsException($"There must be one-to-one correspondence between {type}WithPubkeys " +
                                                    $"from IsinsToTrade and PubKeys from {type}ConnectorSettings.");
            }
        }

        static FormulaContext FillContextsFromFormula(string                                   tradeConnector,
                                                      string                                   isinToTrade,
                                                      string                                   formula,
                                                      ConcurrentMap<string, string>            variableSubstMap,
                                                      int                                      nextActionDelay,
                                                      Dictionary<string, DataConnectorContext> dataConnectorContexts,
                                                      HashSet<string>                          tradeConnectorIsins,
                                                      Dictionary<string, List<FormulaContext>> formulasByVariable,
                                                      HashSet<string>                          numberOnlyVariables,
                                                      Dictionary<string, int>                  nextActionDelayByVariable,
                                                      string                                   hedgeConnector,
                                                      HashSet<string>                          hedgeConnectorIsins)
        {
            var variableNames     = new HashSet<string>();
            var expressionContext = new ExpressionContext();
            expressionContext.Variables.ResolveVariableType += ResolveVariableType;
            IGenericExpression<decimal> expression     = expressionContext.CompileGeneric<decimal>(formula);
            var                         formulaContext = new FormulaContext(isinToTrade, expression);

            //если формула состоит только из чисел
            if (variableNames.Count == 0)
            {
                AddFormulasByVariable(expression.Text, formulaContext);
                numberOnlyVariables.Add(expression.Text);
                AddNextActionDelayByVariable(nextActionDelay, expression.Text);
            }
            else
            {
                //по всем переменным из формулы
                foreach (string variableName in variableNames)
                {
                    expressionContext.Variables[variableName] = decimal.Zero;

                    if (!variableSubstMap.Forward.TryGetValue(variableName, out string actualFullIsin)) actualFullIsin = variableName;
                    (string dataConnectorName, string dataIsin) = SplitDataIsin(isinToTrade, actualFullIsin);

                    //запоминаем для каждой переменной её коннектор и исин
                    AddDataIsinAndConnector(tradeConnector,
                                            dataConnectorContexts,
                                            tradeConnectorIsins,
                                            dataConnectorName,
                                            dataIsin,
                                            hedgeConnector,
                                            hedgeConnectorIsins);

                    AddFormulasByVariable(variableName, formulaContext);
                    AddNextActionDelayByVariable(nextActionDelay, variableName);
                }
            }

            if (formulaContext.IsNumFormula) formulaContext.SetInitialValuesForNumFormula();
            return formulaContext;

            void ResolveVariableType(object sender, ResolveVariableTypeEventArgs e)
            {
                variableNames.Add(e.VariableName);
                e.VariableType = typeof(decimal);
            }

            void AddFormulasByVariable(string variableName, FormulaContext localFormulaContext)
            {
                //добавляем соответствие формулы каждой входящей в неё переменной
                if (!formulasByVariable.TryGetValue(variableName, out List<FormulaContext> formulaContexts))
                {
                    formulaContexts = new List<FormulaContext>();
                    formulasByVariable.Add(variableName, formulaContexts);
                }

                formulaContexts.Add(localFormulaContext);
            }

            void AddNextActionDelayByVariable(int nextActionDelayParam, string variableName)
            {
                //пишем в dictionary наименьший delay для переменной. это нужно для того, чтобы потом с максимальной частотой посылать обновления
                // в очередь сообщений с VariableData. а внутри обработчика слишком частые обновления будут отброшены.
                if (!nextActionDelayByVariable.TryGetValue(variableName, out int nextActionDelayLocal))
                    nextActionDelayByVariable.Add(variableName, nextActionDelayParam);
                else if (nextActionDelayParam < nextActionDelayLocal) nextActionDelayByVariable[variableName] = nextActionDelayParam;
            }
        }

        static void AddDataIsinAndConnector(string                                   tradeConnectorName,
                                            Dictionary<string, DataConnectorContext> dataConnectorContexts,
                                            HashSet<string>                          tradeConnectorIsins,
                                            string                                   dataConnectorName,
                                            string                                   dataIsin,
                                            string                                   hedgeConnectorName  = "",
                                            HashSet<string>                          hedgeConnectorIsins = null)
        {
            if (dataConnectorName == tradeConnectorName)
            {
                tradeConnectorIsins.Add(dataIsin);
                return;
            }

            if (dataConnectorName == hedgeConnectorName)
            {
                hedgeConnectorIsins.Add(dataIsin);
                return;
            }

            if (!dataConnectorContexts.TryGetValue(dataConnectorName, out DataConnectorContext connectorContext))
            {
                IDataConnector dataConnector = CreateDataConnector(dataConnectorName);
                connectorContext = new DataConnectorContext(dataConnector);
                dataConnectorContexts.Add(dataConnectorName, connectorContext);
            }

            connectorContext.IsinsToGet.Add(dataIsin);
        }

        static (string connectorName, string isin) SplitDataIsin(string tradedIsin, string dataIsin)
        {
            string[] tokens = dataIsin.Split('_');
            if (tokens.Length < 2) throw new ConfigErrorsException($"Wrong data isin {dataIsin} for traded isin {tradedIsin}.");
            string connectorName = tokens[0];

            string isin = string.Join('_', tokens.Skip(1));
            return (connectorName, isin);
        }

        static IDataConnector CreateDataConnector(string connectorName)
        {
            IDataConnector dataConnector;
            switch (connectorName)
            {
                case "hitbtc":
                    dataConnector = new HitBTCClient();
                    break;
                case "bitfinex":
                    dataConnector = new BitfinexClient();
                    break;
                case "cgcx":
                    dataConnector = new CGCXClient();
                    break;
                case "idax":
                    dataConnector = new IDaxClient();
                    break;
                case "globitex":
                    dataConnector = new GlobitexClient();
                    break;
                case "qryptos":
                    dataConnector = new QryptosClient();
                    break;
                case "kucoin":
                    dataConnector = new KucoinClient();
                    break;
                case "xena":
                    dataConnector = new XenaClient(null);
                    break;
                case "crex":
                    dataConnector = new CREXClient();
                    break;
                case "dutyfly":
                    dataConnector = new DutyFlyClient();
                    break;
                case "bitmex":
                    dataConnector = new BitMEXClient();
                    break;
                case "tmex":
                    dataConnector = new TmexClient();
                    break;
                case "deribit":
                    dataConnector = new DeribitClient();
                    break;
                case "binance":
                    dataConnector = new BinanceClient();
                    break;
                case "bitstamp":
                    dataConnector = new BitstampClient();
                    break;
                case "coinflex":
                    dataConnector = new CoinFlexClient();
                    break;
                case "woorton":
                    dataConnector = new WoortonClient();
                    break;
                case "finery":
                    dataConnector = new FineryClient();
                    break;
                case "ocean":
                    dataConnector = new OceanClient();
                    break;
                case "woortonv2":
                    dataConnector = new WoortonV2Client();
                    break;
                case "dummy":
                    dataConnector = new DummyClient();
                    break;

                default: throw new ConfigErrorsException($"Unknown data connector type {connectorName}");
            }

            return dataConnector;
        }

        static void RegisterTradeModel(BaseSettings settings, Container container)
        {
            Registration registration;

            switch (settings.TradeModel)
            {
                case "SimultaneousTrades":
                    registration = Lifestyle.Singleton.CreateRegistration(typeof(SimultaneousTrades), container);

                    //container.RegisterSingleton<ITradeModel, SimultaneousTrades>();
                    break;
                case "BookFill":
                    registration = Lifestyle.Singleton.CreateRegistration(typeof(BookFill), container);

                    //container.RegisterSingleton<ITradeModel, BookFill>();
                    break;
                default: throw new ConfigErrorsException($"Unknown trade model type {settings.TradeModel}");
            }

            container.AddRegistration(typeof(ITradeModel), registration);
            registration.SuppressDiagnosticWarning(DiagnosticType.LifestyleMismatch, "Iterate object collection in constructor");
        }

        static void RegisterTradeConnectors(BaseSettings settings, Container container)
        {
            switch (settings.TradeConnector)
            {
                case "hitbtc":
                    CreateCollectionRegistrations<ITradeConnector, HitBTCClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "cgcx":
                    CreateCollectionRegistrations<ITradeConnector, CGCXClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "idax":
                    CreateCollectionRegistrations<ITradeConnector, IDaxClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "qryptos":
                    CreateCollectionRegistrations<ITradeConnector, QryptosClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "globitex":
                    CreateCollectionRegistrations<ITradeConnector, GlobitexClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "kucoin":
                    CreateCollectionRegistrations<ITradeConnector, KucoinClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "xena":
                    CreateCollectionRegistrations<ITradeConnector, XenaClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "crex":
                    CreateCollectionRegistrations<ITradeConnector, CREXClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "dutyfly":
                    CreateCollectionRegistrations<ITradeConnector, DutyFlyClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "tmex":
                    CreateCollectionRegistrations<ITradeConnector, TmexClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "coinflex":
                    CreateCollectionRegistrations<ITradeConnector, CoinFlexClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "woorton":
                    CreateCollectionRegistrations<ITradeConnector, WoortonClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "finery":
                    CreateCollectionRegistrations<ITradeConnector, FineryClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "ocean":
                    CreateCollectionRegistrations<ITradeConnector, OceanClient>(container, settings.TradeConnectorsSettings.Count);
                    break;
                case "woortonv2":
                    CreateCollectionRegistrations<ITradeConnector, WoortonV2Client>(container, settings.TradeConnectorsSettings.Count);
                    break;

                default: throw new ConfigErrorsException($"Unknown trade connector type {settings.TradeConnector}");
            }

            //registration?.SuppressDiagnosticWarning(DiagnosticType.LifestyleMismatch, "won't verify otherwise");
        }

        static void ConditionalRegister(BaseSettings settings, Container container, string tradeModel)
        {
            if (tradeModel != "BookFill") return;
            var bookFillSettingsContainer = (BookFillSettingsContainer)settings;
            if (string.IsNullOrEmpty(bookFillSettingsContainer.HedgeConnector)) return;

            switch (bookFillSettingsContainer.HedgeConnector)
            {
                case "woortonv2":
                    CreateCollectionRegistrations<IHedgeConnector, WoortonV2Client>(container, bookFillSettingsContainer.HedgeConnectorsSettings.Count);
                    break;
                case "woorton":
                    CreateCollectionRegistrations<IHedgeConnector, WoortonClient>(container, bookFillSettingsContainer.HedgeConnectorsSettings.Count);
                    break;
                case "finery":
                    CreateCollectionRegistrations<IHedgeConnector, FineryClient>(container, bookFillSettingsContainer.HedgeConnectorsSettings.Count);
                    break;
                case "ocean":
                    CreateCollectionRegistrations<IHedgeConnector, OceanClient>(container, bookFillSettingsContainer.HedgeConnectorsSettings.Count);
                    break;

                default: throw new ConfigErrorsException($"Unknown trade connector type {bookFillSettingsContainer.HedgeConnector}");
            }
        }

        static void CreateCollectionRegistrations<TInterface, TConcrete>(Container container, int numConnectors)
            where TConcrete : class where TInterface : class
        {
            //регистрируем столько торговых коннекторов, сколько запрашивается        
            //IEnumerable<Type> connectorTypes = Enumerable.Range(1, numConnectors).Select(i => typeof(TConcrete));

            var registrations = new List<Registration>();
            for (int i = 0; i < numConnectors; i++)
            {
                Registration registration = Lifestyle.Transient.CreateRegistration<TConcrete>(container);
                registration.SuppressDiagnosticWarning(DiagnosticType.LifestyleMismatch, "won't run otherwise.");
                registrations.Add(registration);
            }

            //container.AddRegistration(typeof(TConcrete), registration);
            container.Collection.Register<TInterface>(registrations);

            //container.Collection.Register<TInterface>(connectorTypes);
            //return registration;
        }
    }
}