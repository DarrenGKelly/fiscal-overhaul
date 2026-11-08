using System;
using System.Collections.Generic;
using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class ServiceUpkeepSplitTests
    {
        [Fact]
        public void Split_SumsExactlyToTotal()
        {
            var weights = new[] { 1.0, 1.0, 1.0 };
            long[] parts = ServiceUpkeepSplit.Split(100, weights);
            Assert.Equal(100, parts.Sum());
            Assert.Equal(new long[] { 34, 33, 33 }, parts); // the one unit left goes to the first of three equal remainders
        }

        [Fact]
        public void Split_SumsExactly_ForManyAwkwardTotalsAndWeights()
        {
            var random = new Random(7);
            for (int round = 0; round < 500; round++)
            {
                var weights = Enumerable.Range(0, random.Next(1, 20)).Select(_ => random.NextDouble() * 1000).ToArray();
                long total = random.Next(-2_000_000, 2_000_000);
                long[] parts = ServiceUpkeepSplit.Split(total, weights);
                Assert.Equal(total, parts.Sum());
                for (int i = 0; i < parts.Length; i++)
                {
                    double exact = total * weights[i] / weights.Sum();
                    Assert.True(Math.Abs(parts[i] - exact) < 1.0 + 1e-6, $"part {i}: {parts[i]} vs {exact}");
                }
            }
        }

        [Fact]
        public void Split_FollowsTheWeights()
        {
            Assert.Equal(new long[] { 25, 75 }, ServiceUpkeepSplit.Split(100, new[] { 1.0, 3.0 }));
            Assert.NotEqual(new long[] { 50, 50 }, ServiceUpkeepSplit.Split(100, new[] { 1.0, 3.0 }));
        }

        [Fact]
        public void Split_LargestRemainderGetsTheLeftoverUnits()
        {
            // 10 * (3, 7, 10) / 20 = 1.5, 3.5, 5: floors 1, 3, 5 leave 1 unit; the remainders tie at .5, the lower index wins.
            Assert.Equal(new long[] { 2, 3, 5 }, ServiceUpkeepSplit.Split(10, new[] { 3.0, 7.0, 10.0 }));
            // 7 * (1, 6, 3) / 10 = 0.7, 4.2, 2.1: floors 0, 4, 2 leave 1 unit for the largest remainder, .7.
            Assert.Equal(new long[] { 1, 4, 2 }, ServiceUpkeepSplit.Split(7, new[] { 1.0, 6.0, 3.0 }));
        }

        [Fact]
        public void Split_ZeroTotal_GivesZeros()
        {
            Assert.Equal(new long[] { 0, 0 }, ServiceUpkeepSplit.Split(0, new[] { 2.0, 3.0 }));
        }

        [Fact]
        public void Split_ZeroWeights_GivesZeros()
        {
            Assert.Equal(new long[] { 0, 0 }, ServiceUpkeepSplit.Split(100, new[] { 0.0, 0.0 }));
            Assert.Empty(ServiceUpkeepSplit.Split(100, new double[0]));
        }

        [Fact]
        public void Split_OneService_TakesAll()
        {
            Assert.Equal(new long[] { 12345 }, ServiceUpkeepSplit.Split(12345, new[] { 0.001 }));
            Assert.Equal(new long[] { 0, 12345, 0 }, ServiceUpkeepSplit.Split(12345, new[] { 0.0, 4.0, 0.0 }));
        }

        [Fact]
        public void Split_NegativeOrBadWeights_CountAsZero()
        {
            Assert.Equal(new long[] { 0, 100, 0, 0 }, ServiceUpkeepSplit.Split(100, new[] { -5.0, 5.0, double.NaN, double.PositiveInfinity }));
        }

        [Fact]
        public void Split_NegativeTotal_GivesNegativeParts()
        {
            long[] parts = ServiceUpkeepSplit.Split(-10, new[] { 1.0, 1.0, 1.0 });
            Assert.Equal(new long[] { -4, -3, -3 }, parts);
            Assert.Equal(-10, parts.Sum());
        }

        [Fact]
        public void Split_NullWeights_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceUpkeepSplit.Split(1, null));
        }

        [Fact]
        public void SplitSeries_NoDataBeforeTheFirstRecordedPoint_ThenCarriesForward()
        {
            var parent = new long[] { 50, 60, 70, 80, 90 };
            var weights = new float[][] { null, new[] { 1f, 1f }, null, new[] { 1f, 3f }, null };
            long[][] series = ServiceUpkeepSplit.SplitSeries(parent, weights, 2, out int start);
            Assert.Equal(1, start);
            Assert.Equal(new long[] { 0, 30, 35, 20, 23 }, series[0]); // 90 * 1/4 = 22.5: the tie goes to the lower index
            Assert.Equal(new long[] { 0, 30, 35, 60, 67 }, series[1]);
            for (int i = start; i < parent.Length; i++) Assert.Equal(parent[i], series[0][i] + series[1][i]);
        }

        [Fact]
        public void SplitSeries_NothingRecorded_StartIsThePointCount()
        {
            long[][] series = ServiceUpkeepSplit.SplitSeries(new long[] { 5, 6 }, new float[][] { null, null }, 3, out int start);
            Assert.Equal(2, start);
            Assert.All(series, s => Assert.All(s, v => Assert.Equal(0, v)));
        }

        [Fact]
        public void SplitSeries_ShortWeights_CountMissingServicesAsZero()
        {
            // A service seen after a sample was recorded has no weight in it.
            long[][] series = ServiceUpkeepSplit.SplitSeries(new long[] { 10 }, new[] { new[] { 2f } }, 2, out int start);
            Assert.Equal(0, start);
            Assert.Equal(10, series[0][0]);
            Assert.Equal(0, series[1][0]);
        }

        [Fact]
        public void SplitSeries_MismatchedLengths_Throws()
        {
            Assert.Throws<ArgumentException>(() => ServiceUpkeepSplit.SplitSeries(new long[] { 1, 2 }, new float[][] { null }, 1, out _));
        }

        [Fact]
        public void Rows_FollowTheOrder_LeaveOutAllZeroServices_AndSumToTheParent()
        {
            var parent = new long[] { 100, 200, 300 };
            var weights = new float[][] { null, new[] { 1f, 0f, 3f }, new[] { 1f, 0f, 1f } };
            var names = new[] { "Police", "Fire", "Water" };
            List<ServiceRow> rows = ServiceRows.Build(parent, weights, names, new[] { 2, 1, 0, 7 });
            Assert.Equal(new[] { "Water", "Police" }, rows.Select(r => r.Name)); // Fire is 0 throughout; 7 is no column
            Assert.All(rows, r => Assert.Equal(1, r.Start));
            Assert.Equal(new long[] { 0, 150, 150 }, rows[0].Values);
            for (int i = 1; i < parent.Length; i++) Assert.Equal(parent[i], rows.Sum(r => r.Values[i]));
        }

        [Fact]
        public void Rows_NothingRecorded_NoRows()
        {
            Assert.Empty(ServiceRows.Build(new long[] { 100 }, new float[][] { null }, new[] { "Police" }, new[] { 0 }));
        }

        [Fact]
        public void CityTag_LastPointIsTheSampleFillingNow()
        {
            Assert.Equal(100, ServiceUpkeepSplit.CityTag(100, 10, 9));
            Assert.Equal(91, ServiceUpkeepSplit.CityTag(100, 10, 0));
            Assert.NotEqual(100, ServiceUpkeepSplit.CityTag(100, 10, 8));
        }

        [Fact]
        public void DistrictWeightIndex_AlignsWithTheNewestSample()
        {
            // 10 budget samples, weights for the last 4.
            Assert.Equal(3, ServiceUpkeepSplit.DistrictWeightIndex(9, 10, 4));
            Assert.Equal(0, ServiceUpkeepSplit.DistrictWeightIndex(6, 10, 4));
            Assert.Equal(-1, ServiceUpkeepSplit.DistrictWeightIndex(5, 10, 4));
            Assert.Equal(-1, ServiceUpkeepSplit.DistrictWeightIndex(10, 10, 4)); // the live point is not a sample
            Assert.Equal(-1, ServiceUpkeepSplit.DistrictWeightIndex(3, 10, 0));
        }

        [Fact]
        public void Columns_KeepTheirIndex()
        {
            var columns = new ServiceColumns();
            Assert.Equal(0, columns.GetOrAdd("Police"));
            Assert.Equal(1, columns.GetOrAdd("Fire"));
            Assert.Equal(0, columns.GetOrAdd("Police"));
            Assert.Equal(2, columns.Count);
            Assert.Equal(-1, columns.IndexOf("Water"));
        }

        [Fact]
        public void WeightHistory_FindsByTag_ReplacesOlderTags_AndCaps()
        {
            var history = new ServiceWeightHistory();
            history.Add(1, new[] { 1f });
            history.Add(2, new[] { 2f });
            Assert.Equal(2f, history.ForTag(2)[0]);
            Assert.Null(history.ForTag(3));
            history.Add(2, new[] { 5f }); // the same sample again (a reload): replaces it
            Assert.Equal(2, history.Count);
            Assert.Equal(5f, history.ForTag(2)[0]);
            history.Add(1, new[] { 9f }); // an older tag (the sample count went back): drops the newer ones
            Assert.Equal(1, history.Count);
            Assert.Equal(9f, history.ForTag(1)[0]);

            for (int i = 0; i < ServiceWeightHistory.MaxSamples + 10; i++) history.Add(10 + i, new[] { (float)i });
            Assert.Equal(ServiceWeightHistory.MaxSamples, history.Count);
            Assert.Null(history.ForTag(10));
        }

        private sealed class Stream : ISaveReader, ISaveWriter
        {
            private readonly Queue<object> _items = new Queue<object>();
            public void Write(int value) => _items.Enqueue(value);
            public void Write(float value) => _items.Enqueue(value);
            public void Write(string value) => _items.Enqueue(value);
            public int ReadInt() => (int)_items.Dequeue();
            public float ReadFloat() => (float)_items.Dequeue();
            public string ReadString() => (string)_items.Dequeue();
            public int Left => _items.Count;
        }

        [Fact]
        public void City_RoundTrips()
        {
            var columns = new ServiceColumns();
            columns.GetOrAdd("Healthcare");
            columns.GetOrAdd("Education");
            var history = new ServiceWeightHistory();
            history.Add(4, new[] { 1.5f, 2f });
            history.Add(5, new[] { 3f });
            var stream = new Stream();
            ServiceUpkeepSave.WriteCity(stream, columns, history);

            var readColumns = new ServiceColumns();
            Assert.True(ServiceUpkeepSave.ReadCity(stream, readColumns, out ServiceWeightHistory read));
            Assert.Equal(0, stream.Left);
            Assert.Equal("Education", readColumns[1]);
            Assert.Equal(2, read.Count);
            Assert.Equal(new[] { 1.5f, 2f }, read.ForTag(4));
            Assert.Equal(new[] { 3f }, read.ForTag(5));
        }

        [Fact]
        public void City_UnknownVersion_LoadsEmpty()
        {
            var stream = new Stream();
            stream.Write(99);
            var columns = new ServiceColumns();
            columns.GetOrAdd("stale");
            Assert.False(ServiceUpkeepSave.ReadCity(stream, columns, out ServiceWeightHistory read));
            Assert.Equal(0, columns.Count);
            Assert.Equal(0, read.Count);
        }

        private static float[] Sample(float v) => Enumerable.Repeat(v, DistrictBudgetMath.SourceCount).ToArray();

        [Fact]
        public void Districts_RoundTripWithWeights()
        {
            var columns = new ServiceColumns();
            columns.GetOrAdd("Police");
            var budget = new DistrictHistory();
            budget.Add(Sample(1));
            budget.Add(Sample(2));
            var weights = new ServiceWeightHistory();
            weights.Add(7, new[] { 4f });
            var stream = new Stream();
            ServiceUpkeepSave.WriteDistricts(stream, key => stream.Write(key), columns,
                new[] { new KeyValuePair<int, DistrictHistory>(42, budget) }, key => key == 42 ? weights : null);

            var readColumns = new ServiceColumns();
            var got = new List<(int key, DistrictHistory budget, ServiceWeightHistory weights)>();
            Assert.True(ServiceUpkeepSave.ReadDistricts(stream, stream.ReadInt, readColumns, (k, b, w) => got.Add((k, b, w))));
            Assert.Equal(0, stream.Left);
            Assert.Equal("Police", readColumns[0]);
            Assert.Single(got);
            Assert.Equal(42, got[0].key);
            Assert.Equal(new double[] { 1, 2 }, got[0].budget.Series(0));
            Assert.Equal(new[] { 4f }, got[0].weights.ForTag(7));
        }

        [Fact]
        public void Districts_LoadsAVersion1Save_WithoutWeights()
        {
            // The block as DistrictBudgetSystem wrote it before per-service weights: version 1, count, per district key,
            // sample count and samples, nothing else.
            var stream = new Stream();
            stream.Write(1);
            stream.Write(2);
            foreach (int key in new[] { 5, 6 })
            {
                stream.Write(key);
                stream.Write(1);
                foreach (float f in Sample(key)) stream.Write(f);
            }

            var columns = new ServiceColumns();
            var got = new List<(int key, DistrictHistory budget, ServiceWeightHistory weights)>();
            Assert.True(ServiceUpkeepSave.ReadDistricts(stream, stream.ReadInt, columns, (k, b, w) => got.Add((k, b, w))));
            Assert.Equal(0, stream.Left);
            Assert.Equal(0, columns.Count);
            Assert.Equal(new[] { 5, 6 }, got.Select(g => g.key));
            Assert.Equal(new double[] { 6 }, got[1].budget.Series(15));
            Assert.All(got, g => Assert.Null(g.weights));
        }

        [Fact]
        public void Districts_UnknownVersion_LoadsNothing()
        {
            var stream = new Stream();
            stream.Write(3);
            var got = 0;
            Assert.False(ServiceUpkeepSave.ReadDistricts(stream, stream.ReadInt, new ServiceColumns(), (int k, DistrictHistory b, ServiceWeightHistory w) => got++));
            Assert.Equal(0, got);
        }
    }
}
