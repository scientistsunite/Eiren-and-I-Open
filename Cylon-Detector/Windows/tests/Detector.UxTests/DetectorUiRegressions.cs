using Detector.App;
using Detector.Core;
using Detector.History;
using System.Reflection;

internal static partial class Program
{
    static void DetectorPreferenceRegressions()
    {
        var defaults=new Preferences();
        Check(!defaults.ContinuousNetwork&&defaults.ReportIntervalMinutes==60,"Detector defaults to sensor-triggered network recording and 60-minute summaries");
        string root=Path.Combine(Path.GetTempPath(),"Detector-Ui-Settings-"+Guid.NewGuid().ToString("N"));
        try
        {
            var file=new SettingsFile(root);
            foreach(int interval in new[]{1,1440})
            {
                file.Save(defaults with{ContinuousNetwork=true,ReportIntervalMinutes=interval});
                var loaded=new SettingsFile(root).Load();
                Check(loaded.ContinuousNetwork&&loaded.ReportIntervalMinutes==interval,"Continuous network and valid summary interval persist: "+interval);
            }
            foreach(int interval in new[]{0,1441})
            {
                bool rejected=false;try{file.Save(defaults with{ReportIntervalMinutes=interval});}catch(ArgumentOutOfRangeException){rejected=true;}
                Check(rejected,"Unsupported summary interval cannot be saved: "+interval);
            }
            File.Delete(Path.Combine(root,"options.json.bak"));
            File.WriteAllText(Path.Combine(root,"options.json"),"{\"Schema\":1,\"ReportIntervalMinutes\":0}");
            var invalid=new SettingsFile(root);var recovered=invalid.Load();
            Check(recovered.ReportIntervalMinutes==60&&invalid.Warning!=null,"Invalid persisted summary interval falls back with an explicit settings warning");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    static void DetectorObservationRegressions(MainWindow window)
    {
        var render=typeof(MainWindow).GetMethod("RenderPrivacy",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var use=new SensorUse("microphone","Synthetic microphone app",@"C:\Synthetic\microphone.exe",DateTime.UtcNow.AddSeconds(-10).Ticks,null,"Synthetic Windows metadata","Synthetic account");
        var grid=Field<DataGridView>(window,"observedDevices");var label=Field<Label>(window,"sensorObservation");
        var available=Frame(0) with{Sensors=new SensorSnapshot(DateTime.UtcNow,[use],true,"Synthetic limited Windows evidence")};
        render.Invoke(window,new object?[]{available});
        Check(grid.RowCount==1&&grid["State",0].Value?.ToString()=="Windows reports active","Available synthetic metadata is labeled Windows-reported activity");
        Check(label.Text.Contains("does not expose every")&&label.Text.Contains("no audio"),"Device UI explains observational limits and no content collection");
        render.Invoke(window,new object?[]{available with{Sensors=available.Sensors! with{Available=false,Status="Synthetic owner hive unavailable"}}});
        Check(grid["State",0].Value?.ToString()?.Contains("current state unknown")==true,"Unchanged device identity is relabeled unknown when evidence becomes unavailable");
        render.Invoke(window,new object?[]{null});
        Check(grid.RowCount==0&&label.Text.Contains("unavailable")&&label.Text.Contains("does not establish"),"Lost preview clears current activity claims without inferring devices are unused");
        var selector=Field<ComboBox>(window,"activityViewSelector");
        Check(selector.Items.Count==2&&selector.Items[0]?.ToString()?.Contains("anomaly")==true&&selector.Items[1]?.ToString()?.Contains("network")==true,"Activity History exposes unified observations and detailed network history in one window");
    }
}
