using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RMDLEditor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  RMDL v10 / VG rev1 → SMD + QC decompiler
    //
    //  Parses the embedded VG vertex group (magic 0x47567430 '0tVG', version 1)
    //  and writes Valve StudioMDL Data files compatible with StudioMDL / Crowbar.
    //
    //  Output:
    //    ref.smd          – LOD-0 geometry (all body parts, model[0])
    //    idle.smd         – skeleton/bind-pose only (no geometry)
    //    <modelname>.qc   – minimal QC
    // ─────────────────────────────────────────────────────────────────────────
    public static class RMDLDecompiler
    {
        // ── VG vertex flags (vg namespace in studio.h) ────────────────────────
        private const long VF_POSITION        = 0x0000000001L; // float3   12 B
        private const long VF_POSITION_PACKED = 0x0000000002L; // Vector64  8 B
        private const long VF_COLOR           = 0x0000000010L; // Color32   4 B
        private const long VF_NORMAL_PACKED   = 0x0000000200L; // Normal32  4 B
        private const long VF_WEIGHT_BONES    = 0x0000001000L; // 3×byte+1  4 B
        private const long VF_WEIGHT_N        = 0x0000002000L; // 4×uint16  8 B
        private const long VF_WEIGHT_2        = 0x0000004000L; // 2×uint16  4 B
        private const long VF_UV1             = 0x0002000000L; // float2    8 B
        private const long VF_UV2             = 0x0200000000L; // float2    8 B

        // ── Sizes ─────────────────────────────────────────────────────────────
        private const int VG_HDR_SIZE  = 0xE0; // sizeof(vg::rev1::VertexGroupHeader_t)
        private const int MESH_SIZE    = 0x48; // sizeof(vg::rev1::MeshHeader_t)
        private const int LOD_SIZE     = 0x08; // sizeof(vg::rev1::ModelLODHeader_t)

        // ── Vertex data (internal, per-vertex parsed result) ──────────────────
        private struct VertData
        {
            public float X, Y, Z;       // position
            public float NX, NY, NZ;    // normal
            public float U, V;          // UV1 (V already flipped DX→GL)
            public (int Bone, float Weight)[] Links; // bone weight list
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public entry point
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Decompile the RMDL to SMD + QC in <paramref name="outputDir"/>.
        /// Returns a human-readable summary.
        /// </summary>
        public static string Decompile(RMDLFile rmdl, string outputDir)
        {
            // ── Locate VG data ────────────────────────────────────────────────
            // VG may be embedded at VvdOffset, or in a sidecar *.vg file.
            byte[] data;
            int    vgBase;

            int embeddedOff = rmdl.Header.VvdOffset;
            if (embeddedOff > 0 && embeddedOff + VG_HDR_SIZE <= rmdl.RawData.Length
                && I32(rmdl.RawData, embeddedOff) == 0x47567430)
            {
                // Embedded directly in the RMDL
                data   = rmdl.RawData;
                vgBase = embeddedOff;
            }
            else
            {
                // Not embedded – look for a sidecar .vg file next to the .rmdl
                string vgPath = Path.ChangeExtension(rmdl.FilePath, ".vg");
                if (!File.Exists(vgPath))
                    throw new FileNotFoundException(
                        $"VG data is not embedded in the RMDL and no sidecar file was found.\n\n" +
                        $"Expected: {vgPath}\n\n" +
                        $"Place the matching .vg file in the same folder as the .rmdl.");
                data   = File.ReadAllBytes(vgPath);
                vgBase = 0;
            }

            // ── Validate VG header ────────────────────────────────────────────
            if (vgBase + VG_HDR_SIZE > data.Length)
                throw new InvalidDataException(
                    $"VG data is too small (only {data.Length} bytes).");

            int ver = I32(data, vgBase + 4);
            if (ver != 1)
                throw new InvalidDataException(
                    $"Unsupported VG version {ver}; only rev1 (version=1) is handled.");

            // ── VG header field reads (all 64-bit offsets from vgBase) ────────
            long boneRemapOff   = I64(data, vgBase + 0x10);
            long boneRemapCount = I64(data, vgBase + 0x18);
            long meshOff        = I64(data, vgBase + 0x20);
            long indexOff       = I64(data, vgBase + 0x30);
            long vertOff        = I64(data, vgBase + 0x40);
            long lodOff         = I64(data, vgBase + 0x70);
            long lodCount       = I64(data, vgBase + 0x78);

            // ── Bone remap table ──────────────────────────────────────────────
            // Maps hardware (VG) bone index → model bone index.
            int boneRemapLen = (int)Math.Max(0, boneRemapCount);
            var boneRemap    = new byte[boneRemapLen];
            if (boneRemapLen > 0)
                Buffer.BlockCopy(data, vgBase + (int)boneRemapOff,
                                 boneRemap, 0, boneRemapLen);

            // ── LOD 0 header ──────────────────────────────────────────────────
            if (lodCount <= 0)
                throw new InvalidDataException("VG has no LOD headers.");

            int lod0Base  = vgBase + (int)lodOff;
            int lod0Start = U16(data, lod0Base);       // first mesh index in global mesh array
            int lod0Count = U16(data, lod0Base + 2);   // how many meshes belong to LOD 0

            if (lod0Count == 0)
                throw new InvalidDataException("LOD 0 has no meshes.");

            // ── Collect texture names from RMDL body parts (model[0]) ─────────
            // VG mesh order mirrors RMDL body-part → model[0] → mesh order.
            var texNames = new List<string>();
            foreach (var bp in rmdl.BodyParts)
            {
                if (bp.Models.Count == 0) continue;
                foreach (var m in bp.Models[0].Meshes)
                {
                    int  mi  = m.Material;
                    string n = (mi >= 0 && mi < rmdl.Textures.Count)
                               ? rmdl.Textures[mi].Name : "";
                    texNames.Add(string.IsNullOrEmpty(n) ? "missing" : n);
                }
            }

            // ── Geometry pass ─────────────────────────────────────────────────
            Directory.CreateDirectory(outputDir);
            string mdlName = Path.GetFileNameWithoutExtension(rmdl.FilePath);
            if (string.IsNullOrEmpty(mdlName)) mdlName = "model";

            var   triSb    = new StringBuilder(1 << 20);
            int   totalTri = 0;

            for (int mi = 0; mi < lod0Count; mi++)
            {
                int mhOff = vgBase + (int)meshOff + (lod0Start + mi) * MESH_SIZE;

                long mFlags  = I64(data, mhOff + 0x00);
                int  vBufOff = (int)U32(data, mhOff + 0x08); // offset from vertex buffer start
                int  stride  = (int)U32(data, mhOff + 0x0C); // bytes per vertex
                int  vCount  = (int)U32(data, mhOff + 0x10); // number of vertices
                int  iBufOff = I32(data, mhOff + 0x20);      // offset from index buffer start
                int  iCount  = I32(data, mhOff + 0x24);      // number of uint16 indices

                if (vCount == 0 || stride == 0 || iCount == 0) continue;

                // Texture name: just the leaf file name (no path, no extension) for SMD
                string tex = (mi < texNames.Count) ? texNames[mi] : "missing";
                tex = Path.GetFileNameWithoutExtension(tex.Replace('\\', '/'));
                if (string.IsNullOrEmpty(tex)) tex = "missing";

                int vBase = vgBase + (int)vertOff  + vBufOff;
                int iBase = vgBase + (int)indexOff + iBufOff * 2; // indexOffset is uint16_t element index → *2 for bytes

                // Read vertex buffer
                var verts = new VertData[vCount];
                for (int vi = 0; vi < vCount; vi++)
                    verts[vi] = ParseVert(data, vBase + vi * stride, mFlags, boneRemap);

                // Emit triangles (simple triangle list: every 3 indices = 1 triangle)
                int triCount = iCount / 3;
                totalTri += triCount;
                for (int ti = 0; ti < triCount; ti++)
                {
                    int ip = iBase + ti * 6;
                    int a  = U16(data, ip);
                    int b  = U16(data, ip + 2);
                    int c  = U16(data, ip + 4);
                    if (a >= vCount || b >= vCount || c >= vCount) continue;

                    // Winding: Apex index buffer is CW (DX), SMD wants CCW — swap b and c
                    triSb.AppendLine(tex);
                    AppendVert(triSb, in verts[a]);
                    AppendVert(triSb, in verts[c]);
                    AppendVert(triSb, in verts[b]);
                }
            }

            // ── ref.smd (geometry + skeleton) ────────────────────────────────
            string refPath = Path.Combine(outputDir, "ref.smd");
            using (var w = AsciiWriter(refPath))
            {
                w.WriteLine("version 1");
                WriteNodes(w, rmdl.Bones);
                WriteSkeleton(w, rmdl.Bones);
                w.WriteLine("triangles");
                w.Write(triSb);
                w.WriteLine("end");
            }

            // ── idle.smd (skeleton/bind-pose only) ────────────────────────────
            string idlePath = Path.Combine(outputDir, "idle.smd");
            using (var w = AsciiWriter(idlePath))
            {
                w.WriteLine("version 1");
                WriteNodes(w, rmdl.Bones);
                WriteSkeleton(w, rmdl.Bones);
                w.WriteLine("triangles");
                w.WriteLine("end");
            }

            // ── .qc ──────────────────────────────────────────────────────────
            string qcPath = Path.Combine(outputDir, mdlName + ".qc");
            using (var w = AsciiWriter(qcPath))
                WriteQC(w, rmdl, mdlName);

            return $"Decompiled {lod0Count} mesh(es), {totalTri} triangle(s).\n" +
                   $"Output: {outputDir}";
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Vertex parser
        //
        //  Field reading order follows the Vertex_t struct layout (pack 1):
        //    position → positionPacked → weights → bones → normal → color → uv1 → uv2
        // ─────────────────────────────────────────────────────────────────────
        private static VertData ParseVert(byte[] data, int off, long flags, byte[] boneRemap)
        {
            float px = 0, py = 0, pz = 0;
            float nx = 0, ny = 0, nz = 1;
            float  u = 0,  v = 0;
            int cur = off;

            // 1. Position — low 2 bits are an enum (eVertPositionType), not separate flags
            switch ((int)(flags & 3))
            {
                case 1: // VG_POS_UNPACKED: float3, 12 B
                    px = SF(data, cur); py = SF(data, cur + 4); pz = SF(data, cur + 8);
                    cur += 12;
                    break;
                case 2: // VG_POS_PACKED64: Vector64, 8 B (21+21+22 bits fixed-point)
                    ulong p64 = U64(data, cur);
                    px = (float)((p64         & 0x1FFFFFUL) * 0.0009765625 - 1024.0);
                    py = (float)(((p64 >> 21) & 0x1FFFFFUL) * 0.0009765625 - 1024.0);
                    pz = (float)(((p64 >> 42) & 0x3FFFFFUL) * 0.0009765625 - 2048.0);
                    cur += 8;
                    break;
                case 3: // VG_POS_PACKED48: 6 B (unsupported, skip)
                    cur += 6;
                    break;
            }

            // 2+3. Weights+Indices: VERT_BLENDINDICES(0x1000) and VERT_BLENDWEIGHTS_PACKED(0x4000)
            //      are always present together as 8B (weights 4B then indices 4B).
            //      VERT_BLENDWEIGHTS_UNPACKED(0x2000) alone is legacy 8B.
            float wA = 1f, wB = 0f;
            byte  hw0 = 0, hw1 = 0;
            int   numExtra = 0;
            if ((flags & (VF_WEIGHT_BONES | VF_WEIGHT_2)) != 0)
            {
                // Packed: BlendWeightsPacked (4B) then BlendWeightIndices (4B)
                wA       = U16(data, cur)     / 32767f;
                wB       = U16(data, cur + 2) / 32767f;
                hw0      = data[cur + 4];
                hw1      = data[cur + 5];
                numExtra = data[cur + 7]; // boneCount (extra bones beyond first)
                cur += 8;
            }
            else if ((flags & VF_WEIGHT_N) != 0)
            {
                // Legacy unpacked weights (8B) — no bone indices here
                cur += 8;
            }

            // 4. Normal: RSX reads Normal32 unconditionally (no flag check), always 4 B
            UnpackNormal32(U32(data, cur), out nx, out ny, out nz);
            cur += 4;

            // 5. Color (skip, 4 B)
            if ((flags & VF_COLOR) != 0)
                cur += 4;

            // 6. UV1 (float2, 8 B; V flipped from DX top-left to GL bottom-left)
            if ((flags & VF_UV1) != 0)
            {
                u   = SF(data, cur);
                v   = 1f - SF(data, cur + 4);
                cur += 8;
            }

            // 7. UV2 (skip, 8 B)
            if ((flags & VF_UV2) != 0)
                cur += 8;

            // Remap hardware bone indices through the VG bone-state-change table
            int b0 = Remap(hw0, boneRemap);
            int b1 = Remap(hw1, boneRemap);

            // Build per-vertex bone links for SMD
            (int Bone, float Weight)[] links;
            if (numExtra == 0)
            {
                // Single bone – full weight on b0
                links = new[] { (b0, 1f) };
            }
            else
            {
                // Two bones – wA / wB from the packed weight fields
                float w0 = Math.Clamp(wA, 0f, 1f);
                float w1 = Math.Clamp(1f - w0, 0f, 1f);
                links = (w1 > 1e-5f)
                    ? new[] { (b0, w0), (b1, w1) }
                    : new[] { (b0, 1f) };
            }

            return new VertData
            {
                X = px, Y = py, Z = pz,
                NX = nx, NY = ny, NZ = nz,
                U = u, V = v,
                Links = links
            };
        }

        private static int Remap(byte hw, byte[] table) =>
            table.Length > 0 && hw < table.Length ? table[hw] : hw;

        // ─────────────────────────────────────────────────────────────────────
        //  Normal32 unpacking  (dropped-axis scheme, see compressed_vector.h)
        //
        //  Bits 31   : binorm sign (tangent W, not used here)
        //  Bits 30–29: dominant-axis index (idx1)
        //  Bit  28   : sign of dominant component
        //  Bits 27–19: second component  − 256  (9 bits signed offset)
        //  Bits 18–10: third  component  − 256  (9 bits signed offset)
        //  Bits  9– 0: tangent angle (not used here)
        // ─────────────────────────────────────────────────────────────────────
        private static void UnpackNormal32(uint p, out float nx, out float ny, out float nz)
        {
            bool  sign = ((p >> 28) & 1u) != 0;
            float v1   = sign ? -255f : 255f;
            float v2   = (float)((p >> 19) & 0x1FFu) - 256f;
            float v3   = (float)((p >> 10) & 0x1FFu) - 256f;

            // 0x124 = 0b1_0010_0100 – encodes the cyclic (idx2,idx3) mapping:
            //   idx1=0 → idx2=1, idx3=2
            //   idx1=1 → idx2=2, idx3=0
            //   idx1=2 → idx3=0, idx3=1
            int i1 = (int)((p >> 29) & 3u);
            int i2 = (int)((0x124u >> (2 * i1 + 2)) & 3u);
            int i3 = (int)((0x124u >> (2 * i1 + 4)) & 3u);

            float inv = 1f / MathF.Sqrt(255f * 255f + v2 * v2 + v3 * v3);
            float[] n = new float[3];
            n[i1] = v1 * inv;
            n[i2] = v2 * inv;
            n[i3] = v3 * inv;
            nx = n[0]; ny = n[1]; nz = n[2];
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SMD output helpers
        // ─────────────────────────────────────────────────────────────────────
        private static void WriteNodes(StreamWriter w, List<RMDLBone> bones)
        {
            w.WriteLine("nodes");
            if (bones.Count == 0)
            {
                w.WriteLine("0 \"root\" -1");
            }
            else
            {
                for (int i = 0; i < bones.Count; i++)
                    w.WriteLine($"{i} \"{bones[i].Name}\" {bones[i].Parent}");
            }
            w.WriteLine("end");
        }

        private static void WriteSkeleton(StreamWriter w, List<RMDLBone> bones)
        {
            w.WriteLine("skeleton");
            w.WriteLine("time 0");
            if (bones.Count == 0)
            {
                w.WriteLine("0 0 0 0 0 0 0");
            }
            else
            {
                for (int i = 0; i < bones.Count; i++)
                {
                    var b = bones[i];
                    w.WriteLine(
                        $"{i} {Fx(b.PosX)} {Fx(b.PosY)} {Fx(b.PosZ)} " +
                        $"{Fx(b.RotX)} {Fx(b.RotY)} {Fx(b.RotZ)}");
                }
            }
            w.WriteLine("end");
        }

        // SMD vertex line:
        //   <primaryBone> <x> <y> <z>  <nx> <ny> <nz>  <u> <v>
        //   <numLinks> [<bone> <weight> ...]
        private static void AppendVert(StringBuilder sb, in VertData v)
        {
            var  links   = v.Links ?? new[] { (0, 1f) };
            int  primary = links.Length > 0 ? links[0].Bone : 0;

            sb.Append(primary)
              .Append(' ').Append(Fx(v.X))
              .Append(' ').Append(Fx(v.Y))
              .Append(' ').Append(Fx(v.Z))
              .Append(' ').Append(Fx(v.NX))
              .Append(' ').Append(Fx(v.NY))
              .Append(' ').Append(Fx(v.NZ))
              .Append(' ').Append(Fx(v.U))
              .Append(' ').Append(Fx(v.V))
              .Append(' ').Append(links.Length);

            foreach (var (bone, wt) in links)
                sb.Append(' ').Append(bone).Append(' ').Append(Fx(wt));

            sb.AppendLine();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  QC output
        // ─────────────────────────────────────────────────────────────────────
        private static void WriteQC(StreamWriter w, RMDLFile rmdl, string mdlName)
        {
            w.WriteLine($"$modelname \"{mdlName}.mdl\"");
            w.WriteLine();
            w.WriteLine("$surfaceprop \"default\"");
            w.WriteLine();

            var h = rmdl.Header;
            w.WriteLine(
                $"$bbox  {F4(h.HullMin[0])} {F4(h.HullMin[1])} {F4(h.HullMin[2])}" +
                $"  {F4(h.HullMax[0])} {F4(h.HullMax[1])} {F4(h.HullMax[2])}");
            w.WriteLine();

            // Unique material search paths ($cdmaterials)
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tex in rmdl.Textures)
            {
                string d = Path.GetDirectoryName(
                               tex.Name.Replace('\\', '/'))
                           ?.Replace('\\', '/') ?? "";
                dirs.Add(string.IsNullOrEmpty(d) ? "" : d + "/");
            }
            if (dirs.Count == 0) dirs.Add("");
            foreach (var d in dirs)
                w.WriteLine($"$cdmaterials \"{d}\"");

            w.WriteLine();
            w.WriteLine("$body \"body\" \"ref\"");
            w.WriteLine();
            w.WriteLine("$sequence \"idle\" \"idle\" fps 30");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Misc utilities
        // ─────────────────────────────────────────────────────────────────────
        private static StreamWriter AsciiWriter(string path) =>
            new StreamWriter(path, false, new UTF8Encoding(false));

        // Float-to-string helpers – always use invariant culture.
        private static string Fx(float f) => f.ToString("F6", CultureInfo.InvariantCulture);
        private static string F4(float f) => f.ToString("F4", CultureInfo.InvariantCulture);

        // Binary readers
        private static long  I64(byte[] d, int o) => BitConverter.ToInt64 (d, o);
        private static int   I32(byte[] d, int o) => BitConverter.ToInt32 (d, o);
        private static uint  U32(byte[] d, int o) => BitConverter.ToUInt32(d, o);
        private static ulong U64(byte[] d, int o) => BitConverter.ToUInt64(d, o);
        private static int   U16(byte[] d, int o) => BitConverter.ToUInt16(d, o);
        private static float SF (byte[] d, int o) => BitConverter.ToSingle(d, o);
    }
}
