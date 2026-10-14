namespace CirDeck.Core;

public sealed record AppEntry(
    string Name,
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string Source);
