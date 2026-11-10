using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Smokey.SaltFT
{
    public struct SparseEntry
    {
        public int index;
        public Vector3 dv, dn, dt;
    }

    public static class SaltFTPatchCommon
    {
        public const string Magic = "YFTPATCH";

        public static ulong MeshHash(Mesh m)
        {
            ulong h = 1469598103934665603UL;
            var v = m.vertices;
            var uv = m.uv;
            h = Mix(h, m.vertexCount);
            h = Mix(h, m.subMeshCount);
            for (int s = 0; s < m.subMeshCount; s++)
            {
                h = Mix(h, (long)m.GetIndexCount(s));
                var idx = m.GetIndices(s);
                for (int i = 0; i < idx.Length; i++) h = Mix(h, idx[i]);
            }
            for (int i = 0; i < v.Length; i++)
            {
                h = Mix(h, Mathf.RoundToInt(v[i].x * 100000f));
                h = Mix(h, Mathf.RoundToInt(v[i].y * 100000f));
                h = Mix(h, Mathf.RoundToInt(v[i].z * 100000f));
                h = Mix(h, Mathf.RoundToInt(uv[i].x * 10000f));
                h = Mix(h, Mathf.RoundToInt(uv[i].y * 10000f));
            }
            return h;
        }

        static ulong Mix(ulong h, long x)
        {
            unchecked
            {
                h ^= (ulong)x;
                h *= 1099511628211UL;
            }
            return h;
        }

        public static long Pack(int a, int b, int c)
        {
            if (a > b) { int t = a; a = b; b = t; }
            if (b > c) { int t = b; b = c; c = t; }
            if (a > b) { int t = a; a = b; b = t; }
            return ((long)a << 42) | ((long)b << 21) | (long)c;
        }

        public static void W3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        public static Vector3 R3(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        public static void WEntry(BinaryWriter w, SparseEntry e) { w.Write(e.index); W3(w, e.dv); W3(w, e.dn); W3(w, e.dt); }
        public static SparseEntry REntry(BinaryReader r)
        {
            var e = new SparseEntry();
            e.index = r.ReadInt32(); e.dv = R3(r); e.dn = R3(r); e.dt = R3(r);
            return e;
        }
        public static bool NonZero(Vector3 a, Vector3 b, Vector3 c) { return a.sqrMagnitude > 0f || b.sqrMagnitude > 0f || c.sqrMagnitude > 0f; }
    }

    public static class SaltFTPatchApplier
    {
        public static bool ReadHeader(byte[] bytes, out int baseCount, out int baseSub, out ulong hash)
        {
            baseCount = 0; baseSub = 0; hash = 0;
            if (bytes == null || bytes.Length < 24) return false;
            if (Encoding.ASCII.GetString(bytes, 0, 8) != SaltFTPatchCommon.Magic) return false;
            baseCount = BitConverter.ToInt32(bytes, 8);
            baseSub = BitConverter.ToInt32(bytes, 12);
            hash = BitConverter.ToUInt64(bytes, 16);
            return true;
        }

        /// <summary>
        /// Builds a new in-memory mesh = original mesh + the patch. The original mesh is never modified.
        /// Returns null and fills 'error' when the mesh is not the one the patch was made for.
        /// </summary>
        public static Mesh Apply(Mesh B, byte[] patch, out string error)
        {
            error = null;
            int baseCount, baseSub; ulong baseHash;
            if (!ReadHeader(patch, out baseCount, out baseSub, out baseHash)) { error = "The data file is damaged or not a face tracking data file."; return null; }
            if (B.vertexCount != baseCount || B.subMeshCount != baseSub) { error = "This is not the original head mesh the data was made for (vertex count " + B.vertexCount + ", expected " + baseCount + ")."; return null; }
            if (SaltFTPatchCommon.MeshHash(B) != baseHash) { error = "The head mesh looks modified, or was imported with different settings."; return null; }

            var r = new BinaryReader(new MemoryStream(patch));
            r.ReadBytes(8); r.ReadInt32(); r.ReadInt32(); r.ReadUInt64();
            var bounds = new Bounds(SaltFTPatchCommon.R3(r), SaltFTPatchCommon.R3(r));
            int rc = r.ReadInt32(); var removedVerts = new int[rc]; for (int i = 0; i < rc; i++) removedVerts[i] = r.ReadInt32();
            var removedTriSets = new HashSet<long>[baseSub]; var addedTris = new List<int[]>[baseSub];
            for (int sm = 0; sm < baseSub; sm++)
            {
                removedTriSets[sm] = new HashSet<long>();
                int rt = r.ReadInt32(); for (int i = 0; i < rt; i++) removedTriSets[sm].Add(SaltFTPatchCommon.Pack(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
                int at = r.ReadInt32(); addedTris[sm] = new List<int[]>();
                for (int i = 0; i < at; i++) addedTris[sm].Add(new int[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32() });
            }
            int newCount = r.ReadInt32();
            var nPos = new Vector3[newCount]; var nNor = new Vector3[newCount]; var nUv = new Vector2[newCount]; var nBw = new BoneWeight[newCount];
            for (int i = 0; i < newCount; i++)
            {
                nPos[i] = SaltFTPatchCommon.R3(r); nNor[i] = SaltFTPatchCommon.R3(r); nUv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                var bw = new BoneWeight();
                bw.boneIndex0 = r.ReadInt32(); bw.boneIndex1 = r.ReadInt32(); bw.boneIndex2 = r.ReadInt32(); bw.boneIndex3 = r.ReadInt32();
                bw.weight0 = r.ReadSingle(); bw.weight1 = r.ReadSingle(); bw.weight2 = r.ReadSingle(); bw.weight3 = r.ReadSingle();
                nBw[i] = bw;
            }
            int noc = r.ReadInt32(); var normalOv = new List<KeyValuePair<int, Vector3>>();
            for (int i = 0; i < noc; i++) normalOv.Add(new KeyValuePair<int, Vector3>(r.ReadInt32(), SaltFTPatchCommon.R3(r)));
            int baseShapes = r.ReadInt32();
            if (baseShapes != B.blendShapeCount) { error = "The head mesh has a different number of shapes (" + B.blendShapeCount + ") than the data expects (" + baseShapes + ")."; return null; }
            int bse = r.ReadInt32(); var baseShapeEntries = new List<SparseEntry>[baseShapes];
            for (int s = 0; s < baseShapes; s++) baseShapeEntries[s] = new List<SparseEntry>();
            for (int i = 0; i < bse; i++) { int s = r.ReadInt32(); baseShapeEntries[s].Add(SaltFTPatchCommon.REntry(r)); }
            int ftCount = r.ReadInt32();
            var ftNames = new string[ftCount]; var ftW = new float[ftCount]; var ftE = new List<SparseEntry>[ftCount];
            for (int f = 0; f < ftCount; f++)
            {
                ftNames[f] = r.ReadString(); ftW[f] = r.ReadSingle(); int ec = r.ReadInt32(); ftE[f] = new List<SparseEntry>(ec);
                for (int i = 0; i < ec; i++) ftE[f].Add(SaltFTPatchCommon.REntry(r));
            }

            var posOvR = new List<KeyValuePair<int, Vector3>>(); var shapeOvR = new List<KeyValuePair<int, SparseEntry>>();
            if (r.BaseStream.Position < r.BaseStream.Length)
            {
                int poc = r.ReadInt32(); for (int i = 0; i < poc; i++) posOvR.Add(new KeyValuePair<int, Vector3>(r.ReadInt32(), SaltFTPatchCommon.R3(r)));
                int soc = r.ReadInt32(); for (int i = 0; i < soc; i++) { int s = r.ReadInt32(); shapeOvR.Add(new KeyValuePair<int, SparseEntry>(s, SaltFTPatchCommon.REntry(r))); }
            }
            var removedSet = new HashSet<int>(removedVerts);
            var remap = new int[baseCount]; int surv = 0;
            for (int i = 0; i < baseCount; i++) remap[i] = removedSet.Contains(i) ? -1 : surv++;
            int total = surv + newCount;
            int survFinal = surv;
            Func<int, int> fin = p => p < baseCount ? remap[p] : survFinal + (p - baseCount);

            var pb = B.vertices; var nb = B.normals; var ub = B.uv; var bwB = B.boneWeights;
            var tanB = B.tangents; var colB = B.colors; var uv2B = B.uv2;
            var TAN = tanB.Length > 0 ? new Vector4[total] : null; var COL = colB.Length > 0 ? new Color[total] : null; var UV2 = uv2B.Length > 0 ? new Vector2[total] : null;
            var V = new Vector3[total]; var Nrm = new Vector3[total]; var UV = new Vector2[total]; var BW = new BoneWeight[total];
            for (int i = 0; i < baseCount; i++)
            {
                int k = remap[i]; if (k < 0) continue;
                V[k] = pb[i]; Nrm[k] = nb[i]; UV[k] = ub[i]; BW[k] = bwB[i];
                if (TAN != null) TAN[k] = tanB[i]; if (COL != null) COL[k] = colB[i]; if (UV2 != null) UV2[k] = uv2B[i];
            }
            for (int i = 0; i < newCount; i++) { V[surv + i] = nPos[i]; Nrm[surv + i] = nNor[i]; UV[surv + i] = nUv[i]; BW[surv + i] = nBw[i]; }
            foreach (var kv in normalOv) { int k = remap[kv.Key]; if (k >= 0) Nrm[k] = kv.Value; }
            foreach (var kv in posOvR) { int k = remap[kv.Key]; if (k >= 0) V[k] = kv.Value; }

            var R = new Mesh();
            R.name = B.name;
            R.indexFormat = total > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            R.vertices = V; R.normals = Nrm; R.uv = UV;
            if (TAN != null) R.tangents = TAN; if (COL != null) R.colors = COL; if (UV2 != null) R.uv2 = UV2;
            R.bindposes = B.bindposes; R.boneWeights = BW;
            R.subMeshCount = baseSub;
            for (int sm = 0; sm < baseSub; sm++)
            {
                var ib = B.GetIndices(sm); var tris = new List<int>(ib.Length + 3000);
                for (int t = 0; t < ib.Length; t += 3)
                {
                    int a = ib[t], b = ib[t + 1], c = ib[t + 2];
                    if (removedTriSets[sm].Contains(SaltFTPatchCommon.Pack(a, b, c))) continue;
                    if (remap[a] < 0 || remap[b] < 0 || remap[c] < 0) { error = "The data does not match this mesh (a triangle uses a removed vertex)."; UnityEngine.Object.DestroyImmediate(R); return null; }
                    tris.Add(remap[a]); tris.Add(remap[b]); tris.Add(remap[c]);
                }
                foreach (var t in addedTris[sm]) { tris.Add(fin(t[0])); tris.Add(fin(t[1])); tris.Add(fin(t[2])); }
                R.SetTriangles(tris, sm, false);
            }
            R.bounds = bounds;

            var bdv = new Vector3[baseCount]; var bdn = new Vector3[baseCount]; var bdt = new Vector3[baseCount];
            for (int s = 0; s < baseShapes; s++)
            {
                B.GetBlendShapeFrameVertices(s, 0, bdv, bdn, bdt);
                var dv = new Vector3[total]; var dn = new Vector3[total]; var dt = new Vector3[total];
                for (int i = 0; i < baseCount; i++) { int k = remap[i]; if (k < 0) continue; dv[k] = bdv[i]; dn[k] = bdn[i]; dt[k] = bdt[i]; }
                foreach (var e in baseShapeEntries[s]) { int k = surv + e.index; dv[k] = e.dv; dn[k] = e.dn; dt[k] = e.dt; }
                foreach (var kv in shapeOvR) { if (kv.Key != s) continue; int k = remap[kv.Value.index]; if (k >= 0) { dv[k] = kv.Value.dv; dn[k] = kv.Value.dn; dt[k] = kv.Value.dt; } }
                R.AddBlendShapeFrame(B.GetBlendShapeName(s), B.GetBlendShapeFrameWeight(s, 0), dv, dn, dt);
            }
            for (int f = 0; f < ftCount; f++)
            {
                var dv = new Vector3[total]; var dn = new Vector3[total]; var dt = new Vector3[total];
                foreach (var e in ftE[f]) { int k = fin(e.index); dv[k] = e.dv; dn[k] = e.dn; dt[k] = e.dt; }
                R.AddBlendShapeFrame(ftNames[f], ftW[f], dv, dn, dt);
            }
            return R;
        }
    }
}
