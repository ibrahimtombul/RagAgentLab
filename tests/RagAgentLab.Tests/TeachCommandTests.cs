using RagAgentLab.Rag;

namespace RagAgentLab.Tests;

/// <summary>
/// Tests for the chat command that writes to the knowledge base. An unrecognised spelling is not
/// rejected — it falls through to the model as an ordinary question, so the note is silently not
/// stored. That makes the parsing worth pinning down.
/// </summary>
public sealed class TeachCommandTests
{
    [Theory]
    [InlineData("/ogren Şirket aracı talepleri Filo birimine yapılır.")]
    [InlineData("/öğren Şirket aracı talepleri Filo birimine yapılır.")]   // Turkish keyboard
    [InlineData("/ÖĞREN Şirket aracı talepleri Filo birimine yapılır.")]
    [InlineData("/öğret Şirket aracı talepleri Filo birimine yapılır.")]
    [InlineData("/learn Şirket aracı talepleri Filo birimine yapılır.")]
    [InlineData("   /ogren    Şirket aracı talepleri Filo birimine yapılır.   ")]
    public void TryParse_AcceptsEverySpellingAndReturnsTheNote(string message)
    {
        Assert.True(TeachCommand.TryParse(message, out var note));
        Assert.Equal("Şirket aracı talepleri Filo birimine yapılır.", note);
    }

    [Theory]
    [InlineData("Yıllık izin hakkım kaç gün?")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ogren bana bir şey")]                  // no leading slash
    [InlineData("/ogrenmek istiyorum")]                 // the alias must be a whole word
    [InlineData("Bunu /ogren diye kaydedebilir miyim?")] // not at the start
    public void TryParse_LeavesOrdinaryMessagesAlone(string message)
    {
        Assert.False(TeachCommand.TryParse(message, out var note));
        Assert.Equal(string.Empty, note);
    }

    [Fact]
    public void TryParse_AcceptsTheCommandWithNothingAfterIt()
    {
        // The writer rejects an empty note with a message the user can act on, so parsing it as
        // a command is what produces the useful error.
        Assert.True(TeachCommand.TryParse("/ogren", out var note));
        Assert.Equal(string.Empty, note);
    }

    [Fact]
    public void Canonical_IsOneOfTheAcceptedSpellings() =>
        Assert.Contains(TeachCommand.Canonical, TeachCommand.Aliases);
}
