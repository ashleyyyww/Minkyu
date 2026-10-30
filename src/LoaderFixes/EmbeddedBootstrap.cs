namespace CustomAlbums.LoaderFixes;

public static class EmbeddedBootstrap
{
    public static void Install(string payload)
    {
        var assembly = System.Reflection.Assembly.Load(Convert.FromBase64String(payload));
        assembly.GetType("CustomAlbums.LoaderFixes.ScriptingInvokeGuard")!.GetMethod("Install")!.Invoke(null, null);
    }
}
