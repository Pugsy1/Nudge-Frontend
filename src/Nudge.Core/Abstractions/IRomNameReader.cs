using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Reads a table's PinMAME ROM name out of its VBScript. Implemented by
/// <c>Nudge.Vpx.TableFiles.RomNameReader</c>. Deliberately separate from <see cref="ITableFileReader"/>
/// - see AGENTS.md section 4.5 - because it must read the much larger <c>GameStg</c> storage rather
/// than the small <c>TableInfo</c> streams a fast library scan reads. Not currently wired into
/// <see cref="IVpxLibraryScanner"/>; when and how it should run automatically (every scan vs. a
/// separate background pass) is a performance-budget decision for whoever wires it in.
/// </summary>
public interface IRomNameReader
{
    Task<Result<RomNameInfo>> ReadAsync(string path, CancellationToken cancellationToken = default);
}
