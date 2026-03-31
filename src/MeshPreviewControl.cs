using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.WinForms;

namespace RMDLEditor
{
    internal enum TexStatus { Missing, Checker, Loaded }

    // ─────────────────────────────────────────────────────────────────────────
    //  GPU mesh data for one VG mesh group
    // ─────────────────────────────────────────────────────────────────────────
    internal sealed class PreviewMesh : IDisposable
    {
        public string    Name        { get; init; } = "";
        public string    MatPath     { get; init; } = "";  // raw path from RMDL
        public int       Vao         { get; init; }
        public int       Vbo         { get; init; }
        public int       Ibo         { get; init; }
        public int       Count       { get; init; }
        public int       TexId       { get; init; }
        public TexStatus TexStatus   { get; init; }
        public Vector3   PaletteColor { get; init; }

        public int SharedWhiteTex { get; init; } = -1;

        public void Dispose()
        {
            GL.DeleteVertexArray(Vao);
            GL.DeleteBuffer(Vbo);
            GL.DeleteBuffer(Ibo);
            // Don't delete the shared white texture — it's owned by MeshPreviewControl
            if (TexId > 0 && TexId != SharedWhiteTex) GL.DeleteTexture(TexId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  OpenGL 3.3 mesh preview – orbit camera, textured, per-mesh toggle
    // ─────────────────────────────────────────────────────────────────────────
    internal sealed class MeshPreviewControl : UserControl
    {
        // ── GL objects ────────────────────────────────────────────────────────
        private readonly GLControl         _gl;
        private int                        _prog = -1;
        private int                        _uMVP, _uModel, _uTint, _uTex;
        private int                        _whiteTex;   // 1×1 white fallback
        private readonly List<PreviewMesh> _meshes  = new();
        private readonly List<bool>        _visible = new();
        private bool                       _ready;
        private RMDLFile?                  _pending;

        // ── Camera ────────────────────────────────────────────────────────────
        private float   _yaw    =  0.4f;
        private float   _pitch  =  0.3f;
        private float   _radius = 100f;
        private Vector3 _center = Vector3.Zero;
        private Point   _lastMouse;
        private bool    _dragging;

        // ── Side panel controls ───────────────────────────────────────────────
        private readonly Panel          _side      = new();
        private readonly CheckedListBox _list      = new();
        private readonly Label          _matLbl    = new();
        private readonly Label          _statLbl   = new();
        private readonly CheckBox       _wireCheck = new();
        private bool                    _wireframe;

        // ── Shaders ───────────────────────────────────────────────────────────
        private const string VS = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNrm;
layout(location = 2) in vec2 aUV;
uniform mat4 uMVP;
uniform mat4 uModel;
out vec3 vNrm;
out vec2 vUV;
void main() {
    gl_Position = uMVP * vec4(aPos, 1.0);
    vNrm = normalize(mat3(uModel) * aNrm);
    vUV  = aUV;
}";
        private const string FS = @"
#version 330 core
in  vec3 vNrm;
in  vec2 vUV;
uniform sampler2D uTex;
uniform vec3  uTint;
out vec4 fragColor;
void main() {
    vec3  L    = normalize(vec3(1.0, 2.0, 1.5));
    float d    = max(dot(normalize(vNrm), L), 0.0);
    vec3  rim  = vec3(pow(1.0 - max(dot(normalize(vNrm), normalize(vec3(0,0,1))), 0.0), 3.0) * 0.15);
    vec3  base = texture(uTex, vUV).rgb * uTint;
    fragColor  = vec4((0.25 + 0.75 * d) * base + rim, 1.0);
}";

        // ── Per-mesh fallback colours (used when no texture is found) ─────────
        private static readonly Vector3[] Palette =
        {
            new(0.82f, 0.63f, 0.42f), new(0.42f, 0.71f, 0.87f),
            new(0.62f, 0.82f, 0.42f), new(0.87f, 0.51f, 0.51f),
            new(0.71f, 0.56f, 0.82f), new(0.51f, 0.77f, 0.71f),
            new(0.91f, 0.76f, 0.41f), new(0.56f, 0.66f, 0.91f),
        };

        // ── DDS compressed format constants ──────────────────────────────────
        private const int GL_COMPRESSED_RGB_S3TC_DXT1_EXT  = 0x83F0;
        private const int GL_COMPRESSED_RGBA_S3TC_DXT3_EXT = 0x83F2;
        private const int GL_COMPRESSED_RGBA_S3TC_DXT5_EXT = 0x83F3;
        private const int GL_COMPRESSED_RED_RGTC1          = 0x8DBB;
        private const int GL_COMPRESSED_RG_RGTC2           = 0x8DBD;
        private const int GL_COMPRESSED_RGBA_BPTC_UNORM    = 0x8E8C;

        // ── DXGI format IDs ───────────────────────────────────────────────────
        private const int DXGI_BC1 = 71;
        private const int DXGI_BC2 = 74;
        private const int DXGI_BC3 = 77;
        private const int DXGI_BC4 = 80;
        private const int DXGI_BC5 = 83;
        private const int DXGI_BC7 = 98;

        // ─────────────────────────────────────────────────────────────────────
        public MeshPreviewControl()
        {
            Dock = DockStyle.Fill;

            // Side panel ──────────────────────────────────────────────────────
            _side.Dock    = DockStyle.Right;
            _side.Width   = 200;
            _side.Padding = new Padding(4, 6, 4, 4);

            var hdr = new Label
            {
                Text      = "Meshes",
                Dock      = DockStyle.Top,
                Height    = 22,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _list.Dock            = DockStyle.Fill;
            _list.CheckOnClick    = true;
            _list.DrawMode        = DrawMode.OwnerDrawFixed;
            _list.ItemHeight      = 20;
            _list.DrawItem       += OnDrawMeshItem;
            _list.ItemCheck      += OnItemCheck;
            _list.SelectedIndexChanged += OnSelectionChanged;

            _matLbl.Dock         = DockStyle.Bottom;
            _matLbl.Height       = 40;
            _matLbl.Font         = new Font("Segoe UI", 7.5f);
            _matLbl.ForeColor    = Color.Gray;
            _matLbl.Text         = "";
            _matLbl.AutoEllipsis = true;

            _statLbl.Dock      = DockStyle.Bottom;
            _statLbl.Height    = 18;
            _statLbl.Font      = new Font("Segoe UI", 7.5f);
            _statLbl.ForeColor = Color.Gray;
            _statLbl.Text      = "No mesh loaded.";

            _wireCheck.Dock      = DockStyle.Bottom;
            _wireCheck.Height    = 22;
            _wireCheck.Text      = "Wireframe overlay";
            _wireCheck.Font      = new Font("Segoe UI", 8.5f);
            _wireCheck.CheckedChanged += (_, _) =>
            {
                _wireframe = _wireCheck.Checked;
                _gl.Invalidate();
            };

            _side.Controls.Add(_list);
            _side.Controls.Add(hdr);
            _side.Controls.Add(_matLbl);
            _side.Controls.Add(_wireCheck);
            _side.Controls.Add(_statLbl);

            // GL control ──────────────────────────────────────────────────────
            var settings = new GLControlSettings
            {
                API        = OpenTK.Windowing.Common.ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),
                Profile    = OpenTK.Windowing.Common.ContextProfile.Core,
            };
            _gl = new GLControl(settings) { Dock = DockStyle.Fill };
            _gl.Load      += OnLoad;
            _gl.Paint     += OnPaint;
            _gl.Resize    += (_, _) => _gl.Invalidate();
            _gl.MouseDown += OnMouseDown;
            _gl.MouseMove += OnMouseMove;
            _gl.MouseUp   += (_, _) => _dragging = false;
            _gl.MouseWheel += OnWheel;

            Controls.Add(_gl);
            Controls.Add(_side);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public
        // ─────────────────────────────────────────────────────────────────────
        public void LoadMesh(RMDLFile rmdl)
        {
            if (!_ready) { _pending = rmdl; return; }
            _pending = null;

            _gl.MakeCurrent();
            DropMeshes();

            byte[] vgData;
            int    vgBase;
            try
            {
                ResolveVG(rmdl, out vgData, out vgBase);
            }
            catch (Exception ex)
            {
                _statLbl.Text = "No VG: " + ex.Message;
                return;
            }

            List<(string name, string matPath, float[] verts, int[] idx)> groups;
            Vector3 cen; float rad, extX, extY, extZ;
            try
            {
                (groups, cen, rad, extX, extY, extZ) = ParseVG(rmdl, vgData, vgBase);
            }
            catch (Exception ex)
            {
                _statLbl.Text = "Parse error: " + ex.Message + " @ " + ex.StackTrace?.Split('\n')[0];
                return;
            }
            _center = cen;
            _radius = rad * 2.5f;

            float minExt = Math.Min(extX, Math.Min(extY, extZ));
            if (minExt == extX)      { _yaw = MathF.PI / 2f; _pitch = 0.25f; }
            else if (minExt == extZ) { _yaw = 0.15f;          _pitch = 0.25f; }
            else                     { _yaw = 0.15f;          _pitch = 1.1f;  }

            string rmdlDir = Path.GetDirectoryName(rmdl.FilePath) ?? "";
            int totalTri = 0;

            for (int i = 0; i < groups.Count; i++)
            {
                var (name, matPath, vb, idx) = groups[i];
                totalTri += idx.Length / 3;

                int vao = GL.GenVertexArray();
                int vbo = GL.GenBuffer();
                int ibo = GL.GenBuffer();

                GL.BindVertexArray(vao);

                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer,
                              vb.Length * sizeof(float), vb,
                              BufferUsageHint.StaticDraw);

                // layout: pos(3) nrm(3) uv(2)  stride=8 floats=32 bytes
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 32, 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 32, 12);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 32, 24);
                GL.EnableVertexAttribArray(2);

                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
                GL.BufferData(BufferTarget.ElementArrayBuffer,
                              idx.Length * sizeof(int), idx,
                              BufferUsageHint.StaticDraw);

                GL.BindVertexArray(0);

                var paletteColor = Palette[i % Palette.Length];
                var (texId, texStatus) = LoadTexture(matPath, rmdlDir, paletteColor);

                _meshes.Add(new PreviewMesh
                {
                    Name           = name,
                    MatPath        = matPath,
                    Vao            = vao, Vbo = vbo, Ibo = ibo,
                    Count          = idx.Length,
                    TexId          = texId,
                    TexStatus      = texStatus,
                    PaletteColor   = paletteColor,
                    SharedWhiteTex = _whiteTex,
                });
                _visible.Add(true);
                _list.Items.Add(name, true);
            }

            _statLbl.Text = $"{_meshes.Count} mesh(es)  {totalTri:N0} tri(s)  bbox={extX:F1}×{extY:F1}×{extZ:F1}";
            _gl.Invalidate();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Theme support
        // ─────────────────────────────────────────────────────────────────────
        public void ApplyTheme(bool dark)
        {
            Color bg  = dark ? Theme.DarkSurface  : SystemColors.Control;
            Color txt = dark ? Theme.DarkText      : SystemColors.ControlText;
            Color dim = dark ? Theme.DarkTextDim   : SystemColors.GrayText;

            _side.BackColor        = bg;
            _list.BackColor        = dark ? Theme.DarkElevated : SystemColors.Window;
            _list.ForeColor        = txt;
            _matLbl.ForeColor      = dim;
            _statLbl.ForeColor     = dim;
            _wireCheck.BackColor   = bg;
            _wireCheck.ForeColor   = txt;

            foreach (Control c in _side.Controls)
            {
                c.BackColor = bg;
                c.ForeColor = txt;
            }

            if (_ready && _prog >= 0)
            {
                float r = dark ? 0.13f : 0.85f;
                float g = dark ? 0.13f : 0.85f;
                float b = dark ? 0.15f : 0.85f;
                _gl.MakeCurrent();
                GL.ClearColor(r, g, b, 1f);
                _gl.Invalidate();
            }

            _list.Invalidate();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  GL lifecycle
        // ─────────────────────────────────────────────────────────────────────
        private void OnLoad(object? s, EventArgs e)
        {
            _gl.MakeCurrent();
            GL.ClearColor(0.13f, 0.13f, 0.15f, 1f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);

            _prog   = CompileProgram(VS, FS);
            _uMVP   = GL.GetUniformLocation(_prog, "uMVP");
            _uModel = GL.GetUniformLocation(_prog, "uModel");
            _uTint  = GL.GetUniformLocation(_prog, "uTint");
            _uTex   = GL.GetUniformLocation(_prog, "uTex");

            // 1×1 white texture used as the base for palette-coloured meshes
            _whiteTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _whiteTex);
            byte[] white = { 255, 255, 255 };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb,
                          1, 1, 0, PixelFormat.Rgb, PixelType.UnsignedByte, white);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            _ready  = true;

            if (_pending != null) LoadMesh(_pending);
        }

        private void OnPaint(object? s, PaintEventArgs e)
        {
            if (!_ready) return;
            _gl.MakeCurrent();

            int w = _gl.Width, h = _gl.Height;
            if (w <= 0 || h <= 0) return;

            GL.Viewport(0, 0, w, h);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_meshes.Count == 0) { _gl.SwapBuffers(); return; }

            float ex = _radius * MathF.Cos(_pitch) * MathF.Sin(_yaw);
            float ey = _radius * MathF.Sin(_pitch);
            float ez = _radius * MathF.Cos(_pitch) * MathF.Cos(_yaw);

            var view = Matrix4.LookAt(_center + new Vector3(ex, ey, ez), _center, Vector3.UnitY);
            var proj = Matrix4.CreatePerspectiveFieldOfView(
                           MathHelper.DegreesToRadians(45f),
                           (float)w / h,
                           _radius * 0.001f,
                           _radius * 20f);
            var model = Matrix4.Identity;
            var mvp   = model * view * proj;

            GL.UseProgram(_prog);
            GL.UniformMatrix4(_uMVP,   false, ref mvp);
            GL.UniformMatrix4(_uModel, false, ref model);
            GL.Uniform1(_uTex, 0);

            GL.ActiveTexture(TextureUnit.Texture0);

            // Solid pass
            for (int i = 0; i < _meshes.Count; i++)
            {
                if (i < _visible.Count && !_visible[i]) continue;
                var m = _meshes[i];
                GL.BindTexture(TextureTarget.Texture2D, m.TexId);
                var tint = m.TexStatus == TexStatus.Loaded ? Vector3.One : m.PaletteColor;
                GL.Uniform3(_uTint, tint);
                GL.BindVertexArray(m.Vao);
                GL.DrawElements(PrimitiveType.Triangles, m.Count,
                                DrawElementsType.UnsignedInt, 0);
            }

            // Wireframe overlay pass
            if (_wireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                GL.PolygonOffset(-1f, -1f);
                GL.Enable(EnableCap.PolygonOffsetLine);
                GL.Disable(EnableCap.CullFace);

                var wireColor = new Vector3(0f, 0f, 0f);
                for (int i = 0; i < _meshes.Count; i++)
                {
                    if (i < _visible.Count && !_visible[i]) continue;
                    var m = _meshes[i];
                    GL.BindTexture(TextureTarget.Texture2D, m.TexId);
                    GL.Uniform3(_uTint, wireColor);
                    GL.BindVertexArray(m.Vao);
                    GL.DrawElements(PrimitiveType.Triangles, m.Count,
                                    DrawElementsType.UnsignedInt, 0);
                }

                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                GL.Disable(EnableCap.PolygonOffsetLine);
                GL.Enable(EnableCap.CullFace);
            }

            GL.BindVertexArray(0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            _gl.SwapBuffers();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Camera input
        // ─────────────────────────────────────────────────────────────────────
        private void OnMouseDown(object? s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _dragging = true; _lastMouse = e.Location; }
        }

        private void OnMouseMove(object? s, MouseEventArgs e)
        {
            if (!_dragging) return;
            _yaw   += (e.X - _lastMouse.X) * 0.006f;
            _pitch  = Math.Clamp(_pitch - (e.Y - _lastMouse.Y) * 0.006f, -1.5f, 1.5f);
            _lastMouse = e.Location;
            _gl.Invalidate();
        }

        private void OnWheel(object? s, MouseEventArgs e)
        {
            _radius = Math.Max(_radius * (e.Delta > 0 ? 0.88f : 1.13f), 0.01f);
            _gl.Invalidate();
        }

        private void OnItemCheck(object? s, ItemCheckEventArgs e)
        {
            BeginInvoke(() =>
            {
                for (int i = 0; i < _visible.Count; i++)
                    _visible[i] = _list.GetItemChecked(i);
                _gl.Invalidate();
            });
        }

        private void OnSelectionChanged(object? s, EventArgs e)
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _meshes.Count)
            {
                _matLbl.Text = "";
                return;
            }
            var m = _meshes[idx];
            string statusStr = m.TexStatus switch
            {
                TexStatus.Loaded  => "loaded",
                TexStatus.Checker => "not found",
                _                 => "no UV / missing",
            };
            _matLbl.Text = $"[{statusStr}]\n{m.MatPath}";
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Owner-draw mesh list item: name + status dot
        // ─────────────────────────────────────────────────────────────────────
        private void OnDrawMeshItem(object? s, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();

            Color dotColor;
            if (e.Index < _meshes.Count)
            {
                dotColor = _meshes[e.Index].TexStatus switch
                {
                    TexStatus.Loaded  => Color.LimeGreen,
                    TexStatus.Checker => Color.Gold,
                    _                 => Color.Gray,
                };
            }
            else
            {
                dotColor = Color.Gray;
            }

            const int dotSz  = 8;
            const int dotPad = 5;
            var dotRect = new Rectangle(
                e.Bounds.Right - dotSz - dotPad,
                e.Bounds.Top   + (e.Bounds.Height - dotSz) / 2,
                dotSz, dotSz);

            using (var brush = new SolidBrush(dotColor))
                e.Graphics.FillEllipse(brush, dotRect);

            var textRect = new Rectangle(
                e.Bounds.Left, e.Bounds.Top,
                e.Bounds.Width - dotSz - dotPad * 2, e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics,
                e.Index < _list.Items.Count ? _list.Items[e.Index]?.ToString() : "",
                e.Font, textRect, e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            e.DrawFocusRectangle();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  GL helpers
        // ─────────────────────────────────────────────────────────────────────
        private static int CompileProgram(string vertSrc, string fragSrc)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertSrc);
            GL.CompileShader(vs);
            GL.GetShader(vs, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0) throw new Exception("VS: " + GL.GetShaderInfoLog(vs));

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragSrc);
            GL.CompileShader(fs);
            GL.GetShader(fs, ShaderParameter.CompileStatus, out ok);
            if (ok == 0) throw new Exception("FS: " + GL.GetShaderInfoLog(fs));

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vs);
            GL.AttachShader(prog, fs);
            GL.LinkProgram(prog);
            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out ok);
            if (ok == 0) throw new Exception("Link: " + GL.GetProgramInfoLog(prog));

            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            return prog;
        }

        private void DropMeshes()
        {
            foreach (var m in _meshes) m.Dispose();
            _meshes.Clear();
            _visible.Clear();
            _list.Items.Clear();
            _matLbl.Text = "";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ready)
            {
                _gl.MakeCurrent();
                DropMeshes();
                if (_prog >= 0) GL.DeleteProgram(_prog);
                if (_whiteTex > 0) GL.DeleteTexture(_whiteTex);
            }
            base.Dispose(disposing);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Texture loading
        //    matPath  – raw texture path from RMDL (e.g. "models/weapons/r301/body")
        //    rmdlDir  – directory of the .rmdl file
        //  Returns (texId, status).  Always succeeds: falls back to checkerboard.
        // ─────────────────────────────────────────────────────────────────────
        private (int texId, TexStatus status) LoadTexture(string matPath, string rmdlDir, Vector3 paletteColor)
        {
            if (!string.IsNullOrEmpty(matPath))
            {
                string leaf = Path.GetFileNameWithoutExtension(
                                  matPath.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(leaf))
                {
                    string dir = rmdlDir;
                    for (int depth = 0; depth < 4; depth++)
                    {
                        if (string.IsNullOrEmpty(dir)) break;
                        foreach (string ext in new[] { ".dds", ".png" })
                        {
                            string candidate = Path.Combine(dir, leaf + ext);
                            if (File.Exists(candidate))
                            {
                                try
                                {
                                    int id = ext == ".dds"
                                        ? LoadDDS(candidate)
                                        : LoadPNG(candidate);
                                    if (id > 0) return (id, TexStatus.Loaded);
                                }
                                catch { /* try next */ }
                            }
                        }
                        dir = Path.GetDirectoryName(dir) ?? "";
                    }
                }
            }

            // No texture found — return the shared white texture so the
            // palette colour tint renders cleanly via uTint.
            return (_whiteTex, TexStatus.Checker);
        }

        // ─── Checkerboard (64×64 grey/dark) ──────────────────────────────────
        private static int GenerateCheckerboard()
        {
            const int size = 64;
            const int tile = 8;
            var pixels = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool dark = ((x / tile) + (y / tile)) % 2 == 0;
                byte c = dark ? (byte)80 : (byte)180;
                int i = (y * size + x) * 3;
                pixels[i] = c; pixels[i + 1] = c; pixels[i + 2] = c;
            }

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb,
                          size, size, 0, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
            SetTexParams();
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        // ─── PNG loader via System.Drawing ───────────────────────────────────
        private static int LoadPNG(string path)
        {
            using var bmp = new Bitmap(path);
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(
                new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int tex = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, tex);
                // ARGB from GDI+ is actually BGRA in memory; use Bgra pixel format
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                              w, h, 0, PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
                SetTexParams();
                GL.BindTexture(TextureTarget.Texture2D, 0);
                return tex;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        // ─── DDS loader (BC1/BC2/BC3/BC4/BC5/BC7) ───────────────────────────
        private static int LoadDDS(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 128 || BitConverter.ToInt32(d, 0) != 0x20534444) // "DDS "
                throw new InvalidDataException("Not a DDS file.");

            int height = BitConverter.ToInt32(d, 12);
            int width  = BitConverter.ToInt32(d, 16);

            // Pixel format block starts at offset 76
            int pfFourCC = BitConverter.ToInt32(d, 84);

            int glFmt;
            int dataOff;

            const int FOURCC_DX10 = 0x30315844; // "DX10"
            const int FOURCC_DXT1 = 0x31545844;
            const int FOURCC_DXT3 = 0x33545844;
            const int FOURCC_DXT5 = 0x35545844;
            const int FOURCC_BC4U = 0x55344342;
            const int FOURCC_ATI2 = 0x32495441;
            const int FOURCC_BC5U = 0x55354342;

            if (pfFourCC == FOURCC_DX10)
            {
                // DX10 extended header (20 bytes) after the 128-byte header
                dataOff = 148;
                int dxgiFmt = BitConverter.ToInt32(d, 128);
                glFmt = dxgiFmt switch
                {
                    DXGI_BC1 => GL_COMPRESSED_RGB_S3TC_DXT1_EXT,
                    DXGI_BC2 => GL_COMPRESSED_RGBA_S3TC_DXT3_EXT,
                    DXGI_BC3 => GL_COMPRESSED_RGBA_S3TC_DXT5_EXT,
                    DXGI_BC4 => GL_COMPRESSED_RED_RGTC1,
                    DXGI_BC5 => GL_COMPRESSED_RG_RGTC2,
                    DXGI_BC7 => GL_COMPRESSED_RGBA_BPTC_UNORM,
                    _ => throw new NotSupportedException($"DXGI format {dxgiFmt} not supported.")
                };
            }
            else
            {
                dataOff = 128;
                glFmt = pfFourCC switch
                {
                    FOURCC_DXT1              => GL_COMPRESSED_RGB_S3TC_DXT1_EXT,
                    FOURCC_DXT3              => GL_COMPRESSED_RGBA_S3TC_DXT3_EXT,
                    FOURCC_DXT5              => GL_COMPRESSED_RGBA_S3TC_DXT5_EXT,
                    FOURCC_BC4U              => GL_COMPRESSED_RED_RGTC1,
                    FOURCC_ATI2 or FOURCC_BC5U => GL_COMPRESSED_RG_RGTC2,
                    _ => throw new NotSupportedException($"DDS FOURCC 0x{pfFourCC:X8} not supported.")
                };
            }

            // Block size: BC1/BC4 = 8 bytes/block, others = 16
            bool is8 = glFmt == GL_COMPRESSED_RGB_S3TC_DXT1_EXT ||
                       glFmt == GL_COMPRESSED_RED_RGTC1;
            int blockBytes = is8 ? 8 : 16;
            int dataLen = ((width + 3) / 4) * ((height + 3) / 4) * blockBytes;
            if (dataOff + dataLen > d.Length)
                throw new InvalidDataException("DDS data truncated.");

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);

            GL.CompressedTexImage2D(TextureTarget.Texture2D, 0,
                (InternalFormat)glFmt, width, height, 0, dataLen, ref d[dataOff]);

            SetTexParams();
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        private static void SetTexParams()
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  VG parsing
        // ─────────────────────────────────────────────────────────────────────
        private static void ResolveVG(RMDLFile rmdl, out byte[] data, out int vgBase)
        {
            int embOff = rmdl.Header.VvdOffset;
            if (embOff > 0 && embOff + 16 <= rmdl.RawData.Length
                && BitConverter.ToInt32(rmdl.RawData, embOff) == 0x47567430)
            {
                data   = rmdl.RawData;
                vgBase = embOff;
                return;
            }
            string vgPath = Path.ChangeExtension(rmdl.FilePath, ".vg");
            if (!File.Exists(vgPath))
                throw new FileNotFoundException(
                    $"No sidecar .vg found at: {Path.GetFileName(vgPath)}");
            data   = File.ReadAllBytes(vgPath);
            vgBase = 0;
        }

        private static (List<(string name, string matPath, float[] verts, int[] idx)> groups,
                        Vector3 center, float radius, float extX, float extY, float extZ)
            ParseVG(RMDLFile rmdl, byte[] data, int vgBase)
        {
            long meshOff  = I64(data, vgBase + 0x20);
            long indexOff = I64(data, vgBase + 0x30);
            long vertOff  = I64(data, vgBase + 0x40);
            long lodOff   = I64(data, vgBase + 0x70);

            int lod0Base  = vgBase + (int)lodOff;
            int lod0Start = BitConverter.ToUInt16(data, lod0Base);
            int lod0Count = BitConverter.ToUInt16(data, lod0Base + 2);

            // Texture names + raw paths from RMDL
            var texNames = new List<string>();
            var texPaths = new List<string>();
            foreach (var bp in rmdl.BodyParts)
            {
                if (bp.Models.Count == 0) continue;
                foreach (var mesh in bp.Models[0].Meshes)
                {
                    int  mi  = mesh.Material;
                    string n = (mi >= 0 && mi < rmdl.Textures.Count)
                               ? rmdl.Textures[mi].Name : "";
                    texPaths.Add(n);
                    texNames.Add(string.IsNullOrEmpty(n)
                        ? $"mesh_{texNames.Count}"
                        : Path.GetFileNameWithoutExtension(n.Replace('\\', '/')));
                }
            }

            var groups = new List<(string, string, float[], int[])>();
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            const long VF_UV1          = 0x0002000000L;
            const long VF_COLOR        = 0x10L;
            const long VF_WEIGHT_BONES = 0x1000L;
            const long VF_WEIGHT_2     = 0x4000L;
            const long VF_WEIGHT_N     = 0x2000L;

            for (int mi = 0; mi < lod0Count; mi++)
            {
                int  mhOff  = vgBase + (int)meshOff + (lod0Start + mi) * 0x48;
                long mFlags = I64(data, mhOff + 0x00);
                int  vBuf   = (int)U32(data, mhOff + 0x08);
                int  stride = (int)U32(data, mhOff + 0x0C);
                int  vCount = (int)U32(data, mhOff + 0x10);
                int  iBuf   = I32(data, mhOff + 0x20);
                int  iCount = I32(data, mhOff + 0x24);

                if (vCount == 0 || stride == 0 || iCount == 0) continue;

                int vBase = vgBase + (int)vertOff  + vBuf;
                int iBase = vgBase + (int)indexOff + iBuf * 2;

                bool hasUV = (mFlags & VF_UV1) != 0;

                // 8 floats per vertex: pos(3) nrm(3) uv(2)
                float[] vb = new float[vCount * 8];

                for (int vi = 0; vi < vCount; vi++)
                {
                    float px = 0, py = 0, pz = 0;
                    float nx = 0, ny = 0, nz = 1;
                    float tu = 0, tv = 0;
                    int   cur = vBase + vi * stride;

                    switch ((int)(mFlags & 3))
                    {
                        case 1:
                            px = SF(data, cur); py = SF(data, cur + 4); pz = SF(data, cur + 8);
                            cur += 12;
                            break;
                        case 2:
                            ulong p64 = BitConverter.ToUInt64(data, cur);
                            px = (float)((p64         & 0x1FFFFFUL) * 0.0009765625 - 1024.0);
                            py = (float)(((p64 >> 21) & 0x1FFFFFUL) * 0.0009765625 - 1024.0);
                            pz = (float)(((p64 >> 42) & 0x3FFFFFUL) * 0.0009765625 - 2048.0);
                            cur += 8;
                            break;
                        case 3:
                            cur += 6;
                            break;
                    }

                    if ((mFlags & (VF_WEIGHT_BONES | VF_WEIGHT_2)) != 0) cur += 8;
                    else if ((mFlags & VF_WEIGHT_N) != 0)                 cur += 8;

                    UnpackNormal32(U32(data, cur), out nx, out ny, out nz);
                    cur += 4;

                    if ((mFlags & VF_COLOR) != 0) cur += 4;

                    if (hasUV)
                    {
                        tu = SF(data, cur);
                        tv = 1f - SF(data, cur + 4);  // flip V DX→GL
                    }

                    // Remap to GL Y-up: (px,py,pz) → (px,pz,-py)
                    float glX =  px, glY =  pz, glZ = -py;
                    float gnX =  nx, gnY =  nz, gnZ = -ny;

                    int base8 = vi * 8;
                    vb[base8 + 0] = glX; vb[base8 + 1] = glY; vb[base8 + 2] = glZ;
                    vb[base8 + 3] = gnX; vb[base8 + 4] = gnY; vb[base8 + 5] = gnZ;
                    vb[base8 + 6] = tu;  vb[base8 + 7] = tv;

                    if (glX < minX) minX = glX; if (glX > maxX) maxX = glX;
                    if (glY < minY) minY = glY; if (glY > maxY) maxY = glY;
                    if (glZ < minZ) minZ = glZ; if (glZ > maxZ) maxZ = glZ;
                }

                // Index buffer: reverse winding CW→CCW
                int triCount = iCount / 3;
                int[] idx    = new int[triCount * 3];
                for (int ti = 0; ti < triCount; ti++)
                {
                    int ip = iBase + ti * 6;
                    idx[ti * 3 + 0] = BitConverter.ToUInt16(data, ip);
                    idx[ti * 3 + 1] = BitConverter.ToUInt16(data, ip + 4);
                    idx[ti * 3 + 2] = BitConverter.ToUInt16(data, ip + 2);
                }

                string name    = (mi < texNames.Count) ? texNames[mi] : $"mesh_{mi}";
                string matPath = (mi < texPaths.Count) ? texPaths[mi] : "";
                groups.Add((name, matPath, vb, idx));
            }

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            float dX = maxX - minX, dY = maxY - minY, dZ = maxZ - minZ;
            float r  = MathF.Sqrt(dX * dX + dY * dY + dZ * dZ) * 0.5f;
            if (r < 1f) r = 1f;

            return (groups, new Vector3(cx, cy, cz), r, dX, dY, dZ);
        }

        private static void UnpackNormal32(uint p, out float nx, out float ny, out float nz)
        {
            bool  sign = ((p >> 28) & 1u) != 0;
            float v1   = sign ? -255f : 255f;
            float v2   = (float)((p >> 19) & 0x1FFu) - 256f;
            float v3   = (float)((p >> 10) & 0x1FFu) - 256f;
            int   i1   = (int)((p >> 29) & 3u);
            int   i2   = (int)((0x124u >> (2 * i1 + 2)) & 3u);
            int   i3   = (int)((0x124u >> (2 * i1 + 4)) & 3u);
            float inv  = 1f / MathF.Sqrt(255f * 255f + v2 * v2 + v3 * v3);
            float[] n  = new float[3];
            n[i1] = v1 * inv; n[i2] = v2 * inv; n[i3] = v3 * inv;
            nx = n[0]; ny = n[1]; nz = n[2];
        }

        private static long  I64(byte[] d, int o) => BitConverter.ToInt64 (d, o);
        private static int   I32(byte[] d, int o) => BitConverter.ToInt32 (d, o);
        private static uint  U32(byte[] d, int o) => BitConverter.ToUInt32(d, o);
        private static float SF (byte[] d, int o) => BitConverter.ToSingle(d, o);
    }
}
