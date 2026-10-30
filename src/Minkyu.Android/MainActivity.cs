using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Android.OS;
using Android.Views;
using Android.Widget;
using Uri = Android.Net.Uri;
using Path = System.IO.Path;

namespace Minkyu.Manager;

[Activity(Label = "Minkyu", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.Portrait)]
public sealed partial class MainActivity : AndroidX.AppCompat.App.AppCompatActivity
{
    private LibraryStore store = null!;
    private bool setup, busy, searchVisible, settings;
    private int setupStep;
    private string search = "", notice = "", operation = "";
    private ISharedPreferences Preferences => GetSharedPreferences("minkyu", FileCreationMode.Private)!;

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        store = new LibraryStore(this);
        setup = !Preferences.GetBoolean("setup.complete", false);
        setupStep = Preferences.GetInt("setup.step", 0);
        InitAppearance();
        Render();
    }

    private void Pick(int request, bool multiple)
    {
        if (busy)
            return;
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraAllowMultiple, multiple);
        if (request == 41)
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        StartActivityForResult(intent, request);
    }

    protected override async void OnActivityResult(int request, Result result, Intent? data)
    {
        base.OnActivityResult(request, result, data);
        if (result != Result.Ok || data == null || busy)
            return;
        var uris = new List<Uri>();
        if (data.ClipData != null)
            for (var i = 0; i < data.ClipData.ItemCount; i++)
            {
                var uri = data.ClipData.GetItemAt(i)?.Uri;
                if (uri != null)
                    uris.Add(uri);
            }
        else if (data.Data != null)
            uris.Add(data.Data);
        if (uris.Count == 0)
            return;
        operation = request == 42 ? "Preparing APK…" : request == 43 ? "Copying game data…" : "Importing songs…";
        await Work(async () =>
        {
            if (request == 41)
            {
                var imported = 0;
                var kept = 0;
                var rejected = 0;
                foreach (var uri in uris)
                {
                    try
                    {
                        var result = store.ImportAndMove(() => ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Cannot open selected file"), () => DocumentsContract.IsDocumentUri(this, uri) && DocumentsContract.DeleteDocument(ContentResolver!, uri));
                        imported++;
                        if (!result.Moved)
                            kept++;
                        Log((result.Moved ? "Moved " : "Imported; original kept: ") + result.Item.Name);
                    }
                    catch (Exception ex)
                    {
                        rejected++;
                        Log("Import rejected: " + ex.Message);
                    }
                }

                notice = $"{imported} song{(imported == 1 ? "" : "s")} added." + (kept == 0 ? "" : $" Android kept {kept} original file{(kept == 1 ? "" : "s")}.");
                if (rejected > 0)
                    notice += $" {rejected} couldn't be added.";
            }
            else if (request == 43)
            {
                using var input = ContentResolver!.OpenInputStream(uris[0]) ?? throw new IOException("Cannot open game data");
                store.ImportExpansion(input, Log);
                notice = "Game data saved.";
                SetSetupStep(3);
            }
            else if (request == 42)
            {
                var source = Path.Combine(CacheDir!.AbsolutePath, "selected-game.apk");
                try
                {
                    Log("Reading selected APK…");
                    using (var input = ContentResolver!.OpenInputStream(uris[0]) ?? throw new IOException("Cannot open APK"))
                    using (var output = File.Create(source))
                    {
                        var buffer = new byte[1024 * 1024];
                        long total = 0;
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            total += count;
                            if (total > 1024L * 1024 * 1024)
                                throw new IOException("APK exceeds 1 GB.");
                            output.Write(buffer, 0, count);
                        }
                    }

                    await ApkPreparation.PrepareAsync(this, source, Log);
                    notice = "APK ready to install.";
                    SetSetupStep(2);
                }
                finally
                {
                    if (File.Exists(source))
                        File.Delete(source);
                }
            }
        });
    }

    private async Task Work(Func<Task> work)
    {
        busy = true;
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn);
        Render();
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex)
        {
            notice = ExplainFailure(ex);
            RunOnUiThread(() => new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(this)!.SetTitle("Couldn’t finish")!.SetMessage(notice)!.SetPositiveButton("OK", (_, _) => { })!.Show());
            Log(ex.ToString());
        }
        finally
        {
            busy = false;
            Window!.ClearFlags(WindowManagerFlags.KeepScreenOn);
            Render();
        }
    }

    private static void Log(string message) => global::Android.Util.Log.Info("Minkyu", message);

    private void InstallPrepared()
    {
        if (!ApkPreparation.HasCurrentPreparedApk(this))
        {
            setup = true;
            settings = false;
            SetSetupStep(1);
            notice = "Choose your APK again to get the latest changes.";
            Render();
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !PackageManager!.CanRequestPackageInstalls())
        {
            new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(this)!.SetTitle("Allow installs")!.SetMessage("Turn on 'Allow from this source' for Minkyu, then come back here.")!.SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Open settings", (_, _) =>
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                    StartActivity(new Intent(Settings.ActionManageUnknownAppSources, Uri.Parse("package:" + PackageName)));
            })!.Show();
            return;
        }

        new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(this)!.SetTitle("Install Muse Dash?")!.SetMessage("Back up your saves first. If Android asks you to remove the existing game, removing it can delete your progress.")!.SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Install", (_, _) =>
        {
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(Uri.Parse($"content://{LibraryStore.Authority}/apk"), "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            StartActivity(intent);
        })!.Show();
    }

    private void Launch()
    {
        try
        {
            var intent = PackageManager!.GetLaunchIntentForPackage(LibraryStore.GamePackage);
            if (intent == null)
            {
                setup = true;
                SetSetupStep(0);
                notice = "Set up Muse Dash to play your songs.";
                Render();
                return;
            }

            var session = store.CreateCatalog();
            var catalog = Uri.Parse($"content://{LibraryStore.Authority}/catalog")!;
            intent.PutExtra("minkyu.catalog", catalog.ToString());
            intent.PutExtra("minkyu.session", session);
            var clip = ClipData.NewRawUri("Minkyu library", catalog)!;
            foreach (var item in store.Read())
                clip.AddItem(new ClipData.Item(Uri.Parse($"content://{LibraryStore.Authority}/chart/{item.Id}")));
            if (File.Exists(store.ExpansionPath) && File.Exists(store.ExpansionHashPath))
            {
                var uri = Uri.Parse($"content://{LibraryStore.Authority}/expansion")!;
                clip.AddItem(new ClipData.Item(uri));
                intent.PutExtra("minkyu.expansion", uri.ToString());
                intent.PutExtra("minkyu.expansion.sha256", File.ReadAllText(store.ExpansionHashPath).Trim());
            }

            intent.ClipData = clip;
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            for (var i = 0; i < clip.ItemCount; i++)
                GrantUriPermission(LibraryStore.GamePackage, clip.GetItemAt(i)!.Uri!, ActivityFlags.GrantReadUriPermission);
            Log($"Launching with {store.Read().Count} songs");
            StartActivity(intent);
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            notice = "Couldn’t open Muse Dash. Try again, or check Game setup in Settings.";
            Render();
        }
    }

    private string ExplainFailure(Exception error)
    {
        var message = error.Message;
        if (error is UnauthorizedAccessException)
            return "Couldn’t read that file. Pick it again.";
        if (error is OutOfMemoryException)
            return "There isn’t enough free memory. Close other apps and try again.";
        if (operation.StartsWith("Importing", StringComparison.Ordinal))
            return message.StartsWith("Imported ", StringComparison.Ordinal) ? "Some songs couldn’t be added. The rest are in your library." : "This song file won’t open. Try another .mdm file.";
        if (operation.StartsWith("Copying", StringComparison.Ordinal))
            return "This game data file couldn’t be opened. Choose the .obb file that came with Muse Dash.";
        if (operation.StartsWith("Deleting", StringComparison.Ordinal))
            return "Couldn't delete this song. Try again.";
        if (message.Contains("already contains a mod loader", StringComparison.Ordinal))
            return "This APK was already changed. Try your original Muse Dash APK.";
        if (message.Contains("Split APK", StringComparison.OrdinalIgnoreCase) || message.Contains("APKS/XAPK", StringComparison.Ordinal))
            return "This file is only part of the game. Choose the full Muse Dash APK.";
        if (message.Contains("supported", StringComparison.OrdinalIgnoreCase) || message.Contains("ARM64", StringComparison.Ordinal))
            return "This game file isn’t supported yet. Try Muse Dash version 6.7.0.";
        return "Couldn't prepare the game. Try again.";
    }
}
