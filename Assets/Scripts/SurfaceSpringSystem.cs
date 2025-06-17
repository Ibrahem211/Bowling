using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;

public class SurfaceSpringSystem : MonoBehaviour
{
    public float pointSize = 0.05f;
    public float springMaxDistance = 0.3f;
    public Material pointMaterial;

    [Header("Physics")]
    public float gravity = 9.81f;
    public float springStiffness = 100f;

    [Header("Voxelization Settings")]
    public float voxelSize = 0.2f;
    public bool visualizeVoxels = true;

    private Mesh pointMesh;
    private Material defaultMat;

    private List<MassPoint> internalPoints = new List<MassPoint>();
    private List<MassPoint> massPoints = new List<MassPoint>();
    private List<Spring> springs = new List<Spring>();
    private HashSet<string> springSet = new HashSet<string>();
    private Vector3[] forcesA;
    private Vector3[] forcesB;

    private OctreeNode octree;
    private OctreeNode surfaceOctree;
    public float groundY = 0f; // ارتفاع الأرض
    public float restitution = 0.5f; // معامل ارتداد


    void Start()
    {
        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null)
        {
            Debug.LogError("❌ لا يوجد Mesh.");
            return;
        }

        Vector3[] vertices = mesh.vertices;

        foreach (var v in vertices)
        {
            Vector3 worldPos = transform.TransformPoint(v);
            MassPoint mp = new MassPoint(worldPos);
            massPoints.Add(mp);
        }

        Bounds bounds = new Bounds(massPoints[0].position, Vector3.zero);
        foreach (var mp in massPoints)
        {
            bounds.Encapsulate(mp.position);
        }
        surfaceOctree = new OctreeNode(bounds, 8, 6);
        foreach (var mp in massPoints)
        {
            surfaceOctree.Insert(mp);
        }

        foreach (var mp in massPoints)
        {
            Bounds searchBounds = new Bounds(mp.position, Vector3.one * springMaxDistance / 2);
            var neighbors = surfaceOctree.Query(searchBounds);

            foreach (var other in neighbors)
            {
                if (mp == other) continue;

                float dist = Vector3.Distance(mp.position, other.position);
                if (dist < springMaxDistance)
                {
                    AddUniqueSpring(mp, other);
                }
            }
        }

        Destroy(GetComponent<MeshRenderer>());
        Destroy(GetComponent<MeshFilter>());

        Debug.Log($"✅ Created {massPoints.Count} points and {springs.Count} springs.");

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pointMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);

        defaultMat = new Material(Shader.Find("Standard"));
        GenerateInternalPoints(mesh);

        Debug.Log("Total vertices in mesh: " + mesh.vertexCount);
        Debug.Log("Total mass points created: " + massPoints.Count);

        forcesA = new Vector3[springs.Count];
        forcesB = new Vector3[springs.Count];
    }

