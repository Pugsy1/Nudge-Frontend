using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Vpx.TableFiles;

/// <inheritdoc cref="IRomNameReader" />
public sealed class RomNameReader : IRomNameReader
{
    private readonly IGameDataScriptReader _scriptReader;
    private readonly IRomNameParser _parser;

    public RomNameReader(IGameDataScriptReader scriptReader, IRomNameParser parser)
    {
        _scriptReader = scriptReader;
        _parser = parser;
    }

    public async Task<Result<RomNameInfo>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        Result<string> scriptResult = await _scriptReader.ReadScriptAsync(path, cancellationToken).ConfigureAwait(false);

        return scriptResult.IsFailure
            ? Result<RomNameInfo>.Failure(scriptResult.Error)
            : Result<RomNameInfo>.Success(_parser.Parse(scriptResult.Value));
    }
}
