using Application;

namespace Api.Contracts;

public sealed record ValveCommand(bool Open, string Actor);
