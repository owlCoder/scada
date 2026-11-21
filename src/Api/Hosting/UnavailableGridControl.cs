using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace Api.Hosting;

/// <summary>Fallback <see cref="IGridControl"/> for hosts without a grid simulation; see <see cref="UnavailableGridView"/>.</summary>
public sealed class UnavailableGridControl : IGridControl
{
    public Task<GridCommandResult> ExecuteAsync(GridCommand command, Actor actor, string source, CancellationToken cancellationToken) =>
        Task.FromResult(GridCommandResult.Reject(GridCommandRejection.CommunicationLoss, "Grid mode is not available in this host."));
}
