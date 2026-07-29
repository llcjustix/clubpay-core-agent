using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Admin.Services;

namespace ClubPay.Agent.Admin.Tests;

public class ManagerPinServiceTests
{
    private static ManagerPinService Build(string? hash) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:ManagerPinHash"] = hash })
            .Build());

    [Fact]
    public void Verify_CorrectPin_ReturnsTrue()
    {
        // sha256("1234")
        var service = Build("03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4");

        Assert.True(service.Verify("1234"));
    }

    [Fact]
    public void Verify_WrongPin_ReturnsFalse()
    {
        var service = Build("03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4");

        Assert.False(service.Verify("0000"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_NoHashConfigured_AlwaysReturnsFalse(string? hash)
    {
        var service = Build(hash);

        Assert.False(service.Verify("1234"));
        Assert.False(service.Verify(""));
    }

    [Fact]
    public void Verify_HashComparisonIsCaseInsensitive()
    {
        var service = Build("03AC674216F3E15C761EE1A5E255F067953623C8B388B4459E13F978D7C846F4");

        Assert.True(service.Verify("1234"));
    }
}
