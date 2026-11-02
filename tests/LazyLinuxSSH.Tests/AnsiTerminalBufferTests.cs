using LazyLinuxSSH.Core.Terminal;

namespace LazyLinuxSSH.Tests;

public sealed class AnsiTerminalBufferTests
{
    [Fact]
    public void Append_PlainTextAndNewLines_BuildsExpectedDocument()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("one\r\ntwo\nthree");
        var snapshot = buffer.GetSnapshot();
        Assert.Equal("one\ntwo\nthree", snapshot.Text);
        Assert.Empty(snapshot.Spans);
    }

    [Fact]
    public void Append_SgrColors_ReturnsAccurateOffsets()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("a \u001b[1;31mred\u001b[0m z");
        var snapshot = buffer.GetSnapshot();
        Assert.Equal("a red z", snapshot.Text);
        var span = Assert.Single(snapshot.Spans);
        Assert.Equal(2, span.Start);
        Assert.Equal(3, span.Length);
        Assert.Equal(TerminalColor.Red, span.Color);
        Assert.True(span.Bold);
    }

    [Fact]
    public void Append_SplitEscapeSequence_PreservesParserState()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("\u001b[3");
        buffer.Append("2mgreen");
        var snapshot = buffer.GetSnapshot();
        Assert.Equal("green", snapshot.Text);
        Assert.Equal(TerminalColor.Green, Assert.Single(snapshot.Spans).Color);
    }

    [Fact]
    public void Append_CarriageReturn_OverwritesCurrentLine()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("abc\rX");
        Assert.Equal("Xbc", buffer.GetSnapshot().Text);
    }

    [Fact]
    public void Append_Backspace_MovesCursorWithoutErasing()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("abc\bX");
        Assert.Equal("abX", buffer.GetSnapshot().Text);
    }

    [Fact]
    public void Append_ClearLine_RemovesTextAfterCursor()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("abcdef\r\u001b[2C\u001b[K");
        Assert.Equal("ab", buffer.GetSnapshot().Text);
    }

    [Fact]
    public void Append_ClearScreen_ResetsDocument()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("old\ntext\u001b[2Jnew");
        Assert.Equal("new", buffer.GetSnapshot().Text);
    }

    [Fact]
    public void Append_CursorPosition_UpdatesRequestedCell()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("a\nb\u001b[1;1HZ");
        Assert.Equal("Z\nb", buffer.GetSnapshot().Text);
    }

    [Fact]
    public void Append_OscTitle_DoesNotRenderEscapePayload()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("\u001b]0;root@server\aok");
        Assert.Equal("ok", buffer.GetSnapshot().Text);
    }

    [Fact]
    public void Append_TrueColor_ApproximatesNearestAnsiColor()
    {
        var buffer = new AnsiTerminalBuffer();
        buffer.Append("\u001b[38;2;240;40;40mR");
        Assert.Equal(TerminalColor.BrightRed, Assert.Single(buffer.GetSnapshot().Spans).Color);
    }

    [Fact]
    public void Clear_RaisesUpdateAndResetsPendingSequence()
    {
        var buffer = new AnsiTerminalBuffer();
        var updates = 0;
        buffer.Updated += (_, _) => updates++;
        buffer.Append("\u001b[31");
        buffer.Clear();
        buffer.Append("ok");
        Assert.Equal("ok", buffer.GetSnapshot().Text);
        Assert.Equal(3, updates);
    }
}
