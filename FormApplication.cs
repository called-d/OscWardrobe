using System.Resources;

class FormApplication {
    System.Threading.Thread applicationThread;
    System.Drawing.Icon icon;
    NotifyIcon nofifyIcon;

    private static System.IO.Stream GetStream(string name) {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var name_ = $"{assembly.GetName().Name}.{name}";
        return assembly.GetManifestResourceStream(name_) ?? throw new System.Exception($"Resource {name_} not found");
    }

    public static bool ExtractLua(bool force = false) {
        var dirName = "lua";
        if (force) {
            var i = 0;
            while (System.IO.Directory.Exists(dirName)) {
                dirName = $"lua.{i++}";
            }
        } else {
            if (System.IO.Directory.Exists("lua")) return false;
        }
        System.IO.Compression.ZipFile.ExtractToDirectory(
            GetStream("Lua.zip"),
            dirName
        );
        return true;
    }

    public FormApplication() {
        applicationThread = new (() => {
            var executablePath = System.Environment.ProcessPath;
            nofifyIcon = new NotifyIcon {
                Icon = (icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath)),
                Visible = true,
                Text = "OSC Wardrobe",
                ContextMenuStrip = CreateContextMenu(),
            };
            nofifyIcon.MouseClick += (_sender, _e) => nofifyIcon.ContextMenuStrip.Show(Cursor.Position);

            Application.Run();
        });
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        applicationThread.SetApartmentState(System.Threading.ApartmentState.STA);
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
            Size = new System.Drawing.Size(900, 600),
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
            Location = new System.Drawing.Point(0, y),
        });
        l.Font = new System.Drawing.Font(l.Font.Name, l.Font.Size * 2, System.Drawing.FontStyle.Bold);
        y += l.Height + gap;
        panel.Controls.Add(l = new Label {
            AutoSize = true,
            Text = license,
            Location = new System.Drawing.Point(0, y),
        });
        y += l.Height + gap;
        panel.Controls.Add(l = new Label {
            AutoSize = true,
            Text = "Third Party Licenses",
            Location = new System.Drawing.Point(0, y),
        });
        l.Font = new System.Drawing.Font(l.Font.Name, l.Font.Size * 2, System.Drawing.FontStyle.Bold);
        y += l.Height + gap;
        panel.Controls.Add(l = new Label {
            AutoSize = true,
            Text = thirdPartyLicenses,
            Location = new System.Drawing.Point(0, y),
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
        nofifyIcon.Dispose();
    }
}
