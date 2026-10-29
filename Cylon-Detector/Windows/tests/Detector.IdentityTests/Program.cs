using System.IO.Pipes;
using System.Security.Principal;
using Detector.History;
using Detector.Service;

int checks=0;
void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks++;Console.WriteLine("PASS "+message);}
void Reject(Action action,string message){try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException){Check(true,message);return;}throw new InvalidOperationException(message+" was accepted");}
using var identity=WindowsIdentity.GetCurrent();string sid=identity.User!.Value;
var config=new BootConfiguration(sid,Environment.ProcessId,new Preferences{Startup=StartupMode.Boot,LogDirectory=BootConfiguration.DefaultLogs,ReportDirectory=BootConfiguration.DefaultReports});
config.Validate();Check(true,"Selected-account boot policy accepts distinct protected Detector destinations");
foreach(int interval in new[]{0,-1,1441})Reject(()=>(config with{Options=config.Options with{ReportIntervalMinutes=interval}}).Validate(),"Out-of-range summary cadence is rejected before service setup");
Reject(()=>(config with{Options=config.Options with{LogDirectory=BootConfiguration.DefaultReports}}).Validate(),"Main log and report destinations cannot be the same directory");
Reject(()=>(config with{Options=config.Options with{LogDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"EirenAndI-CylonRaider","Logs")}}).Validate(),"Detector boot logging cannot target Raider's protected data root");
Reject(()=>BootConfiguration.Destination(@"\\example.invalid\share\Logs"),"Boot destination rejects a network path without accessing it");
Check(NativeService.ProcessOwner(Environment.ProcessId)==sid,"Process token owner lookup matches the current Windows account");
string pipeName="CylonDetector-IdentityFixture-"+Guid.NewGuid().ToString("N");
using(var server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))
using(var client=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Anonymous))
{
    var connected=server.WaitForConnectionAsync();await client.ConnectAsync(5000);await connected.WaitAsync(TimeSpan.FromSeconds(5));
    Check(PipeRequesterIdentity.OwnerSid(client)==sid,"Sensor observer owner derives from the real connected UI pipe server token");
}
using(var disconnected=new NamedPipeClientStream(".","CylonDetector-Unconnected-"+Guid.NewGuid().ToString("N")))
{
    bool rejected=false;try{_=PipeRequesterIdentity.OwnerSid(disconnected);}catch(Exception ex)when(ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception){rejected=true;}
    Check(rejected,"An unconnected pipe cannot nominate an account for sensor observation");
}
Console.WriteLine($"PASS {checks} Detector identity/service policy assertions. Native local pipes/process tokens only; no service, UAC, ETW or device capture was started.");
