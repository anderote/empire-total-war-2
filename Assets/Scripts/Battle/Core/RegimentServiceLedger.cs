using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace EmpireBattle
{
    public enum ServiceBattleOutcome { Victory, Defeat, Retreat, Draw }

    [Serializable]
    public sealed class RegimentBattleServiceEntry
    {
        public string battleId = "", label = "", date = "";
        public ServiceBattleOutcome outcome;
        public int strengthBefore, survivors, kills, hits, shots;
        public int Casualties => Math.Max(0, strengthBefore - survivors);
        public bool Valid => !string.IsNullOrWhiteSpace(battleId) && battleId.Length <= 128
            && !string.IsNullOrWhiteSpace(label) && label.Length <= 128 && !label.Any(char.IsControl)
            && date != null && date.Length <= 32 && !date.Any(char.IsControl)
            && Enum.IsDefined(typeof(ServiceBattleOutcome), outcome)
            && strengthBefore >= 0 && strengthBefore <= 10000
            && survivors >= 0 && survivors <= strengthBefore
            && kills >= 0 && kills <= RegimentServiceLedger.MaxCounter
            && hits >= 0 && hits <= RegimentServiceLedger.MaxCounter
            && shots >= 0 && shots <= RegimentServiceLedger.MaxCounter;
    }

    [Serializable]
    public sealed class RegimentServiceSequenceMark
    {
        public string nameSpace = "";
        public long highWater;
    }

    /// <summary>Bounded factual service ledger. Encounter commit guards remain the durable exactly-once key.</summary>
    [Serializable]
    public sealed class RegimentServiceLedger
    {
        public const int MaxRecentBattles = 12;
        public const int MaxCounter = 1000000;
        public const int MaxSequenceNamespaces = 16;
        public int battles, victories, defeats, retreats, draws;
        public int lifetimeKills, lifetimeCasualties, serviceMarks;
        public List<RegimentBattleServiceEntry> recent = new List<RegimentBattleServiceEntry>();
        // Compact lifetime idempotency for monotonic keys like "glory:42". Random campaign IDs
        // rely on the owning campaign's durable resolved-encounter guard and the recent window.
        public List<RegimentServiceSequenceMark> sequenceHighWaters = new List<RegimentServiceSequenceMark>();

        public int MoraleBonus => Math.Min(3, serviceMarks / 3);
        public bool Valid => battles >= 0 && battles <= MaxCounter && victories >= 0 && victories <= battles
            && defeats >= 0 && defeats <= battles && retreats >= 0 && retreats <= battles
            && draws >= 0 && draws <= battles && victories + defeats + retreats + draws <= battles
            && lifetimeKills >= 0 && lifetimeKills <= MaxCounter
            && lifetimeCasualties >= 0 && lifetimeCasualties <= MaxCounter
            && serviceMarks >= 0 && serviceMarks <= battles
            && recent != null && recent.Count <= MaxRecentBattles
            && recent.All(e => e != null && e.Valid)
            && recent.Select(e => e.battleId).Distinct(StringComparer.Ordinal).Count() == recent.Count
            && sequenceHighWaters != null && sequenceHighWaters.Count <= MaxSequenceNamespaces
            && sequenceHighWaters.All(m => m != null && IsNamespace(m.nameSpace) && m.highWater >= 0)
            && sequenceHighWaters.Select(m => m.nameSpace).Distinct(StringComparer.Ordinal).Count() == sequenceHighWaters.Count
            && sequenceHighWaters.Count <= battles;

        /// <summary>
        /// Records one resolved battle. Call only inside the mode's existing exactly-once battle commit.
        /// Activity qualifies through verified kills or a meaningful number of hits; merely being loaded
        /// into a battle, firing without effect, being idle, or being destroyed earns no service mark.
        /// </summary>
        public bool Record(RegimentBattleServiceEntry entry)
        {
            if (entry == null || !entry.Valid || recent == null || sequenceHighWaters == null || !Valid) return false;
            var sequence = ParseSequenceId(entry.battleId);
            if (recent.Any(e => string.Equals(e.battleId, entry.battleId, StringComparison.Ordinal))) return false;
            if (sequence != null)
            {
                var mark = sequenceHighWaters.FirstOrDefault(m => string.Equals(m.nameSpace, sequence.nameSpace, StringComparison.Ordinal));
                if (mark != null && sequence.sequence <= mark.highWater) return false;
                if (mark == null && sequenceHighWaters.Count >= MaxSequenceNamespaces) return false;
            }
            if (battles >= MaxCounter) return false;

            battles++;
            if (sequence != null)
            {
                var mark = sequenceHighWaters.FirstOrDefault(m => string.Equals(m.nameSpace, sequence.nameSpace, StringComparison.Ordinal));
                if (mark == null) sequenceHighWaters.Add(new RegimentServiceSequenceMark { nameSpace = sequence.nameSpace, highWater = sequence.sequence });
                else mark.highWater = sequence.sequence;
            }
            if (entry.outcome == ServiceBattleOutcome.Victory) victories++;
            else if (entry.outcome == ServiceBattleOutcome.Defeat) defeats++;
            else if (entry.outcome == ServiceBattleOutcome.Retreat) retreats++;
            else draws++;
            lifetimeKills = SaturatingAdd(lifetimeKills, entry.kills);
            lifetimeCasualties = SaturatingAdd(lifetimeCasualties, entry.Casualties);
            if (entry.survivors > 0 && MeaningfulParticipation(entry.strengthBefore, entry.kills, entry.hits))
                serviceMarks = Math.Min(battles, serviceMarks + 1);
            recent.Insert(0, Copy(entry));
            if (recent.Count > MaxRecentBattles) recent.RemoveRange(MaxRecentBattles, recent.Count - MaxRecentBattles);
            return true;
        }

        public static bool MeaningfulParticipation(int startingStrength, int kills, int hits)
        {
            if (startingStrength <= 0) return false;
            // A small but size-scaled casualty contribution or a substantial
            // number of verified hits qualifies; merely loading into combat does not.
            int killThreshold=Math.Max(1,(startingStrength+19)/20); // 5% of starting strength
            int hitThreshold=Math.Max(3,(startingStrength+4)/5);   // 20% of starting strength
            return kills >= killThreshold || hits >= hitThreshold;
        }

        public RegimentServiceLedger Copy() => new RegimentServiceLedger {
            battles=battles, victories=victories, defeats=defeats, retreats=retreats, draws=draws,
            lifetimeKills=lifetimeKills, lifetimeCasualties=lifetimeCasualties, serviceMarks=serviceMarks,
            recent=recent == null ? new List<RegimentBattleServiceEntry>() : recent.Select(Copy).ToList(),
            sequenceHighWaters=sequenceHighWaters == null ? new List<RegimentServiceSequenceMark>()
                : sequenceHighWaters.Select(m => new RegimentServiceSequenceMark { nameSpace=m.nameSpace, highWater=m.highWater }).ToList()
        };

        public string History(string regimentName)
        {
            string name = string.IsNullOrWhiteSpace(regimentName) ? "This regiment" : regimentName.Trim();
            if (battles == 0) return name + " has not yet seen action.";
            string line = name + " has served in " + battles.ToString("N0", CultureInfo.InvariantCulture)
                + (battles == 1 ? " battle" : " battles") + ", with "
                + victories.ToString("N0", CultureInfo.InvariantCulture) + " victories and "
                + (retreats + defeats + draws).ToString("N0", CultureInfo.InvariantCulture) + " other results.";
            var latest = recent == null ? null : recent.FirstOrDefault();
            if (latest != null)
                line += " Most recently at " + latest.label + ": "
                    + latest.Casualties.ToString("N0", CultureInfo.InvariantCulture) + " losses, "
                    + latest.kills.ToString("N0", CultureInfo.InvariantCulture) + " verified kills, and "
                    + latest.survivors.ToString("N0", CultureInfo.InvariantCulture) + " survivors.";
            return line;
        }

        public static string BattleLabel(string place, int year, int stableSeed)
        {
            string location = string.IsNullOrWhiteSpace(place) ? "Unnamed Field"
                : new string(place.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (location.Length > 96) location = location.Substring(0, 96).TrimEnd();
            if (location.Length == 0) location = "Unnamed Field";
            string[] forms = { "Battle of ", "Action at ", "Engagement near " };
            string label = forms[PositiveMod(stableSeed, forms.Length)] + location;
            return year > 0 ? label + " · " + year.ToString(CultureInfo.InvariantCulture) : label;
        }

        static bool IsBattleId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
        static bool IsNamespace(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 32
            && value.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
        sealed class ParsedSequence { public string nameSpace; public long sequence; }
        static ParsedSequence ParseSequenceId(string value)
        {
            int separator = value == null ? -1 : value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1) return null;
            string nameSpace = value.Substring(0, separator);
            long sequence;
            if (!IsNamespace(nameSpace) || !long.TryParse(value.Substring(separator + 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out sequence) || sequence < 0) return null;
            return new ParsedSequence { nameSpace=nameSpace, sequence=sequence };
        }
        static int PositiveMod(int value, int modulus) => (value % modulus + modulus) % modulus;
        static int SaturatingAdd(int left, int right) => (int)Math.Min(MaxCounter, (long)left + Math.Max(0, right));
        static RegimentBattleServiceEntry Copy(RegimentBattleServiceEntry e) => new RegimentBattleServiceEntry
        {
            battleId = e.battleId, label = e.label, date = e.date, outcome = e.outcome,
            strengthBefore = e.strengthBefore, survivors = e.survivors, kills = e.kills, hits = e.hits, shots = e.shots
        };
    }

    /// <summary>Adapter-facing commit hook; call from the owning mode's existing resolved-battle commit.</summary>
    public static class RegimentServiceCommit
    {
        public static bool Record(UnitProgression progression, string battleId, string battleLabel, string date,
            ServiceBattleOutcome outcome, int strengthBefore, int survivors, int verifiedKills, int verifiedHits, int shots)
        {
            if (progression == null) return false;
            if (progression.service == null) progression.service = new RegimentServiceLedger();
            return progression.service.Record(new RegimentBattleServiceEntry
            {
                battleId = battleId,
                label = battleLabel,
                date = date ?? string.Empty,
                outcome = outcome,
                strengthBefore = strengthBefore,
                survivors = survivors,
                kills = verifiedKills,
                hits = verifiedHits,
                shots = shots
            });
        }
    }
}
