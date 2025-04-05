
class FormApplication {
    readonly Thread applicationThread;
    readonly Icon icon;
    NotifyIcon? nofifyIcon;

    private static Stream GetStream(string name) {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var name_ = $"{assembly.GetName().Name}.{name}";
        return assembly.GetManifestResourceStream(name_) ?? throw new Exception($"Resource {name_} not found");
    }

    public static bool ExtractLua(string workDir, bool force = false) {
        var dirName = "lua";
        if (force) {
            var i = 0;
            while (Directory.Exists(Path.Combine(workDir, dirName))) {
                dirName = $"lua.{i++}";
            }
        } else {
            if (Directory.Exists(Path.Combine(workDir, dirName))) return false;
        }
        System.IO.Compression.ZipFile.ExtractToDirectory(
            GetStream("Lua.zip"),
            Path.Combine(workDir, dirName)
        );
        return true;
    }

    public FormApplication() {
        var executablePath = Environment.ProcessPath ?? throw new Exception("ProcessPath is null");
        icon = Icon.ExtractAssociatedIcon(executablePath) ?? throw new Exception("Icon not found");
        applicationThread = new (() => {
            nofifyIcon = new NotifyIcon {
                Icon = icon,
                Visible = true,
                Text = "OSC Wardrobe",
                ContextMenuStrip = CreateContextMenu(),
            };
            nofifyIcon.MouseClick += (_sender, _e) => nofifyIcon.ContextMenuStrip.Show(Cursor.Position);

            Application.Run();
        });
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        applicationThread.SetApartmentState(ApartmentState.STA);
    }

    private void ShowLicenseWindow() {
        string license;
        string thirdPartyLicenses;
        using (var sr = new StreamReader(GetStream("LICENSE.md"))) {
            license = sr.ReadToEnd();
        }
        using (var sr = new StreamReader(GetStream("THIRD-PARTY-LICENSES.md"))) {
            thirdPartyLicenses = sr.ReadToEnd();
        }

        var form = new Form {
            FormBorderStyle = FormBorderStyle.Sizable,
            Text = "Licenses",
            Size = new Size(900, 600),
            StartPosition = FormStartPosition.CenterScreen,
            Icon = icon,
        };
        var panel = new Panel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };
        panel.SuspendLayout();

        var gap = 10;
        var y = 0;
        Label l;
        panel.Controls.Add(l = new Label {
            AutoSize = true,
            Text = "License",
            Location = new Point(0, y),
        });
        l.Font = new Font(l.Font.Name, l.Font.Size * 2, FontStyle.Bold);
        y += l.Height + gap;
        panel.Controls.Add(l = new Label {
            AutoSize = true,
            Text = license,
            Location = new Point(0, y),
        });
        y += l.Height + gap;
        panel.Controls.Add(l = new Label {
            AutoSize = true,
            Text = "Third Party Licenses",
            Location = new Point(0, y),
        });
        l.Font = new Font(l.Font.Name, l.Font.Size * 2, FontStyle.Bold);
        y += l.Height + gap;
        panel.Controls.Add(l = new Label {
            AutoSize = true,
            Text = thirdPartyLicenses,
            Location = new Point(0, y),
        });
        y += l.Height + gap;

        panel.ResumeLayout(false);
        form.Controls.Add(panel);
        form.Show();
    }

    public ContextMenuStrip CreateContextMenu() {
        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateLuaMenu());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(
            "Show License", null, (_s, _e) => ShowLicenseWindow(), "Show License"
        ));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(
            "&Exit", null, (_s, _e) => ThreadEvents.Exit.Set(), "Exit"
        ));
        return menu;
    }
    private ToolStripMenuItem CreateLuaMenu() {
        var menu = new ToolStripMenuItem("Lua");
        menu.DropDownItems.Add(new ToolStripMenuItem(
            "Extract Default Lua Files", null, (_s, _e) => ThreadEvents.ExtactLua.Set(), "Extract Default Lua Files"
        ));
        return menu;
    }

    public void Start() {
        applicationThread.Start();
    }
    public void Dispose() {
        Application.Exit();
        icon.Dispose();
        nofifyIcon?.Dispose();
    }
}
