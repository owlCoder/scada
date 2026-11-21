using System.Security.Cryptography;
using System.Text;
using Application.Commands;
using Application.Simulation;
using Opc.Ua;
using Opc.Ua.Server;
using Protocols.OpcUa.Nodes;

namespace Protocols.OpcUa;

internal sealed class PlantOpcUaServer(
    OpcUaOptions options, IOpcUaIdentityValidator identities, SimulationEngine simulation,
    ControlCommandService commands, TimeProvider time) : StandardServer
{
    public PlantNodeManager Nodes { get; private set; } = null!;

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        Nodes = new PlantNodeManager(server, configuration, simulation, commands, time);
        return new MasterNodeManager(server, configuration, null, Nodes);
    }

    public override UserTokenPolicyCollection GetUserTokenPolicies(ApplicationConfiguration configuration, EndpointDescription description)
    {
        var policies = base.GetUserTokenPolicies(configuration, description);
        return description.SecurityMode == MessageSecurityMode.None
            ? new UserTokenPolicyCollection(policies.Where(policy => policy.TokenType == UserTokenType.Anonymous))
            : policies;
    }

    protected override void OnServerStarted(IServerInternal server)
    {
        base.OnServerStarted(server);
        server.SessionManager.ImpersonateUser += (_, args) => { Authenticate(args); };
    }

    private IUserIdentity Authenticate(ImpersonateEventArgs args)
    {
        if (options.AllowAnonymous && args.NewIdentity is AnonymousIdentityToken)
            return args.Identity = new UserIdentity(args.NewIdentity);

        if (args.EndpointDescription.SecurityMode == MessageSecurityMode.None ||
            args.NewIdentity is not UserNameIdentityToken { DecryptedPassword: { Length: > 0 } password } token)
            throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);

        try
        {
            // The SDK's impersonation callback is synchronous and runs on a request thread.
            var actor = identities.ValidateAsync(token.UserName, Encoding.UTF8.GetString(password), CancellationToken.None)
                .GetAwaiter().GetResult();
            if (actor is not null)
                return args.Identity = new OpcUaSessionIdentity(actor, token.PolicyId);
        }
        catch (Exception)
        {
            // Keep validator errors and the incoming token out of SDK diagnostics.
            throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }

        throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
    }
}
