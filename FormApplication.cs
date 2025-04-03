class FormApplication {
    System.Threading.Thread applicationThread;
    NotifyIcon icon;

    public FormApplication() {
        applicationThread = new (() => {
            var executablePath = System.Environment.ProcessPath;
            icon = new NotifyIcon {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath),
                Visible = true,
                Text = "OSC Wardrobe",
                ContextMenuStrip = CreateContextMenu(),
            };
            icon.MouseClick += (_sender, _e) => icon.ContextMenuStrip.Show(Cursor.Position);

            Application.Run();
        });
        applicationThread.SetApartmentState(System.Threading.ApartmentState.STA);
    }

    public ContextMenuStrip CreateContextMenu() {
        var menu = new ContextMenuStrip();
        var menuItem = new ToolStripMenuItem(
            "&Exit", null, (_s, _e) => ThreadEvents.Exit.Set(), "Exit"
        );
        menu.Items.Add(menuItem);
        return menu;
    }

    public void Start() {
        applicationThread.Start();
    }
    public void Dispose() {
        Application.Exit();
        icon.Dispose();
    }
}
