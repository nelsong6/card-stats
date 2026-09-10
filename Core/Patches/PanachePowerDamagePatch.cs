using System;
using System.Reflection;
using HarmonyLib;

namespace SpireLens.Core.Patches;

/// <summary>
/// Arm the Panache attribution window as the power begins its strike.
///
/// Panache is the only power in the game that deals damage itself. It does so
/// from <c>AfterCardPlayed</c> through the creature overload of
/// <c>CreatureCmd.Damage</c>, and that overload passes <c>cardSource: null</c>
/// — verified in the decompiled source, where it forwards to
/// <c>Damage(choiceContext, targets, amount, props, dealer, null, null)</c>.
/// The resulting <c>DamageReceivedEntry</c> therefore carries no card, and the
/// tracker's null-source branch dropped it: a Panache in the deck reported
/// zero damage however much it actually dealt.
///
/// A prefix is enough, and a postfix would be wrong. The method is async, so a
/// postfix runs when the Task is returned rather than when the damage has
/// resolved — it would close the window before the entries arrive. Instead the
/// prefix opens a window that the damage observer consumes, bounded by combat
/// history distance so it cannot drift into unrelated damage.
/// </summary>
[HarmonyPatch]
public static class PanachePowerDamagePatch
{
    private static MethodBase? TargetMethod()
    {
        var panacheType = AccessTools.TypeByName(
            "MegaCrit.Sts2.Core.Models.Powers.PanachePower");
        return panacheType == null
            ? null
            : AccessTools.Method(panacheType, "AfterCardPlayed");
    }

    [HarmonyPrefix]
    public static void Prefix(object __instance)
    {
        try
        {
            if (__instance != null)
                RunTracker.NotePanacheDamageStarted(__instance);
        }
        catch (Exception e)
        {
            CoreMain.Logger.Error($"PanachePowerDamagePatch failed: {e.Message}");
        }
    }
}
