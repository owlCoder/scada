namespace Hmi.Services.Grid;

public sealed record GridTransformerCatalogItem(string Name, double Mva, double HvKv, double LvKv, double UkPercent, double LoadLossKw, double NoLoadLossKw, bool Oltc);
