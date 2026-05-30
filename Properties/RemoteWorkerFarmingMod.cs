using HarmonyLib;
using KMod;
using UnityEngine;

namespace RemoteWorkerFarming
{
    public sealed class RemoteWorkerFarmingMod : UserMod2
    {
        public const string ModId = "RemoteWorkerFarming";

        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            Debug.Log($"[{ModId}] Loaded remote farming bridge patches.");
            harmony.PatchAll();
        }
    }
}
