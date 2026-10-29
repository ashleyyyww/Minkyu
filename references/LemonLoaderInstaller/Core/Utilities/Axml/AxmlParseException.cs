using System;

namespace QuestPatcher.Axml
{
    public class AxmlParseException : Exception
    {
        internal AxmlParseException(string? message) : base(message) { }
        internal AxmlParseException(string? message, Exception? cause) : base(message, cause) { }
    }
}
