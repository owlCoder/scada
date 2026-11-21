namespace Application.Detection;

public interface IDetectionStore
{
    Task<bool> AppendAsync(DetectionRecord detection, CancellationToken cancellationToken);
    Task<IReadOnlyList<DetectionRecord>> QueryAsync(DetectionQuery query, CancellationToken cancellationToken);
}
