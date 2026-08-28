using System.IO;
using Nudge.Core.Diagnostics;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Nudge.App.Logging;

/// <summary>
/// Renders a log line and then removes the Windows username from it.
///
/// Call sites already redact the paths they log, but this is the backstop: it catches an exception
/// message, a stack trace or a property that nobody thought to redact. Users paste logs into public
/// forums, so the username must not survive to disk under any circumstances.
/// </summary>
public sealed class RedactingTextFormatter : ITextFormatter
{
    private readonly ITextFormatter _inner;
    private readonly IPathRedactor _redactor;

    public RedactingTextFormatter(string outputTemplate, IPathRedactor redactor)
    {
        _inner = new MessageTemplateTextFormatter(outputTemplate, formatProvider: null);
        _redactor = redactor;
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        // Rendered into a buffer first, because redaction has to run over the finished line.
        using var buffer = new StringWriter();
        _inner.Format(logEvent, buffer);
        output.Write(_redactor.Redact(buffer.ToString()));
    }
}
