using Application.Identity;
using Domain.Grid.Commands;

namespace Application.Grid;

/// <summary>
/// Supervisory control used by the HMI, REST and IEC 60870-5-104. Requires the Operator role (alarm acknowledge too);
/// every call is audited. <paramref name="source"/> names the channel, for example "hmi", "rest" or "iec104".
/// </summary>
public interface IGridControl
{
    Task<GridCommandResult> ExecuteAsync(GridCommand command, Actor actor, string source, CancellationToken cancellationToken);
}
