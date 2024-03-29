﻿using Algorand.Indexer.Model;
using Newtonsoft.Json;

namespace AMMStakingBot.Model
{
    public class MiniAssetHoldingWithAsset : MiniAssetHolding
    {
        public ulong AssetId { get; set; }
    }

    public static class MiniAssetHoldingWithAssetExtension
    {
        public static MiniAssetHoldingWithAsset Convert2MiniAssetHoldingWithAsset(this MiniAssetHolding orig, ulong AssetId)
        {
            var txt = JsonConvert.SerializeObject(orig);
            var ret = JsonConvert.DeserializeObject<MiniAssetHoldingWithAsset>(txt);
            if (ret == null) throw new Exception("Error converting MiniAssetHolding");
            ret.AssetId = AssetId;
            return ret;
        }
    }
}
