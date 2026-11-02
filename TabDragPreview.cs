using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace InerConfig;

// A lightweight, click-through drag preview. It never owns a session or parses file contents.
internal sealed class TabDragPreview : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point,uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size;public NativeRect Monitor,Work;public uint Flags; }
    internal TabDragPreview(string title,string format)
    {
        Width=460;Height=190;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        ShowActivated=false;ShowInTaskbar=false;Topmost=true;IsHitTestVisible=false;
        WindowStartupLocation=WindowStartupLocation.Manual;Left=-10000;Top=-10000;
        var content=new StackPanel{Margin=new Thickness(20)};
        content.Children.Add(new TextBlock{Text="새 창으로 분리",FontSize=13,FontWeight=FontWeights.SemiBold});
        content.Children.Add(new TextBlock{Text=title,FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,16,0,8),TextTrimming=TextTrimming.CharacterEllipsis});
        content.Children.Add(new TextBlock{Text=format+" · 놓으면 이 문서를 새 창으로 엽니다",FontSize=13});
        content.Children.Add(new TextBlock{Text="Esc: 취소 · 다른 창의 탭 위에 놓기: 합치기",FontSize=12,Margin=new Thickness(0,14,0,0)});
        var frame=new Border{Child=content,BorderThickness=new Thickness(2)};
        frame.SetResourceReference(Border.BackgroundProperty,"Surface");frame.SetResourceReference(Border.BorderBrushProperty,"Blue");
        SetResourceReference(ForegroundProperty,"Text");SetResourceReference(BackgroundProperty,"Surface");Content=frame;
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000020);};
    }
    internal void Follow(int x,int y)=>Place(this,x+20,y+18);
    internal static void Place(Window window,int x,int y)
    {
        var hwnd=new WindowInteropHelper(window).Handle;
        if(hwnd==IntPtr.Zero)return;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(GetMonitorInfo(MonitorFromPoint(new NativePoint{X=x,Y=y},2),ref info)&&GetWindowRect(hwnd,out var rect))
        {
            x=Math.Clamp(x,info.Work.Left,Math.Max(info.Work.Left,info.Work.Right-(rect.Right-rect.Left)));
            y=Math.Clamp(y,info.Work.Top,Math.Max(info.Work.Top,info.Work.Bottom-(rect.Bottom-rect.Top)));
        }
        SetWindowPos(hwnd,IntPtr.Zero,x,y,0,0,0x0015); // NOSIZE | NOZORDER | NOACTIVATE
    }
}
