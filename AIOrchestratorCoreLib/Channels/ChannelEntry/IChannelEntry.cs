namespace AIOrchestratorCoreLib.Channels.ChannelEntry;

/// <summary>One append-only channel entry: '## [n] FROM author — date — subject' plus its body.</summary>
public interface IChannelEntry
{
    int Index { get; }
    ChannelAuthors Author { get; }
    string DateText { get; }
    string Subject { get; }
    string Body { get; }
    string RawText { get; }

    /// <summary>
    /// THE TYPE THE WRITER DECLARED, or null for an entry written before typed entries existed.
    ///
    /// <para>
    /// E3 requirement 3 (owner, 2026-09-10): the declared type is PERSISTED, so the state pack and
    /// the digest read it instead of guessing from the subject's first word. The guess is what
    /// produced "a brief that is merely QUOTED becomes the brief" — <c>Brief_Finder</c> matched a
    /// subject beginning `BRIEF —`, and a reviewer quoting a brief back wrote exactly that subject.
    /// </para>
    /// <para>
    /// NULL IS THE COMMON CASE AND NOT AN ERROR. Every entry on every channel today has no type line,
    /// and a session on the old skill goes on writing untyped entries — that is the transition the
    /// brief asks for, and a parser that treated an untyped entry as malformed would make the whole
    /// history unreadable. Callers ask "is this the type I want" and fall back to what they did
    /// before when the answer is null.
    /// </para>
    /// </summary>
    string? Type { get; }
}
