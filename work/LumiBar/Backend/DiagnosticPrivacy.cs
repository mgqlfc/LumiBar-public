using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LumiBar;
internal static class DiagnosticPrivacy
{
    private static readonly Regex PrivateKey=new("-----BEGIN [A-Z ]*PRIVATE KEY-----[\\s\\S]*?-----END [A-Z ]*PRIVATE KEY-----",RegexOptions.Compiled);
    private static readonly Regex Credentials=new("""(?i)(\b(?:api[_-]?key|access[_-]?token|refresh[_-]?token|lease[_-]?token|registration[_-]?token|token|authorization|password|secret|credential|latitude|longitude|lat|lon|title|artist|album|lyrics)\b["']?\s*[:=]\s*)(?:"(?:\\.|[^"\\])*"|'[^']*'|[^\s,;&]+)""",RegexOptions.Compiled);
    private static readonly Regex Authorization=new(@"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+",RegexOptions.Compiled);
    private static readonly Regex Url=new("""(?i)https?://[^\s"'<>]+""",RegexOptions.Compiled);
    private static readonly Regex UserPath=new(@"(?i)\b[A-Z]:\\+Users\\+[^\\/\r\n]+",RegexOptions.Compiled);
    private static readonly Regex LongValue=new(@"\b[A-Za-z0-9_+/=-]{80,}\b",RegexOptions.Compiled);
    private static readonly Regex PrivateField=new(@"(?i)^(api[_-]?key|access[_-]?token|refresh[_-]?token|lease[_-]?token|registration[_-]?token|token|authorization|password|secret|credential|latitude|longitude|lat|lon|city|locatedCity|favorites|title|artist|album|cover|lyricsOffsets|lines)$",RegexOptions.Compiled);
    internal static string Redact(string text)
    {
        text=PrivateKey.Replace(text,"[PRIVATE KEY REDACTED]");
        text=Authorization.Replace(text,"$1 [REDACTED]");
        text=Credentials.Replace(text,m=>m.Groups[1].Value+"[REDACTED]");
        text=Url.Replace(text,m=>Uri.TryCreate(m.Value,UriKind.Absolute,out var uri)?uri.Scheme+"://[HOST]/[PATH]": "[URL REDACTED]");
        text=UserPath.Replace(text,@"C:\Users\[USER]");
        return LongValue.Replace(text,"[LONG VALUE REDACTED]");
    }
    internal static JsonNode? Clean(JsonNode? node)
    {
        if(node is JsonObject obj){foreach(var key in obj.Select(p=>p.Key).ToArray()){var old=obj[key];var cleaned=PrivateField.IsMatch(key)?JsonValue.Create("[REDACTED]"):Clean(old);if(!ReferenceEquals(old,cleaned))obj[key]=cleaned;}}
        else if(node is JsonArray array){for(var i=0;i<array.Count;i++){var old=array[i];var cleaned=Clean(old);if(!ReferenceEquals(old,cleaned))array[i]=cleaned;}}
        else if(node is JsonValue value&&value.TryGetValue<string>(out var text))return JsonValue.Create(Redact(text));
        return node;
    }
}
