using UnityEngine;

public class MassPoint
{
    private static int nextId = 0;

    public readonly int id;
    public Vector3 position;
    public Vector3 velocity;
    public float mass = 1f;
    public float damping = 0.995f;
    public bool isFixed = false;

    private Vector3 forceAccumulator;
    private object forceLock = new object();

    public MassPoint(Vector3 pos)
    {
        id = nextId++;
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
    public void UpdatePhysics(float deltaTime, float groundY, float restitution)
    {
        // تحديث السرعة باستخدام القوة:
        velocity += forceAccumulator / mass * deltaTime;

        // تحديث الموضع:
        position += velocity * deltaTime;

        // تصادم مع الأرض (ground plane عند Y=groundY)
        if (position.y < groundY)
        {
            position.y = groundY; // تثبيت على الأرض
            if (velocity.y < 0)
                velocity.y = -velocity.y * restitution; // عكس السرعة مع خسارة طاقة (الارتداد)
        }

        // تصفية القوة للقفز للمرحلة القادمة:
        forceAccumulator = Vector3.zero;
    }

    public override int GetHashCode()
    {
        return id.GetHashCode();
    }
}
