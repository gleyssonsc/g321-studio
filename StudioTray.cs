using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace G321Studio;

internal sealed class StudioTray : IDisposable
{
    private readonly Drawing.Icon image;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Forms.NotifyIcon notifyIcon;

    public StudioTray(Action open, Action exit)
    {
        var resource = WpfApplication.GetResourceStream(new Uri("/Assets/G321.ico", UriKind.Relative))
            ?? throw new InvalidOperationException("O ícone do G321 Studio não foi encontrado.");
        using var stream = resource.Stream;
        using var asset = new Drawing.Icon(stream);
        image = (Drawing.Icon)asset.Clone();

        Forms.ContextMenuStrip? createdMenu = null;
        Forms.NotifyIcon? createdIcon = null;
        try
        {
            createdMenu = new Forms.ContextMenuStrip();
            createdMenu.Items.Add("Abrir G321 Studio", null, (_, _) => open());
            createdMenu.Items.Add("Sair", null, (_, _) => exit());

            createdIcon = new Forms.NotifyIcon();
            createdIcon.Icon = image;
            createdIcon.Text = "G321 Studio";
            createdIcon.ContextMenuStrip = createdMenu;
            createdIcon.MouseDoubleClick += (_, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left)
                    open();
            };
            createdIcon.Visible = true;

            menu = createdMenu;
            notifyIcon = createdIcon;
        }
        catch
        {
            createdIcon?.Dispose();
            createdMenu?.Dispose();
            image.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        notifyIcon.Dispose();
        menu.Dispose();
        image.Dispose();
    }
}
