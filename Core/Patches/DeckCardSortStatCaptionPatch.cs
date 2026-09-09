using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace SpireLens.Core.Patches;

/// <summary>
/// Adds the active sort's number to a card's own description text.
///
/// This patches the point where the game ASKS FOR the text rather than any
/// point where it renders it. <c>NCard.UpdateVisuals</c> does:
///
///     string text = Model.GetDescriptionForPile(pileType, target);
///     _descriptionLabel.SetTextAutoSize("[center]" + text + "[/center]");
///
/// so contributing to the returned string means the game applies its own
/// centring, font and auto-shrink to our line, exactly as it does to the
/// card's own wording.
///
/// Everything that made the earlier approach fragile disappears at this seam:
///
///   - No timing. The game calls this precisely when it needs the text, so
///     there is nothing to defer, poll, or re-synchronise after a re-sort or
///     a scroll.
///   - No scene access. We never touch a holder, a label or the tree, so no
///     node lifecycle can reach us — which is what previously let a caption
///     throw into the middle of a draw and cut a five-card draw down to two.
///   - Nothing to undo. The string is recomputed on every render, so the
///     caption exists purely as a function of current state. No marker to
///     strip, no stored text to restore, no way to leave a stale number on a
///     card.
/// </summary>
[HarmonyPatch(
    typeof(CardModel),
    nameof(CardModel.GetDescriptionForPile),
    new[] { typeof(PileType), typeof(Creature) })]
internal static class DeckCardSortStatCaptionPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardModel __instance, ref string __result)
    {
        try
        {
            if (!ShouldCaption()) return;
            if (__instance == null) return;
            if (!DeckViewSpireLensSort.TryGetCaption(__instance, out var caption)) return;

            __result = string.IsNullOrEmpty(__result)
                ? caption
                : $"{__result}\n{caption}";
        }
        catch (Exception e)
        {
            // Never let a cosmetic caption disturb the text the game asked
            // for: on any failure the card keeps its own description.
            CoreMain.LogDebug($"DeckCardSortStatCaption failed: {e.Message}");
        }
    }

    /// <summary>
    /// Only while a SpireLens sort is showing on an open deck view. Cards are
    /// described all over the game — the hand, piles, rewards — and this is a
    /// deck-view reading aid, so the gate is the screen being up rather than
    /// anything about the card.
    /// </summary>
    private static bool ShouldCaption()
        => DeckViewSpireLensSort.ActiveMetric != null
           && DeckViewSortMenu.IsDeckViewOpen
           && ViewStatsInjectorPatch.StatsVisibilityEnabled;
}
