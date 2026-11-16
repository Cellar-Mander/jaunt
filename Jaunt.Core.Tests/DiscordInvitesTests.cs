using System.Text.Json;
using Jaunt.Core.Images;

namespace Jaunt.Core.Tests;

public class DiscordInvitesTests
{
    [Fact]
    public void ReadsTheServersIconBannerAndSplashAsPng()
    {
        var images = DiscordInvites.Parse("""
            {"type":0,"code":"lounge","inviter":{"id":"9","avatar":"ffff"},
             "guild":{"id":"1437748251527876670","name":"Lounge","icon":"5305f6fe","banner":"a_63859b38","splash":"c0ffee"}}
            """);

        Assert.Equal(
            new DiscordInvites.Images(
                new Uri("https://cdn.discordapp.com/icons/1437748251527876670/5305f6fe.png?size=128"),
                new Uri("https://cdn.discordapp.com/banners/1437748251527876670/a_63859b38.png?size=1024"),
                new Uri("https://cdn.discordapp.com/splashes/1437748251527876670/c0ffee.png?size=1024")),
            images);
    }

    [Fact]
    public void AnImageHashThatIsntTextIsNone()
    {
        var images = DiscordInvites.Parse("""{"guild":{"id":"1","icon":5,"banner":{"a":"b"},"splash":["c"]}}""");

        Assert.Equal(new DiscordInvites.Images(null, null, null), images);
    }

    [Fact]
    public void AServerWithoutImagesHasNone()
    {
        var images = DiscordInvites.Parse("""{"guild":{"id":"1","icon":null,"banner":"","splash":"../x"}}""");

        Assert.Equal(new DiscordInvites.Images(null, null, null), images);
    }

    [Theory]
    [InlineData("""{"code":"abc","channel":{"type":3}}""")] // a group DM invite
    [InlineData("""{"guild":{"icon":"abc"}}""")]
    [InlineData("""{"guild":{"id":"1/../2","icon":"abc"}}""")]
    [InlineData("""{"guild":"1"}""")]
    [InlineData("""{"message":"Unknown Invite","code":10006}""")]
    [InlineData("[]")]
    public void AnInviteWithoutAServerGivesNothing(string json)
    {
        Assert.Null(DiscordInvites.Parse(json));
    }

    [Fact]
    public void TextThatIsntJsonThrows()
    {
        Assert.ThrowsAny<JsonException>(() => DiscordInvites.Parse("<html>"));
    }

    [Fact]
    public void TheCodeIsEscapedInTheApiAddress()
    {
        Assert.Equal("https://discord.com/api/v10/invites/Lounge-2", DiscordInvites.Url("Lounge-2").AbsoluteUri);
        Assert.Equal("/api/v10/invites/a%2Fb", DiscordInvites.Url("a/b").AbsolutePath);
    }
}
