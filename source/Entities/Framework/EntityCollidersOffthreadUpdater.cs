using OverhaulLib.Utils;
using System.Collections.Immutable;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CollidersLib;

public sealed class EntityCollidersOffthreadUpdater : ModSystem
{
    public ObjectCache<string, ImmutableArray<ShapeElementProtoCollider>>? ProtoCollidersByShape { get; private set; }
    
    
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _api = api;

        _workerThread = TyronThreadPool.CreateDedicatedThread(() => UpdaterLoop(api), "EntityCollidersOffthreadUpdater");

        _updaterTickListener = api.World.RegisterGameTickListener(ScheduleUpdate, _updateTimeMillisec);

        ProtoCollidersByShape = new(api, "EntityCollidersOffthreadUpdater.ProtoCollidersByShape");
    }

    public override void Dispose()
    {
        _api?.World.UnregisterGameTickListener(_updaterTickListener);
        _disposed.SetTrue();
        _canUpdate.Set();
        _workerThread?.Join();
        _workerThread = null;
        ProtoCollidersByShape?.Dispose();
        _api = null;
    }



    private readonly AutoResetEvent _canUpdate = new(false);
    private readonly ThreadSafeBool _disposed = new(false);
    private long _updaterTickListener = -1;
    private const int _updateFps = 30;
    private const float _updateTimeSec = 1f / _updateFps;
    private const int _updateTimeMillisec = (int)_updateTimeSec * 1000;
    private ICoreClientAPI? _api;
    private Thread? _workerThread;


    private void ScheduleUpdate(float deltaTime)
    {
        _canUpdate.Set();
    }

    private void UpdaterLoop(ICoreClientAPI api)
    {
        while (_disposed.Value)
        {
            _canUpdate.WaitOne();

            OffThreadCollidersConstructor.UpdateEntitiesColliders(api);
        }
    }
}