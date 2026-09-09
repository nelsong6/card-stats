using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class EntropyPowerStatsTests
{
    private const string EntropyPowerId = "POWER.ENTROPY";

    // #309 folded the per-power appenders into two shared renderers: the
    // canonical full view a shared meta-power record shows, and the compact
    // summary a physical copy of that Power card shows.
    private static readonly MethodInfo AppendCanonicalMetaPowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendCanonicalMetaPowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendCanonicalMetaPowerStats not found.");

    private static readonly MethodInfo AppendMetaPowerLifetimeStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendMetaPowerLifetimeStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendMetaPowerLifetimeStats not found.");

    [Fact]
    public void EntropyGeneratedCards_CountOnlySuccessfulObservedResults()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: true,
            CardRarity.Common,
            originalWasBound: true);
        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: true,
            CardRarity.Uncommon,
            originalWasBound: false);
        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: true,
            CardRarity.Rare,
            originalWasBound: true);
        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: true,
            CardRarity.Basic,
            originalWasBound: false);
        RunTracker.RecordEntropyGeneratedCardForTest(
            agg,
            success: false,
            CardRarity.Rare,
            originalWasBound: true);

        Assert.Equal(4, agg.EntropyCardsGenerated);
        Assert.Equal(2, agg.EntropyChainsOfBindingBroken);
        Assert.Equal(1, agg.EntropyCommonCardsGenerated);
        Assert.Equal(1, agg.EntropyUncommonCardsGenerated);
        Assert.Equal(1, agg.EntropyRareCardsGenerated);
    }

    [Fact]
    public void Promotion_MergesEntropyPowerStatsAndCombatDenominator()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[EntropyPowerId] = new PowerAggregate
        {
            PowerId = EntropyPowerId,
            DisplayName = "Entropy",
            EntropyChainsOfBindingBroken = 1,
            EntropyCardsGenerated = 2,
            EntropyCommonCardsGenerated = 1,
            EntropyUncommonCardsGenerated = 1,
            CombatsActive = 1,
        };
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[EntropyPowerId] = new PowerAggregate
        {
            PowerId = EntropyPowerId,
            DisplayName = "Entropy",
            EntropyChainsOfBindingBroken = 1,
            EntropyCardsGenerated = 5,
            EntropyCommonCardsGenerated = 2,
            EntropyUncommonCardsGenerated = 1,
            EntropyRareCardsGenerated = 2,
            CombatsActive = 1,
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertRepresentativeAggregate(
            run.MetaStats.PowerAggregates[EntropyPowerId]);
    }

    [Fact]
    public void EntropyTooltip_FullViewShowsRequestedBreakdownAndAverage()
    {
        var body = AppendEntropyPowerStats(
            CreateRepresentativeAggregate(),
            compact: false);

        Assert.Contains("Chains of Binding broken", body);
        Assert.Contains("Commons generated", body);
        Assert.Contains("Uncommons generated", body);
        Assert.Contains("Rares generated", body);
        Assert.Contains("Avg cards generated / active turn", body);
        Assert.Contains("[b]3.5[/b]", body);
    }

    [Fact]
    // The compact view now leads with the power's primary outcome rather
    // than its chain-break count; the breakdown stays full-view only.
    public void EntropyTooltip_CompactViewKeepsOnlyPrimaryOutcome()
    {
        var body = AppendEntropyPowerStats(
            CreateRepresentativeAggregate(),
            compact: true);

        Assert.Contains("Cards generated", body);
        Assert.DoesNotContain("Commons generated", body);
        Assert.DoesNotContain("Uncommons generated", body);
        Assert.DoesNotContain("Rares generated", body);
        Assert.DoesNotContain("Avg cards generated / active turn", body);
    }

    private static PowerAggregate CreateRepresentativeAggregate()
        => new()
        {
            PowerId = EntropyPowerId,
            DisplayName = "Entropy",
            EntropyChainsOfBindingBroken = 2,
            EntropyCardsGenerated = 7,
            EntropyCommonCardsGenerated = 3,
            EntropyUncommonCardsGenerated = 2,
            EntropyRareCardsGenerated = 2,
            CombatsActive = 2,
            // #309 moved the averages onto the shared Meta* turn
            // counters. Mirror the legacy fixture values onto them so the
            // expected averages below still mean what they meant before.
            MetaActiveTurns = 2,
            RateEntropyCardsGenerated = 7,
        };

    private static void AssertRepresentativeAggregate(PowerAggregate agg)
    {
        Assert.Equal(EntropyPowerId, agg.PowerId);
        Assert.Equal("Entropy", agg.DisplayName);
        Assert.Equal(2, agg.EntropyChainsOfBindingBroken);
        Assert.Equal(7, agg.EntropyCardsGenerated);
        Assert.Equal(3, agg.EntropyCommonCardsGenerated);
        Assert.Equal(2, agg.EntropyUncommonCardsGenerated);
        Assert.Equal(2, agg.EntropyRareCardsGenerated);
        Assert.Equal(2, agg.CombatsActive);
    }

    private static string AppendEntropyPowerStats(
        PowerAggregate agg,
        bool compact)
    {
        // Resolve through the registry rather than naming the id here:
        // ids come from the game's types, so a hand-written constant
        // silently stops matching when a type is renamed.
        var definition = MetaPowerRegistry.All.Single(
            candidate => candidate.DisplayName == "Entropy");
        var sb = new StringBuilder();
        var card = (Entropy)RuntimeHelpers.GetUninitializedObject(
            typeof(Entropy));
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[definition.PowerId] = agg;
        if (compact)
        {
            // Compact rows, as the physical Power card shows them. Resolving a
            // card to its definition needs the game's model db, so that step is
            // covered by MetaPowerRegistry's tests rather than faked here.
            _ = AppendMetaPowerLifetimeStatsMethod.Invoke(
                null,
                [sb, definition, agg, metaStats, false]);
        }
        else
        {
            // The shared meta-power record shows the canonical full view.
            _ = AppendCanonicalMetaPowerStatsMethod.Invoke(
                null,
                [sb, definition, metaStats]);
        }
        return sb.ToString();
    }
}
