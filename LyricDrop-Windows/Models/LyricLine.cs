using System;

namespace LyricDrop.Models;

public sealed class LyricLine
{
    public Guid Id { get; } = Guid.NewGuid();
    public double Time { get; init; }
    public string Text { get; init; } = string.Empty;
}
