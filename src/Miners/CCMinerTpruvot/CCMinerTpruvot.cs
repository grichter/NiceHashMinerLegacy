﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NHM.Common;
using NHM.Common.Enums;
using MinerPlugin;
using MinerPluginToolkitV1.CCMinerCommon;

namespace CCMinerTpruvot
{
    public class CCMinerTpruvot : CCMinerBase
    {
        public CCMinerTpruvot(string uuid) : base(uuid)
        { }

        protected override string AlgorithmName(AlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case AlgorithmType.X16R: return "x16r";
                case AlgorithmType.Lyra2REv3: return "lyra2v3";
            }
            // TODO throw exception
            return "";
        }

        public override async Task<BenchmarkResult> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            var ret = await base.StartBenchmark(stop, benchmarkType);
            if (_algorithmType == AlgorithmType.X16R)
            {
                try
                {
                    foreach (var infoPair in ret.AlgorithmTypeSpeeds)
                    {
                        infoPair.Speed = infoPair.Speed * 0.4563831001472754;
                    }
                }
                catch (Exception)
                {
                }
            }
            return ret;
        }

        public override Tuple<string, string> GetBinAndCwdPaths()
        {
            var pluginRootBins = Paths.MinerPluginsPath(_uuid, "bins");
            var binPath = Path.Combine(pluginRootBins, "ccminer-x64.exe");
            return Tuple.Create(binPath, pluginRootBins);
        }
    }
}
