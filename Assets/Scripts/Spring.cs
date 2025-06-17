using UnityEngine;

public class Spring
{
    public MassPoint a, b;
    public float restLength;
    public float stiffness;

    public Spring(MassPoint a, MassPoint b, float stiffness)
    {
        this.a = a;
        this.b = b;
        this.restLength = Vector3.Distance(a.position, b.position);
        this.stiffness = stiffness;
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
