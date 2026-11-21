namespace Domain.Grid.Snapshots;

public sealed record GridTotals(
    double LoadMw,
    double GenerationMw,
    double SourceImportMw,
    double LossesMw,
    int Customers,
    int CustomersSupplied);
