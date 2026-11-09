namespace Brain.Models;

/// <summary>The Claude models brain asks for one answer: a system prompt and a prompt in, the text of the answer out.</summary>
internal interface IModels
{
    /// <summary>
    /// The answer of <paramref name="model"/>, haiku, sonnet, opus or fable, at <paramref name="effort"/>, one of
    /// <see cref="Conversations.ConversationStore.Efforts"/>, within <paramref name="timeout"/>; throws a refusal that
    /// says why when there is none.
    /// </summary>
    string Ask(string model, string effort, string system, string prompt, TimeSpan timeout);
}
