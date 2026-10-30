using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Color;
using Google.Android.Material.Dialog;
using Google.Android.Material.FloatingActionButton;
using Google.Android.Material.ProgressIndicator;
using Color = Android.Graphics.Color;
using View = Android.Views.View;
using Orientation = Android.Widget.Orientation;
using Uri = Android.Net.Uri;

namespace Minkyu.Manager;

public sealed partial class MainActivity
{
    private LinearLayout page = null!, body = null!;
    private TextView status = null!, summary = null!;
    private SongAdapter? songAdapter;
    private readonly Dictionary<string, Bitmap> artwork = new();
    private int D(float value) => (int)(value * Resources!.DisplayMetrics!.Density + .5f);
    private Color ThemeColor(int attr)
    {
        using var value = new TypedValue();
        Theme!.ResolveAttribute(attr, value, true);
        return new Color(value.Data);
    }

    private void InitAppearance()
    {
        DynamicColors.ApplyToActivityIfAvailable(this);
        Window!.SetSoftInputMode(SoftInput.AdjustResize);
    }

    private TextView Text(string value, int size = 14, bool title = false)
    {
        var text = new Google.Android.Material.TextView.MaterialTextView(this)
        {
            Text = value
        };
        text.SetTextSize(ComplexUnitType.Sp, size);
        text.SetTextColor(ThemeColor(title ? Resource.Attribute.colorOnSurface : Resource.Attribute.colorOnSurfaceVariant));
        return text;
    }

    private void Gap(LinearLayout layout, int dp) => layout.AddView(new View(this), new LinearLayout.LayoutParams(1, D(dp)));
    private MaterialButton ActionButton(string text, Action action, bool secondary = false)
    {
        var button = new MaterialButton(this, null, secondary ? Resource.Attribute.materialButtonOutlinedStyle : Resource.Attribute.materialButtonStyle)
        {
            Text = text,
            Enabled = !busy
        };
        button.SetAllCaps(false);
        button.Click += (_, _) => action();
        return button;
    }

