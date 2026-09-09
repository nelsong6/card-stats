using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class JugglingPowerStatsTests
{
    private const string JugglingPowerId = "POWER.JUGGLING";

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

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void NormalizeAttackCount_ShowsRawTurnProgressWithoutCappingAtTrigger(
        int attacksPlayedThisTurn,
        int expected)
    {
        Assert.Equal(
            expected,
            JugglingPowerDisplayAmountPatch.NormalizeAttackCount(attacksPlayedThisTurn));
    }

    [Fact]
    public void RunMetaStats_JugglingPowerAggregate_DefaultsSafely()
    {
        var metaStats = new RunMetaStats();

        Assert.Empty(metaStats.PowerAggregates);
        var agg = new PowerAggregate();
        Assert.Equal(0, agg.AttacksCopied);
        Assert.Equal(0, agg.CommonAttacksCopied);
        Assert.Equal(0, agg.UncommonAttacksCopied);
        Assert.Equal(0, agg.RareAttacksCopied);
        Assert.Equal(0, agg.TurnsActive);
        Assert.Equal(0, agg.CombatsActive);
    }

    [Fact]
    public void RunMetaStats_JugglingPowerAggregate_JsonRoundtripPreservesFields()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[JugglingPowerId] = CreateRepresentativeAggregate();

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"power_aggregates\"", json);
        Assert.Contains("\"attacks_copied\"", json);
        Assert.Contains("\"turns_active\"", json);
        Assert.NotNull(restored);
        AssertRepresentativeAggregate(restored!.MetaStats.PowerAggregates[JugglingPowerId]);
    }

    [Fact]
    public void RunTracker_JugglingCopies_CountOnlySuccessfulObservedRarities()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordJugglingCopyForTest(agg, success: true, CardRarity.Common);
        RunTracker.RecordJugglingCopyForTest(agg, success: true, CardRarity.Uncommon);
        RunTracker.RecordJugglingCopyForTest(agg, success: true, CardRarity.Rare);
        RunTracker.RecordJugglingCopyForTest(agg, success: true, CardRarity.Basic);
        RunTracker.RecordJugglingCopyForTest(agg, success: false, CardRarity.Rare);

        Assert.Equal(4, agg.AttacksCopied);
        Assert.Equal(1, agg.CommonAttacksCopied);
        Assert.Equal(1, agg.UncommonAttacksCopied);
        Assert.Equal(1, agg.RareAttacksCopied);
    }

    [Fact]
    public void RunTracker_Promotion_MergesJugglingPowerAggregates()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[JugglingPowerId] = new PowerAggregate
        {
            PowerId = JugglingPowerId,
            DisplayName = "Juggling",
            AttacksCopied = 2,
            CommonAttacksCopied = 1,
            TurnsActive = 2,
            CombatsActive = 1,
        };
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[JugglingPowerId] = new PowerAggregate
        {
            PowerId = JugglingPowerId,
            DisplayName = "Juggling",
            AttacksCopied = 5,
            CommonAttacksCopied = 2,
            UncommonAttacksCopied = 2,
            RareAttacksCopied = 2,
            TurnsActive = 3,
            CombatsActive = 1,
        };

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertRepresentativeAggregate(run.MetaStats.PowerAggregates[JugglingPowerId]);
    }

    [Fact]
    public void JugglingTooltip_FullViewShowsPowerOwnedCopyBreakdownAndAverages()
    {
        var body = AppendJugglingPowerStats(CreateRepresentativeAggregate(), compact: false);

        Assert.Contains("Total attacks copied", body);
        Assert.Contains("Common attacks copied", body);
        Assert.Contains("Uncommon attacks copied", body);
        Assert.Contains("Rare attacks copied", body);
        Assert.Contains("Avg attacks copied / turn", body);
        Assert.Contains("[b]1.4[/b]", body);
        Assert.Contains("Avg attacks copied / active turn", body);
        Assert.Contains("[b]3.5[/b]", body);
    }

    [Fact]
    public void JugglingTooltip_CompactViewKeepsOnlyTotalAttacksCopied()
    {
        var body = AppendJugglingPowerStats(CreateRepresentativeAggregate(), compact: true);

        Assert.Contains("Total attacks copied", body);
        Assert.DoesNotContain("Common attacks copied", body);
        Assert.DoesNotContain("unCommon attacks copied", body);
        Assert.DoesNotContain("Rare attacks copied", body);
        Assert.DoesNotContain("avg copies per turn", body);
        Assert.DoesNotContain("avg copies per combat", body);
    }

    [Fact]
    public void RunMetaStats_OlderShapeWithoutPowerAggregates_DefaultsToEmpty()
    {
        var metaStats = JsonSerializer.Deserialize<RunMetaStats>("{}", RunStorage.Options);

        Assert.NotNull(metaStats);
        Assert.Empty(metaStats!.PowerAggregates);
    }

    private static PowerAggregate CreateRepresentativeAggregate() =>
        new()
        {
            PowerId = JugglingPowerId,
            DisplayName = "Juggling",
            AttacksCopied = 7,
            CommonAttacksCopied = 3,
            UncommonAttacksCopied = 2,
            RareAttacksCopied = 2,
            TurnsActive = 5,
            CombatsActive = 2,
            // #309 moved the averages onto the shared Meta* turn
            // counters. Mirror the legacy fixture values onto them so the
            // expected averages below still mean what they meant before.
            MetaDeckTurns = 5,
            RateAttacksCopied = 7,
            MetaActiveTurns = 2,
        };

    private static void AssertRepresentativeAggregate(PowerAggregate agg)
    {
        Assert.Equal(JugglingPowerId, agg.PowerId);
        Assert.Equal("Juggling", agg.DisplayName);
        Assert.Equal(7, agg.AttacksCopied);
        Assert.Equal(3, agg.CommonAttacksCopied);
        Assert.Equal(2, agg.UncommonAttacksCopied);
        Assert.Equal(2, agg.RareAttacksCopied);
        Assert.Equal(5, agg.TurnsActive);
        Assert.Equal(2, agg.CombatsActive);
    }

    private static string AppendJugglingPowerStats(PowerAggregate agg, bool compact)
    {
        // Resolve through the registry rather than naming the id here:
        // ids come from the game's types, so a hand-written constant
        // silently stops matching when a type is renamed.
        var definition = MetaPowerRegistry.All.Single(
            candidate => candidate.DisplayName == "Juggling");
        var sb = new StringBuilder();
        var card = (Juggling)RuntimeHelpers.GetUninitializedObject(typeof(Juggling));
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
