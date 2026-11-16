using System.IO.Compression;
using System.Text.Json.Nodes;
using LumiBar;
internal static class DiagnosticsTests
{
    internal static async Task<List<string>> Run()
    {
        var result=new List<string>();void Check(bool value,string name)=>result.Add($"{(value?"PASS":"FAIL")} {name}");
        var credentials="apiKey=EXAMPLE_PRIVATE_123 password='not for export' Authorization: Bearer token.secret.signature lat=39.363478 lon=117.069352";
        var redacted=DiagnosticPrivacy.Redact(credentials);
        var menuSecret=new string('a',64);var menuJson=JsonNode.Parse("{\"registrationToken\":\""+menuSecret+"\"}");
        Check(!DiagnosticPrivacy.Clean(JsonNode.Parse("{\"locatedCity\":{\"name\":\"PrivatePosition\",\"latitude\":30}}"))!.ToJsonString().Contains("PrivatePosition"),"Diagnostic export removes the separately cached located city");
        Check(!DiagnosticPrivacy.Redact("registrationToken="+menuSecret).Contains(menuSecret)&&!DiagnosticPrivacy.Clean(menuJson)!.ToJsonString().Contains(menuSecret),"Plugin menu registration tokens are redacted in text and structured diagnostic data");
        Check(!redacted.Contains("EXAMPLE_PRIVATE")&&!redacted.Contains("not for export")&&!redacted.Contains("token.secret")&&!redacted.Contains("39.363478"),"Diagnostic logs redact credentials and location parameters");
        Check(!DiagnosticPrivacy.Redact("https://example.com/private/path?key=private&lat=39.3").Contains("example.com"),"Diagnostic URLs omit hosts, query values and paths");
        Check(!DiagnosticPrivacy.Redact(@"C:\Users\Alice\Documents\project\App.cs:line 42").Contains("Alice"),"Diagnostic paths omit Windows account names");
        var pem="-----BEGIN PRIVATE KEY-----\nVERY_PRIVATE_DATA\n-----END PRIVATE KEY-----";
        Check(!DiagnosticPrivacy.Redact(pem).Contains("VERY_PRIVATE_DATA"),"Diagnostic export excludes complete private key blocks");
        var node=JsonNode.Parse("""{"normal":{"count":2},"items":[{"apiKey":"private"}],"city":{"name":"SecretTown","lat":39.3}}""")!;
        var cleaned=DiagnosticPrivacy.Clean(node)!.ToJsonString();Check(!cleaned.Contains("SecretTown")&&!cleaned.Contains("\"private\"")&&cleaned.Contains("count"),"Nested diagnostic JSON preserves useful fields and masks private objects");
        var folder=Path.Combine(AppContext.BaseDirectory,"test-data","diagnostics-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder,"diagnostics.log"),credentials+"\n"+@"C:\Users\Alice\AppData\test");
        await File.WriteAllTextAsync(Path.Combine(folder,"settings.json"),"DO_NOT_EXPORT_SETTINGS");await File.WriteAllTextAsync(Path.Combine(folder,"secret.pem"),"DO_NOT_EXPORT_KEY");
        var zip=Path.Combine(folder,"报告.zip");await DiagnosticArchive.Write(zip,folder,new{version="test",title="SecretSong",city=new{name="SecretTown"},state=new{playing=true}},Array.Empty<object>());
        using(var archive=ZipFile.OpenRead(zip)){
            Check(archive.Entries.Any(e=>e.FullName=="运行状态.json")&&archive.Entries.Any(e=>e.FullName=="日志/diagnostics.log"),"Diagnostic archive contains runtime state and recent logs");
            var text=string.Join("\n",archive.Entries.Select(e=>{using var reader=new StreamReader(e.Open());return reader.ReadToEnd();}));
            Check(!text.Contains("SecretSong")&&!text.Contains("SecretTown")&&!text.Contains("EXAMPLE_PRIVATE")&&!text.Contains("DO_NOT_EXPORT"),"Exported ZIP excludes media, city, extra settings and unlisted secret files");
        }
        using(var stop=new CancellationTokenSource()){stop.Cancel();try{await DiagnosticArchive.Write(Path.Combine(folder,"cancel.zip"),folder,new{},new{},stop.Token);Check(false,"Cancelled exports leave no output or partial archive");}catch(OperationCanceledException){Check(!File.Exists(Path.Combine(folder,"cancel.zip"))&&!Directory.GetFiles(folder,"*.tmp").Any(),"Cancelled exports leave no output or partial archive");}}
        var source=Path.Combine(folder,"bridge.dll");await File.WriteAllBytesAsync(source,[1,2,3,4]);var first=TaskbarComponentCache.Prepare(source,folder);Check(File.Exists(first)&&first!=source&&File.ReadAllBytes(first).SequenceEqual(new byte[]{1,2,3,4}),"Taskbar bridge loads from a verified cache outside its install directory");
        Check(TaskbarComponentCache.Prepare(source,folder)==first,"Unchanged taskbar component reuses its verified content address");
        await File.WriteAllBytesAsync(source,[5,6,7]);var second=TaskbarComponentCache.Prepare(source,folder);Check(second!=first&&File.Exists(first),"New taskbar component never overwrites an Explorer-loaded old component");
        await File.WriteAllBytesAsync(second,[0]);try{TaskbarComponentCache.Prepare(source,folder);Check(false,"Corrupt cached taskbar DLL is rejected before loading");}catch(InvalidDataException){Check(true,"Corrupt cached taskbar DLL is rejected before loading");}
        return result;
    }
}