    private void Render()
    {
        songAdapter = null;
        page = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        page.SetBackgroundColor(ThemeColor(Resource.Attribute.colorSurface));
        page.SetOnApplyWindowInsetsListener(new PageInsets());
        var toolbar = new MaterialToolbar(this)
        {
            Title = setup ? "Setup" : settings ? "Settings" : "Songs"
        };
        if (settings || setup)
        {
            if (settings || Preferences.GetBoolean("setup.complete", false) || setupStep > 0)
            {
                toolbar.SetNavigationIcon(Resource.Drawable.ic_arrow_back);
                toolbar.NavigationClick += (_, _) =>
                {
                    if (busy)
                        return;
                    if (settings)
                        settings = false;
                    else if (setupStep > 0)
                        SetSetupStep(setupStep - 1);
                    else
                        setup = false;
                    Render();
                };
            }
        }
        else
        {
            toolbar.Menu!.Add(0, 1, 0, "Search")!.SetIcon(Resource.Drawable.ic_search)!.SetShowAsAction(ShowAsAction.Always);
            toolbar.Menu!.Add(0, 2, 1, "Settings")!.SetIcon(Resource.Drawable.ic_settings)!.SetShowAsAction(ShowAsAction.Always);
            toolbar.MenuItemClick += (_, e) =>
            {
                if (busy)
                    return;
                if (e.Item!.ItemId == 2)
                {
                    settings = true;
                    Render();
                }
                else
                {
                    searchVisible = !searchVisible;
                    if (!searchVisible)
                        search = "";
                    Render();
                }
            };
        }

        page.AddView(toolbar, new LinearLayout.LayoutParams(-1, D(64)));
        status = Text(busy ? operation : notice, 12);
        status.SetPadding(D(16), D(4), D(16), D(6));
        status.SetMaxLines(2);
        status.Visibility = string.IsNullOrEmpty(busy ? operation : notice) ? ViewStates.Gone : ViewStates.Visible;
        if (setup || settings)
        {
            var scroll = new ScrollView(this)
            {
                FillViewport = true
            };
            body = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical
            };
            body.SetPadding(D(24), D(16), D(24), D(24));
            scroll.AddView(body);
            page.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
            if (setup)
                RenderSetup();
            else
                RenderSettings();
        }
        else
            RenderLibrary();
        page.AddView(status);
        if (busy)
            page.AddView(new LinearProgressIndicator(this) { Indeterminate = true }, new LinearLayout.LayoutParams(-1, D(4)));
        SetContentView(page);
        page.RequestApplyInsets();
    }

    private void RenderLibrary()
    {
        if (searchVisible)
        {
            var container = new Google.Android.Material.TextField.TextInputLayout(this)
            {
                Hint = "Search songs"
            };
            container.SetPadding(D(16), 0, D(16), D(4));
            var input = new Google.Android.Material.TextField.TextInputEditText(container.Context!)
            {
                Text = search
            };
            input.SetSingleLine(true);
            input.SetTextSize(ComplexUnitType.Sp, 16);
            container.AddView(input);
            page.AddView(container);
            input.TextChanged += (_, _) =>
            {
                search = input.Text ?? "";
                RefreshSongs();
            };
        }

        var content = new FrameLayout(this);
        var list = new RecyclerView(this);
        list.SetLayoutManager(new LinearLayoutManager(this));
        list.SetPadding(0, 0, 0, D(104));
        list.SetClipToPadding(false);
        songAdapter = new SongAdapter(this);
        list.SetAdapter(songAdapter);
        content.AddView(list, new FrameLayout.LayoutParams(-1, -1));
        if (store.Read().Count == 0)
        {
            var empty = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical
            };
            empty.SetGravity(GravityFlags.Center);
            empty.SetPadding(D(32), 0, D(32), D(80));
            var icon = new ImageView(this);
            icon.SetImageResource(Resource.Drawable.ic_music_note);
            icon.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(ThemeColor(Resource.Attribute.colorOnSurfaceVariant));
            empty.AddView(icon, new LinearLayout.LayoutParams(D(48), D(48)));
            Gap(empty, 16);
            var title = Text("No songs yet", 22, true);
            title.Gravity = GravityFlags.Center;
            empty.AddView(title);
            Gap(empty, 8);
            var help = Text("Import .mdm files to add songs.");
            help.Gravity = GravityFlags.Center;
            empty.AddView(help);
            content.AddView(empty, new FrameLayout.LayoutParams(-1, -1));
        }

        var import = new ExtendedFloatingActionButton(this)
        {
            Text = "Import songs",
            Icon = GetDrawable(Resource.Drawable.ic_add),
            Enabled = !busy,
            ContentDescription = "Import songs"
        };
        import.Click += (_, _) => Pick(41, true);
        content.AddView(import, new FrameLayout.LayoutParams(-2, D(56), GravityFlags.Bottom | GravityFlags.Right) { RightMargin = D(16), BottomMargin = D(16) });
        page.AddView(content, new LinearLayout.LayoutParams(-1, 0, 1));
        var footer = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        footer.SetPadding(D(16), D(8), D(16), D(8));
        footer.SetGravity(GravityFlags.CenterVertical);
        summary = Text("");
        footer.AddView(summary, new LinearLayout.LayoutParams(0, -2, 1));
        var launch = ActionButton("Play", Launch);
        launch.Icon = GetDrawable(Resource.Drawable.ic_play_arrow);
        footer.AddView(launch);
        page.AddView(footer);
        RefreshSongs();
    }

    private void RefreshSongs()
    {
        var all = store.Read();
        var selected = all.Where(i => (i.Name + " " + i.Author).Contains(search, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        songAdapter?.SetItems(selected);
        if (summary != null)
        {
            var count = all.Count;
            summary.Text = $"{count} song{(count == 1 ? "" : "s")}";
        }
    }

    private void SongMenu(LibraryItem item)
    {
        if (busy)
            return;
        new MaterialAlertDialogBuilder(this).SetTitle("Delete " + item.Name + "?")!.SetMessage("This deletes the song and its .mdm file.")!.SetNegativeButton("Cancel", (_, _) =>
        {
        })!.SetPositiveButton("Delete", async (_, _) =>
        {
            operation = "Deleting song…";
            await Work(() =>
            {
                store.Remove(item.Id);
                notice = "Song deleted.";
                return Task.CompletedTask;
            });
        })!.Show();
    }

    private void SetSetupStep(int value)
    {
        setupStep = value;
        Preferences.Edit()!.PutInt("setup.step", value)!.Apply();
    }

    private bool IsGameInstalled() => PackageManager!.GetLaunchIntentForPackage(LibraryStore.GamePackage) != null;
    private void FinishSetup()
    {
        Preferences.Edit()!.PutBoolean("setup.complete", true)!.Apply();
        setup = false;
        settings = false;
        notice = "";
        Render();
    }

    private void RenderSetup()
    {
        if (setupStep == 0)
        {
            Gap(body, 24);
            body.AddView(Text("Set up Muse Dash", 28, true));
            Gap(body, 16);
            body.AddView(Text("Get the game ready, then add your .mdm songs.", 16));
            Gap(body, 32);
            body.AddView(ActionButton("Get started", () =>
            {
                SetSetupStep(1);
                Render();
            }));
            Gap(body, 12);
            if (IsGameInstalled())
                body.AddView(ActionButton("Skip setup", () =>
                {
                    FinishSetup();
                    Toast.MakeText(this, "You can run setup later in Settings.", ToastLength.Short)!.Show();
                }, true));
            return;
        }

        body.AddView(Text($"Step {setupStep} of 3", 14));
        Gap(body, 24);
        if (setupStep == 1)
        {
            body.AddView(Text("Choose your Muse Dash APK", 28, true));
            Gap(body, 16);
            body.AddView(Text("Pick the APK you saved on your phone.", 16));
            Gap(body, 32);
            body.AddView(ActionButton("Choose APK", () => Pick(42, false)));
            if (ApkPreparation.HasCurrentPreparedApk(this))
            {
                Gap(body, 12);
                body.AddView(ActionButton("Continue", () =>
                {
                    SetSetupStep(2);
                    Render();
                }, true));
            }
        }
        else if (setupStep == 2)
        {
            body.AddView(Text("Add the game data", 28, true));
            Gap(body, 16);
            body.AddView(Text("Choose the .obb file that came with your APK.", 16));
            Gap(body, 32);
            body.AddView(ActionButton("Choose OBB", () => Pick(43, false)));
            if (File.Exists(store.ExpansionPath))
            {
                Gap(body, 12);
                body.AddView(ActionButton("Continue", () =>
                {
                    SetSetupStep(3);
                    Render();
                }, true));
            }
        }
        else
        {
            body.AddView(Text("Install Muse Dash", 28, true));
            Gap(body, 16);
            body.AddView(Text("The game is ready to install. If Android asks you to remove your old copy, back up your saves first. Removing the game can delete your progress.", 16));
            Gap(body, 32);
            body.AddView(ActionButton("Install", InstallPrepared));
            Gap(body, 12);
            body.AddView(ActionButton("Done", () =>
            {
                if (!IsGameInstalled())
                {
                    Toast.MakeText(this, "Install Muse Dash first, then tap Done.", ToastLength.Long)!.Show();
                    return;
                }

                FinishSetup();
            }, true));
        }
    }

    private void RenderSettings()
    {
        Setting("Game setup", "Prepare or reinstall Muse Dash", () =>
        {
            setup = true;
            settings = false;
            SetSetupStep(0);
            Render();
        });
        var version = PackageManager!.GetPackageInfo(PackageName!, 0)!.VersionName;
        Setting("About", "Version " + version, () => new MaterialAlertDialogBuilder(this).SetTitle("Minkyu")!.SetMessage("Version " + version)!.SetPositiveButton("Close", (_, _) =>
        {
        })!.Show());
    }

    private void Setting(string title, string subtitle, Action action)
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Clickable = true,
            Focusable = true
        };
        row.SetPadding(0, D(20), 0, D(20));
        row.AddView(Text(title, 16, true));
        Gap(row, 5);
        row.AddView(Text(subtitle));
        row.Click += (_, _) => action();
        body.AddView(row);
    }

    private sealed class SongAdapter(MainActivity activity) : RecyclerView.Adapter
    {
        private List<LibraryItem> items = new();
        public void SetItems(List<LibraryItem> values)
        {
            items = values;
            NotifyDataSetChanged();
        }

        public override int ItemCount => items.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var row = new LinearLayout(activity)
            {
                Orientation = Orientation.Horizontal
            };
            row.SetGravity(GravityFlags.CenterVertical);
            row.SetPadding(activity.D(16), activity.D(12), activity.D(4), activity.D(12));
            row.LayoutParameters = new RecyclerView.LayoutParams(-1, -2);
            var cover = new Google.Android.Material.ImageView.ShapeableImageView(activity);
            cover.SetScaleType(ImageView.ScaleType.CenterCrop);
            cover.SetImageResource(Resource.Drawable.ic_music_note);
            row.AddView(cover, new LinearLayout.LayoutParams(activity.D(56), activity.D(56)) { RightMargin = activity.D(16) });
            var text = new LinearLayout(activity)
            {
                Orientation = Orientation.Vertical
            };
            var title = activity.Text("", 16, true);
            title.SetMaxLines(2);
            title.Ellipsize = TextUtils.TruncateAt.End;
            var author = activity.Text("", 14);
            author.SetSingleLine(true);
            author.Ellipsize = TextUtils.TruncateAt.End;
            var details = activity.Text("", 12);
            text.AddView(title);
            activity.Gap(text, 3);
            text.AddView(author);
            activity.Gap(text, 3);
            text.AddView(details);
            row.AddView(text, new LinearLayout.LayoutParams(0, -2, 1));
            var more = new AndroidX.AppCompat.Widget.AppCompatImageButton(activity)
            {
                ContentDescription = "Song options"
            };
            more.SetImageResource(Resource.Drawable.ic_more_vert);
            using (var value = new TypedValue())
            {
                activity.Theme!.ResolveAttribute(Android.Resource.Attribute.SelectableItemBackgroundBorderless, value, true);
                more.SetBackgroundResource(value.ResourceId);
            }

            more.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(activity.ThemeColor(Resource.Attribute.colorOnSurfaceVariant));
            row.AddView(more, new LinearLayout.LayoutParams(activity.D(48), activity.D(48)));
            return new SongHolder(row, cover, title, author, details, more);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var view = (SongHolder)holder;
            var item = items[position];
            view.Title.Text = item.Name;
            view.Author.Text = item.Author;
            view.Details.Text = $"Level {item.Difficulty}";
            view.Cover.SetImageResource(Resource.Drawable.ic_music_note);
            try
            {
                if (!activity.artwork.TryGetValue(item.Id, out var bitmap))
                {
                    var bytes = activity.store.Cover(item);
                    if (bytes != null)
                    {
                        bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, new BitmapFactory.Options { InSampleSize = 4 });
                        if (bitmap != null)
                            activity.artwork[item.Id] = bitmap;
                    }
                }

                if (bitmap != null)
                    view.Cover.SetImageBitmap(bitmap);
            }
            catch
            {
            }

            view.Item = item;
            view.More.ContentDescription = "Options for " + item.Name;
            if (!view.Bound)
            {
                view.More.Click += (_, _) => activity.SongMenu(view.Item!);
                view.ItemView.Click += (_, _) => activity.SongMenu(view.Item!);
                view.Bound = true;
            }
        }
    }

    private sealed class SongHolder(View root, ImageView cover, TextView title, TextView author, TextView details, View more) : RecyclerView.ViewHolder(root)
    {
        internal readonly ImageView Cover = cover;
        internal readonly TextView Title = title, Author = author, Details = details;
        internal readonly View More = more;
        internal LibraryItem? Item;
        internal bool Bound;
    }

    private sealed class PageInsets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout() | WindowInsets.Type.Ime());
                view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            }
            else
                view.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
            return insets;
        }
    }
}
