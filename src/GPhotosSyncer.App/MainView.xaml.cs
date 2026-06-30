using System.Linq;
using GPhotosSyncer.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace GPhotosSyncer.App;

public sealed partial class MainView : UserControl
{
    public MainViewModel Vm { get; } = new();

    public MainView()
    {
        InitializeComponent();
        RootGrid.DataContext = Vm; // enables the ElementName binding used by the "Quitar" buttons
    }

    private void OnSourcesDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (e.DragUIOverride is not null)
            {
                e.DragUIOverride.Caption = "Añadir como origen";
                e.DragUIOverride.IsContentVisible = true;
            }
        }
    }

    private async void OnSourcesDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var folders = items.OfType<StorageFolder>().Select(f => f.Path).ToList();
            if (folders.Count > 0) Vm.AddFromPaths(folders);
        }
        catch { /* ignore malformed drops */ }
        finally { deferral.Complete(); }
    }
}
