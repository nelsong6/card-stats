using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class RupturePowerStatsTests
{
    private const string RupturePowerId = "POWER.RUPTURE";

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
    public void PowerAggregate_RuptureFields_DefaultAndSerialize()
    {
        var empty = new PowerAggregate();
        Assert.Equal(0m, empty.StrengthGained);
        Assert.Equal(0, empty.TurnsActive);

        var run = new RunData();
        run.MetaStats.PowerAggregates[RupturePowerId] =
            CreateAggregate(strengthGained: 18m, turnsActive: 6);

        var json = JsonSerializer.Serialize(run, RunStorage.Options);
        var restored = JsonSerializer.Deserialize<RunData>(
            json,
            RunStorage.Options);

        Assert.Contains("\"strength_gained\"", json);
        Assert.Contains("\"turns_active\"", json);
        Assert.NotNull(restored);
        AssertAggregate(
            restored!.MetaStats.PowerAggregates[RupturePowerId],
            strengthGained: 18m,
            turnsActive: 6);
    }

    [Fact]
    public void RunTracker_RuptureHelper_RecordsOnlyPositiveStrength()
    {
        var agg = new PowerAggregate();

        RunTracker.RecordRuptureStrengthGainedForTest(agg, 12m);
        RunTracker.RecordRuptureStrengthGainedForTest(agg, 6m);
        RunTracker.RecordRuptureStrengthGainedForTest(agg, 0m);
        RunTracker.RecordRuptureStrengthGainedForTest(agg, -3m);

        Assert.Equal(18m, agg.StrengthGained);
    }

    [Fact]
    public void Promotion_MergesRuptureNumeratorAndDenominator()
    {
        var run = new RunData();
        run.MetaStats.PowerAggregates[RupturePowerId] =
            CreateAggregate(strengthGained: 7m, turnsActive: 2);
        var pending = new PendingCombat();
        pending.MetaStats.PowerAggregates[RupturePowerId] =
            CreateAggregate(strengthGained: 11m, turnsActive: 4);

        RunTracker.PromotePendingCombatIntoRun(pending, run);

        AssertAggregate(
            run.MetaStats.PowerAggregates[RupturePowerId],
            strengthGained: 18m,
            turnsActive: 6);
    }

    [Fact]
    public void RuptureTooltip_ProjectsStrengthAndPerActiveTurn()
    {
        // Resolve through the registry rather than naming the id here:
        // ids come from the game's types, so a hand-written constant
        // silently stops matching when a type is renamed.
        var definition = MetaPowerRegistry.All.Single(
            candidate => candidate.DisplayName == "Rupture");
        var sb = new StringBuilder();
        var metaStats = new RunMetaStats();
        metaStats.PowerAggregates[definition.PowerId] =
            CreateAggregate(strengthGained: 18m, turnsActive: 6);

        _ = AppendCanonicalMetaPowerStatsMethod.Invoke(
            null,
            [sb, definition, metaStats]);

        var body = sb.ToString();
        Assert.Contains("Strength gained", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.Contains("Avg strength gained / active turn", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    [Trait("Category", "RequiresLiveGame")]
    public void Patches_TargetBothRupturePayoffCallbacks()
    {
        var damageTarget = AccessTools.Method(
            typeof(RupturePower),
            nameof(RupturePower.AfterDamageReceived),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(Creature),
                typeof(DamageResult),
                typeof(ValueProp),
                typeof(Creature),
                typeof(CardModel),
            });
        var cardTarget = AccessTools.Method(
            typeof(RupturePower),
            nameof(RupturePower.AfterCardPlayed),
            new[]
            {
                typeof(PlayerChoiceContext),
                typeof(CardPlay),
            });

        Assert.NotNull(damageTarget);
        Assert.NotNull(cardTarget);
    }

    private static PowerAggregate CreateAggregate(
        decimal strengthGained,
        int turnsActive) =>
        new()
        {
            PowerId = RupturePowerId,
            DisplayName = "Rupture",
            StrengthGained = strengthGained,
            TurnsActive = turnsActive,
            // #309 moved the averages onto the shared Meta* turn
            // counters. Mirror the legacy fixture values onto them so the
            // expected averages below still mean what they meant before.
            MetaActiveTurns = turnsActive,
            RateStrengthGained = strengthGained,
        };

    private static void AssertAggregate(
        PowerAggregate agg,
        decimal strengthGained,
        int turnsActive)
    {
        Assert.Equal(RupturePowerId, agg.PowerId);
        Assert.Equal("Rupture", agg.DisplayName);
        Assert.Equal(strengthGained, agg.StrengthGained);
        Assert.Equal(turnsActive, agg.TurnsActive);
    }
}
