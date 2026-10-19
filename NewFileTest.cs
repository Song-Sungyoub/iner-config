using System.Windows;
using System.Windows.Controls;
namespace InerConfig;

public partial class MainWindow
{
    private async Task TestNewFiles()
    {
        foreach(ItemsControl menu in new ItemsControl[]{new MenuItem(),new ContextMenu()})
        {
            PopulateNewFileMenu(menu);
            if(menu.Items.Count!=5)throw new Exception("New file formats missing");
            foreach(MenuItem item in menu.Items)
            {
                int count=sessions.Count;
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent,item));
                var created=Session;
                await Parse();
                if(sessions.Count!=count+1||created==null||created.Format!=(string)item.Header||created.FilePath!=null||!created.Dirty||document==null)
                    throw new Exception("New file creation failed: "+item.Header);
                sessions.Remove(created);
            }
        }
    }
}
