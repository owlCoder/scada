namespace Application.Registers;

/// <summary>Mirrors the relevant Modbus exception codes.</summary>
public enum RegisterOperationStatus { Success, IllegalAddress, IllegalValue, Denied, UnknownUnit }
