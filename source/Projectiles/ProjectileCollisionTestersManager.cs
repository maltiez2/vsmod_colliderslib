using CollidersLib.VectorsUtils;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CollidersLib.Projectiles;

public sealed class ProjectileCollisionTestersManagerServer
{
    public ProjectileCollisionTestersManagerServer(ICoreServerAPI api, Action<long, long, Vector3d, float> sendUpdateCallback)
    {
        _api = api;
        _api.World.RegisterGameTickListener(SendUpdates, 0, 0);
        _sendUpdateCallback = sendUpdateCallback;
    }

    public void StartUpdates(Entity projectile, float radius)
    {
        _projectiles.Add(projectile);
        _radiuses.Add(projectile.EntityId, radius);
        SendUpdate(projectile);
    }

    public void StopUpdates(long projectileId)
    {
        Entity? projectile = _projectiles.Find(element => element.EntityId == projectileId);
        if (projectile != null)
        {
            _projectiles.Remove(projectile);
            _radiuses.Remove(projectile.EntityId);
        }
    }

    private readonly ICoreServerAPI _api;
    private readonly List<Entity> _projectiles = [];
    private readonly Dictionary<long, float> _radiuses = [];
    private readonly Action<long, long, Vector3d, float> _sendUpdateCallback;

    private void SendUpdates(float deltaTime)
    {
        long[] belowMapEntities = _projectiles
            .Where(entity => entity.Pos.Y < 0)
            .Select(entity => entity.EntityId)
            .ToArray();

        foreach (long entityId in belowMapEntities)
        {
            StopUpdates(entityId);
        }

        foreach (Entity entity in _projectiles)
        {
            SendUpdate(entity);
        }
    }
    private void SendUpdate(Entity entity)
    {
        long timeStamp = _api.World.ElapsedMilliseconds;
        Vector3d position = entity.Pos.XYZ.ToOpenTK();
        _sendUpdateCallback.Invoke(entity.EntityId, timeStamp, position, _radiuses[entity.EntityId]);
    }
}

public sealed class ProjectileCollisionTestersManagerClient
{
    public ProjectileCollisionTestersManagerClient(ICoreClientAPI api)
    {
        _api = api;
        _api.World.RegisterGameTickListener(ProcessUpdates, 0, 0);
    }

    public void QueueUpdate(long projectileId, long timeStamp, Vector3d position, float radius, bool reset = false)
    {
        if (!_testers.ContainsKey(projectileId))
        {
            _testers.Add(projectileId, new(_api, radius, position, projectileId));
            _updateQueues.Add(projectileId, new());
            _lastProcessedTimeStamps.Add(projectileId, timeStamp);
        }

        if (reset)
        {
            _updateQueues[projectileId].Clear();
        }

        _updateQueues[projectileId].Enqueue(new(timeStamp, position, reset));
    }

    public void StopTester(long projectileId)
    {
        _testers.Remove(projectileId);
        _updateQueues.Remove(projectileId);
        _lastProcessedTimeStamps.Remove(projectileId);
    }


    private readonly struct CollisionUpdateData
    {
        public readonly long TimeStamp;
        public readonly Vector3d Position;
        public readonly bool Reset;

        public CollisionUpdateData(long timeStamp, Vector3d position, bool reset = false)
        {
            TimeStamp = timeStamp;
            Position = position;
            Reset = reset;
        }
    }


    private readonly Dictionary<long, ProjectileCollisionTester> _testers = [];
    private readonly Dictionary<long, Queue<CollisionUpdateData>> _updateQueues = [];
    private readonly Dictionary<long, long> _lastProcessedTimeStamps = [];
    private readonly ICoreClientAPI _api;


    private void ProcessUpdates(float deltaTime)
    {
        foreach (long projectileId in _testers.Keys)
        {
            ProcessProjectileUpdate(projectileId);
        }
    }

    private void ProcessProjectileUpdate(long projectileId)
    {
        ProjectileCollisionTester tester = _testers[projectileId];
        Queue<CollisionUpdateData> queue = _updateQueues[projectileId];
        long lastProcessedTimeStamp = _lastProcessedTimeStamps[projectileId];

        while (queue.Count > 0)
        {
            CollisionUpdateData updateData = queue.Dequeue();
            if (lastProcessedTimeStamp > updateData.TimeStamp)
            {
                continue;
            }

            tester.OnGameTick(_api, updateData.Position, updateData.Reset);
            lastProcessedTimeStamp = updateData.TimeStamp;
            _lastProcessedTimeStamps[projectileId] = lastProcessedTimeStamp;
        }
    }
}
