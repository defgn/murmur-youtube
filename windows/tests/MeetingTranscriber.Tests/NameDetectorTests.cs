using MeetingTranscriber.Core;
using Xunit;

namespace MeetingTranscriberTests;

public sealed class NameDetectorTests
{
    [Theory]
    [InlineData("Giang")]
    [InlineData("giang")]
    [InlineData("what do you think Giang?")]
    [InlineData("Yang, can you hear me?")]
    [InlineData("yang")]
    [InlineData("thank you jang for the update")]
    [InlineData("JANG")]
    [InlineData("jiang, over to you")]
    [InlineData("What do you think Giang? Should we keep the late arriving fact window at 3 days or cut it to 1?")]
    public void Positive_vectors_hit(string text) =>
        Assert.True(NameDetector.MentionsName(text));

    [Theory]
    [InlineData("giant")]
    [InlineData("The Giant Panda export ran fine.")]
    [InlineData("Jiangsu")]
    [InlineData("Jiangsu province sales are flat.")]
    [InlineData("G.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Morning all. Bridge tables for orders to customers are mapped.")]
    public void Negative_vectors_miss(string text) =>
        Assert.False(NameDetector.MentionsName(text));

    [Fact]
    public void Null_is_negative() => Assert.False(NameDetector.MentionsName(null));

    [Fact]
    public void Variant_is_reported_upper_invariant()
    {
        Assert.Equal("GIANG", NameDetector.MatchedVariant("what do you think Giang?"));
        Assert.Equal("YANG", NameDetector.MatchedVariant("yang"));
        Assert.Equal("JANG", NameDetector.MatchedVariant("Jang!"));
        Assert.Equal("JIANG", NameDetector.MatchedVariant("jiang"));
        Assert.Null(NameDetector.MatchedVariant("giant step"));
    }
}
