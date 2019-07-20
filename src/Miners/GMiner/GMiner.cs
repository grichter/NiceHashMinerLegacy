﻿using MinerPlugin;
using MinerPluginToolkitV1;
using MinerPluginToolkitV1.Configs;
using Newtonsoft.Json;
using NHM.Common;
using NHM.Common.Enums;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static NHM.Common.StratumServiceHelpers;

namespace GMinerPlugin
{
    // NOTE: GMiner will NOT run if the VS debugger is attached to NHML. 
    // Detach the debugger to use GMiner.

    // benchmark is 
    public class GMiner : MinerBase
    {
        private const double DevFee = 2.0;
        private HttpClient _httpClient;
        private int _apiPort;
        // command line parts
        private string _devices;

        protected readonly Dictionary<string, int> _mappedDeviceIds = new Dictionary<string, int>();


        public GMiner(string uuid, Dictionary<string, int> mappedDeviceIds) : base(uuid)
        {
            _mappedDeviceIds = mappedDeviceIds;
        }

        protected virtual string AlgorithmName(AlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case AlgorithmType.ZHash:
                    return "144_5";
                case AlgorithmType.Beam:
                    return "150_5";
                case AlgorithmType.GrinCuckaroo29:
                    return "cuckaroo29";
                case AlgorithmType.GrinCuckatoo31:
                    return "grin31";
                case AlgorithmType.CuckooCycle:
                    return "cuckoo29";
                case AlgorithmType.GrinCuckarood29:
                    return "cuckarood29";
                default:
                    return "";
            }
        }

        private string CreateCommandLine(string username)
        {
            // API port function might be blocking
            _apiPort = GetAvaliablePort();

            var algo = AlgorithmName(_algorithmType);

            var urlWithPort = GetLocationUrl(_algorithmType, _miningLocation, NhmConectionType.NONE);
            var split = urlWithPort.Split(':');
            var url = split[0];
            var port = split[1];

            var cmd = $"-a {algo} -s {url} -n {port} -u {username} -d {_devices} -w 0 --api {_apiPort} {_extraLaunchParameters}";

            if (_algorithmType == AlgorithmType.ZHash)
            {
                cmd += " --pers auto";
            }

            return cmd;
        }

        public async override Task<ApiData> GetMinerStatsDataAsync()
        {
            // lazy init
            if (_httpClient == null) _httpClient = new HttpClient();
            var ad = new ApiData();
            try
            {
                var result = await _httpClient.GetStringAsync($"http://127.0.0.1:{_apiPort}/stat");
                var summary = JsonConvert.DeserializeObject<JsonApiResponse>(result);                

                var gpus = _miningPairs.Select(pair => pair.Device);
                var perDeviceSpeedInfo = new Dictionary<string, IReadOnlyList<AlgorithmTypeSpeedPair>>();
                var perDevicePowerInfo = new Dictionary<string, int>();
                var totalSpeed = 0d;
                var totalPowerUsage = 0;
                foreach (var gpu in gpus)
                {
                    var currentDevStats = summary.devices.Where(devStats => devStats.gpu_id == _mappedDeviceIds[gpu.UUID]).FirstOrDefault();
                    if (currentDevStats == null) continue;
                    totalSpeed += currentDevStats.speed;
                    perDeviceSpeedInfo.Add(gpu.UUID, new List<AlgorithmTypeSpeedPair>() { new AlgorithmTypeSpeedPair(_algorithmType, currentDevStats.speed * (1 - DevFee * 0.01)) });
                    var kPower = currentDevStats.power_usage * 1000;
                    totalPowerUsage += kPower;
                    perDevicePowerInfo.Add(gpu.UUID, kPower);
                }
                ad.AlgorithmSpeedsTotal = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, totalSpeed * (1 - DevFee * 0.01)) };
                ad.PowerUsageTotal = totalPowerUsage;
                ad.AlgorithmSpeedsPerDevice = perDeviceSpeedInfo;
                ad.PowerUsagePerDevice = perDevicePowerInfo;
            }
            catch (Exception e)
            {
                Logger.Error(_logGroup, $"Error occured while getting API stats: {e.Message}");
                //CurrentMinerReadStatus = MinerApiReadStatus.NETWORK_EXCEPTION;
            }

            return ad;
        }

        public async override Task<BenchmarkResult> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            // determine benchmark time 
            // settup times
            var benchmarkTime = MinerBenchmarkTimeSettings.ParseBenchmarkTime(new List<int> { 20, 60, 120 }, MinerBenchmarkTimeSettings, _miningPairs, benchmarkType); // in seconds

            // use demo user and disable the watchdog
            var commandLine = CreateCommandLine(MinerToolkit.DemoUserBTC);
            var binPathBinCwdPair = GetBinAndCwdPaths();
            var binPath = binPathBinCwdPair.Item1;
            var binCwd = binPathBinCwdPair.Item2;
            Logger.Info(_logGroup, $"Benchmarking started with command: {commandLine}");
            var bp = new BenchmarkProcess(binPath, binCwd, commandLine, GetEnvironmentVariables());

            double benchHashesSum = 0;
            double benchHashResult = 0;
            int benchIters = 0;
            int targetBenchIters = Math.Max(1, (int)Math.Floor(benchmarkTime / 30d));
            // TODO implement fallback average, final benchmark 
            bp.CheckData = (string data) => {
                var hashrateFoundPair = MinerToolkit.TryGetHashrateAfter(data, "Total Speed:");
                var hashrate = hashrateFoundPair.Item1;
                var found = hashrateFoundPair.Item2;
                if (!found) return new BenchmarkResult { AlgorithmTypeSpeeds = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, benchHashResult) }, Success = false };

                // sum and return
                benchHashesSum += hashrate;
                benchIters++;

                benchHashResult = (benchHashesSum / benchIters) * (1 - DevFee * 0.01);

                return new BenchmarkResult
                {
                    AlgorithmTypeSpeeds = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, benchHashResult) },
                    Success = benchIters >= targetBenchIters
                };
            };

            var benchmarkTimeout = TimeSpan.FromSeconds(benchmarkTime + 5);
            var benchmarkWait = TimeSpan.FromMilliseconds(500);
            var t = MinerToolkit.WaitBenchmarkResult(bp, benchmarkTimeout, benchmarkWait, stop);
            return await t;
        }

        public override Tuple<string, string> GetBinAndCwdPaths()
        {
            var pluginRoot = Path.Combine(Paths.MinerPluginsPath(), _uuid);
            var pluginRootBins = Path.Combine(pluginRoot, "bins");
            var binPath = Path.Combine(pluginRootBins, "miner.exe");
            var binCwd = pluginRootBins;
            return Tuple.Create(binPath, binCwd);
        }

        protected override IEnumerable<MiningPair> GetSortedMiningPairs(IEnumerable<MiningPair> miningPairs)
        {
            var pairsList = miningPairs.ToList();
            // sort by _mappedDeviceIds
            pairsList.Sort((a, b) => _mappedDeviceIds[a.Device.UUID].CompareTo(_mappedDeviceIds[b.Device.UUID]));
            return pairsList;
        }

        protected override void Init()
        {
            var mappedDevIDs = _miningPairs.Select(p => _mappedDeviceIds[p.Device.UUID]);
            _devices = string.Join(" ", mappedDevIDs);
        }

        protected override string MiningCreateCommandLine()
        {
            return CreateCommandLine(_username);
        }
    }
}
