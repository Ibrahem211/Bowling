using UnityEngine;

public class MassPoint
{
    public Vector3 position;
    public Vector3 velocity;
    public float mass = 1f;
    public float damping = 0.995f;
    public bool isFixed = false;

    private Vector3 forceAccumulator;
    private object forceLock = new object();

    public MassPoint(Vector3 pos)
    {
        position = pos;
        velocity = Vector3.zero;
        forceAccumulator = Vector3.zero;
    }

    public void AddForce(Vector3 force)
    {
        if (isFixed) return;
        lock (forceLock)
        {
            forceAccumulator += force;
        }
    }

    public void UpdatePhysics(float deltaTime)
    {
        if (isFixed) return;

        Vector3 acceleration = forceAccumulator / mass;
        velocity += acceleration * deltaTime;
        velocity *= damping;
        position += velocity * deltaTime;

        forceAccumulator = Vector3.zero;
    }
}
