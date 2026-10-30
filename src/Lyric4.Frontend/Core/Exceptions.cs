// Frozen from release/v4.6.0 (002b7a4b) by tooling/from4/freeze.py: Lyric 4.6 as it was. Do not edit.
using System;
using System.Collections.Generic;
using System.Text;

namespace Lyric4.Core
{
    public sealed class InternalCompilationException(string message, Exception? inner = null) : Exception(message?.ToString(), inner);
    public static class InternalExceptions
    {
        public static void Unreachable(object value)
        {
            throw new InternalCompilationException($"unreachable: unexpected {value}");
        }
    }
}
