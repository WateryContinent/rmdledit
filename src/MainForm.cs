using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace RMDLEditor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Theme colour palettes
    // ─────────────────────────────────────────────────────────────────────────
    internal static class Theme
    {
        // ── Dark (VS-style) ──────────────────────────────────────────────────
        public static readonly Color DarkBg        = Color.FromArgb(0x1E, 0x1E, 0x1E);
        public static readonly Color DarkSurface   = Color.FromArgb(0x25, 0x25, 0x26);
        public static readonly Color DarkElevated  = Color.FromArgb(0x2D, 0x2D, 0x30);
        public static readonly Color DarkBorder    = Color.FromArgb(0x3F, 0x3F, 0x46);
        public static readonly Color DarkText      = Color.FromArgb(0xD4, 0xD4, 0xD4);
        public static readonly Color DarkTextDim   = Color.FromArgb(0x85, 0x85, 0x85);
        public static readonly Color DarkSel       = Color.FromArgb(0x09, 0x47, 0x71);
        public static readonly Color DarkSelText   = Color.White;
        public static readonly Color DarkAccent    = Color.FromArgb(0x00, 0x7A, 0xCC);

        // ── Light (system defaults) ──────────────────────────────────────────
        public static readonly Color LightBg       = SystemColors.Control;
        public static readonly Color LightSurface  = SystemColors.Window;
        public static readonly Color LightElevated = SystemColors.ControlLight;
        public static readonly Color LightBorder   = SystemColors.ControlDark;
        public static readonly Color LightText     = SystemColors.ControlText;
        public static readonly Color LightTextDim  = SystemColors.GrayText;
        public static readonly Color LightSel      = SystemColors.Highlight;
        public static readonly Color LightSelText  = SystemColors.HighlightText;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Custom menu/toolbar renderer for dark mode
    // ─────────────────────────────────────────────────────────────────────────
    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder                   => Theme.DarkBorder;
        public override Color MenuItemBorder               => Theme.DarkBorder;
        public override Color MenuItemSelected             => Theme.DarkElevated;
        public override Color MenuItemSelectedGradientBegin=> Theme.DarkElevated;
        public override Color MenuItemSelectedGradientEnd  => Theme.DarkElevated;
        public override Color MenuItemPressedGradientBegin => Theme.DarkSel;
        public override Color MenuItemPressedGradientEnd   => Theme.DarkSel;
        public override Color MenuItemPressedGradientMiddle=> Theme.DarkSel;
        public override Color MenuStripGradientBegin       => Theme.DarkElevated;
        public override Color MenuStripGradientEnd         => Theme.DarkElevated;
        public override Color ToolStripDropDownBackground  => Theme.DarkElevated;
        public override Color ImageMarginGradientBegin     => Theme.DarkElevated;
        public override Color ImageMarginGradientMiddle    => Theme.DarkElevated;
        public override Color ImageMarginGradientEnd       => Theme.DarkElevated;
        public override Color SeparatorDark                => Theme.DarkBorder;
        public override Color SeparatorLight               => Theme.DarkBorder;
        public override Color StatusStripGradientBegin     => Theme.DarkElevated;
        public override Color StatusStripGradientEnd       => Theme.DarkElevated;
        public override Color CheckBackground              => Theme.DarkSel;
        public override Color CheckPressedBackground       => Theme.DarkAccent;
        public override Color CheckSelectedBackground      => Theme.DarkAccent;
        public override Color ToolStripBorder              => Theme.DarkBorder;
        public override Color ToolStripContentPanelGradientBegin => Theme.DarkBg;
        public override Color ToolStripContentPanelGradientEnd   => Theme.DarkBg;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Owner-painted TabControl — fully themed, no white chrome
    // ─────────────────────────────────────────────────────────────────────────
    internal class ThemedTabControl : TabControl
    {
        public bool IsDark { get; set; }

        public ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Padding = new Point(12, 4);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* suppressed — OnPaint fills everything */ }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (TabCount == 0) return;

            Color bg      = IsDark ? Theme.DarkBg       : SystemColors.Control;
            Color surface = IsDark ? Theme.DarkSurface  : SystemColors.Window;
            Color elev    = IsDark ? Theme.DarkElevated : SystemColors.ControlLight;
            Color border  = IsDark ? Theme.DarkBorder   : SystemColors.ControlDark;
            Color fg      = IsDark ? Theme.DarkText     : SystemColors.ControlText;

            var g = e.Graphics;

            // 1 — fill entire control background
            using (var b = new SolidBrush(bg))
                g.FillRectangle(b, ClientRectangle);

            // 2 — draw each tab button
            for (int i = 0; i < TabCount; i++)
            {
                var  rect = GetTabRect(i);
                bool sel  = (i == SelectedIndex);
                Color tabBg = sel ? surface : elev;

                using (var b = new SolidBrush(tabBg))
                    g.FillRectangle(b, rect);

                using var pen = new Pen(border);
                if (sel)
                {
                    // top, left, right — leave bottom open so it "merges" with content
                    g.DrawLine(pen, rect.Left,      rect.Top,    rect.Right - 1, rect.Top);
                    g.DrawLine(pen, rect.Left,      rect.Top,    rect.Left,      rect.Bottom);
                    g.DrawLine(pen, rect.Right - 1, rect.Top,    rect.Right - 1, rect.Bottom);
                }
                else
                {
                    g.DrawRectangle(pen, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
                }

                using var sf = new StringFormat
                    { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(TabPages[i].Text, Font, new SolidBrush(fg), (RectangleF)rect, sf);
            }

            // 3 — draw border around content area
            var dr = DisplayRectangle;
            using (var pen = new Pen(border))
                g.DrawRectangle(pen, dr.Left - 1, dr.Top - 1, dr.Width + 1, dr.Height + 1);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PropertyGrid model for the Header tab
    // ─────────────────────────────────────────────────────────────────────────
    public class HeaderEditModel
    {
        // ── 1. Identity (read-only) ───────────────────────────────────────────
        [Category("1. Identity"), DisplayName("ID (magic)"), ReadOnly(true)]
        public string Id { get; set; } = "";

        [Category("1. Identity"), DisplayName("Version"), ReadOnly(true)]
        public int Version { get; set; }

        [Category("1. Identity"), DisplayName("Checksum"), ReadOnly(true)]
        public string Checksum { get; set; } = "";

        [Category("1. Identity"), DisplayName("Internal Name"), ReadOnly(true)]
        public string InternalName { get; set; } = "";

        [Category("1. Identity"), DisplayName("File Size (bytes)"), ReadOnly(true)]
        public int FileLength { get; set; }

        // ── 2. Bounding ───────────────────────────────────────────────────────
        [Category("2. Bounding"), DisplayName("Eye Position")]
        [Description("Space-separated X Y Z")]
        public string EyePosition { get; set; } = "";

        [Category("2. Bounding"), DisplayName("Illum Position")]
        [Description("Illumination reference position. Space-separated X Y Z")]
        public string IllumPosition { get; set; } = "";

        [Category("2. Bounding"), DisplayName("Hull Min")]
        [Description("Collision hull minimum. Space-separated X Y Z")]
        public string HullMin { get; set; } = "";

        [Category("2. Bounding"), DisplayName("Hull Max")]
        [Description("Collision hull maximum. Space-separated X Y Z")]
        public string HullMax { get; set; } = "";

        [Category("2. Bounding"), DisplayName("View BB Min")]
        [Description("View bounding box minimum. Space-separated X Y Z")]
        public string ViewBBMin { get; set; } = "";

        [Category("2. Bounding"), DisplayName("View BB Max")]
        [Description("View bounding box maximum. Space-separated X Y Z")]
        public string ViewBBMax { get; set; } = "";

        // ── 3. Physics ────────────────────────────────────────────────────────
        [Category("3. Physics"), DisplayName("Mass")]
        public float Mass { get; set; }

        [Category("3. Physics"), DisplayName("Contents")]
        public int Contents { get; set; }

        [Category("3. Physics"), DisplayName("Default Fade Distance")]
        public float DefaultFadeDist { get; set; }

        [Category("3. Physics"), DisplayName("Gather Size")]
        public float GatherSize { get; set; }

        // ── 4. Render ─────────────────────────────────────────────────────────
        [Category("4. Render"), DisplayName("Flags")]
        [Description("Hex flags value, e.g. 0x00000000")]
        public string Flags { get; set; } = "";

        [Category("4. Render"), DisplayName("Root LOD")]
        public byte RootLOD { get; set; }

        [Category("4. Render"), DisplayName("Num Allowed Root LODs")]
        public byte NumAllowedRootLODs { get; set; }

        [Category("4. Render"), DisplayName("Const Dir Light Dot")]
        public byte ConstDirLightDot { get; set; }

        [Category("4. Render"), DisplayName("Vert Anim FP Scale")]
        public float FlVertAnimFixedPointScale { get; set; }

        [Category("4. Render"), DisplayName("Surface Prop Lookup")]
        public int SurfacePropLookup { get; set; }

        // ── 5. Counts (read-only) ─────────────────────────────────────────────
        [Category("5. Counts"), DisplayName("Bones"),            ReadOnly(true)] public int NumBones             { get; set; }
        [Category("5. Counts"), DisplayName("Bone Controllers"), ReadOnly(true)] public int NumBoneControllers   { get; set; }
        [Category("5. Counts"), DisplayName("Hitbox Sets"),      ReadOnly(true)] public int NumHitboxSets        { get; set; }
        [Category("5. Counts"), DisplayName("Animations"),       ReadOnly(true)] public int NumLocalAnim         { get; set; }
        [Category("5. Counts"), DisplayName("Sequences"),        ReadOnly(true)] public int NumLocalSeq          { get; set; }
        [Category("5. Counts"), DisplayName("Textures"),         ReadOnly(true)] public int NumTextures          { get; set; }
        [Category("5. Counts"), DisplayName("Skin Refs"),        ReadOnly(true)] public int NumSkinRef           { get; set; }
        [Category("5. Counts"), DisplayName("Skin Families"),    ReadOnly(true)] public int NumSkinFamilies      { get; set; }
        [Category("5. Counts"), DisplayName("Body Parts"),       ReadOnly(true)] public int NumBodyParts         { get; set; }
        [Category("5. Counts"), DisplayName("Attachments"),      ReadOnly(true)] public int NumLocalAttachments  { get; set; }
        [Category("5. Counts"), DisplayName("IK Chains"),        ReadOnly(true)] public int NumIKChains          { get; set; }
        [Category("5. Counts"), DisplayName("RUI Panels"),       ReadOnly(true)] public int UIPanelCount         { get; set; }
        [Category("5. Counts"), DisplayName("Bone Followers"),   ReadOnly(true)] public int BoneFollowerCount    { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Main form
    // ─────────────────────────────────────────────────────────────────────────
    public class MainForm : Form
    {
        // ── Controls ────────────────────────────────────────────────────────
        private readonly MenuStrip              _menu        = new();
        private readonly StatusStrip            _status      = new();
        private readonly ToolStripStatusLabel   _statusLabel = new();
        private readonly ThemedTabControl       _tabs        = new();

        // Header tab
        private readonly TabPage      _tabHeader    = new TabPage("Header");
        private readonly PropertyGrid _propGrid     = new();
        private HeaderEditModel?      _propGridModel;

        // Textures tab
        private readonly TabPage      _tabTextures  = new TabPage("Textures");
        private readonly DataGridView _texGrid      = new();

        // Materials tab
        private readonly TabPage      _tabMaterials = new TabPage("Material Types");
        private readonly DataGridView _matGrid      = new();

        // Body Parts tab
        private readonly TabPage      _tabBodyParts = new TabPage("Body Parts");
        private readonly TreeView     _bpTree       = new();
        private readonly Panel        _bpDetail     = new();

        // RUI Panels tab
        private readonly TabPage      _tabRui       = new TabPage("RUI Panels");
        private readonly DataGridView _ruiGrid      = new();

        // Bones tab
        private readonly TabPage      _tabBones     = new TabPage("Bones");
        private readonly DataGridView _boneGrid     = new();

        // Decompile tab
        private readonly TabPage     _tabDecompile = new TabPage("Decompile");
        private readonly TextBox     _decompFileBox = new();
        private readonly TextBox     _decompOutBox  = new();
        private readonly Button      _decompFileBtn = new() { Text = "Browse…", FlatStyle = FlatStyle.Flat };
        private readonly Button      _decompOutBtn  = new() { Text = "Browse…", FlatStyle = FlatStyle.Flat };
        private readonly Button      _decompBtn     = new() { Text = "Decompile", FlatStyle = FlatStyle.Flat };
        private readonly RichTextBox _decompLog     = new();

        // Preview tab
        private readonly TabPage          _tabPreview = new TabPage("Preview");
        private readonly MeshPreviewControl _preview  = new();

        // ── State ───────────────────────────────────────────────────────────
        private RMDLFile? _rmdl;
        private bool      _dirty;

        // ── Theme ───────────────────────────────────────────────────────────
        private bool                _darkMode   = false;
        private ToolStripMenuItem?  _darkItem;

        // ─────────────────────────────────────────────────────────────────────
        public MainForm()
        {
            Text          = "RMDLEdit";
            Size          = new Size(1100, 720);
            MinimumSize   = new Size(800, 550);
            StartPosition = FormStartPosition.CenterScreen;

            var appIcon = LoadEmbeddedIcon();
            Icon = appIcon ?? SystemIcons.Application;

            BuildMenu();
            BuildStatus();
            BuildTabs();
            BuildHeaderTab();
            BuildTexturesTab();
            BuildMaterialsTab();
            BuildBodyPartsTab();
            BuildRuiTab();
            BuildBonesTab();
            BuildDecompileTab();
            BuildPreviewTab();

            Controls.Add(_tabs);
            Controls.Add(_menu);
            Controls.Add(_status);

            SetStatus("Ready – open an RMDL v10 file to begin.");
            SetTabsEnabled(false);

            // Apply default (light) theme
            ApplyTheme(dark: false);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Menu
        // ─────────────────────────────────────────────────────────────────────
        private void BuildMenu()
        {
            _menu.Dock = DockStyle.Top;

            var fileMenu = new ToolStripMenuItem("File");
            AddItem(fileMenu, "Open…\tCtrl+O",  OnOpen);
            AddItem(fileMenu, "Save\tCtrl+S",   OnSave);
            AddItem(fileMenu, "Save As…",       OnSaveAs);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            AddItem(fileMenu, "Exit", (s, e) => Close());

            var editMenu = new ToolStripMenuItem("Edit");
            AddItem(editMenu, "Copy selected GUID",     OnCopyGuid);
            AddItem(editMenu, "Paste GUID to selected", OnPasteGuid);

            var viewMenu = new ToolStripMenuItem("View");
            _darkItem = new ToolStripMenuItem("Dark Mode") { CheckOnClick = true };
            _darkItem.Click += (s, e) => ToggleDarkMode();
            viewMenu.DropDownItems.Add(_darkItem);

            var helpMenu = new ToolStripMenuItem("Help");
            AddItem(helpMenu, "About", OnAbout);

            _menu.Items.Add(fileMenu);
            _menu.Items.Add(editMenu);
            _menu.Items.Add(viewMenu);
            _menu.Items.Add(helpMenu);

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.O) { e.Handled = true; OnOpen(s, e); }
                if (e.Control && e.KeyCode == Keys.S) { e.Handled = true; OnSave(s, e); }
            };
        }

        private static void AddItem(ToolStripMenuItem parent, string text, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += handler;
            parent.DropDownItems.Add(item);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Status bar
        // ─────────────────────────────────────────────────────────────────────
        private void BuildStatus()
        {
            _statusLabel.Spring    = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _status.Items.Add(_statusLabel);
            _status.Dock = DockStyle.Bottom;
        }

        private void SetStatus(string msg) => _statusLabel.Text = " " + msg;

        // ─────────────────────────────────────────────────────────────────────
        //  Tab container
        // ─────────────────────────────────────────────────────────────────────
        private void BuildTabs()
        {
            _tabs.Dock    = DockStyle.Fill;
            _tabs.Padding = new Point(8, 4);
            _tabs.TabPages.Add(_tabHeader);
            _tabs.TabPages.Add(_tabTextures);
            _tabs.TabPages.Add(_tabMaterials);
            _tabs.TabPages.Add(_tabBodyParts);
            _tabs.TabPages.Add(_tabRui);
            _tabs.TabPages.Add(_tabBones);
            _tabs.TabPages.Add(_tabPreview);
        }

        private void SetTabsEnabled(bool en)
        {
            foreach (TabPage tp in _tabs.TabPages)
            {
                if (tp == _tabPreview) continue; // always accessible
                tp.Enabled = en;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Dark mode
        // ─────────────────────────────────────────────────────────────────────
        private void ToggleDarkMode()
        {
            _darkMode = _darkItem?.Checked ?? !_darkMode;
            ApplyTheme(_darkMode);
        }

        private void ApplyTheme(bool dark)
        {
            _darkMode = dark;
            if (_darkItem != null) _darkItem.Checked = dark;

            // Colours for this pass
            Color bg       = dark ? Theme.DarkBg       : Theme.LightBg;
            Color surface  = dark ? Theme.DarkSurface  : Theme.LightSurface;
            Color elevated = dark ? Theme.DarkElevated : Theme.LightElevated;
            Color border   = dark ? Theme.DarkBorder   : Theme.LightBorder;
            Color text     = dark ? Theme.DarkText     : Theme.LightText;
            Color textDim  = dark ? Theme.DarkTextDim  : Theme.LightTextDim;
            Color sel      = dark ? Theme.DarkSel      : Theme.LightSel;
            Color selText  = dark ? Theme.DarkSelText  : Theme.LightSelText;

            // ── Form ─────────────────────────────────────────────────────────
            BackColor = bg;

            // ── Menu & status strip ───────────────────────────────────────────
            if (dark)
            {
                var renderer = new ToolStripProfessionalRenderer(new DarkColorTable())
                    { RoundedEdges = false };
                _menu.Renderer   = renderer;
                _status.Renderer = renderer;
            }
            else
            {
                _menu.RenderMode   = ToolStripRenderMode.ManagerRenderMode;
                _status.RenderMode = ToolStripRenderMode.ManagerRenderMode;
            }
            _menu.BackColor   = elevated;
            _menu.ForeColor   = text;
            _status.BackColor = elevated;
            _status.ForeColor = text;
            foreach (ToolStripItem it in _menu.Items)
                ApplyMenuItemTheme(it, text);
            _statusLabel.ForeColor = text;

            // ── Tabs ─────────────────────────────────────────────────────────
            _tabs.IsDark    = dark;
            _tabs.BackColor = surface;
            _tabs.Invalidate();
            foreach (TabPage tp in _tabs.TabPages)
            {
                tp.BackColor = surface;
                tp.ForeColor = text;
            }

            // ── PropertyGrid (Header tab) ─────────────────────────────────────
            _propGrid.BackColor          = bg;
            _propGrid.ViewBackColor      = surface;
            _propGrid.ViewForeColor      = text;
            _propGrid.LineColor          = border;
            _propGrid.CategoryForeColor  = dark ? Theme.DarkAccent : SystemColors.ActiveCaption;
            _propGrid.HelpBackColor      = elevated;
            _propGrid.HelpForeColor      = textDim;
            _propGrid.CommandsBackColor  = elevated;
            _propGrid.CommandsForeColor  = text;

            // ── DataGridViews ─────────────────────────────────────────────────
            foreach (var grid in new[] { _texGrid, _matGrid, _ruiGrid, _boneGrid })
                ApplyGridTheme(grid, surface, elevated, border, text, textDim, sel, selText);

            // ── TreeView ─────────────────────────────────────────────────────
            _bpTree.BackColor = surface;
            _bpTree.ForeColor = text;

            // ── Detail panel labels ───────────────────────────────────────────
            _bpDetail.BackColor = surface;
            foreach (Control c in _bpDetail.Controls)
            {
                c.BackColor = surface;
                c.ForeColor = text;
            }

            // ── All child panels / buttons / labels (walk tab page children) ──
            foreach (TabPage tp in _tabs.TabPages)
                ApplyControlsTheme(tp.Controls, bg, surface, elevated, border, text, textDim, sel, selText);

            // ── Preview control ───────────────────────────────────────────────
            _preview.ApplyTheme(dark);

            Invalidate(true);
        }

        private static void ApplyMenuItemTheme(ToolStripItem item, Color text)
        {
            item.ForeColor = text;
            if (item is ToolStripMenuItem mi)
                foreach (ToolStripItem child in mi.DropDownItems)
                    ApplyMenuItemTheme(child, text);
        }

        private static void ApplyGridTheme(DataGridView grid,
            Color surface, Color elevated, Color border,
            Color text, Color textDim, Color sel, Color selText)
        {
            grid.EnableHeadersVisualStyles            = false;
            grid.BackgroundColor                      = surface;
            grid.GridColor                            = border;
            grid.DefaultCellStyle.BackColor           = surface;
            grid.DefaultCellStyle.ForeColor           = text;
            grid.DefaultCellStyle.SelectionBackColor  = sel;
            grid.DefaultCellStyle.SelectionForeColor  = selText;
            grid.AlternatingRowsDefaultCellStyle.BackColor          = elevated;
            grid.AlternatingRowsDefaultCellStyle.ForeColor          = text;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = sel;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = selText;
            grid.ColumnHeadersDefaultCellStyle.BackColor = elevated;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = elevated;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = text;
            grid.RowHeadersDefaultCellStyle.BackColor = elevated;
            grid.RowHeadersDefaultCellStyle.ForeColor = text;
        }

        private static void ApplyControlsTheme(Control.ControlCollection controls,
            Color bg, Color surface, Color elevated, Color border,
            Color text, Color textDim, Color sel, Color selText)
        {
            foreach (Control c in controls)
            {
                switch (c)
                {
                    case Panel p:
                        p.BackColor = surface;
                        break;
                    case Label lbl:
                        lbl.BackColor = Color.Transparent;
                        lbl.ForeColor = lbl.ForeColor == Theme.DarkTextDim || lbl.ForeColor == Theme.LightTextDim || lbl.ForeColor == Color.DimGray || lbl.ForeColor == Color.Gray
                            ? textDim : text;
                        break;
                    case Button btn:
                        btn.BackColor = elevated;
                        btn.ForeColor = text;
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderColor = border;
                        break;
                    case RichTextBox rtb:
                        rtb.BackColor = surface;
                        rtb.ForeColor = text;
                        break;
                    case CheckedListBox clb:
                        clb.BackColor = surface;
                        clb.ForeColor = text;
                        break;
                    case TextBox tb:
                        tb.BackColor = surface;
                        tb.ForeColor = text;
                        tb.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case SplitContainer sc:
                        sc.BackColor = bg;
                        sc.Panel1.BackColor = surface;
                        sc.Panel2.BackColor = surface;
                        ApplyControlsTheme(sc.Panel1.Controls, bg, surface, elevated, border, text, textDim, sel, selText);
                        ApplyControlsTheme(sc.Panel2.Controls, bg, surface, elevated, border, text, textDim, sel, selText);
                        break;
                }
                // Recurse into non-special containers
                if (c is not DataGridView && c is not PropertyGrid &&
                    c is not TreeView     && c is not SplitContainer &&
                    c.Controls.Count > 0)
                {
                    ApplyControlsTheme(c.Controls, bg, surface, elevated, border, text, textDim, sel, selText);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Header tab  (PropertyGrid)
        // ─────────────────────────────────────────────────────────────────────
        private void BuildHeaderTab()
        {
            _propGrid.Dock           = DockStyle.Fill;
            _propGrid.PropertySort   = PropertySort.Categorized;
            _propGrid.ToolbarVisible = false;

            _propGrid.PropertyValueChanged += (s, e) =>
            {
                try { ApplyHeaderFromModel(); MarkDirty(); }
                catch { }
            };

            _tabHeader.Controls.Add(_propGrid);
        }

        private void PopulateHeaderTab()
        {
            var h = _rmdl!.Header;
            _propGridModel = new HeaderEditModel
            {
                Id                        = $"0x{h.Id:X8}",
                Version                   = h.Version,
                Checksum                  = $"0x{h.Checksum:X8}",
                InternalName              = h.InternalName,
                FileLength                = h.Length,
                EyePosition               = Vec3Str(h.EyePosition),
                IllumPosition             = Vec3Str(h.IllumPosition),
                HullMin                   = Vec3Str(h.HullMin),
                HullMax                   = Vec3Str(h.HullMax),
                ViewBBMin                 = Vec3Str(h.ViewBBMin),
                ViewBBMax                 = Vec3Str(h.ViewBBMax),
                Mass                      = h.Mass,
                Contents                  = h.Contents,
                DefaultFadeDist           = h.DefaultFadeDist,
                GatherSize                = h.GatherSize,
                Flags                     = $"0x{h.Flags:X8}",
                RootLOD                   = h.RootLOD,
                NumAllowedRootLODs        = h.NumAllowedRootLODs,
                ConstDirLightDot          = h.ConstDirLightDot,
                FlVertAnimFixedPointScale = h.FlVertAnimFixedPointScale,
                SurfacePropLookup         = h.SurfacePropLookup,
                NumBones                  = h.NumBones,
                NumBoneControllers        = h.NumBoneControllers,
                NumHitboxSets             = h.NumHitboxSets,
                NumLocalAnim              = h.NumLocalAnim,
                NumLocalSeq               = h.NumLocalSeq,
                NumTextures               = h.NumTextures,
                NumSkinRef                = h.NumSkinRef,
                NumSkinFamilies           = h.NumSkinFamilies,
                NumBodyParts              = h.NumBodyParts,
                NumLocalAttachments       = h.NumLocalAttachments,
                NumIKChains               = h.NumIKChains,
                UIPanelCount              = h.UIPanelCount,
                BoneFollowerCount         = h.BoneFollowerCount
            };
            _propGrid.SelectedObject = _propGridModel;
            // Re-apply theme so the new PropertyGrid state picks up current colours
            ApplyTheme(_darkMode);
        }

        private void ApplyHeaderFromModel()
        {
            if (_rmdl == null || _propGridModel == null) return;
            var h = _rmdl.Header;

            h.Mass                     = _propGridModel.Mass;
            h.Contents                 = _propGridModel.Contents;
            h.DefaultFadeDist          = _propGridModel.DefaultFadeDist;
            h.GatherSize               = _propGridModel.GatherSize;
            h.FlVertAnimFixedPointScale= _propGridModel.FlVertAnimFixedPointScale;
            h.SurfacePropLookup        = _propGridModel.SurfacePropLookup;
            h.RootLOD                  = _propGridModel.RootLOD;
            h.NumAllowedRootLODs       = _propGridModel.NumAllowedRootLODs;
            h.ConstDirLightDot         = _propGridModel.ConstDirLightDot;

            try
            {
                string f = _propGridModel.Flags.Replace("0x","").Replace("0X","").Trim();
                h.Flags = (int)Convert.ToUInt32(f, 16);
            }
            catch { }

            try { h.EyePosition   = ParseVec3(_propGridModel.EyePosition); }   catch { }
            try { h.IllumPosition = ParseVec3(_propGridModel.IllumPosition); }  catch { }
            try { h.HullMin       = ParseVec3(_propGridModel.HullMin); }        catch { }
            try { h.HullMax       = ParseVec3(_propGridModel.HullMax); }        catch { }
            try { h.ViewBBMin     = ParseVec3(_propGridModel.ViewBBMin); }      catch { }
            try { h.ViewBBMax     = ParseVec3(_propGridModel.ViewBBMax); }      catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Textures tab
        // ─────────────────────────────────────────────────────────────────────
        private void BuildTexturesTab()
        {
            _texGrid.Dock                  = DockStyle.Fill;
            _texGrid.AllowUserToAddRows    = false;
            _texGrid.AllowUserToDeleteRows = false;
            _texGrid.SelectionMode         = DataGridViewSelectionMode.FullRowSelect;
            _texGrid.MultiSelect           = false;
            _texGrid.AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill;
            _texGrid.RowHeadersVisible     = false;
            _texGrid.EditMode              = DataGridViewEditMode.EditOnKeystrokeOrF2;
            _texGrid.CellEndEdit           += OnTexCellEndEdit;

            _texGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#",           ReadOnly = true,  FillWeight = 5  });
            _texGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name",        ReadOnly = false, FillWeight = 45 });
            _texGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "GUID (hex)",  ReadOnly = false, FillWeight = 35 });
            _texGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File Offset", ReadOnly = true,  FillWeight = 15 });

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4, 4, 4, 4) };

            var computeBtn = new Button
            {
                Text      = "Compute GUID from Name",
                Dock      = DockStyle.Left,
                Width     = 190,
                FlatStyle = FlatStyle.Flat
            };
            computeBtn.Click += OnComputeGuidClick;

            var hint = new Label
            {
                Text      = "  Name edits are in-place (truncates if longer than original).  GUID = HashString(\"material/<name>.rpak\").",
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            };

            btnPanel.Controls.Add(hint);
            btnPanel.Controls.Add(computeBtn);

            _tabTextures.Controls.Add(_texGrid);
            _tabTextures.Controls.Add(btnPanel);
        }

        private void PopulateTexturesTab()
        {
            _texGrid.Rows.Clear();
            for (int i = 0; i < _rmdl!.Textures.Count; i++)
            {
                var t = _rmdl.Textures[i];
                _texGrid.Rows.Add(i, t.Name, $"0x{t.TextureGuid:X16}", $"0x{t.FileOffset:X}");
            }
        }

        private void OnTexCellEndEdit(object? s, DataGridViewCellEventArgs e)
        {
            if (_rmdl == null || e.RowIndex < 0) return;

            if (e.ColumnIndex == 1) // Name
            {
                var cell = _texGrid.Rows[e.RowIndex].Cells[1];
                if (cell.Value == null) return;
                string newName = cell.Value.ToString()!.Trim();
                string written = _rmdl.EditTextureName(e.RowIndex, newName);
                cell.Value = written;
                if (written != newName)
                    SetStatus($"Name truncated to {written.Length} chars (original length limit).");
                MarkDirty();
            }
            else if (e.ColumnIndex == 2) // GUID
            {
                var cell = _texGrid.Rows[e.RowIndex].Cells[2];
                if (cell.Value == null) return;
                try
                {
                    string raw  = cell.Value.ToString()!.Replace("0x","").Replace("0X","").Trim();
                    ulong  guid = Convert.ToUInt64(raw, 16);
                    _rmdl.Textures[e.RowIndex].TextureGuid = guid;
                    cell.Value = $"0x{guid:X16}";
                    MarkDirty();
                }
                catch
                {
                    MessageBox.Show("Invalid GUID hex value.", "Parse Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnComputeGuidClick(object? s, EventArgs e)
        {
            if (_rmdl == null) return;
            int rowIdx = _texGrid.CurrentRow?.Index ?? -1;
            if (rowIdx < 0 || rowIdx >= _rmdl.Textures.Count) return;

            string name = _rmdl.Textures[rowIdx].Name;
            if (string.IsNullOrEmpty(name))
            {
                SetStatus("Select a texture row with a non-empty name first.");
                return;
            }

            ulong guid = RMDLFile.ComputeTextureGuid(name);
            _rmdl.Textures[rowIdx].TextureGuid   = guid;
            _texGrid.Rows[rowIdx].Cells[2].Value = $"0x{guid:X16}";
            MarkDirty();
            SetStatus($"Computed GUID for \"{name}\":  0x{guid:X16}");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Material Types tab
        // ─────────────────────────────────────────────────────────────────────
        private void BuildMaterialsTab()
        {
            _matGrid.Dock                  = DockStyle.Fill;
            _matGrid.AllowUserToAddRows    = false;
            _matGrid.AllowUserToDeleteRows = false;
            _matGrid.SelectionMode         = DataGridViewSelectionMode.FullRowSelect;
            _matGrid.AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill;
            _matGrid.RowHeadersVisible     = false;
            _matGrid.CellEndEdit           += OnMatCellEndEdit;

            _matGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#",           ReadOnly = true,  FillWeight = 5  });
            _matGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Texture",     ReadOnly = true,  FillWeight = 45 });
            _matGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type (byte)", ReadOnly = false, FillWeight = 15 });
            _matGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type Name",   ReadOnly = true,  FillWeight = 25 });
            _matGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Offset",      ReadOnly = true,  FillWeight = 10 });

            var info = new Panel { Dock = DockStyle.Bottom, Height = 32 };
            info.Controls.Add(new Label
            {
                Text      = "  SKNP = 4 (skinned prop)   RGDP = 7 (rigid prop).  Press F2 or start typing to edit the Type cell.",
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            });

            _tabMaterials.Controls.Add(_matGrid);
            _tabMaterials.Controls.Add(info);
        }

        private void PopulateMaterialsTab()
        {
            _matGrid.Rows.Clear();
            for (int i = 0; i < _rmdl!.Materials.Count; i++)
            {
                var    m       = _rmdl.Materials[i];
                string texName = i < _rmdl.Textures.Count ? _rmdl.Textures[i].Name : "";
                _matGrid.Rows.Add(i, texName, m.TypeValue, m.TypeName, $"0x{m.FileOffset:X}");
            }
        }

        private void OnMatCellEndEdit(object? s, DataGridViewCellEventArgs e)
        {
            if (_rmdl == null || e.ColumnIndex != 2 || e.RowIndex < 0) return;
            var cell = _matGrid.Rows[e.RowIndex].Cells[2];
            if (cell.Value == null) return;
            try
            {
                byte val = byte.Parse(cell.Value.ToString()!);
                _rmdl.Materials[e.RowIndex].TypeValue    = val;
                _matGrid.Rows[e.RowIndex].Cells[3].Value = _rmdl.Materials[e.RowIndex].TypeName;
                MarkDirty();
            }
            catch
            {
                MessageBox.Show("Invalid byte value (0–255).", "Parse Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Body Parts tab
        // ─────────────────────────────────────────────────────────────────────
        private void BuildBodyPartsTab()
        {
            var split = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                SplitterDistance = 260,
                Orientation      = Orientation.Horizontal
            };

            _bpTree.Dock        = DockStyle.Fill;
            _bpTree.AfterSelect += OnBpTreeSelect;
            split.Panel1.Controls.Add(_bpTree);

            _bpDetail.Dock       = DockStyle.Fill;
            _bpDetail.AutoScroll = true;
            split.Panel2.Controls.Add(_bpDetail);

            _tabBodyParts.Controls.Add(split);
        }

        private void PopulateBodyPartsTab()
        {
            _bpTree.Nodes.Clear();
            foreach (var bp in _rmdl!.BodyParts)
            {
                var bpNode = new TreeNode($"[BodyPart]  {bp.Name}") { Tag = bp };
                foreach (var mdl in bp.Models)
                {
                    var mdlNode = new TreeNode($"[Model]  {mdl.Name}  ({mdl.NumVertices:N0} verts)") { Tag = mdl };
                    foreach (var mesh in mdl.Meshes)
                        mdlNode.Nodes.Add(new TreeNode($"[Mesh]  mat={mesh.Material}  verts={mesh.NumVertices:N0}  id={mesh.MeshId}") { Tag = mesh });
                    bpNode.Nodes.Add(mdlNode);
                }
                _bpTree.Nodes.Add(bpNode);
            }
            _bpTree.ExpandAll();
        }

        private void OnBpTreeSelect(object? s, TreeViewEventArgs e)
        {
            _bpDetail.Controls.Clear();
            if (e.Node?.Tag == null) return;

            var lines = new List<string>();
            if (e.Node.Tag is RMDLBodyPart bp)
            {
                lines.Add($"Name:         {bp.Name}");
                lines.Add($"Base:         {bp.Base}");
                lines.Add($"Models:       {bp.Models.Count}");
                lines.Add($"File Offset:  0x{bp.FileOffset:X}");
            }
            else if (e.Node.Tag is RMDLModel mdl)
            {
                lines.Add($"Name:            {mdl.Name}");
                lines.Add($"Vertices:        {mdl.NumVertices:N0}");
                lines.Add($"Bounding Radius: {mdl.BoundRadius:G}");
                lines.Add($"Meshes:          {mdl.Meshes.Count}");
            }
            else if (e.Node.Tag is RMDLMesh mesh)
            {
                lines.Add($"Material Index:  {mesh.Material}");
                lines.Add($"Vertices:        {mesh.NumVertices:N0}");
                lines.Add($"Vertex Offset:   {mesh.VertexOffset}");
                lines.Add($"Mesh ID:         {mesh.MeshId}");
                lines.Add($"Center:          {mesh.CenterX:G}  {mesh.CenterY:G}  {mesh.CenterZ:G}");
                lines.Add($"File Offset:     0x{mesh.FileOffset:X}");
            }

            Color fg = _darkMode ? Theme.DarkText : Theme.LightText;
            Color bg = _darkMode ? Theme.DarkSurface : Theme.LightSurface;
            int y = 8;
            foreach (var line in lines)
            {
                _bpDetail.Controls.Add(new Label
                {
                    Text      = line,
                    Location  = new Point(8, y),
                    Size      = new Size(500, 18),
                    AutoSize  = false,
                    Font      = new Font("Consolas", 9f),
                    ForeColor = fg,
                    BackColor = bg
                });
                y += 22;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  RUI Panels tab
        // ─────────────────────────────────────────────────────────────────────
        private void BuildRuiTab()
        {
            _ruiGrid.Dock                  = DockStyle.Fill;
            _ruiGrid.AllowUserToAddRows    = false;
            _ruiGrid.AllowUserToDeleteRows = false;
            _ruiGrid.SelectionMode         = DataGridViewSelectionMode.FullRowSelect;
            _ruiGrid.AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill;
            _ruiGrid.RowHeadersVisible     = false;
            _ruiGrid.ReadOnly              = true;

            foreach (var h in new[] { "#", "Name Hash", "Num Parents", "Num Verts", "Num Faces",
                                       "Parent Idx", "Vertex Idx", "Vert Map Idx", "Face Data Idx", "Hdr Offset" })
                _ruiGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h });

            _tabRui.Controls.Add(_ruiGrid);
        }

        private void PopulateRuiTab()
        {
            _ruiGrid.Rows.Clear();
            for (int i = 0; i < _rmdl!.RuiPanels.Count; i++)
            {
                var r = _rmdl.RuiPanels[i];
                _ruiGrid.Rows.Add(i, $"0x{r.NameHash:X8}",
                    r.NumParents, r.NumVertices, r.NumFaces,
                    r.ParentIndex, r.VertexIndex, r.VertMapIndex, r.FaceDataIndex,
                    $"0x{r.FileOffset:X}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Bones tab
        // ─────────────────────────────────────────────────────────────────────
        private void BuildBonesTab()
        {
            _boneGrid.Dock                  = DockStyle.Fill;
            _boneGrid.AllowUserToAddRows    = false;
            _boneGrid.AllowUserToDeleteRows = false;
            _boneGrid.SelectionMode         = DataGridViewSelectionMode.FullRowSelect;
            _boneGrid.AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill;
            _boneGrid.RowHeadersVisible     = false;
            _boneGrid.ReadOnly              = true;

            foreach (var h in new[] { "#", "Name", "Parent",
                                       "Pos X", "Pos Y", "Pos Z",
                                       "Rot X", "Rot Y", "Rot Z",
                                       "Flags", "Phys Bone", "Offset" })
                _boneGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h });

            _tabBones.Controls.Add(_boneGrid);
        }

        private void PopulateBonesTab()
        {
            _boneGrid.Rows.Clear();
            for (int i = 0; i < _rmdl!.Bones.Count; i++)
            {
                var b = _rmdl.Bones[i];
                _boneGrid.Rows.Add(i, b.Name, b.Parent,
                    b.PosX.ToString("G4"), b.PosY.ToString("G4"), b.PosZ.ToString("G4"),
                    b.RotX.ToString("G4"), b.RotY.ToString("G4"), b.RotZ.ToString("G4"),
                    $"0x{b.Flags:X}", b.PhysicsBone,
                    $"0x{b.FileOffset:X}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  File actions
        // ─────────────────────────────────────────────────────────────────────
        private void OnOpen(object? s, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Open RMDL v10 File",
                Filter = "RMDL Files (*.rmdl)|*.rmdl|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            LoadFile(dlg.FileName);
        }

        private void LoadFile(string path)
        {
            try
            {
                _rmdl  = RMDLFile.Load(path);
                _dirty = false;
                Text   = $"RMDLEdit – {Path.GetFileName(path)}";
                SetStatus($"Loaded: {path}   ({_rmdl.RawData.Length:N0} bytes)");
                SetTabsEnabled(true);
                PopulateAllTabs();
                try { _preview.LoadMesh(_rmdl); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load file:\n{ex.Message}", "Load Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateAllTabs()
        {
            PopulateHeaderTab();
            PopulateTexturesTab();
            PopulateMaterialsTab();
            PopulateBodyPartsTab();
            PopulateRuiTab();
            PopulateBonesTab();
        }

        private void OnSave(object? s, EventArgs e)
        {
            if (_rmdl == null) return;
            try
            {
                _rmdl.Save(_rmdl.FilePath);
                _dirty = false;
                Text   = Text.TrimEnd('*').TrimEnd();
                SetStatus($"Saved: {_rmdl.FilePath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save:\n{ex.Message}", "Save Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSaveAs(object? s, EventArgs e)
        {
            if (_rmdl == null) return;
            using var dlg = new SaveFileDialog
            {
                Title    = "Save RMDL As",
                Filter   = "RMDL Files (*.rmdl)|*.rmdl|All Files (*.*)|*.*",
                FileName = Path.GetFileName(_rmdl.FilePath)
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                _rmdl.Save(dlg.FileName);
                _dirty = false;
                Text   = $"RMDLEdit – {Path.GetFileName(dlg.FileName)}";
                SetStatus($"Saved: {dlg.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save:\n{ex.Message}", "Save Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Decompile tab
        // ─────────────────────────────────────────────────────────────────────
        private void BuildDecompileTab()
        {
            _tabDecompile.Padding = new Padding(8);

            // ── Input table (file + output folder rows) ───────────────────────
            var table = new TableLayoutPanel
            {
                Dock        = DockStyle.Top,
                ColumnCount = 3,
                RowCount    = 2,
                Height      = 76,
                Padding     = new Padding(8, 10, 8, 4),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            var lblFile = new Label { Text = "RMDL file:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _decompFileBox.Dock             = DockStyle.Fill;
            _decompFileBox.PlaceholderText  = "Select an RMDL v10 file…";
            _decompFileBox.TextChanged     += (s, e) => AutoFillOutputDir();
            _decompFileBtn.Dock             = DockStyle.Fill;
            _decompFileBtn.Click           += OnDecompFileBrowse;

            var lblOut = new Label { Text = "Output folder:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _decompOutBox.Dock            = DockStyle.Fill;
            _decompOutBox.PlaceholderText = "Default: <exe>/decompiled/<model name>/";
            _decompOutBtn.Dock            = DockStyle.Fill;
            _decompOutBtn.Click          += OnDecompOutBrowse;

            table.Controls.Add(lblFile,       0, 0);
            table.Controls.Add(_decompFileBox, 1, 0);
            table.Controls.Add(_decompFileBtn, 2, 0);
            table.Controls.Add(lblOut,         0, 1);
            table.Controls.Add(_decompOutBox,  1, 1);
            table.Controls.Add(_decompOutBtn,  2, 1);

            // ── Decompile button row ──────────────────────────────────────────
            var btnPanel = new Panel { Dock = DockStyle.Top, Height = 46 };
            _decompBtn.Location  = new Point(8, 8);
            _decompBtn.Size      = new Size(130, 30);
            _decompBtn.Click    += OnDecompileExecute;
            btnPanel.Controls.Add(_decompBtn);

            // ── Log output ────────────────────────────────────────────────────
            _decompLog.Dock        = DockStyle.Fill;
            _decompLog.ReadOnly    = true;
            _decompLog.Font        = new Font("Consolas", 9f);
            _decompLog.ScrollBars  = RichTextBoxScrollBars.Vertical;
            _decompLog.BorderStyle = BorderStyle.FixedSingle;
            _decompLog.WordWrap    = false;

            // Controls added bottom-to-top (Fill before Top panels)
            _tabDecompile.Controls.Add(_decompLog);
            _tabDecompile.Controls.Add(btnPanel);
            _tabDecompile.Controls.Add(table);
        }

        private void BuildPreviewTab()
        {
            _preview.Dock = DockStyle.Fill;
            _tabPreview.Controls.Add(_preview);
        }

        private void AutoFillOutputDir()
        {
            string path = _decompFileBox.Text.Trim();
            if (string.IsNullOrEmpty(path)) return;

            // Only auto-fill if the output box is empty or still matches the
            // previously auto-generated path (i.e. user hasn't customised it).
            string name       = Path.GetFileNameWithoutExtension(path);
            string autoPath   = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "decompiled", name);
            string currentOut = _decompOutBox.Text.Trim();

            if (string.IsNullOrEmpty(currentOut) ||
                currentOut.StartsWith(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "decompiled"),
                    StringComparison.OrdinalIgnoreCase))
            {
                _decompOutBox.Text = autoPath;
            }
        }

        private void OnDecompFileBrowse(object? s, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Select RMDL File",
                Filter = "RMDL Files (*.rmdl)|*.rmdl|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            _decompFileBox.Text = dlg.FileName;
        }

        private void OnDecompOutBrowse(object? s, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description         = "Choose output folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (!string.IsNullOrEmpty(_decompOutBox.Text))
                dlg.InitialDirectory = _decompOutBox.Text;
            if (dlg.ShowDialog() != DialogResult.OK) return;
            _decompOutBox.Text = dlg.SelectedPath;
        }

        private void OnDecompileExecute(object? s, EventArgs e)
        {
            var warn = MessageBox.Show(
                "The decompiler is unfinished and will likely not work correctly for complex models.\n\n" +
                "Multi-weight skinning, LODs, and certain vertex formats may produce broken or missing geometry.\n\n" +
                "Continue anyway?",
                "Decompiler Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (warn != DialogResult.Yes) return;

            string rmdlPath = _decompFileBox.Text.Trim();
            if (string.IsNullOrEmpty(rmdlPath))
            {
                DecompLog("[Error] No RMDL file specified.");
                return;
            }
            if (!File.Exists(rmdlPath))
            {
                DecompLog($"[Error] File not found: {rmdlPath}");
                return;
            }

            string outDir = _decompOutBox.Text.Trim();
            if (string.IsNullOrEmpty(outDir))
            {
                string mdlName = Path.GetFileNameWithoutExtension(rmdlPath);
                outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "decompiled", mdlName);
            }

            _decompLog.Clear();
            DecompLog($"Loading {Path.GetFileName(rmdlPath)}…");

            try
            {
                var rmdl = RMDLFile.Load(rmdlPath);
                DecompLog($"  {rmdl.Header.NumBones} bone(s)  " +
                           $"{rmdl.Header.NumBodyParts} body part(s)  " +
                           $"{rmdl.Header.NumTextures} texture(s)");
                DecompLog("Decompiling…");

                string result = RMDLDecompiler.Decompile(rmdl, outDir);
                DecompLog(result);
                DecompLog("Done.");
                SetStatus($"Decompiled → {outDir}");
            }
            catch (Exception ex)
            {
                DecompLog($"[Error] {ex.Message}");
                SetStatus("Decompile failed.");
            }
        }

        private void DecompLog(string msg)
        {
            _decompLog.AppendText(msg + Environment.NewLine);
            _decompLog.ScrollToCaret();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Edit actions
        // ─────────────────────────────────────────────────────────────────────
        private void OnCopyGuid(object? s, EventArgs e)
        {
            if (_texGrid.CurrentRow == null) return;
            var cell = _texGrid.CurrentRow.Cells[2];
            if (cell.Value != null)
                Clipboard.SetText(cell.Value.ToString() ?? "");
        }

        private void OnPasteGuid(object? s, EventArgs e)
        {
            if (_texGrid.CurrentRow == null || _rmdl == null) return;
            try
            {
                string raw  = Clipboard.GetText().Trim().Replace("0x","").Replace("0X","");
                ulong  guid = Convert.ToUInt64(raw, 16);
                int    ri   = _texGrid.CurrentRow.Index;
                _rmdl.Textures[ri].TextureGuid         = guid;
                _texGrid.CurrentRow.Cells[2].Value     = $"0x{guid:X16}";
                MarkDirty();
            }
            catch
            {
                MessageBox.Show("Clipboard doesn't contain a valid hex GUID.", "Paste Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  About dialog
        // ─────────────────────────────────────────────────────────────────────
        private void OnAbout(object? s, EventArgs e)
        {
            Color dlgBg   = _darkMode ? Theme.DarkBg      : SystemColors.Control;
            Color dlgText = _darkMode ? Theme.DarkText     : SystemColors.ControlText;
            Color dlgDim  = _darkMode ? Theme.DarkTextDim  : Color.Gray;

            using var dlg = new Form
            {
                Text            = "About RMDLEdit",
                Size            = new Size(400, 310),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox     = false,
                MinimizeBox     = false,
                StartPosition   = FormStartPosition.CenterParent,
                BackColor       = dlgBg,
                Icon            = Icon
            };

            Bitmap? logo = null;
            int     topY = 12;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                // Try editlogo.png first, then icon.png
                var stream = asm.GetManifestResourceStream("RMDLEditor.src.editlogo.png")
                          ?? asm.GetManifestResourceStream("RMDLEditor.src.icon.png");
                if (stream != null)
                {
                    using (stream)
                        logo = new Bitmap(stream);
                    var pic = new PictureBox
                    {
                        Image    = logo,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Size     = new Size(72, 72),
                        Location = new Point((400 - 72) / 2, topY),
                        BackColor= Color.Transparent
                    };
                    dlg.Controls.Add(pic);
                    topY += 80;
                }
            }
            catch { }

            void AddLabel(string t, int y, float sz = 9f,
                          FontStyle st = FontStyle.Regular, Color? col = null)
            {
                dlg.Controls.Add(new Label
                {
                    Text      = t,
                    AutoSize  = false,
                    Location  = new Point(0, y),
                    Size      = new Size(400, 28),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font      = new Font("Segoe UI", sz, st),
                    ForeColor = col ?? dlgText,
                    BackColor = Color.Transparent
                });
            }

            AddLabel("RMDLEdit",  topY,       18f, FontStyle.Bold);
            AddLabel("Apex Legends RMDL v10 editor  (studio version 54)", topY + 32, 8.5f);
            AddLabel("Edits textures, material types, header fields, and more.", topY + 52, 8f, FontStyle.Regular, dlgDim);
            AddLabel("© WateryContinent02", topY + 82, 9.5f, FontStyle.Regular, dlgDim);

            var ok = new Button
            {
                Text         = "OK",
                DialogResult = DialogResult.OK,
                Size         = new Size(80, 28),
                Location     = new Point(160, topY + 118),
                FlatStyle    = FlatStyle.Flat,
                BackColor    = _darkMode ? Theme.DarkElevated : SystemColors.ButtonFace,
                ForeColor    = dlgText
            };
            if (_darkMode)
                ok.FlatAppearance.BorderColor = Theme.DarkBorder;

            dlg.Controls.Add(ok);
            dlg.AcceptButton = ok;
            dlg.ShowDialog(this);
            logo?.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Dirty / close tracking
        // ─────────────────────────────────────────────────────────────────────
        private void MarkDirty()
        {
            if (!_dirty)
            {
                _dirty = true;
                if (!Text.EndsWith("*")) Text += " *";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty)
            {
                var r = MessageBox.Show("You have unsaved changes. Save before closing?",
                    "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes)       OnSave(null, EventArgs.Empty);
                else if (r == DialogResult.Cancel) e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Static helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads the app icon from embedded resources.
        /// Tries editlogo.png first, then icon.png. Returns null if neither is found.
        /// </summary>
        private static Icon? LoadEmbeddedIcon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var stream = asm.GetManifestResourceStream("RMDLEditor.src.editlogo.png")
                          ?? asm.GetManifestResourceStream("RMDLEditor.src.icon.png");
                if (stream == null) return null;
                using (stream)
                using (var bmp = new Bitmap(stream))
                    return Icon.FromHandle(bmp.GetHicon());
            }
            catch { return null; }
        }

        private static string Vec3Str(float[] v) =>
            v.Length >= 3 ? $"{v[0]:G} {v[1]:G} {v[2]:G}" : "0 0 0";

        private static float[] ParseVec3(string s)
        {
            var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return new float[3];
            return new[] { float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]) };
        }
    }
}
