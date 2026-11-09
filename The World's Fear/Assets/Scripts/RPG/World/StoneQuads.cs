using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TWF
{
    // Builds the stone geometry of stairwells and dungeons. Faces are one-sided, so a wall between the camera
    // and the hero simply isn't drawn, from whichever side the camera looks.
    public class StoneQuads
    {
        const float TileWorldSize = 4f;   // the stone textures repeat every four units

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();

        // Deeper than this (+Z is down) a surface has faded to black. 0 keeps every surface at full colour.
        public float darkDepth;

        public int Count => verts.Count;

        public static Material Stone(string texture)
        {
            var tex = EnvArt.Tex(texture);
            tex.wrapMode = TextureWrapMode.Repeat;
            var m = WorldTiles.MeshMaterial("stone_" + texture, tex);
            m.SetFloat("_Cull", (float)CullMode.Back);
            return m;
        }

        // A rectangle from o along u (length a) and w (length b), visible from the side n points to.
        public void Add(Vector3 o, Vector3 u, Vector3 w, Vector3 n, float a, float b)
        {
            int i = verts.Count;
            Corner(o, n, Vector3.Dot(o, u), Vector3.Dot(o, w));
            Corner(o + u * a, n, Vector3.Dot(o, u) + a, Vector3.Dot(o, w));
            Corner(o + w * b, n, Vector3.Dot(o, u), Vector3.Dot(o, w) + b);
            Corner(o + u * a + w * b, n, Vector3.Dot(o, u) + a, Vector3.Dot(o, w) + b);
            if (Vector3.Dot(Vector3.Cross(u, w), n) > 0f) tris.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
            else tris.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 });
        }

        void Corner(Vector3 p, Vector3 n, float s, float t)
        {
            verts.Add(p);
            normals.Add(n);
            uvs.Add(new Vector2(s, t) / TileWorldSize);
            float light = darkDepth > 0f ? 1f - Mathf.Clamp01(p.z / darkDepth) : 1f;
            colors.Add(new Color(light, light, light, 1f));
        }

        public GameObject Build(Transform parent, string name, Material material, bool shadows = false)
        {
            if (verts.Count == 0 || material == null) return null;
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }
    }
}
