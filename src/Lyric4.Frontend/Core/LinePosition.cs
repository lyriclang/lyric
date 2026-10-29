// Frozen from release/v4.6.0 (002b7a4b) by tooling/from4/freeze.py: Lyric 4.6 as it was. Do not edit.
namespace Lyric4.Core;

public readonly record struct LinePosition(int Line, int Column)
{
    public override string ToString() => $"{Line}:{Column}";
}