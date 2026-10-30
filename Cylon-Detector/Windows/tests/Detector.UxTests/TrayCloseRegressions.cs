using System.Runtime.InteropServices;
using System.Reflection;
using Detector.App;
using Detector.History;

internal static partial class Program
{
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(nint window);
    static async Task TrayCloseRegressions(MainWindow window)
    {
        var preferenceField=typeof(MainWindow).GetField("preferences",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var original=Field<Preferences>(window,"preferences");
        var navigation=Field<ListBox>(window,"navigation");
        var failures=new List<string>();
        try
        {
            preferenceField.SetValue(window,original with{ContinueInTray=true});
            foreach(var section in new[]{0,3,4})
            foreach(var state in new[]{FormWindowState.Normal,FormWindowState.Maximized,FormWindowState.Minimized})
            {
                navigation.SelectedIndex=section;
                window.WindowState=state;
                await Task.Delay(100);
                nint before=window.Handle;
                window.Close();
                await Task.Delay(100);
                bool nativeVisible=IsWindowVisible(window.Handle);
                bool recreated=before!=window.Handle;
                Console.WriteLine($"TRAY CLOSE section={navigation.Items[section]} state={state} managedVisible={window.Visible} nativeVisible={nativeVisible} recreated={recreated} disposed={window.IsDisposed} taskbarStyle={window.ShowInTaskbar}");
                if(window.Visible||nativeVisible||recreated||window.IsDisposed)
                    failures.Add($"{navigation.Items[section]}, {state}: visible={window.Visible}/{nativeVisible}, recreated={recreated}, disposed={window.IsDisposed}");
                var trayIcon=Field<NotifyIcon>(window,"tray");
                typeof(NotifyIcon).GetMethod("OnMouseClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(trayIcon,new object[]{new MouseEventArgs(MouseButtons.Left,1,0,0,0)});
                await Task.Delay(100);
                Check(window.Visible&&IsWindowVisible(window.Handle)&&window.ShowInTaskbar,$"{state}: tray left-click restores existing {navigation.Items[section]} window");
                Check(Application.OpenForms.Cast<Form>().Where(form=>form.TopLevel).SequenceEqual(new[]{window}),"Tray lifecycle has only one top-level application form");
                Check(window.WindowState==(state==FormWindowState.Maximized?FormWindowState.Maximized:FormWindowState.Normal),$"{state}: tray restoration retains maximized state or restores minimized window");
            }
            Check(failures.Count==0,"One close hides all native application windows without handle recreation in tray mode: "+string.Join("; ",failures));
        }
        finally{preferenceField.SetValue(window,original);}
    }
}
