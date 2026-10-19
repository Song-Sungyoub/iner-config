using System.Windows;

namespace InerConfig;
public partial class App : Application
{
    internal WorkspaceStore? Workspace { get; set; }
    private InstanceService? instance;
    private readonly SemaphoreSlim argumentGate = new(1, 1);
    internal MainWindow? ActiveEditor { get; set; }
    private async Task OpenArguments(string[] args)
    {
        await argumentGate.WaitAsync();
        try
        {
        while (System.Windows.Interop.ComponentDispatcher.IsThreadModal || InerConfig.MainWindow.Editors.Any(w=>w.ReferenceEditor!=null)) await Task.Delay(100);
        var window = ActiveEditor is { IsLoaded: true } active ? active : InerConfig.MainWindow.Editors.FirstOrDefault();
        if (window == null) return;
        window.BringForward();
        if (args.Length == 2 && args[0] == "--data") window.ShowCrypto(args[1]);
        else if (args.Length > 0 && args.All(a => !a.StartsWith("--"))) foreach (var path in args) await window.OpenPathAsync(path);
        else if (args.Length > 0 && !(args.Length == 1 && args[0] == "--skip-update-once")) MessageBox.Show(window, "사용법: InerConfig.exe [파일 경로 ...] 또는 --data \"암호문\"");
        }
        finally { argumentGate.Release(); }
    }
    protected override void OnExit(ExitEventArgs e) { if(e.ApplicationExitCode==0)SessionBackups.Cleanup(); instance?.Dispose(); base.OnExit(e); }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        if (e.Args.Length == 1 && e.Args[0] == "--default-apps")
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=InerConfig") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Windows 기본 앱 설정을 열지 못했습니다.\n" + ex.Message, "InerConfig"); }
            Shutdown();
            return;
        }
        if (e.Args.Contains("--self-test"))
        {
            try { SelfTest.Run(e.Args.Skip(1).ToArray()); Shutdown(0); }
            catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"self-test-error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        if (!e.Args.Contains("--ui-test"))
        {
            instance = new InstanceService();
            if (!instance.IsPrimary)
            {
                try { await instance.Forward(e.Args); Shutdown(0); }
                catch (Exception ex) { MessageBox.Show("기존 InerConfig 창으로 전달하지 못했습니다. 잠시 후 다시 여세요.\n" + ex.Message, "InerConfig"); Shutdown(1); }
                return;
            }
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if(e.Args.Length==2&&e.Args[0]=="--ui-test")
        {
            try{await window.RunUiTest(e.Args[1]);Shutdown(0);}catch(Exception ex){System.IO.Directory.CreateDirectory(e.Args[1]);System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1],"ui-test-error.txt"),ex.ToString());Shutdown(1);}return;
        }
        var recoveryWarnings=new List<string>();
        try
        {
            Workspace=new WorkspaceStore(WorkspaceStore.DefaultPath);
            var savedWindows=Workspace.LoadWindows(recoveryWarnings);
            foreach(var saved in savedWindows)
            {
                var target=saved==savedWindows[0]?window:new MainWindow();
                try { await target.RestoreWorkspace(saved);if(target!=window)target.Show(); }
                catch(Exception ex) { recoveryWarnings.Add("창 복원 실패: "+ex.Message);if(target!=window)target.Close(); }
            }
        }
        catch(Exception ex) { recoveryWarnings.Add("작업 복원 초기화 실패: "+ex.Message); }
        if(recoveryWarnings.Count>0)MessageBox.Show(window,string.Join("\n",recoveryWarnings),"작업 복원 안내",MessageBoxButton.OK,MessageBoxImage.Warning);
        if (instance != null) _ = instance.Listen(args => Dispatcher.BeginInvoke(new Action(async () => await OpenArguments(args))));
        if (!(e.Args.Length == 2 && e.Args[0] == "--data")) window.ChooseInitialView();
        if (e.Args.Length == 1 && e.Args[0] == "--skip-update-once") { window.ShowUpdateResult(); return; }
        await OpenArguments(e.Args);
        window.ShowUpdateResult();
        await window.CheckUpdates();
    }
}
