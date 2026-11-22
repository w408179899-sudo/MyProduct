using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

internal sealed partial class AionVmmGameApi : IRevivePathPoseGameApi
{
    private static readonly TimeSpan RevivePathPoseCachePeriod = TimeSpan.FromMilliseconds(20);

    public Task<OperationResult<PathFollowPose>> ReadRevivePathPoseAsync(
        GameApiReadContext context, CancellationToken cancellationToken) => Task.Run(() =>
        _stableSnapshotReads.Execute(BuildStableSnapshotReadScopeKey(context), AionVmmSnapshotChannels.RevivePathPose,
            () => ReadRevivePathPoseCore(context, cancellationToken)), cancellationToken);

    private OperationResult<PathFollowPose> ReadRevivePathPoseCore(GameApiReadContext context, CancellationToken stop)
    {
        var channel = AionVmmSnapshotChannels.RevivePathPose;
        var connection = GetOrCreateConnection(context.VmmDeviceName);
        var key = BuildConnectionKey(context.VmmDeviceName);
        lock (connection.SyncRoot)
        {
            stop.ThrowIfCancellationRequested();
            var sessionKey = BuildStableSnapshotSessionKey(context);
            _stableSnapshots.TryGet(sessionKey, channel, out var previous);
            if (previous is not null)
            {
                var age = DateTimeOffset.UtcNow - previous.Pose.CapturedAt;
                if (age >= TimeSpan.Zero && age < RevivePathPoseCachePeriod)
                {
                    lock (_connectionSync)
                    {
                        if (_connections.TryGetValue(key, out var active) && ReferenceEquals(active, connection))
                            return OperationResult<PathFollowPose>.Ok(previous.Pose);
                    }
                }
            }

            OperationResult<RevivePathPoseCapture> observed;
            try { observed = CaptureRevivePathPose(connection, context, previous); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                observed = OperationResult<RevivePathPoseCapture>.Fail(ex.Message);
            }

            stop.ThrowIfCancellationRequested();
            lock (_connectionSync)
            {
                if (!_connections.TryGetValue(key, out var current) || !ReferenceEquals(current, connection))
                    return OperationResult<PathFollowPose>.Fail("Revive path pose connection generation changed.");

                var result = ResolveStableRead(context, channel, observed, DateTimeOffset.UtcNow, null).Result;
                return result.Success && result.Value is { } publication
                    ? OperationResult<PathFollowPose>.Ok(publication.Pose)
                    : OperationResult<PathFollowPose>.Fail(result.Error ?? "Revive path pose is unavailable.");
            }
        }
    }

    private OperationResult<RevivePathPoseCapture> CaptureRevivePathPose(
        VmmConnection connection, GameApiReadContext context, RevivePathPoseCapture? previous)
    {
        if (!TryResolveProcess(connection.Vmm, context, out var process, out var error))
            return OperationResult<RevivePathPoseCapture>.Fail(error);

        var module = ResolveModuleName();
        var gameBase = process.GetModuleBase(module);
        if (gameBase == 0) return OperationResult<RevivePathPoseCapture>.Fail("Module not found: " + module);

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out var entityId, true))
            return OperationResult<RevivePathPoseCapture>.Fail("Revive path pose player identity read failed.");
        if (entityId == 0 || previous is not null && (previous.EntityId != entityId || previous.ModuleBase != gameBase))
            ClearRevivePathPose(context);
        if (entityId == 0) return OperationResult<RevivePathPoseCapture>.Fail("Revive path pose player is absent.");

        if (!TryReadPointer(process, gameBase + EntitySystemPointerRva, out var entitySystem, true) ||
            !TryReadPointer(process, entitySystem + EntityTreeOffset, out var tree, true) ||
            !TryFindEntityById(process, tree, entityId, out var entity, true))
            return OperationResult<RevivePathPoseCapture>.Fail("Revive path pose entity lookup failed.");
        if (previous is not null && previous.EntityAddress != entity) ClearRevivePathPose(context);

        if (!TryReadBytes(process, entity + EntityWorldPositionOffset, 3 * sizeof(float), out var coordinates, true))
            return OperationResult<RevivePathPoseCapture>.Fail("Revive path pose position read failed.");
        var position = new Vector3Snapshot(BitConverter.ToSingle(coordinates, 0),
            BitConverter.ToSingle(coordinates, sizeof(float)), BitConverter.ToSingle(coordinates, 2 * sizeof(float)));
        if (!IsUsableEntityPosition(position.X, position.Y, position.Z))
            return OperationResult<RevivePathPoseCapture>.Fail("Revive path pose position is invalid.");

        var capturedAt = DateTimeOffset.UtcNow;
        double? cameraYaw = null, cameraPitch = null, actorYaw = null;
        if (TryReadCameraAngles(process, gameBase, out var pitch, out _, out var yaw, true))
        {
            if (float.IsFinite(pitch)) cameraPitch = GetCameraPitchDegrees(pitch);
            if (float.IsFinite(yaw)) cameraYaw = GetCameraYawDegrees(yaw);
        }
        if (cameraYaw is null && TryReadSingle(process, entity + EntityWorldAnglesOffset + 8, out var rawActorYaw, true) &&
            float.IsFinite(rawActorYaw))
            actorYaw = NormalizeSignedDegrees(rawActorYaw);

        if (!TryReadUInt16(process, gameBase + LocalEntityIdRva, out var finalEntityId, true))
            return OperationResult<RevivePathPoseCapture>.Fail("Revive path pose final player identity read failed.");
        if (finalEntityId != entityId)
        {
            ClearRevivePathPose(context);
            return OperationResult<RevivePathPoseCapture>.Fail("Revive path pose player changed during capture.");
        }

        var pose = new PathFollowPose(position, capturedAt, cameraYaw, cameraPitch, actorYaw);
        return OperationResult<RevivePathPoseCapture>.Ok(new(pose, gameBase, entityId, entity));
    }

    private void ClearRevivePathPose(GameApiReadContext context) =>
        _stableSnapshots.ClearChannel(BuildStableSnapshotSessionKey(context), AionVmmSnapshotChannels.RevivePathPose);
}

internal sealed record RevivePathPoseCapture(PathFollowPose Pose, ulong ModuleBase, ushort EntityId, ulong EntityAddress);
