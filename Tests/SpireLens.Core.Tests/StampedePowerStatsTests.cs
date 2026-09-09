using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class StampedePowerStatsTests
{
    private const string StampedePowerId = "POWER.STAMPEDE";

    // #309 folded the per-power appenders into two shared renderers: the
    // canonical full view a shared meta-power record shows, and the compact
    // summary a physical copy of that Power card shows.
    private static readonly MethodInfo AppendCanonicalMetaPowerStatsMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendCanonicalMetaPowerStats",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendCanonicalMetaPowerStats not found.");

    private static readonly MethodInfo AppendPhysicalMetaPowerSummaryMethod =
        typeof(CardHoverShowPatch).GetMethod(
            "AppendPhysicalMetaPowerSummary",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "AppendPhysicalMetaPowerSummary not found.");

    [Fact]
    public void PowerAggregate_StampedeFields_DefaultAndSerialize()
    {
        var empty = new PowerAggregate();
        Assert.Equal(0, empty.StampedeAttacksPlayed);
        Assert.Equal(0, empty.StampedeCommonAttacksPlayed);
        Assert.Equal(0, empty.StampedeUncommonAttacksPlayed);
        Assert.Equal(0, empty.StampedeRareAttacksPlayed);
        Assert.Equal(0, empty.StampedeEnergySaved);

        var run = new RunData();
        run.MetaStats.PowerAggregates[StampedePowerId] = CreateAggregate(
            attacks: 9,
            common: 4,
            uncommon: 3,
            rare: 2,
            energySaved: 14);

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(json, RunStorage.Options);

        Assert.Contains("\"stampede_attacks_played\"", json);
        Assert.NotNull(restored);
        AssertAggregate(restored!.MetaStats.PowerAggregates[StampedePowerId]);
    }

    [Fact]
    public void RunTracker_StampedeHelper_TracksRarityAndNonNegativeEnergy()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordStampedeAttackForTest(agg, CardRarity.Common, 1);
        RunTracker.RecordStampedeAttackForTest(agg, CardRarity.Uncommon, 2);
        RunTracker.RecordStampedeAttackForTest(agg, CardRarity.Rare, 3);
        RunTracker.RecordStampedeAttackForTest(agg, CardRarity.Basic, -4);

        Assert.Equal(4, agg.StampedeAttacksPlayed);
        Assert.Equal(1, agg.StampedeCommonAttacksPlayed);
        Assert.Equal(1, agg.StampedeUncommonAttacksPlayed);
        Assert.Equal(1, agg.StampedeRareAttacksPlayed);
        Assert.Equal(6, agg.StampedeEnergySaved);
    }

    [Fact]
    public void Promotion_MergesStampedePowerTotals()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[StampedePowerId] = CreateAggregate(
            attacks: 4,
            common: 2,
            uncommon: 1,
            rare: 1,
            energySaved: 6);
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[StampedePowerId] = CreateAggregate(
            attacks: 5,
            common: 2,
            uncommon: 2,
            rare: 1,
            energySaved: 8);

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertAggregate(run.MetaStats.PowerAggregates[StampedePowerId]);
    }

    [Fact]
    public void StampedeTooltip_ProjectsSharedPowerTotals()
    {
        // Resolve through the registry rather than naming the id here:
        // ids come from the game's types, so a hand-written constant
        // silently stops matching when a type is renamed.
        var definition = MetaPowerRegistry.All.Single(
            candidate => candidate.DisplayName == "Stampede");
        var sb = new StringBuilder();
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[definition.PowerId] = CreateAggregate(
            attacks: 9,
            common: 4,
            uncommon: 3,
            rare: 2,
            energySaved: 14);

        _ = AppendCanonicalMetaPowerStatsMethod.Invoke(
            null,
            [sb, definition, metaStats]);

        var body = sb.ToString();
        Assert.Contains("Attacks stampeded", body);
        Assert.Contains("Common attacks", body);
        Assert.Contains("Uncommon attacks", body);
        Assert.Contains("Rare attacks", body);
        Assert.Contains("saved", body);
        Assert.Contains("[b]14[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patches_TargetExpectedStampedeAndAutoPlayMethods()
    {
        var powerTarget = typeof(StampedePower).GetMethod(
            nameof(StampedePower.AfterAutoPostPlayPhaseEntered),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(Player),
            });
        var autoPlayTarget = typeof(CardCmd).GetMethod(
            nameof(CardCmd.AutoPlay),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(CardModel),
                typeof(Creature),
                typeof(AutoPlayType),
                typeof(bool),
                typeof(bool),
            });

        Assert.NotNull(powerTarget);
        Assert.NotNull(autoPlayTarget);
    }

    private static PowerAggregate CreateAggregate(
        int attacks,
        int common,
        int uncommon,
        int rare,
        int energySaved) =>
        new()
        {
            PowerId = StampedePowerId,
            DisplayName = "Stampede",
            StampedeAttacksPlayed = attacks,
            StampedeCommonAttacksPlayed = common,
            StampedeUncommonAttacksPlayed = uncommon,
            StampedeRareAttacksPlayed = rare,
            StampedeEnergySaved = energySaved,
        };

    private static void AssertAggregate(PowerAggregate agg)
    {
        Assert.Equal("POWER.STAMPEDE", agg.PowerId);
        Assert.Equal("Stampede", agg.DisplayName);
        Assert.Equal(9, agg.StampedeAttacksPlayed);
        Assert.Equal(4, agg.StampedeCommonAttacksPlayed);
        Assert.Equal(3, agg.StampedeUncommonAttacksPlayed);
        Assert.Equal(2, agg.StampedeRareAttacksPlayed);
        Assert.Equal(14, agg.StampedeEnergySaved);
    }
}
