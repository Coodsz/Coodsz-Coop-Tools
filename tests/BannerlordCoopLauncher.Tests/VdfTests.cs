using BannerlordCoopLauncher.Core.Parsing;

namespace BannerlordCoopLauncher.Tests;

public class VdfTests
{
    [Fact]
    public void Truncated_object_throws_vdf_exception()
    {
        Assert.Throws<VdfException>(() => Vdf.Parse("\"a\" { \"b\" \"c\""));
    }

    [Theory]
    [InlineData("\"a\" {  \"b\"")]
    [InlineData("\"a\" { \"b\" \"c")]
    [InlineData("\"a\"")]
    public void Text_cut_off_anywhere_throws_vdf_exception(string text)
    {
        Assert.Throws<VdfException>(() => Vdf.Parse(text));
    }
}
