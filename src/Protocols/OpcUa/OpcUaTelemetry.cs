using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Protocols.OpcUa;

internal sealed class OpcUaTelemetry(ILoggerFactory loggerFactory) : TelemetryContextBase(loggerFactory);
