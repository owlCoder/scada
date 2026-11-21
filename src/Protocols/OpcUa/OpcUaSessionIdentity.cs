using Application.Identity;
using Opc.Ua;

namespace Protocols.OpcUa;

internal sealed class OpcUaSessionIdentity(Actor actor, string policyId) :
    UserIdentity(new UserNameIdentityToken { UserName = actor.Name, PolicyId = policyId })
{
    public static Actor Anonymous { get; } = new("opcua-anonymous", AccessRole.Viewer);

    public Actor Actor { get; } = actor;

    public static Actor ActorFor(ISystemContext context) =>
        (context as SessionSystemContext)?.UserIdentity is OpcUaSessionIdentity identity ? identity.Actor : Anonymous;
}
