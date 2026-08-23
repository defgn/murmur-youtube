using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// The arithmetic self-correction spec: the "three potatoes no one potato no three minus
/// one potatoes → 2 potatoes" behaviour, vector by vector.
/// </summary>
public sealed class ArithmeticSimplifierTests
{
    [Theory]
    [InlineData(
        "can I have three potatoes no one potato no three minus one potatoes",
        "can I have 2 potatoes")]
    [InlineData(
        "can I have three potatoes no,,,one potato,,, No three minus one potatoes",
        "can I have 2 potatoes")]
    [InlineData(
        "three potatoes, no, one potato, no, three minus one potatoes",
        "2 potatoes")]
    [InlineData("three potatoes no one potato", "1 potato")]
    [InlineData("three potatoes no two potatoes", "2 potatoes")]
    [InlineData("two apples no wait three pears", "3 pears")]
    [InlineData("I want five books, actually six books", "I want 6 books")]
    [InlineData("No three minus one potatoes", "No 2 potatoes")]
    [InlineData("twenty one take away three potatoes", "18 potatoes")]
    [InlineData("three minus one potatoes", "2 potatoes")]
    [InlineData("3 potatoes no 1 potato no 3 minus 1 potatoes", "2 potatoes")]
    [InlineData("one hundred minus ten potatoes", "90 potatoes")]
    [InlineData("four plus five apples", "9 apples")]
    public void Correction_chains_and_arithmetic_resolve(string input, string expected)
    {
        ArithmeticSimplifier.Simplify(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("no potatoes", "no potatoes")]                    // negation, not a correction
    [InlineData("no, three potatoes", "no, three potatoes")]      // an answer, not a correction
    [InlineData("one potato", "one potato")]                      // nothing to simplify
    [InlineData("three hundred potatoes", "three hundred potatoes")]
    [InlineData("minus five degrees", "minus five degrees")]      // a negative number, not an op
    [InlineData("I'll take three please", "I'll take three please")]  // "please" is not a unit
    [InlineData("two plus two equals four", "two plus two equals four")]  // a statement, not a fix
    [InlineData("one minus three potatoes", "one minus three potatoes")]  // below zero: leave alone
    [InlineData("three potatoes and two pears", "three potatoes and two pears")]  // no correction
    [InlineData("one two three", "one two three")]                // counting, not a quantity
    [InlineData("take away the rubbish", "take away the rubbish")]
    public void Anything_ambiguous_is_left_exactly_as_dictated(string input, string expected)
    {
        ArithmeticSimplifier.Simplify(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("   ", "   ")]
    public void Empty_and_whitespace_pass_through(string? input, string? expected)
    {
        ArithmeticSimplifier.Simplify(input!).ShouldBe(expected);
    }

    [Fact]
    public void Long_chains_keep_only_the_last_correction()
    {
        ArithmeticSimplifier.Simplify(
            "one apple no two apples no three apples no four apples").ShouldBe("4 apples");
    }

    [Fact]
    public void Compound_and_digit_numbers_mix()
    {
        ArithmeticSimplifier.Simplify(
            "twenty two people no thirty one people").ShouldBe("31 people");
        ArithmeticSimplifier.Simplify(
            "12 chairs no 14 chairs").ShouldBe("14 chairs");
    }
}
