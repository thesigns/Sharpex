namespace Sharpex.Tests;

public class TokenizeTests
{
    [Fact]
    public void Single_unquoted_token()
    {
        var tokens = Sharpex.Tokenize("log foo");
        Assert.Equal(["log", "foo"], tokens);
    }

    [Fact]
    public void Two_unquoted_tokens_with_special_char()
    {
        var tokens = Sharpex.Tokenize("log foo foo:");
        Assert.Equal(["log", "foo", "foo:"], tokens);
    }

    [Fact]
    public void Quoted_string_with_space()
    {
        var tokens = Sharpex.Tokenize("log \"foo foo\"");
        Assert.Equal(["log", "foo foo"], tokens);
    }

    [Fact]
    public void Escaped_quotes_inside_quoted_string()
    {
        var tokens = Sharpex.Tokenize("log \"foo \"\"quote\"\" foo\"");
        Assert.Equal(["log", "foo \"quote\" foo"], tokens);
    }

    [Fact]
    public void Single_escaped_quote()
    {
        // """" = opening " + "" (escaped ") + closing "
        var tokens = Sharpex.Tokenize("log \"\"\"\"");
        Assert.Equal(["log", "\""], tokens);
    }

    [Fact]
    public void Unterminated_quote_throws()
    {
        Assert.Throws<FormatException>(() => Sharpex.Tokenize("log \"\"\""));
    }

    [Fact]
    public void Quote_space_quote()
    {
        // """ """ = " + "" (→") + space + "" (→") + "
        var tokens = Sharpex.Tokenize("log \"\"\"\" \"\"\"\"\"\"");
        Assert.Equal(["log", "\"", "\"\""], tokens);
    }

    [Fact]
    public void Mixed_quoted_and_unquoted()
    {
        var tokens = Sharpex.Tokenize("log \"foo \" \"quote\" \" foo\"");
        Assert.Equal(["log", "foo ", "quote", " foo"], tokens);
    }

    [Fact]
    public void Five_parameters_mixed_types()
    {
        var tokens = Sharpex.Tokenize("log \"foo \" \"quote\" \" foo\" true 3.1415");
        Assert.Equal(["log", "foo ", "quote", " foo", "true", "3.1415"], tokens);
    }

    [Fact]
    public void Empty_input()
    {
        var tokens = Sharpex.Tokenize("");
        Assert.Empty(tokens);
    }

    [Fact]
    public void Empty_quoted_string()
    {
        var tokens = Sharpex.Tokenize("log \"\"");
        Assert.Equal(["log", ""], tokens);
    }
}