void GenerateInternalPoints(Mesh mesh)
    {
        Vector3 worldMin = massPoints[0].position;
        Vector3 worldMax = massPoints[0].position;
        foreach (var mp in massPoints)
        {
            worldMin = Vector3.Min(worldMin, mp.position);
            worldMax = Vector3.Max(worldMax, mp.position);
        }
        Bounds bounds = new Bounds();
        bounds.SetMinMax(worldMin, worldMax);

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        float voxelSpacing = voxelSize;

        octree = new OctreeNode(bounds, 8, 6);
        surfaceOctree = new OctreeNode(bounds, 8, 6);

        foreach (var surfacePoint in massPoints)
        {
            surfaceOctree.Insert(surfacePoint);
        }

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        List<Triangle> worldTriangles = new List<Triangle>();

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
            Vector3 v1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
            Vector3 v2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
            worldTriangles.Add(new Triangle(v0, v1, v2));
        }

        int internalCount = 0;

        for (float x = bounds.min.x; x <= bounds.max.x; x += voxelSpacing)
        {
            for (float y = bounds.min.y; y <= bounds.max.y; y += voxelSpacing)
            {
                for (float z = bounds.min.z; z <= bounds.max.z; z += voxelSpacing)
                {
                    Vector3 point = new Vector3(x, y, z);

                    if (IsPointInsideMesh(point, worldTriangles))
                    {
                        MassPoint mp = new MassPoint(point);
                        internalPoints.Add(mp);
                        octree.Insert(mp);
                        internalCount++;
                    }
                }
            }
        }

        Debug.Log($"✅ Internal points generated: {internalCount}");

        int countBefore = springs.Count;

        foreach (var mp in internalPoints)
        {
            Bounds searchBounds = new Bounds(mp.position, Vector3.one * springMaxDistance * 2);
            var neighbors = octree.Query(searchBounds);

            foreach (var other in neighbors)
            {
                if (other == mp) continue;
                float dist = Vector3.Distance(mp.position, other.position);
                if (dist < springMaxDistance)
                {
                    AddUniqueSpring(mp, other);
                }
            }
        }

        int internalSpringCount = springs.Count - countBefore;

        countBefore = springs.Count;

        foreach (var internalPoint in internalPoints)
        {
            Bounds searchBounds = new Bounds(internalPoint.position, Vector3.one * springMaxDistance * 2);
            var nearbySurface = surfaceOctree.Query(searchBounds);

            foreach (var surfacePoint in nearbySurface)
            {
                float dist = Vector3.Distance(internalPoint.position, surfacePoint.position);
                if (dist < springMaxDistance)
                {
                    AddUniqueSpring(internalPoint, surfacePoint);

                }
            }
        }

        int surfaceSpringCount = springs.Count - countBefore;

        Debug.Log($"🔗 Internal-Internal Springs: {internalSpringCount}");
        Debug.Log($"🔗 Internal-Surface Springs: {surfaceSpringCount}");
    }

    bool IsPointInsideMesh(Vector3 point, List<Triangle> triangles)
    {
        int hitCount = 0;
        Vector3 rayDirection = Vector3.right;

        foreach (var tri in triangles)
        {
            if (RayIntersectsTriangle(point, rayDirection, tri, out float distance))
            {
                if (distance >= 0)
                {
                    hitCount++;
                }
            }
        }

        return (hitCount % 2) == 1;
    }
    void Update()
    {
        float deltaTime = 0.02f;

        Parallel.For(0, springs.Count, i =>
        {
            var s = springs[i];
            Vector3 delta = s.b.position - s.a.position;
            float dist = delta.magnitude;
            if (dist == 0) return;

            Vector3 direction = delta / dist;
            float forceMag = (dist - s.restLength) * s.stiffness;
            Vector3 force = direction * forceMag;

            forcesA[i] = force;
            forcesB[i] = -force;
        });

        for (int i = 0; i < springs.Count; i++)
        {
            springs[i].a.AddForce(forcesA[i]);
            springs[i].b.AddForce(forcesB[i]);
        }

        Vector3 gravityForce = Vector3.down * gravity;

        Parallel.ForEach(massPoints, mp =>
        {
            mp.AddForce(gravityForce * mp.mass);
        });

        Parallel.ForEach(internalPoints, mp =>
        {
            mp.AddForce(gravityForce * mp.mass);
        });

        Parallel.ForEach(massPoints, mp =>
        {
            mp.UpdatePhysics(deltaTime, groundY, restitution);
        });

        Parallel.ForEach(internalPoints, mp =>
        {
            mp.UpdatePhysics(deltaTime, groundY, restitution);
        });
    }


    void OnDrawGizmos()
    {
        if (massPoints == null || springs == null) return;

        Gizmos.color = Color.green;
        foreach (var mp in massPoints)
        {
            Gizmos.DrawSphere(mp.position, pointSize);
        }

        Gizmos.color = Color.cyan;
        foreach (var s in springs)
        {
            Gizmos.DrawLine(s.a.position, s.b.position);
        }

        if (visualizeVoxels && internalPoints != null)
        {
            Gizmos.color = Color.red;
            foreach (var mp in internalPoints)
            {
                Gizmos.DrawSphere(mp.position, pointSize);
            }
        }
    }

    void OnRenderObject()
    {
        if (massPoints == null || pointMesh == null || defaultMat == null) return;

        defaultMat.SetPass(0);
        foreach (var mp in massPoints)
        {
            Graphics.DrawMeshNow(pointMesh, Matrix4x4.TRS(mp.position, Quaternion.identity, Vector3.one * pointSize));
        }
    }

    void AddUniqueSpring(MassPoint a, MassPoint b)
    {
        string key = GetSpringKey(a, b);
        if (springSet.Contains(key)) return;

        springs.Add(new Spring(a, b, springStiffness));
        springSet.Add(key);
    }

    string GetSpringKey(MassPoint a, MassPoint b)
    {
        int hashA = a.GetHashCode();
        int hashB = b.GetHashCode();

        if (hashA < hashB)
            return $"{hashA}_{hashB}";
        else
            return $"{hashB}_{hashA}";
    }

    struct Triangle
    {
        public Vector3 v0, v1, v2;
        public Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            v0 = a; v1 = b; v2 = c;
        }
    }

    bool RayIntersectsTriangle(Vector3 origin, Vector3 direction, Triangle tri, out float t)
    {
        t = 0;
        Vector3 edge1 = tri.v1 - tri.v0;
        Vector3 edge2 = tri.v2 - tri.v0;
        Vector3 h = Vector3.Cross(direction, edge2);
        float a = Vector3.Dot(edge1, h);
        if (a > -1e-5f && a < 1e-5f)
            return false;

        float f = 1.0f / a;
        Vector3 s = origin - tri.v0;
        float u = f * Vector3.Dot(s, h);
        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(direction, q);
        if (v < 0.0f || u + v > 1.0f)
            return false;

        t = f * Vector3.Dot(edge2, q);
        if (t > 1e-5f)
            return true;

        return false;
    }
}
