using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

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

    // 崩溃修复：
    // 植物的 Harvestable/Uprootable 使用 multitoolContext("harvest")，
    // 因此 StandardWorker.StartWork 会走 multitool 路径并 StartSM()，
    // 而 MultitoolController.InitializeStates 第一步即 ToggleSnapOn("dig")，
    // 它需要 worker 身上的 SnapOn 组件。复制人有 SnapOn，但坞派出的机器人
    // RemoteWorker 没有，于是抛出 "RemoteWorker does not have component SnapOn"
    // 并在后续空引用处崩溃。
    //
    // 这里只在 worker 为 RemoteWorker 时关闭 multitool 路径，改走"操作机器"
    // 交互动画(override anims)。复制人(worker 非 RemoteWorker)原样走 multitool
    // 砍击逻辑，零影响。
    [HarmonyPatch(typeof(Workable), nameof(Workable.GetAnim))]
    internal static class Workable_GetAnim_RemoteWorker_Patch
    {
        private static KAnimFile[] interactAnims;

        private static void Postfix(WorkerBase worker, ref Workable.AnimInfo __result)
        {
            // 仅处理走 multitool 路径(smi != null)的情形
            if (__result.smi == null)
            {
                return;
            }

            // 仅对远程机器人生效
            if (!(worker is RemoteWorker))
            {
                return;
            }

            // 防御性判断：若该 worker 恰好拥有 SnapOn 组件，则无需改写
            if (worker.GetComponent<SnapOn>() != null)
            {
                return;
            }

            // 关闭 multitool 路径。这个尚未 StartSM 的 MultitoolController 实例
            // 直接丢弃即可，不会产生副作用。
            __result.smi = null;

            // 改用"操作机器"交互动画。Workable.workAnims 默认值为
            // { "working_pre", "working_loop" }，而 anim_interacts_fabricator_generic_kanim
            // 内含这两个动画，可被 StandardWorker.StartWork 的 override 路径正常播放。
            if (interactAnims == null)
            {
                KAnimFile anim = Assets.GetAnim("anim_interacts_fabricator_generic_kanim");
                if (anim != null)
                {
                    interactAnims = new[] { anim };
                }
            }

            if (interactAnims != null &&
                (__result.overrideAnims == null || __result.overrideAnims.Length == 0))
            {
                __result.overrideAnims = interactAnims;
            }
        }
    }
}
