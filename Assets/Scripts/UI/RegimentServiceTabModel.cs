using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EmpireBattle
{
    /// <summary>A read-only presentation row. Values come directly from a committed service entry.</summary>
    public sealed class RegimentServiceBattleRow
    {
        public string BattleLabel { get; }
        public string Date { get; }
        public string Outcome { get; }
        public int StartingStrength { get; }
        public int Survivors { get; }
        public int Casualties { get; }
        public int VerifiedKills { get; }
        public int VerifiedHits { get; }
        public int Shots { get; }

        internal RegimentServiceBattleRow(RegimentBattleServiceEntry entry)
        {
            BattleLabel = entry.label;
            Date = entry.date;
            Outcome = entry.outcome.ToString();
            StartingStrength = entry.strengthBefore;
            Survivors = entry.survivors;
            Casualties = entry.Casualties;
            VerifiedKills = entry.kills;
            VerifiedHits = entry.hits;
            Shots = entry.shots;
        }
    }

    /// <summary>
    /// UI contract for the Regiment Manager's Service tab. Construct on demand from saved progression;
    /// this view model never mutates progression, regiment identity, or service history.
    /// </summary>
    public sealed class RegimentServiceTabModel
    {
        public string RegimentName { get; }
        public string Summary { get; }
        public int Battles { get; }
        public int Victories { get; }
        public int Defeats { get; }
        public int Retreats { get; }
        public int Draws { get; }
        public int ServiceMarks { get; }
        public int MoraleBonus { get; }
        public int LifetimeKills { get; }
        public int LifetimeCasualties { get; }
        public IReadOnlyList<RegimentServiceBattleRow> RecentBattles { get; }

        RegimentServiceTabModel(string name, RegimentServiceLedger ledger)
        {
            RegimentName = name;
            Battles = ledger.battles;
            Victories = ledger.victories;
            Defeats = ledger.defeats;
            Retreats = ledger.retreats;
            Draws = ledger.draws;
            ServiceMarks = ledger.serviceMarks;
            MoraleBonus = ledger.MoraleBonus;
            LifetimeKills = ledger.lifetimeKills;
            LifetimeCasualties = ledger.lifetimeCasualties;
            Summary = BuildSummary(name, ledger);
            RecentBattles = Array.AsReadOnly((ledger.recent ?? new List<RegimentBattleServiceEntry>())
                .Where(e => e != null && e.Valid).Select(e => new RegimentServiceBattleRow(e)).ToArray());
        }

        public static RegimentServiceTabModel From(UnitProgression progression, string currentRegimentName)
        {
            var ledger = progression?.service;
            if (ledger == null || !ledger.Valid) ledger = new RegimentServiceLedger();
            var name = string.IsNullOrWhiteSpace(currentRegimentName) ? "Unnamed regiment" : currentRegimentName.Trim();
            return new RegimentServiceTabModel(name, ledger);
        }

        static string BuildSummary(string name, RegimentServiceLedger ledger)
        {
            if (ledger.battles == 0) return name + " has not yet seen action.";
            string summary = name + " has served in " + ledger.battles.ToString("N0", CultureInfo.InvariantCulture)
                + (ledger.battles == 1 ? " battle" : " battles") + ": "
                + ledger.victories.ToString("N0", CultureInfo.InvariantCulture) + " victories, "
                + ledger.defeats.ToString("N0", CultureInfo.InvariantCulture) + " defeats, "
                + ledger.retreats.ToString("N0", CultureInfo.InvariantCulture) + " retreats, and "
                + ledger.draws.ToString("N0", CultureInfo.InvariantCulture) + " draws. It has "
                + ledger.serviceMarks.ToString("N0", CultureInfo.InvariantCulture) + " participation marks, "
                + ledger.lifetimeKills.ToString("N0", CultureInfo.InvariantCulture) + " verified kills, and "
                + ledger.lifetimeCasualties.ToString("N0", CultureInfo.InvariantCulture) + " recorded losses."
                + (ledger.MoraleBonus > 0 ? " Service adds +" + ledger.MoraleBonus.ToString(CultureInfo.InvariantCulture) + " maximum morale." : "");
            var latest = ledger.recent == null ? null : ledger.recent.FirstOrDefault(e => e != null && e.Valid);
            if (latest != null)
            {
                summary += " Most recently at " + latest.label
                    + (string.IsNullOrWhiteSpace(latest.date) ? "" : " (" + latest.date + ")")
                    + " (" + latest.outcome.ToString().ToLowerInvariant() + "), it recorded "
                    + latest.kills.ToString("N0", CultureInfo.InvariantCulture)
                    + (latest.kills == 1 ? " verified kill and " : " verified kills and ")
                    + latest.hits.ToString("N0", CultureInfo.InvariantCulture) + " hits; "
                    + latest.Casualties.ToString("N0", CultureInfo.InvariantCulture) + " were lost and "
                    + latest.survivors.ToString("N0", CultureInfo.InvariantCulture) + " survived.";
            }
            return summary;
        }
    }
}
