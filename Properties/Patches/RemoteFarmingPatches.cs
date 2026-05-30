using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RemoteWorkerFarming.Patches
{
    internal static class RemoteFarmRegistry
    {
        internal static readonly FieldInfo HarvestableChoreField =
            AccessTools.Field(typeof(Harvestable), "chore");

        internal static readonly FieldInfo UprootableChoreField =
            AccessTools.Field(typeof(Uprootable), "chore");

        private static readonly ConditionalWeakTable<Workable, RemoteFarmWorkTarget> providers =
            new ConditionalWeakTable<Workable, RemoteFarmWorkTarget>();

        internal static void Register(Workable workable, FieldInfo choreField)
        {
            if (workable == null || choreField == null)
            {
                return;
            }

            if (providers.TryGetValue(workable, out _))
            {
                return;
            }

            var provider = new RemoteFarmWorkTarget(workable, choreField);
            providers.Add(workable, provider);
            Components.RemoteDockWorkTargets.Add(workable.GetMyWorldId(), provider);
        }

        internal static void Unregister(Workable workable)
        {
            if (workable == null)
            {
                return;
            }

            if (providers.TryGetValue(workable, out RemoteFarmWorkTarget provider))
            {
                providers.Remove(workable);
                Components.RemoteDockWorkTargets.Remove(workable.GetMyWorldId(), provider);
            }
        }
    }

    [HarmonyPatch(typeof(Harvestable), "OnSpawn")]
    internal static class Harvestable_OnSpawn_Patch
    {
        private static void Postfix(Harvestable __instance)
        {
            RemoteFarmRegistry.Register(__instance, RemoteFarmRegistry.HarvestableChoreField);
        }
    }

    [HarmonyPatch(typeof(Harvestable), "OnCleanUp")]
    internal static class Harvestable_OnCleanUp_Patch
    {
        private static void Postfix(Harvestable __instance)
        {
            RemoteFarmRegistry.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(Uprootable), "OnSpawn")]
    internal static class Uprootable_OnSpawn_Patch
    {
        private static void Postfix(Uprootable __instance)
        {
            RemoteFarmRegistry.Register(__instance, RemoteFarmRegistry.UprootableChoreField);
        }
    }

    [HarmonyPatch(typeof(Uprootable), "OnCleanUp")]
    internal static class Uprootable_OnCleanUp_Patch
    {
        private static void Postfix(Uprootable __instance)
        {
            RemoteFarmRegistry.Unregister(__instance);
        }
    }

    // 崩溃修复 + 收获动画：
    // 植物的 Harvestable/Uprootable 使用 multitoolContext("harvest")，
    // 因此 StandardWorker.StartWork 会走 multitool 路径并 StartSM()，
    // 而 MultitoolController.InitializeStates 第一步即 ToggleSnapOn("dig")，
    // 它会调用 worker.Get<SnapOn>().AttachSnapOnByName("dig")。坞派出的
    // RemoteWorker 没有 SnapOn 组件，于是抛出
    // "RemoteWorker does not have component SnapOn" 并崩溃。
    //
    // 解决思路（参考侦查者 ScoutRover/BaseRover 与复制人 BaseMinionConfig）：
    // RemoteWorker 本质是"复制人骨架"(body_comp_default + snapTo_rgtHand)，
    // 因此最自然的做法不是关闭 multitool 路径，而是把复制人现成的装配补齐：
    //   1) 补加载 anim_construction_default_kanim —— 复制人的挥动作 kanim，
    //      含 multitool 路径需要的 dig_fwd_pre/loop/pst 等动画；RemoteWorker
    //      默认未加载，缺它会"读条无动画"。
    //   2) 照抄复制人的 SnapOn 配置（snapTo_rgtHand 上挂各 context 对应工具，
    //      harvest/tend -> plant_harvester_gun_kanim），既修复崩溃，又让机器人
    //      真正"手持收割枪挥动"，视觉与复制人一致。
    // 复制人本身不受影响（其 prefab 是 MinionConfig，不是 RemoteWorkerConfig）。
    [HarmonyPatch(typeof(RemoteWorkerConfig), nameof(RemoteWorkerConfig.CreatePrefab))]
    internal static class RemoteWorkerConfig_CreatePrefab_Patch
    {
        private static void Postfix(GameObject __result)
        {
            if (__result == null)
            {
                return;
            }

            EnsureWorkAnimsLoaded(__result);
            EnsureSnapOn(__result);
        }

        // 把复制人挥动作 kanim 追加进 RemoteWorker 的 AnimFiles。
        private static void EnsureWorkAnimsLoaded(GameObject prefab)
        {
            KBatchedAnimController kbac = prefab.GetComponent<KBatchedAnimController>();
            if (kbac == null)
            {
                return;
            }

            KAnimFile workAnim = Assets.GetAnim("anim_construction_default_kanim");
            if (workAnim == null)
            {
                return;
            }

            KAnimFile[] existing = kbac.AnimFiles ?? new KAnimFile[0];
            foreach (KAnimFile f in existing)
            {
                if (f == workAnim)
                {
                    return; // 已加载，避免重复
                }
            }

            var list = new List<KAnimFile>(existing) { workAnim };
            kbac.AnimFiles = list.ToArray();
        }

        // 照抄复制人(BaseMinionConfig)的 SnapOn 配置。RemoteWorker 共用复制人骨架，
        // snapTo_rgtHand 符号存在，故可直接复用。
        private static void EnsureSnapOn(GameObject prefab)
        {
            if (prefab.GetComponent<SnapOn>() != null)
            {
                return;
            }

            SnapOn snapOn = prefab.AddOrGet<SnapOn>();
            snapOn.snapPoints = new List<SnapOn.SnapPoint>
            {
                MakeHandPoint("dig", "excavator_kanim"),
                MakeHandPoint("build", "constructor_gun_kanim"),
                MakeHandPoint("fetchliquid", "water_gun_kanim"),
                MakeHandPoint("paint", "painting_gun_kanim"),
                MakeHandPoint("harvest", "plant_harvester_gun_kanim"),
                MakeHandPoint("capture", "net_gun_kanim"),
                MakeHandPoint("attack", "attack_gun_kanim"),
                MakeHandPoint("pickup", "pickupdrop_gun_kanim"),
                MakeHandPoint("store", "pickupdrop_gun_kanim"),
                MakeHandPoint("disinfect", "plant_spray_gun_kanim"),
                MakeHandPoint("tend", "plant_harvester_gun_kanim")
            };
        }

        // 构造一个挂在右手(snapTo_rgtHand)、pointName 固定为 "dig"
        // (MultitoolController.ToggleSnapOn("dig") 用)、按 context 区分工具的挂载点。
        private static SnapOn.SnapPoint MakeHandPoint(string context, string buildKanim)
        {
            return new SnapOn.SnapPoint
            {
                pointName = "dig",
                automatic = false,
                context = context,
                buildFile = Assets.GetAnim(buildKanim),
                overrideSymbol = "snapTo_rgtHand"
            };
        }
    }
}
