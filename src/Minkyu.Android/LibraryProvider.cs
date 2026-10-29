using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Uri = Android.Net.Uri;

namespace Minkyu.Manager;

[ContentProvider(new[] { LibraryStore.Authority }, Exported = false, GrantUriPermissions = true)]
public sealed class LibraryProvider : ContentProvider
{
    public override bool OnCreate() => true;
    private string Resolve(Uri uri)
    {
        var store = new LibraryStore(Context!);
        var segments = uri.PathSegments!;
        if (uri.Authority != LibraryStore.Authority)
            throw new Java.Lang.SecurityException("Unexpected content authority");
        if (segments.Count == 1)
        {
            if (segments[0] == "catalog")
                return store.CatalogPath;
            if (segments[0] == "apk")
                return store.PreparedApk;
            if (segments[0] == "expansion")
                return store.ExpansionPath;
        }

        if (segments.Count == 2 && segments[0] == "chart" && store.Read().Any(i => i.Id == segments[1]))
            return store.ChartPath(segments[1]);
        throw new Java.IO.FileNotFoundException("Library item unavailable");
    }

    public override ParcelFileDescriptor? OpenFile(Uri uri, string mode)
    {
        if (mode != "r")
            throw new Java.Lang.SecurityException("Library access is read-only");
        return ParcelFileDescriptor.Open(new Java.IO.File(Resolve(uri)), ParcelFileMode.ReadOnly);
    }

    public override string? GetType(Uri uri) => uri.LastPathSegment == "apk" ? "application/vnd.android.package-archive" : uri.LastPathSegment == "catalog" ? "application/json" : "application/octet-stream";
    public override ICursor? Query(Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder)
    {
        var file = new FileInfo(Resolve(uri));
        var columns = projection ?? new[] { IOpenableColumns.DisplayName, IOpenableColumns.Size };
        var cursor = new MatrixCursor(columns);
        var row = cursor.NewRow();
        foreach (var column in columns)
            row!.Add(column == IOpenableColumns.DisplayName ? new Java.Lang.String(file.Name) : column == IOpenableColumns.Size ? Java.Lang.Long.ValueOf(file.Exists ? file.Length : 0) : null);
        return cursor;
    }

    public override Uri? Insert(Uri uri, ContentValues? values) => throw new Java.Lang.UnsupportedOperationException();
    public override int Delete(Uri uri, string? selection, string[]? selectionArgs) => throw new Java.Lang.UnsupportedOperationException();
    public override int Update(Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => throw new Java.Lang.UnsupportedOperationException();
}
