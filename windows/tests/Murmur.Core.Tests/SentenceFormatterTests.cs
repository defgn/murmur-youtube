using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// The deterministic sentence tier: spacing, capitals, terminal punctuation.
/// Conservative by design — words and numbers are never rewritten, decimals survive.
/// </summary>
public sealed class SentenceFormatterTests
{
    [Theory]
    [InlineData("hello world", "Hello world.")]
    [InlineData("i use cloud code every day", "I use cloud code every day.")]
    [InlineData("can I have 2 potatoes", "Can I have 2 potatoes.")]
    [InlineData("wait no that's wrong. i mean two", "Wait no that's wrong. I mean two.")]
    [InlineData("hello   world", "Hello world.")]
    [InlineData("no", "No.")]
    [InlineData("Hello, world!", "Hello, world!")]
    [InlineData("the answer is 3.14", "The answer is 3.14.")]
    [InlineData("what time is it", "What time is it.")]
    [InlineData("  leading and trailing  ", "Leading and trailing.")]
    [InlineData("i think we should go", "I think we should go.")]
    [InlineData("um actually I think we should go", "Um actually I think we should go.")]
    [InlineData("so the answer is 42", "So the answer is 42.")]
    [InlineData("three potatoes no one potato no three minus one potatoes",
        "Three potatoes no one potato no three minus one potatoes.")]
    public void Format_applies_spacing_capitals_and_terminal_punctuation(string input, string expected)
    {
        SentenceFormatter.Format(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_leaves_blank_text_alone(string input)
    {
        SentenceFormatter.Format(input).ShouldBe(input);
    }
}
