using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace RMDLEditor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Constants
    // ─────────────────────────────────────────────────────────────────────────
    public static class RMDLConstants
    {
        public const int IDST           = 0x54534449; // "IDST"
        public const int VERSION_APEX   = 54;         // r5 / rmdl v10 maps to studio version 54
        public const int TEXTURE_STRIDE = 12;         // sizeof(mstudiotexture_t) with pack(4)
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Data classes
    // ─────────────────────────────────────────────────────────────────────────

    public class RMDLHeader
    {
        // Basic identity
        public int    Id          { get; set; }
        public int    Version     { get; set; }
        public int    Checksum    { get; set; }
        public string InternalName{ get; set; } = "";
        public int    Length      { get; set; }

        // Bounding / eye
        public float[] EyePosition  { get; set; } = new float[3];
        public float[] IllumPosition{ get; set; } = new float[3];
        public float[] HullMin      { get; set; } = new float[3];
        public float[] HullMax      { get; set; } = new float[3];
        public float[] ViewBBMin    { get; set; } = new float[3];
        public float[] ViewBBMax    { get; set; } = new float[3];

        // Flags / misc
        public int   Flags                     { get; set; }
        public float Mass                      { get; set; }
        public int   Contents                  { get; set; }
        public float DefaultFadeDist           { get; set; }
        public float GatherSize                { get; set; }
        public float FlVertAnimFixedPointScale { get; set; }
        public int   SurfacePropLookup         { get; set; }

        // LOD
        public byte RootLOD           { get; set; }
        public byte NumAllowedRootLODs{ get; set; }
        public byte ConstDirLightDot  { get; set; }

        // Counts (read-only display)
        public int NumBones             { get; set; }
        public int NumBoneControllers   { get; set; }
        public int NumHitboxSets        { get; set; }
        public int NumLocalAnim         { get; set; }
        public int NumLocalSeq          { get; set; }
        public int NumTextures          { get; set; }
        public int NumCDTextures        { get; set; }
        public int NumSkinRef           { get; set; }
        public int NumSkinFamilies      { get; set; }
        public int NumBodyParts         { get; set; }
        public int NumLocalAttachments  { get; set; }
        public int NumIKChains          { get; set; }
        public int UIPanelCount         { get; set; }
        public int NumLocalPoseParams   { get; set; }
        public int KeyValueSize         { get; set; }
        public int NumSrcBoneTransform  { get; set; }
        public int BoneFollowerCount    { get; set; }
        public int ActivityListVersion  { get; set; }

        // Embedded sub-file offsets
        public int VtxOffset { get; set; }
        public int VvdOffset { get; set; }
        public int VvcOffset { get; set; }
        public int PhyOffset { get; set; }
        public int VtxSize   { get; set; }
        public int VvdSize   { get; set; }
        public int VvcSize   { get; set; }
        public int PhySize   { get; set; }
        public int VvwOffset { get; set; }
        public int VvwSize   { get; set; }
        public int BvhOffset { get; set; }

        // Raw file offsets (for in-place editing)
        public int RawTextureIndex     { get; set; }
        public int RawMaterialTypesIndex{ get; set; }
        public int RawBodyPartIndex    { get; set; }
        public int RawUIPanelOffset    { get; set; }
        public int RawBoneIndex        { get; set; }
        public int RawKeyValueIndex    { get; set; }
        public int RawSkinIndex        { get; set; }
        public int RawHitboxSetIndex   { get; set; }
    }

    public class RMDLTexture
    {
        public int    FileOffset { get; set; }    // absolute offset in file of this mstudiotexture_t
        public string Name       { get; set; } = "";
        public ulong  TextureGuid{ get; set; }
        public int    GuidOffset { get; set; }    // absolute file offset of the 8-byte GUID field
    }

    public class RMDLMaterialType
    {
        public int    FileOffset { get; set; }
        public byte   TypeValue  { get; set; }
        // Known values: SKNP=4, RGDP=7, etc.
        public string TypeName => TypeValue switch
        {
            0 => "UNKN (0)",
            4 => "SKNP (4)",
            7 => "RGDP (7)",
            _ => $"0x{TypeValue:X2} ({TypeValue})"
        };
    }

    public class RMDLRuiPanel
    {
        public int    FileOffset  { get; set; }
        public int    NameHash    { get; set; }
        public int    RuiMeshIndex{ get; set; }

        // rui mesh
        public int   MeshFileOffset{ get; set; }
        public short NumParents    { get; set; }
        public short NumVertices   { get; set; }
        public short NumFaces      { get; set; }
        public short Unk           { get; set; }
        public int   ParentIndex   { get; set; }
        public int   VertexIndex   { get; set; }
        public int   UnkIndex      { get; set; }
        public int   VertMapIndex  { get; set; }
        public int   FaceDataIndex { get; set; }
    }

    public class RMDLBone
    {
        public int    FileOffset { get; set; }
        public string Name       { get; set; } = "";
        public int    Parent     { get; set; }
        public float  PosX       { get; set; }
        public float  PosY       { get; set; }
        public float  PosZ       { get; set; }
        public float  RotX       { get; set; }  // euler
        public float  RotY       { get; set; }
        public float  RotZ       { get; set; }
        public float  ScaleX     { get; set; }
        public float  ScaleY     { get; set; }
        public float  ScaleZ     { get; set; }
        public int    Flags      { get; set; }
        public int    PhysicsBone{ get; set; }
    }

    public class RMDLMesh
    {
        public int    FileOffset  { get; set; }
        public int    Material    { get; set; }
        public int    NumVertices { get; set; }
        public int    VertexOffset{ get; set; }
        public int    MeshId      { get; set; }
        public float  CenterX     { get; set; }
        public float  CenterY     { get; set; }
        public float  CenterZ     { get; set; }
    }

    public class RMDLModel
    {
        public string          Name         { get; set; } = "";
        public float           BoundRadius  { get; set; }
        public int             NumVertices  { get; set; }
        public List<RMDLMesh>  Meshes       { get; set; } = new();
    }

    public class RMDLBodyPart
    {
        public int              FileOffset  { get; set; }
        public string           Name        { get; set; } = "";
        public int              Base        { get; set; }
        public List<RMDLModel>  Models      { get; set; } = new();
    }

    public class RMDLHitboxSet
    {
        public string Name      { get; set; } = "";
        public int    NumBoxes  { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Main RMDL file class
    // ─────────────────────────────────────────────────────────────────────────
    public class RMDLFile
    {
        public string FilePath { get; private set; } = "";
        public byte[] RawData  { get; private set; } = Array.Empty<byte>();

        public RMDLHeader          Header       { get; private set; } = new();
        public List<RMDLTexture>   Textures     { get; private set; } = new();
        public List<RMDLMaterialType> Materials { get; private set; } = new();
        public List<RMDLRuiPanel>  RuiPanels    { get; private set; } = new();
        public List<RMDLBone>      Bones        { get; private set; } = new();
        public List<RMDLBodyPart>  BodyParts    { get; private set; } = new();
        public List<RMDLHitboxSet> HitboxSets   { get; private set; } = new();
        public string              KeyValues    { get; private set; } = "";
        public short[]             SkinTable    { get; private set; } = Array.Empty<short>();

        // ── Load ──────────────────────────────────────────────────────────────
        public static RMDLFile Load(string path)
        {
            var file = new RMDLFile();
            file.FilePath = path;
            file.RawData  = File.ReadAllBytes(path);
            file.Parse();
            return file;
        }

        // ── Save ──────────────────────────────────────────────────────────────
        public void Save(string path)
        {
            // Flush in-memory edits back to RawData, then write
            FlushEditsToRaw();
            File.WriteAllBytes(path, RawData);
            FilePath = path;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────
        private int    ReadInt32  (int off) => BitConverter.ToInt32  (RawData, off);
        private uint   ReadUInt32 (int off) => BitConverter.ToUInt32 (RawData, off);
        private ulong  ReadUInt64 (int off) => BitConverter.ToUInt64 (RawData, off);
        private float  ReadFloat  (int off) => BitConverter.ToSingle (RawData, off);
        private short  ReadInt16  (int off) => BitConverter.ToInt16  (RawData, off);
        private ushort ReadUInt16 (int off) => BitConverter.ToUInt16 (RawData, off);

        private string ReadNullTermString(int absoluteOff)
        {
            if (absoluteOff <= 0 || absoluteOff >= RawData.Length) return "";
            int end = absoluteOff;
            while (end < RawData.Length && RawData[end] != 0) end++;
            return Encoding.UTF8.GetString(RawData, absoluteOff, end - absoluteOff);
        }

        // Read a null-terminated string where 'relativeOff' is stored at 'fieldOff'
        // and the string address = fieldOff + relativeOff
        private string ReadRelString(int fieldOff)
        {
            int rel = ReadInt32(fieldOff);
            if (rel == 0) return "";
            return ReadNullTermString(fieldOff + rel);
        }

        private float[] ReadVector3(int off) => new float[]
        {
            ReadFloat(off), ReadFloat(off + 4), ReadFloat(off + 8)
        };

        // ─────────────────────────────────────────────────────────────────────
        //  Parsing
        // ─────────────────────────────────────────────────────────────────────
        private void Parse()
        {
            if (RawData.Length < 4)
                throw new InvalidDataException("File too small to be an RMDL.");

            int id = ReadInt32(0);
            if (id != RMDLConstants.IDST)
                throw new InvalidDataException($"Not an RMDL file (bad magic 0x{id:X8}).");

            int version = ReadInt32(4);
            if (version != RMDLConstants.VERSION_APEX)
                throw new InvalidDataException($"Unsupported RMDL version {version}. Expected 54 (v10).");

            ParseHeader();
            ParseTextures();
            ParseMaterialTypes();
            ParseBones();
            ParseBodyParts();
            ParseRuiPanels();
            ParseHitboxSets();
            ParseSkins();
            ParseKeyValues();
        }

        private void ParseHeader()
        {
            var h = Header;
            h.Id       = ReadInt32(0);
            h.Version  = ReadInt32(4);
            h.Checksum = ReadInt32(8);

            // sznameindex at offset 12 is relative to offset 12 itself
            h.InternalName = ReadRelString(12);

            // name[64] fixed at offset 16
            int nameEnd = 16;
            while (nameEnd < 80 && RawData[nameEnd] != 0) nameEnd++;
            // InternalName from fixed field if rel string is empty
            if (string.IsNullOrEmpty(h.InternalName))
                h.InternalName = Encoding.UTF8.GetString(RawData, 16, nameEnd - 16);

            h.Length = ReadInt32(80);

            h.EyePosition   = ReadVector3(84);
            h.IllumPosition = ReadVector3(96);
            h.HullMin       = ReadVector3(108);
            h.HullMax       = ReadVector3(120);
            h.ViewBBMin     = ReadVector3(132);
            h.ViewBBMax     = ReadVector3(144);

            h.Flags = ReadInt32(156);

            h.NumBones           = ReadInt32(160);
            h.RawBoneIndex       = ReadInt32(164);
            h.NumBoneControllers = ReadInt32(168);
            // bonecontrollerindex @ 172
            h.NumHitboxSets      = ReadInt32(176);
            h.RawHitboxSetIndex  = ReadInt32(180);
            h.NumLocalAnim       = ReadInt32(184);
            // localanimindex @ 188
            h.NumLocalSeq        = ReadInt32(192);
            // localseqindex @ 196
            h.ActivityListVersion = ReadInt32(200);

            h.RawMaterialTypesIndex = ReadInt32(204);
            h.NumTextures           = ReadInt32(208);
            h.RawTextureIndex       = ReadInt32(212);
            h.NumCDTextures         = ReadInt32(216);
            // cdtextureindex @ 220
            h.NumSkinRef            = ReadInt32(224);
            h.NumSkinFamilies       = ReadInt32(228);
            h.RawSkinIndex          = ReadInt32(232);
            h.NumBodyParts          = ReadInt32(236);
            h.RawBodyPartIndex      = ReadInt32(240);
            h.NumLocalAttachments   = ReadInt32(244);
            // localattachmentindex @ 248

            // numlocalnodes @ 252, localnodeindex @ 256, localnodenameindex @ 260
            // unkNodeCount @ 264, nodeDataOffsetsOffset @ 268
            // meshOffset @ 272
            // deprecated flex @ 276..291

            h.NumIKChains  = ReadInt32(292);
            // ikchainindex @ 296
            h.UIPanelCount  = ReadInt32(300);
            h.RawUIPanelOffset = ReadInt32(304);

            h.NumLocalPoseParams = ReadInt32(308);
            // localposeparamindex @ 312
            // surfacepropindex @ 316
            h.RawKeyValueIndex = ReadInt32(320);
            h.KeyValueSize     = ReadInt32(324);

            // numlocalikautoplaylocks @ 328, localikautoplaylockindex @ 332

            h.Mass     = ReadFloat(336);
            h.Contents = ReadInt32(340);

            // numincludemodels @ 344, includemodelindex @ 348
            // virtualModel @ 352
            // bonetablebynameindex @ 356

            h.ConstDirLightDot   = RawData[360];
            h.RootLOD            = RawData[361];
            h.NumAllowedRootLODs = RawData[362];
            // unused @ 363

            h.DefaultFadeDist           = ReadFloat(364);
            h.GatherSize                = ReadFloat(368);
            // deprecated_numflexcontrollerui @ 372
            // deprecated_flexcontrolleruiindex @ 376
            h.FlVertAnimFixedPointScale = ReadFloat(380);
            h.SurfacePropLookup         = ReadInt32(384);
            // sourceFilenameOffset @ 388
            h.NumSrcBoneTransform = ReadInt32(392);
            // srcbonetransformindex @ 396
            // illumpositionattachmentindex @ 400
            // linearboneindex @ 404
            // procBoneCount @ 408
            // procBoneTableOffset @ 412
            // linearProcBoneOffset @ 416
            // deprecated bone flex driver @ 420..427
            // deprecated per-tri AABB @ 428..443
            // unkStringOffset @ 444

            h.VtxOffset = ReadInt32(448);
            h.VvdOffset = ReadInt32(452);
            h.VvcOffset = ReadInt32(456);
            h.PhyOffset = ReadInt32(460);
            h.VtxSize   = ReadInt32(464);
            h.VvdSize   = ReadInt32(468);
            h.VvcSize   = ReadInt32(472);
            h.PhySize   = ReadInt32(476);

            // deprecated_unkOffset @ 480, deprecated_unkCount @ 484
            h.BoneFollowerCount = ReadInt32(488);
            // boneFollowerOffset @ 492
            // mins @ 496, maxs @ 508
            // unk3_v54[3] @ 520
            h.BvhOffset = ReadInt32(532);
            // unk4_v54[2] @ 536
            h.VvwOffset = ReadInt32(540);
            h.VvwSize   = ReadInt32(544);
        }

        private void ParseTextures()
        {
            Textures.Clear();
            int baseOff = Header.RawTextureIndex;
            for (int i = 0; i < Header.NumTextures; i++)
            {
                int off = baseOff + i * RMDLConstants.TEXTURE_STRIDE;
                if (off + RMDLConstants.TEXTURE_STRIDE > RawData.Length) break;

                int    szNameIdx = ReadInt32(off);
                ulong  guid      = ReadUInt64(off + 4);
                string name      = szNameIdx != 0 ? ReadNullTermString(off + szNameIdx) : "";

                Textures.Add(new RMDLTexture
                {
                    FileOffset  = off,
                    Name        = name,
                    TextureGuid = guid,
                    GuidOffset  = off + 4
                });
            }
        }

        private void ParseMaterialTypes()
        {
            Materials.Clear();
            int baseOff = Header.RawMaterialTypesIndex;
            if (baseOff <= 0) return;
            for (int i = 0; i < Header.NumTextures; i++)
            {
                int off = baseOff + i;
                if (off >= RawData.Length) break;
                Materials.Add(new RMDLMaterialType
                {
                    FileOffset = off,
                    TypeValue  = RawData[off]
                });
            }
        }

        // sizeof(r5::v8::mstudiobone_t)
        // sznameindex(4) + parent(4) + bonecontroller[6](24) + pos(12) + quat(16) + rot(12) + scale(12)
        // + poseToBone(48) + qAlignment(16) + flags(4) + proctype(4) + procindex(4) + physicsbone(4)
        // + surfacepropidx(4) + contents(4) + surfacepropLookup(4) + unk_B0(4) + collisionIndex(4)
        // = 4+4+24+12+16+12+12+48+16+4+4+4+4+4+4+4+4+4 = 184 bytes
        private const int BONE_STRIDE = 184;

        private void ParseBones()
        {
            Bones.Clear();
            int baseOff = Header.RawBoneIndex;
            if (baseOff <= 0) return;
            for (int i = 0; i < Header.NumBones; i++)
            {
                int off = baseOff + i * BONE_STRIDE;
                if (off + BONE_STRIDE > RawData.Length) break;

                string name   = ReadRelString(off);           // sznameindex @ off
                int    parent = ReadInt32(off + 4);
                // skip bonecontroller[6] @ off+8 (24 bytes)
                float  px = ReadFloat(off + 32);              // pos @ off+32
                float  py = ReadFloat(off + 36);
                float  pz = ReadFloat(off + 40);
                // quat @ off+44 (16 bytes)
                float  rx = ReadFloat(off + 60);              // rot (euler) @ off+60
                float  ry = ReadFloat(off + 64);
                float  rz = ReadFloat(off + 68);
                float  sx = ReadFloat(off + 72);              // scale @ off+72
                float  sy = ReadFloat(off + 76);
                float  sz = ReadFloat(off + 80);
                // poseToBone @ off+84 (48 bytes)
                // qAlignment @ off+132 (16 bytes)
                int  flags      = ReadInt32(off + 148);       // flags @ off+148
                // proctype @ 152, procindex @ 156
                int  physBone   = ReadInt32(off + 160);

                Bones.Add(new RMDLBone
                {
                    FileOffset  = off,
                    Name        = name,
                    Parent      = parent,
                    PosX = px, PosY = py, PosZ = pz,
                    RotX = rx, RotY = ry, RotZ = rz,
                    ScaleX = sx, ScaleY = sy, ScaleZ = sz,
                    Flags       = flags,
                    PhysicsBone = physBone
                });
            }
        }

        // mstudiobodyparts_t: sznameindex(4)+nummodels(4)+base(4)+modelindex(4) = 16
        private const int BODYPART_STRIDE = 16;

        // mstudiomodel_t: name[64](64)+unkStringOffset(4)+type(4)+boundingradius(4)
        //   +nummeshes(4)+meshindex(4)+numvertices(4)+vertexindex(4)+tangentsindex(4)
        //   +numattachments(4)+attachmentindex(4)+deprecated_numeyeballs(4)+deprecated_eyeballindex(4)
        //   +pad[4](16)+colorindex(4)+uv2index(4) = 64+4+4+4+4+4+4+4+4+4+4+4+4+16+4+4 = 152
        private const int MODEL_STRIDE = 152;

        // mstudiomesh_t (pack 4):
        //   material(4)+modelindex(4)+numvertices(4)+vertexoffset(4)
        //   +deprecated_numflexes(4)+deprecated_flexindex(4)
        //   +deprecated_materialtype(4)+deprecated_materialparam(4)
        //   +meshid(4)+center(12)+vertexloddata(struct)+pUnknown(ptr 8)
        // mstudio_meshvertexdata_t: pModelData(4) + numLODVertexes[8](32) = 36
        // total: 4+4+4+4+4+4+4+4+4+12+36+8 = 96? Let's use a safe stride
        private const int MESH_STRIDE = 96;

        private void ParseBodyParts()
        {
            BodyParts.Clear();
            int baseOff = Header.RawBodyPartIndex;
            if (baseOff <= 0) return;

            for (int i = 0; i < Header.NumBodyParts; i++)
            {
                int bpOff = baseOff + i * BODYPART_STRIDE;
                if (bpOff + BODYPART_STRIDE > RawData.Length) break;

                string bpName    = ReadRelString(bpOff);
                int    numModels = ReadInt32(bpOff + 4);
                int    bpBase    = ReadInt32(bpOff + 8);
                int    modelIdx  = ReadInt32(bpOff + 12);

                var bp = new RMDLBodyPart
                {
                    FileOffset = bpOff,
                    Name       = bpName,
                    Base       = bpBase
                };

                for (int j = 0; j < numModels; j++)
                {
                    int mOff = bpOff + modelIdx + j * MODEL_STRIDE;
                    if (mOff + MODEL_STRIDE > RawData.Length) break;

                    int mnEnd = mOff;
                    while (mnEnd < mOff + 64 && RawData[mnEnd] != 0) mnEnd++;
                    string modelName = Encoding.UTF8.GetString(RawData, mOff, mnEnd - mOff);

                    float boundRad   = ReadFloat(mOff + 68);
                    int   numMeshes  = ReadInt32(mOff + 72);
                    int   meshIdx    = ReadInt32(mOff + 76);
                    int   numVerts   = ReadInt32(mOff + 80);

                    var mdl = new RMDLModel
                    {
                        Name         = modelName,
                        BoundRadius  = boundRad,
                        NumVertices  = numVerts
                    };

                    for (int k = 0; k < numMeshes; k++)
                    {
                        int meshOff = mOff + meshIdx + k * MESH_STRIDE;
                        if (meshOff + MESH_STRIDE > RawData.Length) break;

                        mdl.Meshes.Add(new RMDLMesh
                        {
                            FileOffset   = meshOff,
                            Material     = ReadInt32(meshOff),
                            NumVertices  = ReadInt32(meshOff + 8),
                            VertexOffset = ReadInt32(meshOff + 12),
                            MeshId       = ReadInt32(meshOff + 32),
                            CenterX      = ReadFloat(meshOff + 36),
                            CenterY      = ReadFloat(meshOff + 40),
                            CenterZ      = ReadFloat(meshOff + 44),
                        });
                    }

                    bp.Models.Add(mdl);
                }

                BodyParts.Add(bp);
            }
        }

        // mstudiorruiheader_t: namehash(4) + ruimeshindex(4) = 8
        private const int RUI_HDR_STRIDE = 8;
        // mstudioruimesh_t: short*4 + int*5 = 8 + 20 = 28
        private const int RUI_MESH_STRIDE = 28;

        private void ParseRuiPanels()
        {
            RuiPanels.Clear();
            int count = Header.UIPanelCount;
            int baseOff = Header.RawUIPanelOffset;
            if (count <= 0 || baseOff <= 0) return;

            for (int i = 0; i < count; i++)
            {
                int hdrOff = baseOff + i * RUI_HDR_STRIDE;
                if (hdrOff + RUI_HDR_STRIDE > RawData.Length) break;

                int nameHash    = ReadInt32(hdrOff);
                int meshRelIdx  = ReadInt32(hdrOff + 4);     // this is an absolute offset in the file
                int meshOff     = meshRelIdx;

                var panel = new RMDLRuiPanel
                {
                    FileOffset   = hdrOff,
                    NameHash     = nameHash,
                    RuiMeshIndex = meshRelIdx,
                };

                if (meshOff > 0 && meshOff + RUI_MESH_STRIDE <= RawData.Length)
                {
                    panel.MeshFileOffset = meshOff;
                    panel.NumParents     = ReadInt16(meshOff + 0);
                    panel.NumVertices    = ReadInt16(meshOff + 2);
                    panel.NumFaces       = ReadInt16(meshOff + 4);
                    panel.Unk            = ReadInt16(meshOff + 6);
                    panel.ParentIndex    = ReadInt32(meshOff + 8);
                    panel.VertexIndex    = ReadInt32(meshOff + 12);
                    panel.UnkIndex       = ReadInt32(meshOff + 16);
                    panel.VertMapIndex   = ReadInt32(meshOff + 20);
                    panel.FaceDataIndex  = ReadInt32(meshOff + 24);
                }

                RuiPanels.Add(panel);
            }
        }

        // mstudiohitboxset_t: sznameindex(4)+numhitboxes(4)+hitboxindex(4) = 12
        private const int HITBOXSET_STRIDE = 12;

        private void ParseHitboxSets()
        {
            HitboxSets.Clear();
            int baseOff = Header.RawHitboxSetIndex;
            if (baseOff <= 0) return;
            for (int i = 0; i < Header.NumHitboxSets; i++)
            {
                int off = baseOff + i * HITBOXSET_STRIDE;
                if (off + HITBOXSET_STRIDE > RawData.Length) break;
                HitboxSets.Add(new RMDLHitboxSet
                {
                    Name     = ReadRelString(off),
                    NumBoxes = ReadInt32(off + 4)
                });
            }
        }

        private void ParseSkins()
        {
            int skinOff = Header.RawSkinIndex;
            int count   = Header.NumSkinRef * Header.NumSkinFamilies;
            if (skinOff <= 0 || count <= 0) { SkinTable = Array.Empty<short>(); return; }
            SkinTable = new short[count];
            for (int i = 0; i < count; i++)
            {
                int off = skinOff + i * 2;
                if (off + 2 > RawData.Length) break;
                SkinTable[i] = ReadInt16(off);
            }
        }

        private void ParseKeyValues()
        {
            int koff = Header.RawKeyValueIndex;
            int ksize = Header.KeyValueSize;
            if (koff <= 0 || ksize <= 0) { KeyValues = ""; return; }
            int end = koff;
            while (end < koff + ksize && end < RawData.Length && RawData[end] != 0) end++;
            KeyValues = Encoding.UTF8.GetString(RawData, koff, end - koff);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Flush in-memory edits back to RawData
        // ─────────────────────────────────────────────────────────────────────
        private void FlushEditsToRaw()
        {
            // Header numeric fields (in-place)
            WriteFloat(84,  Header.EyePosition[0]);
            WriteFloat(88,  Header.EyePosition[1]);
            WriteFloat(92,  Header.EyePosition[2]);
            WriteFloat(96,  Header.IllumPosition[0]);
            WriteFloat(100, Header.IllumPosition[1]);
            WriteFloat(104, Header.IllumPosition[2]);
            WriteFloat(108, Header.HullMin[0]);
            WriteFloat(112, Header.HullMin[1]);
            WriteFloat(116, Header.HullMin[2]);
            WriteFloat(120, Header.HullMax[0]);
            WriteFloat(124, Header.HullMax[1]);
            WriteFloat(128, Header.HullMax[2]);
            WriteFloat(132, Header.ViewBBMin[0]);
            WriteFloat(136, Header.ViewBBMin[1]);
            WriteFloat(140, Header.ViewBBMin[2]);
            WriteFloat(144, Header.ViewBBMax[0]);
            WriteFloat(148, Header.ViewBBMax[1]);
            WriteFloat(152, Header.ViewBBMax[2]);

            WriteInt32(156, Header.Flags);
            WriteFloat(336, Header.Mass);
            WriteInt32(340, Header.Contents);
            WriteFloat(364, Header.DefaultFadeDist);
            WriteFloat(368, Header.GatherSize);
            WriteFloat(380, Header.FlVertAnimFixedPointScale);
            WriteInt32(384, Header.SurfacePropLookup);

            RawData[360] = Header.ConstDirLightDot;
            RawData[361] = Header.RootLOD;
            RawData[362] = Header.NumAllowedRootLODs;

            // Textures – GUID is at GuidOffset (fixed size, in-place)
            foreach (var tex in Textures)
                WriteUInt64(tex.GuidOffset, tex.TextureGuid);

            // Material types
            foreach (var mat in Materials)
                RawData[mat.FileOffset] = mat.TypeValue;
        }

        // Write helpers
        private void WriteInt32 (int off, int   v) => BitConverter.GetBytes(v).CopyTo(RawData, off);
        private void WriteFloat (int off, float v) => BitConverter.GetBytes(v).CopyTo(RawData, off);
        private void WriteUInt64(int off, ulong v) => BitConverter.GetBytes(v).CopyTo(RawData, off);

        // ─────────────────────────────────────────────────────────────────────
        //  String editing
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Edits the null-terminated string pointed to by the sznameindex field at fieldOff.
        /// Writes in-place; truncates if newValue is longer than the current string's allocated space.
        /// Returns the string actually written.
        /// </summary>
        private string EditStringInPlace(int fieldOff, string newValue)
        {
            int rel = ReadInt32(fieldOff);
            if (rel == 0) return "";
            int strStart = fieldOff + rel;
            if (strStart <= 0 || strStart >= RawData.Length) return "";

            // Measure the current string's byte length
            int strEnd = strStart;
            while (strEnd < RawData.Length && RawData[strEnd] != 0) strEnd++;
            int maxBytes = strEnd - strStart;

            byte[] newBytes = Encoding.Latin1.GetBytes(newValue);
            if (newBytes.Length > maxBytes)
                newBytes = newBytes[..maxBytes];

            // Zero out old string + null terminator slot, then write new bytes
            Array.Clear(RawData, strStart, maxBytes + 1);
            newBytes.CopyTo(RawData, strStart);
            // null terminator is already set by the Clear above

            return Encoding.Latin1.GetString(newBytes);
        }

        /// <summary>
        /// Edits the name of the texture at the given index in-place.
        /// Returns the actual string written (may be truncated if longer than original).
        /// </summary>
        public string EditTextureName(int index, string newName)
        {
            if (index < 0 || index >= Textures.Count) return "";
            var tex = Textures[index];
            // sznameindex is at byte 0 of the texture struct (tex.FileOffset)
            string written = EditStringInPlace(tex.FileOffset, newName);
            tex.Name = written;
            return written;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Respawn HashString  (ported from rmdlconv/src/core/utils.h)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the Respawn HashString value for an ASCII string.
        /// Processes 4 bytes at a time with case-folding.
        /// </summary>
        public static ulong HashString(string str)
        {
            byte[] raw = Encoding.Latin1.GetBytes(str);
            // Zero-pad to nearest multiple of 4, plus an extra 4-byte sentinel
            int padLen = ((raw.Length / 4) + 2) * 4;
            byte[] buf = new byte[padLen];
            Array.Copy(raw, buf, raw.Length);

            uint ReadU32(int wordIdx) => BitConverter.ToUInt32(buf, wordIdx * 4);

            // Detect null bytes in a 32-bit word (classic "has-zero-byte" trick)
            static uint NullCheck(uint v) => ~v & (v - 0x01010101u) & 0x80808080u;

            // Case-fold: mirrors the original case-insensitive hash behaviour
            static uint CaseFold(uint v)
            {
                uint x    = v ^ 0x5C5C5C5Cu;
                uint mask = (~x >> 7) & ((x - 0x01010101u) >> 7) & 0x01010101u;
                return unchecked(v - 45u * mask) & 0xDFDFDFDFu;
            }

            int   v1 = 0;   // current word index
            ulong v2 = 0;
            int   v3 = 0;   // byte count of fully-processed words

            uint firstWord = ReadU32(0);
            uint v4 = CaseFold(firstWord);
            uint i  = NullCheck(firstWord);

            // Process 4-byte words until a null byte is found
            while (i == 0)
            {
                ulong v6 = v4;
                uint  v7 = ReadU32(v1 + 1);
                v1++;
                v3 += 4;

                ulong T = unchecked(((0xFB8C4D96501uL * v6) >> 24) + 0x633D5F1uL * v2);
                v2 = (T >> 61) ^ T;

                uint v8 = ~v7 & (v7 - 0x01010101u);
                v4 = CaseFold(v7);
                i  = v8 & 0x80808080u;
            }

            // Find bit position of the first null byte in the final word
            int  v9          = -1;
            uint isolatedBit = unchecked(i & (uint)(-(int)i));
            uint v10         = unchecked(isolatedBit - 1u);
            if (v10 != 0)
                v9 = 31 - BitOperations.LeadingZeroCount(v10);

            return unchecked(
                  0x633D5F1uL * v2
                + ((0xFB8C4D96501uL * (ulong)(v4 & v10)) >> 24)
                - 0xAE502812AA7333uL * (ulong)(uint)(v3 + v9 / 8)
            );
        }

        /// <summary>
        /// Returns the GUID for a material by name.
        /// Equivalent to HashString("material/" + textureName + ".rpak").
        /// </summary>
        public static ulong ComputeTextureGuid(string textureName)
            => HashString("material/" + textureName + ".rpak");
    }
}
