using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// The disfluency-removal contract. These vectors are the specification: change the
/// behaviour, change the vectors first, watch this go red, then make it green.
/// </summary>
public sealed class DisfluencyCleanerTests
{
    [Theory]
    [InlineData("um I went to the shop", "I went to the shop")]
    [InlineData("I went to the shop uh to buy milk", "I went to the shop to buy milk")]
    [InlineData("er, that's not right", "that's not right")]
    [InlineData("I think, erm, it's fine", "I think, it's fine")]
    [InlineData("hmm let me think", "let me think")]
    [InlineData("mm I suppose so", "I suppose so")]
    [InlineData("ahem, excuse me", "excuse me")]
    [InlineData("um um um I'm ready", "I'm ready")]
    [InlineData("I was like, uh, actually ready", "I was like, actually ready")]  // uh removed, "actually" mid-sentence survives
    public void Unconditional_fillers_are_removed(string input, string expected)
    {
        DisfluencyCleaner.Clean(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("uh-huh that's right", "that's right")]       // hyphenated filler intact
    [InlineData("mm-hmm sure", "sure")]
    [InlineData("mhm okay", "okay")]
    public void Hyphenated_fillers_are_removed_whole(string input, string expected)
    {
        DisfluencyCleaner.Clean(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Actually, I think we should go", "I think we should go")]
    [InlineData("Actually I think we should go", "I think we should go")]   // no comma — still a filler marker
    [InlineData("Basically, it works", "it works")]
    [InlineData("Literally the best one", "the best one")]
    [InlineData("Honestly, I don't care", "I don't care")]
    [InlineData("Frankly it's over", "it's over")]
    public void Filler_markers_at_sentence_start_are_removed(string input, string expected)
    {
        DisfluencyCleaner.Clean(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Well, that's a good point", "that's a good point")]
    [InlineData("So, I think we're done", "I think we're done")]
    [InlineData("Right, let's begin", "let's begin")]
    [InlineData("Okay, here's the plan", "here's the plan")]
    [InlineData("Like, I was walking home", "I was walking home")]
    [InlineData("You know, it happens", "it happens")]
    [InlineData("I mean, it's complicated", "it's complicated")]
    public void Comma_gated_markers_are_removed_with_comma(string input, string expected)
    {
        DisfluencyCleaner.Clean(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("So the answer is 42", "So the answer is 42")]   // meaningful "so" survives
    [InlineData("Like I said earlier, it's fine", "Like I said earlier, it's fine")]  // meaningful "like"
    [InlineData("Well done everyone", "Well done everyone")]     // "well" as adverb survives
    [InlineData("Right now I need coffee", "Right now I need coffee")]  // "right" as intensifier
    [InlineData("You know the answer", "You know the answer")]   // actual directive survives
    public void Meaningful_markers_survive(string input, string expected)
    {
        DisfluencyCleaner.Clean(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("um, actually I think so", "I think so")]        // filler then marker, both removed
    [InlineData("Actually, um, let's go", "let's go")]           // marker then filler
    [InlineData("I went to the shop. Um, then I came home.", "I went to the shop. then I came home.")]
    public void Combined_disfluencies_are_cleaned(string input, string expected)
    {
        DisfluencyCleaner.Clean(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("no fillers here", "no fillers here")]
    public void Edge_cases_are_safe(string input, string expected)
    {
        DisfluencyCleaner.Clean(input).ShouldBe(expected);
    }
}
