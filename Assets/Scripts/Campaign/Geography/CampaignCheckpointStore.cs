using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EmpireCampaign.Persistence
{
    /// <summary>
    /// A small independent ring of integrity-checked campaign checkpoints. Manual user
    /// saves live elsewhere and are never enumerated, rotated, renamed, or deleted here.
    /// The caller supplies the game's full compatibility validator (deserialize + Adopt).
    /// </summary>
    public sealed class CampaignCheckpointStore
    {
        const string Magic = "ETW2-CAMPAIGN-CHECKPOINT-1";
        readonly string directory;
        readonly int capacity;
        readonly Action<string, byte[]> atomicWriter;
        readonly Action beforePromote;

        public CampaignCheckpointStore(string directory, int capacity = 3, Action<string, byte[]> atomicWriter = null, Action beforePromote = null)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Checkpoint directory is required.", nameof(directory));
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity), "At least two slots are required to preserve the previous checkpoint during a write.");
            this.directory = directory;
            this.capacity = capacity;
            this.beforePromote = beforePromote;
            this.atomicWriter = atomicWriter ?? ((path, bytes) => WriteAtomically(path, bytes, this.beforePromote));
        }

        string SlotPath(int slot) => Path.Combine(directory, "campaign-checkpoint-" + slot + ".dat");

        public bool HasValidCheckpoint(Func<string, bool> validate)
        {
            string ignored; long sequence;
            return TryLoad(validate, out ignored, out sequence);
        }

        public bool TrySave(string payload, Func<string, bool> validate, out string reason)
        {
            reason = "Checkpoint was not written.";
            if (payload == null || validate == null) { reason = "Checkpoint data or validator is missing."; return false; }
            if (!SafeValidate(validate, payload)) { reason = "Campaign state failed compatibility validation."; return false; }

            var records = ReadValid(validate).ToArray();
            long latest = records.Length == 0 ? 0 : records.Max(r => r.Sequence);
            if (latest == long.MaxValue) { reason = "Checkpoint sequence is exhausted."; return false; }
            long next = latest + 1;
            int target = (int)(next % capacity);
            string envelope = Encode(next, payload);
            try
            {
                Directory.CreateDirectory(directory);
                atomicWriter(SlotPath(target), new UTF8Encoding(false).GetBytes(envelope));
                reason = "Checkpoint saved.";
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                reason = "Checkpoint write failed: " + e.Message;
                return false;
            }
        }

        public bool TryLoad(Func<string, bool> validate, out string payload, out long sequence)
        {
            payload = null; sequence = 0;
            if (validate == null) return false;
            foreach (var record in ReadValid(validate).OrderByDescending(r => r.Sequence))
            { payload = record.Payload; sequence = record.Sequence; return true; }
            return false;
        }

        public IEnumerable<long> ValidSequences(Func<string, bool> validate) =>
            ReadValid(validate).Select(r => r.Sequence).OrderByDescending(n => n).ToArray();

        IEnumerable<Record> ReadValid(Func<string, bool> validate)
        {
            for (int i = 0; i < capacity; i++)
            {
                string path = SlotPath(i);
                if (!File.Exists(path)) continue;
                byte[] bytes = null;
                try
                { bytes = File.ReadAllBytes(path); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is DecoderFallbackException)
                { /* A damaged/unreadable slot is ignored; older valid checkpoints remain usable. */ }
                if (bytes == null) continue;
                var record = Decode(bytes);
                if (record != null && SafeValidate(validate, record.Payload)) yield return record;
            }
        }

        static bool SafeValidate(Func<string, bool> validate, string payload)
        { try { return validate(payload); } catch { return false; } }

        static string Encode(long sequence, string payload)
        {
            byte[] data = new UTF8Encoding(false).GetBytes(payload);
            return Magic + "\n" + sequence + "\n" + Hex(SHA256.Create().ComputeHash(data)) + "\n" + Convert.ToBase64String(data) + "\n";
        }

        static Record Decode(byte[] bytes)
        {
            string text = new UTF8Encoding(false, true).GetString(bytes);
            string[] fields = text.Split('\n');
            if (fields.Length < 4 || fields[0] != Magic || !long.TryParse(fields[1], out long sequence) || sequence <= 0) return null;
            byte[] payloadBytes;
            try { payloadBytes = Convert.FromBase64String(fields[3]); } catch (FormatException) { return null; }
            if (!string.Equals(fields[2], Hex(SHA256.Create().ComputeHash(payloadBytes)), StringComparison.OrdinalIgnoreCase)) return null;
            string payload;
            try { payload = new UTF8Encoding(false, true).GetString(payloadBytes); } catch (DecoderFallbackException) { return null; }
            return new Record { Sequence = sequence, Payload = payload };
        }

        static string Hex(byte[] bytes)
        { var b = new StringBuilder(bytes.Length * 2); foreach (byte value in bytes) b.Append(value.ToString("x2")); return b.ToString(); }

        static void WriteAtomically(string destination, byte[] bytes, Action beforePromote)
        {
            string folder = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(folder);
            string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                beforePromote?.Invoke();
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        sealed class Record { public long Sequence; public string Payload; }
    }
}
