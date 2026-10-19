using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.Editor.Art.Procedural
{
    /// <summary>
    /// Growing vertex/triangle lists of one model (positions, normals, vertex colours, one bone per vertex). Triangles are wound
    /// outwards by the vertex normals, so generators do not have to care about the winding.
    /// </summary>
    public sealed class MeshBuilderBuffer
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Color32> Colors = new List<Color32>();
        public readonly List<int> Bones = new List<int>();
        public readonly List<int> Triangles = new List<int>();

        public int VertexCount => Positions.Count;
        public int TriangleCount => Triangles.Count / 3;

        public int AddVertex(Vector3 position, Vector3 normal, Color32 color, int bone)
        {
            Positions.Add(position);
            Normals.Add(normal.normalized);
            Colors.Add(color);
            Bones.Add(bone);
            return Positions.Count - 1;
        }

        /// <summary>Adds a triangle facing the side its vertex normals point to (Unity: front = Cross(b − a, c − a)).</summary>
        public void AddTriangle(int a, int b, int c)
        {
            Vector3 pa = Positions[a];
            Vector3 face = Vector3.Cross(Positions[b] - pa, Positions[c] - pa);
            Vector3 n = Normals[a] + Normals[b] + Normals[c];
            if (Vector3.Dot(face, n) < 0f) { int t = b; b = c; c = t; }
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
        }

        /// <summary>Two triangles over a quad a-b-c-d (in order around it), split along the shorter diagonal.</summary>
        public void AddQuad(int a, int b, int c, int d)
        {
            if ((Positions[a] - Positions[c]).sqrMagnitude <= (Positions[b] - Positions[d]).sqrMagnitude)
            {
                AddTriangle(a, b, c);
                AddTriangle(a, c, d);
            }
            else
            {
                AddTriangle(a, b, d);
                AddTriangle(b, c, d);
            }
        }

        public void Clear()
        {
            Positions.Clear();
            Normals.Clear();
            Colors.Clear();
            Bones.Clear();
            Triangles.Clear();
        }

        /// <summary>Writes positions, normals, colours and triangles into the mesh (cleared first); bone weights are the caller's.</summary>
        public void WriteTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = Positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetColors(Colors);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
