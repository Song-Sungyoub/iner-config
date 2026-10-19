using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace InerConfig;

public partial class MainWindow
{
    internal Window? ReferenceEditor { get; private set; }
    private Task<bool> ShowReferenceEdit(EditDialog dialog)=>ShowReferenceWindow(dialog,dialog.ShowForReferenceAsync);
    private async Task<bool> ShowReferenceWindow(Window dialog,Func<Task<bool>> show)
    {
        if(ReferenceEditor!=null)return false;
        ReferenceEditor=dialog;parseTimer.Stop();
        void MouseGuard(object sender,MouseButtonEventArgs e)
        {
            var bar=Find<ScrollBar>(e.OriginalSource as DependencyObject);
            if(bar==null || !EditorArea.IsAncestorOf(bar))e.Handled=true;
        }
        void DropGuard(object sender,DragEventArgs e) { e.Effects=DragDropEffects.None;e.Handled=true; }
        void MenuGuard(object sender,ContextMenuEventArgs e)=>e.Handled=true;
        void TextGuard(object sender,TextCompositionEventArgs e)=>e.Handled=true;
        PreviewMouseDown+=MouseGuard;PreviewMouseUp+=MouseGuard;ContextMenuOpening+=MenuGuard;PreviewDragOver+=DropGuard;PreviewDrop+=DropGuard;PreviewTextInput+=TextGuard;
        try { return await show(); }
        finally
        {
            PreviewMouseDown-=MouseGuard;PreviewMouseUp-=MouseGuard;ContextMenuOpening-=MenuGuard;PreviewDragOver-=DropGuard;PreviewDrop-=DropGuard;PreviewTextInput-=TextGuard;
            ReferenceEditor=null;
        }
    }
}
