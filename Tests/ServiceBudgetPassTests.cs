using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class ServiceBudgetPassTests
    {
        [Fact]
        public void NotApplying_NothingLeftOver_Idles()
        {
            Assert.Equal(ServiceBudgetPassKind.None, ServiceBudgetPass.Plan(applies: false, appliedBefore: false, fullTrigger: true, safetyDue: true));
        }

        [Fact]
        public void StoppedApplying_OneFullRestorePass()
        {
            Assert.Equal(ServiceBudgetPassKind.Full, ServiceBudgetPass.Plan(applies: false, appliedBefore: true, fullTrigger: false, safetyDue: false));
        }

        [Fact]
        public void StartedApplying_FullPass()
        {
            Assert.Equal(ServiceBudgetPassKind.Full, ServiceBudgetPass.Plan(applies: true, appliedBefore: false, fullTrigger: false, safetyDue: false));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Applying_TriggerOrSafety_FullPass(bool trigger, bool safety)
        {
            Assert.Equal(ServiceBudgetPassKind.Full, ServiceBudgetPass.Plan(applies: true, appliedBefore: true, fullTrigger: trigger, safetyDue: safety));
        }

        [Fact]
        public void Applying_Quiet_OnlyChangedBuildings()
        {
            Assert.Equal(ServiceBudgetPassKind.Changed, ServiceBudgetPass.Plan(applies: true, appliedBefore: true, fullTrigger: false, safetyDue: false));
        }
    }
}
