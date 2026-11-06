using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DistrictKeyTests
    {
        [Fact]
        public void RoundTrip()
        {
            Assert.True(DistrictKey.TryParse(DistrictKey.Format(12, 3), out int index, out int version));
            Assert.Equal(12, index);
            Assert.Equal(3, version);
        }

        [Theory]
        [InlineData("outside")]
        [InlineData("")]
        [InlineData("x:y")]
        [InlineData("12:")]
        [InlineData(":3")]
        [InlineData("-1:3")]
        public void NotAnEntity(string key)
        {
            Assert.False(DistrictKey.TryParse(key, out _, out _));
        }
    }
}
