namespace Brain.Voices;

/// <summary>One message the user typed, with its transcript timestamp.</summary>
internal sealed record VoiceMessage(string Timestamp, string Text);
