namespace Domain.Grid.Commands;

public enum GridCommandRejection
{
    None, NotAuthorized, UnknownTarget, InvalidValue, NotSelected, SelectionExpired, Interlocked,
    AlreadyInState, RateLimited, Replay, CommunicationLoss, Paused
}
