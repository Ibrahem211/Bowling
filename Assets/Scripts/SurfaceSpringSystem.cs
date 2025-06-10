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

        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            AddUniqueSpring(massPoints[i0], massPoints[i1]);
            AddUniqueSpring(massPoints[i1], massPoints[i2]);
            AddUniqueSpring(massPoints[i2], massPoints[i0]);
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
        Bounds bounds = mesh.bounds;
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
            for (float z = bounds.min.z; z <= bounds.max.z; z += voxelSpacing)
            {
                List<float> intersections = new List<float>();

                foreach (var tri in worldTriangles)
                {
                    if (RayIntersectsTriangleVertical(x, z, tri, out float yHit))
                    {
                        intersections.Add(yHit);
                    }
                }

                intersections.Sort();

                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    float startY = intersections[i];
                    float endY = intersections[i + 1];

                    for (float y = startY; y <= endY; y += voxelSpacing)
                    {
                        Vector3 point = new Vector3(x, y, z);
                        MassPoint mp = new MassPoint(point);
                        internalPoints.Add(mp);
                        octree.Insert(mp);
                        internalCount++;
                    }
                }
            }
        }

        Debug.Log($"✅ Internal points generated: {internalCount}");

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
                    springs.Add(new Spring(mp, other));
                }
            }
        }

        foreach (var internalPoint in internalPoints)
        {
            Bounds searchBounds = new Bounds(internalPoint.position, Vector3.one * springMaxDistance * 2);
            var nearbySurface = surfaceOctree.Query(searchBounds);

            foreach (var surfacePoint in nearbySurface)
            {
                float dist = Vector3.Distance(internalPoint.position, surfacePoint.position);
                if (dist < springMaxDistance)
                {
                    springs.Add(new Spring(internalPoint, surfacePoint));
                }
            }
        }
    }




    bool IsPointInsideMesh(Vector3 point)
    {
        int hits = 0;
        Vector3 up = Vector3.up;
        Ray ray = new Ray(point - up * 1000f, up);
        RaycastHit[] rayHits = Physics.RaycastAll(ray, 2000f);

        foreach (var hit in rayHits)
        {
            if (hit.collider.gameObject == this.gameObject)
            {
                hits++;
            }
        }

        return hits % 2 == 1;
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
            mp.UpdatePhysics(deltaTime);
        });

        Parallel.ForEach(internalPoints, mp =>
        {
            mp.UpdatePhysics(deltaTime);
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

        springs.Add(new Spring(a, b));
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

    bool RayIntersectsTriangleVertical(float x, float z, Triangle tri, out float yHit)
    {
        yHit = 0;

        Vector2 p = new Vector2(x, z);
        Vector2 a = new Vector2(tri.v0.x, tri.v0.z);
        Vector2 b = new Vector2(tri.v1.x, tri.v1.z);
        Vector2 c = new Vector2(tri.v2.x, tri.v2.z);

        if (!PointInTriangle2D(p, a, b, c)) return false;

        Vector3 normal = Vector3.Cross(tri.v1 - tri.v0, tri.v2 - tri.v0).normalized;
        float d = -Vector3.Dot(normal, tri.v0);

        if (Mathf.Abs(normal.y) < 1e-5f) return false;

        float y = -(normal.x * x + normal.z * z + d) / normal.y;
        yHit = y;
        return true;
    }

    bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = 0.5f * (-b.y * c.x + a.y * (-b.x + c.x) + a.x * (b.y - c.y) + b.x * c.y);
        float s = 1 / (2 * area) * (a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y);
        float t = 1 / (2 * area) * (a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y);
        return s >= 0 && t >= 0 && (s + t) <= 1;
    }

}
