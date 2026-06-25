namespace Roadhog.Core.Model;

public sealed record WorldObjectSnapshot(
    ushort EntityId,
    uint ServerObjectId,
    string Name,
    string ObjectKind,
    Vector3Snapshot? Position,
    double? DistanceToLocalPlayer);
