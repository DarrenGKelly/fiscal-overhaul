using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Splits the Budget tab's Service Upkeep row (one vanilla ExpenseSource) into one part per city service, by
    /// per-service weights (vanilla's per-service upkeep estimate for the city, each service building's upkeep for a
    /// district). The parts are whole numbers that sum exactly to the row. Pure, so the Tests project compiles it.
    /// </summary>
    public static class ServiceUpkeepSplit
    {
        /// <summary>Game.City.ExpenseSource.ServiceUpkeep.</summary>
        public const int ServiceUpkeepSource = 5;

        /// <summary>
        /// <paramref name="total"/> split by <paramref name="weights"/> with largest-remainder rounding: each part is
        /// floor(|total| * w / sum(w)), and the units left over go to the largest remainders (ties: the lower index),
        /// so the parts sum exactly to <paramref name="total"/>; a negative total gives negative parts. Negative, NaN
        /// and infinite weights count as 0. All zeros when the total or the weight sum is 0 (nothing to split by).
        /// </summary>
        public static long[] Split(long total, IReadOnlyList<double> weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            int n = weights.Count;
            var result = new long[n];
            double sum = 0;
            for (int i = 0; i < n; i++) sum += Usable(weights[i]);
            if (total == 0 || sum <= 0) return result;

            long magnitude = Math.Abs(total);
            var remainders = new double[n];
            long assigned = 0;
            for (int i = 0; i < n; i++)
            {
                double exact = magnitude * Usable(weights[i]) / sum;
                long part = (long)Math.Floor(exact);
                result[i] = part;
                remainders[i] = exact - part;
                assigned += part;
            }

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int byRemainder = remainders[b].CompareTo(remainders[a]);
                return byRemainder != 0 ? byRemainder : a.CompareTo(b);
            });
            long left = magnitude - assigned;
            for (int k = 0; k < n && left > 0; k++)
            {
                if (Usable(weights[order[k]]) <= 0) continue;
                result[order[k]]++;
                left--;
            }
            // Floating-point slack (never more than a unit or two): settle it on the heaviest part.
            if (left != 0)
            {
                int heaviest = 0;
                for (int i = 1; i < n; i++) if (Usable(weights[i]) > Usable(weights[heaviest])) heaviest = i;
                result[heaviest] += left;
            }

            if (total < 0) for (int i = 0; i < n; i++) result[i] = -result[i];
            return result;
        }

        private static double Usable(double w) => w > 0 && !double.IsInfinity(w) ? w : 0;

        /// <summary>
        /// The city weight tag of point <paramref name="local"/> of a vanilla statistic series of
        /// <paramref name="seriesLength"/> points (complete samples, then the live one): the series' last point is the
        /// sample filling now, tagged <paramref name="sampleCount"/> (CityStatisticsSystem.sampleCount), and each
        /// earlier point one less.
        /// </summary>
        public static int CityTag(int sampleCount, int seriesLength, int local) => sampleCount - (seriesLength - 1 - local);

        /// <summary>
        /// The index into a district's weight history (<paramref name="weightCount"/> samples, aligned with the newest of
        /// its <paramref name="sampleCount"/> budget samples) for budget sample <paramref name="local"/>, or -1 when the
        /// weights don't go back that far (samples recorded before the split was).
        /// </summary>
        public static int DistrictWeightIndex(int local, int sampleCount, int weightCount)
        {
            if (local < 0 || local >= sampleCount) return -1;
            int index = local - (sampleCount - weightCount);
            return index >= 0 && index < weightCount ? index : -1;
        }

        /// <summary>
        /// One series per service over a run of chart points: point i's <paramref name="parent"/> value
        /// <see cref="Split"/> by that point's weights (<paramref name="weightsPerPoint"/>[i], indexed by service; a
        /// shorter array counts the missing services as 0). A null entry has no recorded weights: before the first
        /// recorded point the services have no data (their values are 0 and <paramref name="start"/> is that first
        /// point, or the point count when none was recorded); after it, the last recorded weights carry forward, so every
        /// point from <paramref name="start"/> on sums exactly to its parent. Result: [service][point].
        /// </summary>
        public static long[][] SplitSeries(IReadOnlyList<long> parent, IReadOnlyList<float[]> weightsPerPoint, int serviceCount, out int start)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (weightsPerPoint == null) throw new ArgumentNullException(nameof(weightsPerPoint));
            if (weightsPerPoint.Count != parent.Count) throw new ArgumentException("One weights entry per point.", nameof(weightsPerPoint));
            if (serviceCount < 0) throw new ArgumentOutOfRangeException(nameof(serviceCount));

            int points = parent.Count;
            var series = new long[serviceCount][];
            for (int s = 0; s < serviceCount; s++) series[s] = new long[points];
            start = points;
            var weights = new double[serviceCount];
            bool have = false;
            for (int i = 0; i < points; i++)
            {
                float[] recorded = weightsPerPoint[i];
                if (recorded != null)
                {
                    for (int s = 0; s < serviceCount; s++) weights[s] = s < recorded.Length ? recorded[s] : 0;
                    if (!have) start = i;
                    have = true;
                }
                if (!have) continue;
                long[] parts = Split(parent[i], weights);
                for (int s = 0; s < serviceCount; s++) series[s][i] = parts[s];
            }
            return series;
        }
    }

    /// <summary>One service's child row under Service Upkeep: its prefab name, first point with data, and values.</summary>
    public class ServiceRow
    {
        public string Name;
        /// <summary>The first chart point with a recorded split; the values before it are 0 and not drawn.</summary>
        public int Start;
        public long[] Values;
    }

    public static class ServiceRows
    {
        /// <summary>
        /// The services' rows for the chart points: <paramref name="parent"/> (the Service Upkeep row per point, as
        /// written) split per service (<see cref="ServiceUpkeepSplit.SplitSeries"/>), in <paramref name="order"/> (column
        /// indices; ones out of range are skipped), leaving out the services that are 0 at every point with data (not
        /// built yet, or no data at all).
        /// </summary>
        public static List<ServiceRow> Build(IReadOnlyList<long> parent, IReadOnlyList<float[]> weightsPerPoint, IReadOnlyList<string> names, IReadOnlyList<int> order)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            if (order == null) throw new ArgumentNullException(nameof(order));
            long[][] series = ServiceUpkeepSplit.SplitSeries(parent, weightsPerPoint, names.Count, out int start);
            var rows = new List<ServiceRow>();
            foreach (int column in order)
            {
                if (column < 0 || column >= names.Count) continue;
                long[] values = series[column];
                bool any = false;
                for (int i = start; i < values.Length && !any; i++) any = values[i] != 0;
                if (any) rows.Add(new ServiceRow { Name = names[column], Start = start, Values = values });
            }
            return rows;
        }
    }

    /// <summary>The services weights are recorded for, by prefab name; a name keeps its column for good.</summary>
    public class ServiceColumns
    {
        private readonly List<string> _names = new List<string>();

        public int Count => _names.Count;

        public string this[int column] => _names[column];

        public int IndexOf(string name) => _names.IndexOf(name);

        /// <summary>The names, by column.</summary>
        public IReadOnlyList<string> Names => _names;

        /// <summary>The name's column, added at the end the first time the name is seen.</summary>
        public int GetOrAdd(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            int index = _names.IndexOf(name);
            if (index >= 0) return index;
            _names.Add(name);
            return _names.Count - 1;
        }

        public void Clear() => _names.Clear();
    }

    /// <summary>
    /// A capped history of per-service weight samples (indexed by <see cref="ServiceColumns"/>), each tagged with the
    /// sample it belongs to (for the city, CityStatisticsSystem.sampleCount, so a weight sample lines up with the
    /// vanilla statistic sample it describes). Tags only grow: a sample with a tag at or below the newest replaces it.
    /// </summary>
    public class ServiceWeightHistory
    {
        private readonly List<int> _tags = new List<int>();
        private readonly List<float[]> _samples = new List<float[]>();

        /// <summary>As many samples as a district keeps (36 months).</summary>
        public const int MaxSamples = DistrictHistory.MaxSamples;

        public int Count => _samples.Count;

        public int TagAt(int index) => _tags[index];

        public float[] At(int index) => _samples[index];

        public void Add(int tag, float[] weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            while (_tags.Count > 0 && _tags[_tags.Count - 1] >= tag)
            {
                _tags.RemoveAt(_tags.Count - 1);
                _samples.RemoveAt(_samples.Count - 1);
            }
            _tags.Add(tag);
            _samples.Add((float[])weights.Clone());
            if (_samples.Count > MaxSamples)
            {
                int drop = _samples.Count - MaxSamples;
                _tags.RemoveRange(0, drop);
                _samples.RemoveRange(0, drop);
            }
        }

        /// <summary>The weights recorded with <paramref name="tag"/>, or null.</summary>
        public float[] ForTag(int tag)
        {
            int index = _tags.BinarySearch(tag);
            return index >= 0 ? _samples[index] : null;
        }

        public void Clear()
        {
            _tags.Clear();
            _samples.Clear();
        }
    }

    /// <summary>The save's primitive reads (the game's IReader, wrapped, or a test stream).</summary>
    public interface ISaveReader
    {
        int ReadInt();
        float ReadFloat();
        string ReadString();
    }

    /// <summary>The save's primitive writes (the game's IWriter, wrapped, or a test stream).</summary>
    public interface ISaveWriter
    {
        void Write(int value);
        void Write(float value);
        void Write(string value);
    }

    /// <summary>
    /// The save encodings of the per-service weights: the city's store (ServiceUpkeepSystem) and the per-district
    /// block (DistrictBudgetSystem), including the district block's older format without them.
    /// </summary>
    public static class ServiceUpkeepSave
    {
        /// <summary>The city store's format: version, columns, history.</summary>
        public const int CityFormatVersion = 1;

        public static void WriteColumns(ISaveWriter writer, ServiceColumns columns)
        {
            writer.Write(columns.Count);
            for (int i = 0; i < columns.Count; i++) writer.Write(columns[i]);
        }

        public static void ReadColumns(ISaveReader reader, ServiceColumns columns)
        {
            columns.Clear();
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++) columns.GetOrAdd(reader.ReadString() ?? string.Empty);
        }

        public static void WriteHistory(ISaveWriter writer, ServiceWeightHistory history)
        {
            writer.Write(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                float[] sample = history.At(i);
                writer.Write(history.TagAt(i));
                writer.Write(sample.Length);
                for (int s = 0; s < sample.Length; s++) writer.Write(sample[s]);
            }
        }

        public static ServiceWeightHistory ReadHistory(ISaveReader reader)
        {
            var history = new ServiceWeightHistory();
            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                int tag = reader.ReadInt();
                var sample = new float[reader.ReadInt()];
                for (int s = 0; s < sample.Length; s++) sample[s] = reader.ReadFloat();
                history.Add(tag, sample);
            }
            return history;
        }

        public static void WriteCity(ISaveWriter writer, ServiceColumns columns, ServiceWeightHistory history)
        {
            writer.Write(CityFormatVersion);
            WriteColumns(writer, columns);
            WriteHistory(writer, history);
        }

        /// <summary>Reads the city store; false (and both left empty) for a version this build doesn't know.</summary>
        public static bool ReadCity(ISaveReader reader, ServiceColumns columns, out ServiceWeightHistory history)
        {
            columns.Clear();
            history = new ServiceWeightHistory();
            if (reader.ReadInt() != CityFormatVersion) return false;
            ReadColumns(reader, columns);
            history = ReadHistory(reader);
            return true;
        }

        /// <summary>
        /// The per-district block (<see cref="DistrictHistory.FormatVersion"/>): version, then for version 2 the service
        /// columns; the district count; per district its key (<paramref name="writeKey"/>), its sample count and
        /// samples, then for version 2 its weight history.
        /// </summary>
        public static void WriteDistricts<TKey>(ISaveWriter writer, Action<TKey> writeKey, ServiceColumns columns,
            IReadOnlyList<KeyValuePair<TKey, DistrictHistory>> districts, Func<TKey, ServiceWeightHistory> weightsOf)
        {
            writer.Write(DistrictHistory.FormatVersion);
            WriteColumns(writer, columns);
            writer.Write(districts.Count);
            foreach (KeyValuePair<TKey, DistrictHistory> pair in districts)
            {
                writeKey(pair.Key);
                writer.Write(pair.Value.Count);
                float[] flat = pair.Value.ToFlat();
                for (int i = 0; i < flat.Length; i++) writer.Write(flat[i]);
                WriteHistory(writer, weightsOf(pair.Key) ?? new ServiceWeightHistory());
            }
        }

        /// <summary>
        /// Reads the per-district block, calling <paramref name="add"/> per district with its history and its weights
        /// (null from a version 1 save, which has none: the split starts with the next sample). False, with nothing
        /// added, for a version this build doesn't know.
        /// </summary>
        public static bool ReadDistricts<TKey>(ISaveReader reader, Func<TKey> readKey, ServiceColumns columns,
            Action<TKey, DistrictHistory, ServiceWeightHistory> add)
        {
            columns.Clear();
            int version = reader.ReadInt();
            if (version != 1 && version != DistrictHistory.FormatVersion) return false;
            bool hasWeights = version >= 2;
            if (hasWeights) ReadColumns(reader, columns);
            int districtCount = reader.ReadInt();
            for (int d = 0; d < districtCount; d++)
            {
                TKey key = readKey();
                int count = reader.ReadInt();
                var flat = new float[count * DistrictBudgetMath.SourceCount];
                for (int i = 0; i < flat.Length; i++) flat[i] = reader.ReadFloat();
                ServiceWeightHistory weights = hasWeights ? ReadHistory(reader) : null;
                add(key, DistrictHistory.FromFlat(count, flat), weights);
            }
            return true;
        }
    }
}
