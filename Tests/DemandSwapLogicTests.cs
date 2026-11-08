using System;
using System.Reflection;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DemandSwapLogicTests
    {
        // Stand-ins for the three vanilla demand systems: each declares its own private m_TaxSystem, as the game's do.
        private class FakeTaxSystem { }
        private class FakeResidential { private FakeTaxSystem m_TaxSystem = new FakeTaxSystem(); public FakeTaxSystem Get() => m_TaxSystem; }
        private class FakeCommercial { private FakeTaxSystem m_TaxSystem = new FakeTaxSystem(); public FakeTaxSystem Get() => m_TaxSystem; }
        private class FakeIndustrial { private FakeTaxSystem m_TaxSystem = new FakeTaxSystem(); public FakeTaxSystem Get() => m_TaxSystem; }
        private class NoField { }
        private class WrongType { private int m_TaxSystem = 0; public int Get() => m_TaxSystem; }

        private static readonly Type[] Three = { typeof(FakeResidential), typeof(FakeCommercial), typeof(FakeIndustrial) };

        [Fact]
        public void ResolveFields_GivesEachTypeItsOwnField_WhichSetsThatTypesInstance()
        {
            FieldInfo[] fields = DemandSwapLogic.ResolveFields(Three, "m_TaxSystem", typeof(FakeTaxSystem), out string problem);
            Assert.Null(problem);
            Assert.Equal(3, fields.Length);
            for (int i = 0; i < 3; i++) Assert.Equal(Three[i], fields[i].DeclaringType);

            var instances = new object[] { new FakeResidential(), new FakeCommercial(), new FakeIndustrial() };
            Assert.True(DemandSwapLogic.FitsInstances(fields, instances, out problem), problem);
            var replacement = new FakeTaxSystem();
            for (int i = 0; i < 3; i++) fields[i].SetValue(instances[i], replacement);
            Assert.Same(replacement, ((FakeCommercial)instances[1]).Get());
            Assert.Same(replacement, ((FakeIndustrial)instances[2]).Get());
        }

        // Regression (batch-economy-4): one FieldInfo from the residential type, used on all three, threw
        // ArgumentException on every update. FitsInstances catches that pairing before the swap.
        [Fact]
        public void OneTypesField_OnTheOthers_DoesntFit_AndWouldThrow()
        {
            FieldInfo residential = typeof(FakeResidential).GetField("m_TaxSystem", DemandSwapLogic.InstanceFields);
            var fields = new[] { residential, residential, residential };
            var instances = new object[] { new FakeResidential(), new FakeCommercial(), new FakeIndustrial() };

            Assert.False(DemandSwapLogic.FitsInstances(fields, instances, out string problem));
            Assert.Contains("FakeCommercial", problem);
            Assert.Throws<ArgumentException>(() => residential.GetValue(instances[1]));
        }

        [Fact]
        public void ResolveFields_FailsNamingTheTypeThatLacksTheField()
        {
            Type[] types = { typeof(FakeResidential), typeof(NoField), typeof(FakeIndustrial) };
            Assert.Null(DemandSwapLogic.ResolveFields(types, "m_TaxSystem", typeof(FakeTaxSystem), out string problem));
            Assert.Equal("NoField.m_TaxSystem is missing", problem);
        }

        [Fact]
        public void ResolveFields_FailsNamingTheTypeWithTheWrongFieldType()
        {
            Type[] types = { typeof(FakeResidential), typeof(FakeCommercial), typeof(WrongType) };
            Assert.Null(DemandSwapLogic.ResolveFields(types, "m_TaxSystem", typeof(FakeTaxSystem), out string problem));
            Assert.StartsWith("WrongType.m_TaxSystem is Int32", problem);
        }

        [Fact]
        public void FitsInstances_FailsOnACountMismatch()
        {
            FieldInfo[] fields = DemandSwapLogic.ResolveFields(Three, "m_TaxSystem", typeof(FakeTaxSystem), out _);
            Assert.False(DemandSwapLogic.FitsInstances(fields, new object[] { new FakeResidential() }, out _));
        }

        [Theory]
        [InlineData(true, true, false, true)]
        [InlineData(false, true, false, false)]
        [InlineData(true, false, false, false)]
        [InlineData(true, true, true, false)]
        public void UseView_OnlyWhenStructureOkEngineChargingAndNotFaulted(bool structureOk, bool charging, bool faulted, bool expected)
            => Assert.Equal(expected, DemandSwapLogic.UseView(structureOk, charging, faulted));

        [Theory]
        [InlineData(DemandReads.Unknown, DemandReads.Vanilla, true)]   // master off at load: the vanilla line logs
        [InlineData(DemandReads.Vanilla, DemandReads.View, true)]
        [InlineData(DemandReads.View, DemandReads.Vanilla, true)]
        [InlineData(DemandReads.Vanilla, DemandReads.Vanilla, false)] // not every update
        [InlineData(DemandReads.View, DemandReads.View, false)]
        public void ShouldLog_OnceOnEachChange(DemandReads current, DemandReads next, bool expected)
            => Assert.Equal(expected, DemandSwapLogic.ShouldLog(current, next));
    }
}
