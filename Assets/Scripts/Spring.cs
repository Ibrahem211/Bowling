using UnityEngine;

public class Spring
{
    public MassPoint a, b;
    public float restLength;
    public float stiffness = 50f;

    public Spring(MassPoint a, MassPoint b)
    {
        this.a = a;
        this.b = b;
        restLength = Vector3.Distance(a.position, b.position);
    }

    public void ApplySpringForce(float deltaTime)
    {
        Vector3 delta = b.position - a.position;
        float dist = delta.magnitude;
        if (dist == 0f) return;

        float displacement = dist - restLength;
        Vector3 force = delta / dist * (displacement * stiffness); 

        a.velocity += force * deltaTime;
        b.velocity -= force * deltaTime;
    }
}
