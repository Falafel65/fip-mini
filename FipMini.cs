// FipMini — mini lecteur FIP natif (WPF + contrôles média système Windows).
// Compilé avec le csc.exe livré avec Windows (C# 5) : voir build.ps1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using WF = System.Windows.Forms;
using WM = Windows.Media;
using WMC = Windows.Media.Core;
using WMP = Windows.Media.Playback;
using WSS = Windows.Storage.Streams;

sealed class Station
{
    public readonly string Name, Slug;
    public readonly int Id;
    public Station(string name, int id, string slug) { Name = name; Id = id; Slug = slug; }
    public string Aac { get { return "https://icecast.radiofrance.fr/" + Slug + "-hifi.aac"; } }
    public string Hls { get { return "https://stream.radiofrance.fr/" + Slug + "/" + Slug + "_hifi.m3u8"; } }
    public string Meta { get { return "https://api.radiofrance.fr/livemeta/pull/" + Id; } }
    public string Live { get { return "https://api.radiofrance.fr/livemeta/live/" + Id + "/webrf_fip_player"; } }
    public bool NoPull; // l'API détaillée n'existe pas pour cette webradio
}

sealed class Track
{
    public string Key = "", Title = "", Artist = "", Album = "", AlbumLine = "", Visual, Thumb, Link;
    public long Start, End;
}

static class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        var mutex = new Mutex(true, "FipMini.SingleInstance", out created);
        // Un second lancement réaffiche la fenêtre de l'instance déjà ouverte (souvent cachée dans la zone de notification).
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, "FipMini.Show");
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, "FipMini.Quit");
        bool askQuit = Array.IndexOf(Environment.GetCommandLineArgs(), "--quit") > 0; // FipMini.exe --quit : ferme proprement l'instance ouverte
        if (!created) { (askQuit ? quit : show).Set(); return; }
        if (askQuit) return;
        AppIdentity.Ensure();
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var app = new Application();
        var win = new MainWindow();
        ThreadPool.RegisterWaitForSingleObject(show,
            delegate { win.Dispatcher.BeginInvoke(new Action(win.ShowWindow)); }, null, -1, false);
        ThreadPool.RegisterWaitForSingleObject(quit,
            delegate { win.Dispatcher.BeginInvoke(new Action(win.Quit)); }, null, -1, false);
        app.SessionEnding += delegate { win.Quit(); };
        app.Run(win);
        GC.KeepAlive(mutex);
    }
}

// Identité de l'application pour Windows. Sans elle, le panneau média affiche « Application inconnue » :
// Windows retrouve le nom et l'icône via un raccourci du menu Démarrer portant le même AppUserModelID.
static class AppIdentity
{
    const string AppId = "FipMini", ShortcutName = "FIP Mini.lnk";

    public static void Ensure()
    {
        SetCurrentProcessExplicitAppUserModelID(AppId);
        try
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            var link = (IShellLinkW)new ShellLink();
            var file = (System.Runtime.InteropServices.ComTypes.IPersistFile)link;
            if (File.Exists(lnk))
            {
                // Déjà en place et à jour : on ne touche à rien.
                file.Load(lnk, 0);
                var sb = new StringBuilder(520);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                if (string.Equals(sb.ToString(), exe, StringComparison.OrdinalIgnoreCase)) return;
            }
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe));
            link.SetDescription("Mini lecteur FIP");
            var key = new PropertyKey { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 }; // System.AppUserModel.ID
            var value = new PropVariant { vt = 31 /* VT_LPWSTR */, p = Marshal.StringToCoTaskMemUni(AppId) };
            var store = (IPropertyStore)link;
            store.SetValue(ref key, ref value);
            store.Commit();
            Marshal.FreeCoTaskMem(value.p);
            file.Save(lnk, true);
        }
        catch (Exception) { }
    }

    [DllImport("shell32.dll")]
    static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string id);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PropertyKey { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort vt; ushort r1, r2, r3; public IntPtr p; IntPtr p2; }
}

sealed class MainWindow : Window
{
    static readonly Station[] Stations = {
        new Station("FIP", 7, "fip"),
        new Station("FIP Rock", 64, "fiprock"),
        new Station("FIP Jazz", 65, "fipjazz"),
        new Station("FIP Groove", 66, "fipgroove"),
        new Station("FIP Monde", 69, "fipworld"),
        new Station("FIP Nouveautés", 70, "fipnouveautes"),
        new Station("FIP Reggae", 71, "fipreggae"),
        new Station("FIP Electro", 74, "fipelectro"),
        new Station("FIP Metal", 77, "fipmetal"),
        new Station("FIP Pop", 78, "fippop"),
        new Station("FIP Hip-Hop", 95, "fiphiphop"),
        new Station("FIP Sacré français", 96, "fipsacrefrancais"),
        new Station("FIP Cultes", 709, "fipcultes"),
    };

    static readonly Brush Bg = Frozen("#141414"), Fg = Frozen("#F2F2F2"), Dim = Frozen("#9A9A9A"),
                          Track = Frozen("#333333"), Accent = Frozen("#E2007A"), Hover = Frozen("#2A2A2A");
    const string GlyphPlay = "", GlyphPause = "", GlyphClose = "", GlyphMin = "";
    static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

    readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FipMini", "settings.ini");

    // Lecture
    readonly WMP.MediaPlayer player = new WMP.MediaPlayer();
    readonly WM.SystemMediaTransportControls smtc;
    Station station = Stations[0];
    bool wantPlay, useHls;
    int failures;

    // Métadonnées
    readonly DispatcherTimer pollTimer = new DispatcherTimer();
    readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
    int generation;
    string currentKey = "";
    long curStart, curEnd;
    string linkUrl;

    // UI
    readonly Image cover = new Image { Stretch = Stretch.UniformToFill };
    readonly TextBlock stationText, titleText, artistText, albumText, nextText, elapsedText, remainText, playGlyph;
    readonly ProgressBar progress = Bar(), volumeBar = Bar();
    readonly ThumbButtonInfo thumbButton = new ThumbButtonInfo();
    readonly WF.NotifyIcon tray = new WF.NotifyIcon();
    bool quitting;
    readonly Border volBox;
    readonly TextBlock volText;
    readonly DispatcherTimer volTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
    static readonly ImageSource PlayImg = GlyphImage("M0,0 L0,14 L12,7 Z"),
                                PauseImg = GlyphImage("M0,0 H4 V14 H0 Z M8,0 H12 V14 H8 Z");

    public MainWindow()
    {
        Title = "FIP";
        Width = 440; Height = 148;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanMinimize;
        Background = Bg;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(0) });

        // --- Mise en page ---
        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var coverBox = new Border { Background = Accent, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand,
                                    ToolTip = "Ouvrir le titre (YouTube / Apple Music)" };
        var coverGrid = new Grid();
        coverGrid.Children.Add(new Image { Source = LogoImage(), Stretch = Stretch.Uniform }); // visible sans pochette
        coverGrid.Children.Add(cover);
        coverBox.Child = coverGrid;
        coverBox.SizeChanged += delegate {
            coverGrid.Clip = new RectangleGeometry(new Rect(0, 0, coverBox.ActualWidth, coverBox.ActualHeight), 6, 6); };
        coverBox.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
        coverBox.MouseLeftButtonUp += delegate { OpenLink(); };
        root.Children.Add(coverBox);

        var right = new Grid();
        Grid.SetColumn(right, 2);
        for (int i = 0; i < 7; i++)
            right.RowDefinitions.Add(new RowDefinition { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        root.Children.Add(right);

        // Ligne 0 : station + boutons de fenêtre
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, -4, -6, 2) };
        var close = IconButton(GlyphClose, 10, 26, delegate { Hide(); });
        close.ToolTip = "Réduire dans la zone de notification";
        var min = IconButton(GlyphMin, 10, 26, delegate { WindowState = WindowState.Minimized; });
        DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(min, Dock.Right);
        header.Children.Add(close); header.Children.Add(min);
        stationText = Txt(11, Accent, FontWeights.SemiBold);
        stationText.Cursor = Cursors.Hand;
        stationText.VerticalAlignment = VerticalAlignment.Center;
        stationText.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
        stationText.MouseLeftButtonUp += delegate { ToggleDrawer(); };
        stationText.ToolTip = "Choisir une webradio";
        header.Children.Add(stationText);
        right.Children.Add(header);

        titleText = Txt(15, Fg, FontWeights.SemiBold); Place(right, titleText, 1);
        artistText = Txt(13, Fg, FontWeights.Normal); Place(right, artistText, 2);
        albumText = Txt(11, Dim, FontWeights.Normal); albumText.Margin = new Thickness(0, 2, 0, 0); Place(right, albumText, 3);

        // Ligne 5 : progression du titre
        var prog = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        prog.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        prog.ColumnDefinitions.Add(new ColumnDefinition());
        prog.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        elapsedText = Txt(10, Dim, FontWeights.Normal); remainText = Txt(10, Dim, FontWeights.Normal);
        progress.Margin = new Thickness(8, 0, 8, 0); progress.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(progress, 1); Grid.SetColumn(remainText, 2);
        prog.Children.Add(elapsedText); prog.Children.Add(progress); prog.Children.Add(remainText);
        Place(right, prog, 5);

        // Ligne 6 : lecture/pause, volume, à suivre
        var controls = new DockPanel { LastChildFill = true };
        playGlyph = new TextBlock { FontFamily = Icons, FontSize = 13, Foreground = Brushes.White, Text = GlyphPlay,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var play = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Accent,
                                Child = playGlyph, Cursor = Cursors.Hand, ToolTip = "Lecture / Pause (Espace)" };
        play.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
        play.MouseLeftButtonUp += delegate { Toggle(); };
        controls.Children.Add(play);

        volumeBar.Maximum = 1; volumeBar.VerticalAlignment = VerticalAlignment.Center;
        volBox = new Border { Width = 64, Height = 20, Background = Brushes.Transparent, Child = volumeBar,
                              Margin = new Thickness(10, 0, 10, 0), Cursor = Cursors.Hand };
        MouseButtonEventHandler volDown = delegate(object s, MouseButtonEventArgs e) {
            volBox.CaptureMouse(); SetVolume(e.GetPosition(volBox).X / volBox.ActualWidth); e.Handled = true; };
        volBox.MouseLeftButtonDown += volDown;
        volBox.MouseMove += delegate(object s, MouseEventArgs e) {
            if (volBox.IsMouseCaptured) SetVolume(e.GetPosition(volBox).X / volBox.ActualWidth); };
        volBox.MouseLeftButtonUp += delegate { volBox.ReleaseMouseCapture(); };
        controls.Children.Add(volBox);

        // Pourcentage affiché brièvement à chaque changement de volume
        volText = Txt(11, Fg, FontWeights.SemiBold);
        volText.VerticalAlignment = VerticalAlignment.Center;
        volText.Margin = new Thickness(0, 0, 10, 0);
        volText.Visibility = Visibility.Collapsed;
        controls.Children.Add(volText);
        volTimer.Tick += delegate { volTimer.Stop(); volText.Visibility = Visibility.Collapsed; };

        nextText = Txt(11, Dim, FontWeights.Normal); nextText.VerticalAlignment = VerticalAlignment.Center;
        controls.Children.Add(nextText);
        Place(right, controls, 6);

        // Lecteur en haut (hauteur fixe), tiroir des webradios en dessous
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BaseHeight) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.Children.Add(root);
        Place(outer, BuildDrawer(), 1);
        Content = outer;

        // --- Interactions fenêtre ---
        MouseLeftButtonDown += delegate { try { DragMove(); } catch (InvalidOperationException) { } };
        MouseWheel += delegate(object s, MouseWheelEventArgs e) { SetVolume(player.Volume + e.Delta / 120 * 0.05); };
        KeyDown += delegate(object s, KeyEventArgs e) {
            if (e.Key == Key.Space) Toggle();
            else if (e.Key == Key.Escape) WindowState = WindowState.Minimized;
        };
        SourceInitialized += delegate {
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref round, sizeof(int));
        };

        // Bouton lecture/pause dans l'aperçu de la barre des tâches
        thumbButton.Description = "Lecture / Pause";
        thumbButton.ImageSource = PlayImg;
        thumbButton.Click += delegate { Toggle(); };
        TaskbarItemInfo = new TaskbarItemInfo();
        TaskbarItemInfo.ThumbButtonInfos.Add(thumbButton);

        // --- Contrôles média système (SMTC) : pilotés à la main ---
        player.CommandManager.IsEnabled = false;
        player.AudioCategory = WMP.MediaPlayerAudioCategory.Media;
        smtc = player.SystemMediaTransportControls;
        smtc.IsEnabled = true;
        smtc.IsPlayEnabled = smtc.IsPauseEnabled = smtc.IsStopEnabled = true;
        smtc.IsNextEnabled = smtc.IsPreviousEnabled = true;
        smtc.ButtonPressed += delegate(WM.SystemMediaTransportControls s, WM.SystemMediaTransportControlsButtonPressedEventArgs e) {
            var b = e.Button;
            Dispatcher.BeginInvoke(new Action(delegate { OnSystemButton(b); }));
        };
        player.MediaFailed += delegate { Dispatcher.BeginInvoke(new Action(OnStreamLost)); };
        player.MediaEnded += delegate { Dispatcher.BeginInvoke(new Action(OnStreamLost)); };
        player.MediaOpened += delegate { failures = 0; };
        retryTimer.Tick += delegate { retryTimer.Stop(); if (wantPlay) StartStream(); };

        pollTimer.Tick += delegate { Poll(); };
        clock.Tick += delegate { UpdateClock(); };
        clock.Start();

        LoadSettings();
        BuildMenu();
        BuildTray();
        // Après le démarrage du Dispatcher, pour que les await reviennent sur le thread UI.
        Loaded += delegate {
            SetStation(station, true);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--drawer") > 0) ToggleDrawer(); // démarre tiroir ouvert
        };
    }

    // ---------- Lecture ----------

    void Toggle() { if (wantPlay) Pause(); else Play(); }

    void Play() { wantPlay = true; failures = 0; StartStream(); RefreshPlayState(); }

    void Pause()
    {
        // Flux direct : on coupe la connexion plutôt que de mettre en tampon.
        wantPlay = false;
        retryTimer.Stop();
        player.Pause();
        player.Source = null;
        RefreshPlayState();
    }

    void StartStream()
    {
        player.Source = WMC.MediaSource.CreateFromUri(new Uri(useHls ? station.Hls : station.Aac));
        player.Play();
    }

    void OnStreamLost()
    {
        if (!wantPlay) return;
        failures++;
        useHls = !useHls; // on alterne Icecast AAC / HLS
        retryTimer.Interval = TimeSpan.FromSeconds(Math.Min(30, 2 * failures));
        retryTimer.Stop(); retryTimer.Start();
    }

    void RefreshPlayState()
    {
        playGlyph.Text = wantPlay ? GlyphPause : GlyphPlay;
        thumbButton.ImageSource = wantPlay ? PauseImg : PlayImg;
        smtc.PlaybackStatus = wantPlay ? WM.MediaPlaybackStatus.Playing : WM.MediaPlaybackStatus.Paused;
    }

    void OnSystemButton(WM.SystemMediaTransportControlsButton b)
    {
        switch (b)
        {
            case WM.SystemMediaTransportControlsButton.Play: Play(); break;
            case WM.SystemMediaTransportControlsButton.Pause:
            case WM.SystemMediaTransportControlsButton.Stop: Pause(); break;
            case WM.SystemMediaTransportControlsButton.Next: StepStation(1); break;
            case WM.SystemMediaTransportControlsButton.Previous: StepStation(-1); break;
        }
    }

    void SetVolume(double v)
    {
        v = Math.Max(0, Math.Min(1, v));
        player.Volume = v;
        volumeBar.Value = v;
        string pct = Math.Round(v * 100) + " %";
        volBox.ToolTip = "Volume : " + pct + " (molette)";
        if (!IsLoaded) return; // pas d'affichage au chargement des réglages
        volText.Text = pct;
        volText.Visibility = Visibility.Visible;
        volTimer.Stop(); volTimer.Start();
    }

    void StepStation(int delta)
    {
        int i = Array.IndexOf(Stations, station);
        SetStation(Stations[(i + delta + Stations.Length) % Stations.Length], wantPlay);
    }

    void SetStation(Station s, bool autoplay)
    {
        station = s;
        generation++;
        useHls = false;
        currentKey = "";
        foreach (object o in ContextMenu.Items)
        {
            var mi = o as MenuItem;
            if (mi != null && mi.Tag is Station) mi.IsChecked = mi.Tag == s;
        }
        ShowTrack(null, null, null);
        RefreshTiles();
        if (autoplay) Play(); else RefreshPlayState();
        Poll();
    }

    // ---------- Métadonnées ----------

    async void Poll()
    {
        pollTimer.Stop();
        int gen = generation;
        Station st = station;
        double wait = 10;
        try
        {
            Track cur = null, next = null;
            string show = "";
            if (!st.NoPull)
            {
                // API détaillée (album, année, label) ; absente pour certaines webradios.
                bool ok = false;
                try
                {
                    string json = await Fetch(st.Meta);
                    if (gen != generation) return;
                    ok = ParsePull(json, out cur, out show, out next);
                }
                catch (WebException ex)
                {
                    var r = ex.Response as HttpWebResponse;
                    if (r == null || r.StatusCode != HttpStatusCode.NotFound) throw;
                }
                if (!ok) st.NoPull = true;
            }
            if (st.NoPull)
            {
                string json = await Fetch(st.Live);
                if (gen != generation) return;
                ParseLive(json, out cur, out show, out next);
            }
            ShowTrack(cur, show, next);
            if (cur != null) wait = Math.Max(3, Math.Min(60, cur.End - Now() + 2));
        }
        catch (Exception) { wait = 15; }
        if (gen != generation) return;
        pollTimer.Interval = TimeSpan.FromSeconds(wait);
        pollTimer.Start();
    }

    static async Task<string> Fetch(string url)
    {
        using (var wc = new WebClient { Encoding = Encoding.UTF8 })
        {
            wc.Headers[HttpRequestHeader.UserAgent] = "FipMini/1.0";
            return await wc.DownloadStringTaskAsync(url);
        }
    }

    // livemeta/pull/{id} : liste d'étapes horodatées, avec album, année, label, liens.
    static bool ParsePull(string json, out Track cur, out string show, out Track next)
    {
        cur = null; next = null; show = "";
        var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
        var steps = root != null && root.ContainsKey("steps") ? root["steps"] as Dictionary<string, object> : null;
        if (steps == null) return false;
        long now = Now();
        Dictionary<string, object> c = null, sh = null, n = null;
        foreach (var kv in steps)
        {
            var s = kv.Value as Dictionary<string, object>;
            if (s == null) continue;
            long st = L(s, "start"), en = L(s, "end");
            bool song = S(s, "embedType") == "song";
            if (st <= now && now < en)
            {
                if (song) { if (c == null || st > L(c, "start")) c = s; }
                else if (sh == null || L(s, "depth") > L(sh, "depth")) sh = s;
            }
            else if (song && st > now && (n == null || st < L(n, "start"))) n = s;
        }
        if (c != null) cur = StepTrack(c);
        if (n != null) next = StepTrack(n);
        if (sh != null) show = S(sh, "title").Trim();
        return true;
    }

    static Track StepTrack(Dictionary<string, object> d)
    {
        var t = new Track();
        t.Key = S(d, "stepId");
        t.Title = S(d, "title");
        t.Artist = Artist(d);
        t.Album = S(d, "titreAlbum");
        t.AlbumLine = Join("  ·  ", t.Album, S(d, "anneeEditionMusique"), S(d, "label"));
        t.Visual = t.Thumb = S(d, "visual");
        t.Link = S(d, "lienYoutube") != "" ? S(d, "lienYoutube") : S(d, "path");
        t.Start = L(d, "start"); t.End = L(d, "end");
        return t;
    }

    // livemeta/live/{id}/webrf_fip_player : titre en cours et suivant, disponible pour toutes les webradios.
    static void ParseLive(string json, out Track cur, out string show, out Track next)
    {
        cur = null; next = null; show = "";
        var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
        if (root == null) return;
        object o;
        var now = root.TryGetValue("now", out o) ? o as Dictionary<string, object> : null;
        if (now != null)
        {
            cur = LiveTrack(now, false);
            string first = S(now, "firstLine").Trim();
            if (cur == null && first != "Le direct") show = first;
        }
        var arr = root.TryGetValue("next", out o) ? o as object[] : null;
        if (arr != null && arr.Length > 0) next = LiveTrack(arr[0] as Dictionary<string, object>, true);
    }

    static Track LiveTrack(Dictionary<string, object> d, bool allowShow)
    {
        if (d == null) return null;
        bool song = S(d, "songUuid") != "";
        if (!song && !allowShow) return null;
        var t = new Track();
        t.Title = S(d, "firstLine").Trim();
        t.Artist = song ? S(d, "secondLine").Trim() : "";
        t.Start = L(d, "startTime"); t.End = L(d, "endTime");
        t.Key = S(d, "songUuid") + "|" + t.Start;
        string c = S(d, "cover");
        if (c != "")
        {
            t.Visual = "https://www.radiofrance.fr/pikapi/images/" + c + "/400x400";
            t.Thumb = "https://www.radiofrance.fr/pikapi/images/" + c + "/88x88";
        }
        return t;
    }

    void RefreshStationLabel()
    {
        stationText.Text = (station.Name + (showTitleCur != "" ? "  ·  " + showTitleCur : "")).ToUpperInvariant()
                           + (drawerOpen ? "  ▴" : "  ▾");
    }

    void ShowTrack(Track cur, string showTitle, Track next)
    {
        showTitleCur = showTitle ?? "";
        RefreshStationLabel();
        nextText.Text = next != null ? "À suivre : " + Join(" - ", next.Artist, next.Title) : "";
        nextText.ToolTip = nextText.Text == "" ? null : nextText.Text;

        string key = cur != null ? cur.Key : "none|" + showTitleCur;
        if (key == currentKey) return;
        currentKey = key;

        string title, artist, album, visual = null;
        if (cur != null)
        {
            title = cur.Title;
            artist = cur.Artist;
            album = cur.AlbumLine;
            visual = cur.Visual;
            curStart = cur.Start; curEnd = cur.End;
            linkUrl = cur.Link;
        }
        else
        {
            title = showTitleCur != "" ? showTitleCur : station.Name;
            artist = "En direct";
            album = "";
            curStart = curEnd = 0;
            linkUrl = null;
        }
        titleText.Text = title; titleText.ToolTip = title;
        artistText.Text = artist; artistText.ToolTip = artist;
        albumText.Text = album; albumText.ToolTip = album == "" ? null : album;
        Title = cur != null ? Join(" — ", artist, title) + " · " + station.Name : station.Name;
        tray.Text = Title.Length > 63 ? Title.Substring(0, 62) + "…" : Title; // limite NotifyIcon : 63 caractères
        UpdateClock();

        // Contrôles média système : titre, artiste, album, pochette
        Uri art = null;
        if (!string.IsNullOrEmpty(visual)) Uri.TryCreate(visual, UriKind.Absolute, out art);
        if (art != null && art.Scheme != "https") art = null;
        var du = smtc.DisplayUpdater;
        du.Type = WM.MediaPlaybackType.Music;
        du.MusicProperties.Title = title;
        du.MusicProperties.Artist = cur != null ? artist : station.Name;
        du.MusicProperties.AlbumTitle = cur != null ? cur.Album : "";
        du.Thumbnail = art != null ? WSS.RandomAccessStreamReference.CreateFromUri(art) : null;
        du.Update();

        cover.Source = null;
        if (art != null) LoadCover(art, key);
    }

    // ---------- Tiroir des webradios ----------

    sealed class Tile
    {
        public Station St; public Border Box; public Image Img; public TextBlock Name, Line; public string Thumb; public long End;
    }

    const double BaseHeight = 148;
    static readonly Brush Selected = Frozen("#262626");
    static readonly ImageSource Logo = LogoImage();
    readonly List<Tile> tiles = new List<Tile>();
    readonly DispatcherTimer drawerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
    Border drawer;
    bool drawerOpen;
    double shiftedTop = double.NaN, topBeforeDrawer;
    string showTitleCur = "";

    UIElement BuildDrawer()
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var s in Stations)
        {
            var tile = new Tile { St = s };
            tile.Img = new Image { Stretch = Stretch.UniformToFill };
            var art = new Grid { Width = 36, Height = 36, Clip = new RectangleGeometry(new Rect(0, 0, 36, 36), 4, 4) };
            art.Children.Add(new Image { Source = Logo });
            art.Children.Add(tile.Img);
            tile.Name = Txt(10, Dim, FontWeights.SemiBold);
            tile.Name.Text = s.Name.ToUpperInvariant();
            tile.Line = Txt(12, Fg, FontWeights.Normal);
            tile.Line.Text = "…";
            var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(tile.Name);
            text.Children.Add(tile.Line);
            var row = new DockPanel();
            row.Children.Add(art);
            row.Children.Add(text);
            tile.Box = new Border { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(1), CornerRadius = new CornerRadius(6),
                                    Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row };
            var t = tile;
            t.Box.MouseEnter += delegate { if (t.St != station) t.Box.Background = Hover; };
            t.Box.MouseLeave += delegate { RefreshTiles(); };
            t.Box.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { e.Handled = true; };
            t.Box.MouseLeftButtonUp += delegate { SetStation(t.St, true); };
            tiles.Add(tile);
            grid.Children.Add(tile.Box);
        }
        drawer = new Border { Padding = new Thickness(9, 8, 9, 9), BorderBrush = Track, BorderThickness = new Thickness(0, 1, 0, 0),
                              Child = grid, Visibility = Visibility.Collapsed };
        // Les autres webradios ne sont interrogées que tiroir ouvert et fenêtre visible.
        drawerTimer.Tick += delegate {
            if (drawerOpen && IsVisible && WindowState != WindowState.Minimized) RefreshDrawer(); };
        return drawer;
    }

    void ToggleDrawer()
    {
        drawerOpen = !drawerOpen;
        drawer.Visibility = drawerOpen ? Visibility.Visible : Visibility.Collapsed;
        if (drawerOpen)
        {
            drawer.Measure(new Size(Width, double.PositiveInfinity));
            Height = BaseHeight + drawer.DesiredSize.Height;
            try
            {
                // Si le tiroir dépasse en bas de l'écran, on remonte la fenêtre le temps qu'il est ouvert.
                var wa = WF.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
                double scale = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M22;
                double bottom = wa.Bottom / scale;
                if (Top + Height > bottom)
                {
                    topBeforeDrawer = Top;
                    Top = shiftedTop = Math.Max(wa.Top / scale, bottom - Height);
                }
            }
            catch (Exception) { }
            RefreshTiles();
            RefreshDrawer();
            drawerTimer.Start();
        }
        else
        {
            drawerTimer.Stop();
            Height = BaseHeight;
            if (!double.IsNaN(shiftedTop) && Top == shiftedTop) Top = topBeforeDrawer; // sauf si déplacée entre-temps
            shiftedTop = double.NaN;
        }
        RefreshStationLabel();
    }

    void RefreshTiles()
    {
        foreach (var t in tiles)
        {
            bool sel = t.St == station;
            t.Box.Background = sel ? Selected : (t.Box.IsMouseOver ? Hover : Brushes.Transparent);
            t.Name.Foreground = sel ? Accent : Dim;
        }
    }

    void RefreshDrawer() { foreach (var t in tiles) LoadTile(t); }

    async void LoadTile(Tile t)
    {
        try
        {
            string json = await Fetch(t.St.Live);
            Track cur, next;
            string show;
            ParseLive(json, out cur, out show, out next);
            // L'API renvoie parfois « Le direct » en plein morceau : on garde alors le titre déjà affiché.
            if (cur == null && Now() < t.End) return;
            t.End = cur != null ? cur.End : 0;
            string line = cur != null ? Join(" - ", cur.Artist, cur.Title) : (show != "" ? show : "En direct");
            t.Line.Text = line;
            t.Box.ToolTip = line;
            string thumb = cur != null ? cur.Thumb : null;
            if (thumb == t.Thumb) return;
            t.Thumb = thumb;
            t.Img.Source = null;
            if (thumb == null) return;
            byte[] data;
            using (var wc = new WebClient()) data = await wc.DownloadDataTaskAsync(thumb);
            if (t.Thumb != thumb) return;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 72;
            bi.StreamSource = new MemoryStream(data);
            bi.EndInit();
            bi.Freeze();
            t.Img.Source = bi;
        }
        catch (Exception) { }
    }

    async void LoadCover(Uri url, string key)
    {
        try
        {
            byte[] data;
            using (var wc = new WebClient()) data = await wc.DownloadDataTaskAsync(url);
            if (key != currentKey) return;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 248;
            bi.StreamSource = new MemoryStream(data);
            bi.EndInit();
            bi.Freeze();
            cover.Source = bi;
        }
        catch (Exception) { }
    }

    void UpdateClock()
    {
        long now = Now();
        if (curEnd > curStart && now >= curStart)
        {
            long len = curEnd - curStart, pos = Math.Min(len, now - curStart);
            progress.Maximum = len; progress.Value = pos;
            elapsedText.Text = Mmss(pos); remainText.Text = "-" + Mmss(len - pos);
        }
        else
        {
            progress.Value = 0;
            elapsedText.Text = "direct"; remainText.Text = "";
        }
    }

    void OpenLink()
    {
        Uri u;
        if (linkUrl != null && Uri.TryCreate(linkUrl, UriKind.Absolute, out u) && u.Scheme == "https")
            try { Process.Start(u.AbsoluteUri); } catch (Exception) { }
    }

    // ---------- Menu, réglages ----------

    void BuildMenu()
    {
        var menu = new ContextMenu();
        foreach (var s in Stations)
        {
            var st = s;
            var mi = new MenuItem { Header = s.Name, IsCheckable = true, Tag = s };
            mi.Click += delegate { SetStation(st, wantPlay); };
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var top = new MenuItem { Header = "Toujours au premier plan", IsCheckable = true, IsChecked = Topmost };
        top.Click += delegate { Topmost = top.IsChecked; };
        menu.Items.Add(top);
        var link = new MenuItem { Header = "Ouvrir le titre (YouTube / Apple Music)" };
        link.Click += delegate { OpenLink(); };
        menu.Items.Add(link);
        var quit = new MenuItem { Header = "Quitter" };
        quit.Click += delegate { Quit(); };
        menu.Items.Add(quit);
        ContextMenu = menu;
    }

    // ---------- Zone de notification ----------

    void BuildTray()
    {
        try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
        catch (Exception) { tray.Icon = System.Drawing.SystemIcons.Application; }
        tray.Text = "FIP";
        tray.MouseClick += delegate(object s, WF.MouseEventArgs e) { if (e.Button == WF.MouseButtons.Left) ShowWindow(); };
        var menu = new WF.ContextMenuStrip();
        menu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) {
            // Reconstruit à chaque ouverture pour refléter l'état courant.
            menu.Items.Clear();
            menu.Items.Add(wantPlay ? "Pause" : "Lecture", null, delegate { Toggle(); });
            menu.Items.Add("Afficher", null, delegate { ShowWindow(); });
            menu.Items.Add(new WF.ToolStripSeparator());
            foreach (var st in Stations)
            {
                var target = st;
                var item = new WF.ToolStripMenuItem(st.Name, null, delegate { SetStation(target, wantPlay); });
                item.Checked = st == station;
                menu.Items.Add(item);
            }
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Quitter", null, delegate { Quit(); });
            e.Cancel = false;
        };
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
    }

    public void ShowWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void Quit() { quitting = true; Close(); }

    // Fermer la fenêtre (×, Alt+F4) la cache ; seul « Quitter » termine l'application.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!quitting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    void LoadSettings()
    {
        double vol = 0.8;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try
        {
            if (File.Exists(settingsPath))
            {
                var d = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(settingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                }
                string v;
                if (d.TryGetValue("station", out v))
                    foreach (var s in Stations) if (s.Slug == v) station = s;
                if (d.TryGetValue("volume", out v)) vol = double.Parse(v, CultureInfo.InvariantCulture);
                if (d.TryGetValue("topmost", out v)) Topmost = v == "1";
                string l, t;
                if (d.TryGetValue("left", out l) && d.TryGetValue("top", out t))
                {
                    double x = double.Parse(l, CultureInfo.InvariantCulture), y = double.Parse(t, CultureInfo.InvariantCulture);
                    // On ne restaure la position que si elle est encore visible à l'écran.
                    if (x >= SystemParameters.VirtualScreenLeft && y >= SystemParameters.VirtualScreenTop &&
                        x + Width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                        y + Height <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Left = x; Top = y;
                    }
                }
            }
        }
        catch (Exception) { }
        SetVolume(vol);
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            if (drawerOpen) ToggleDrawer(); // pour enregistrer la position sans le décalage du tiroir
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            var b = RestoreBounds;
            File.WriteAllLines(settingsPath, new[] {
                "station=" + station.Slug,
                "volume=" + player.Volume.ToString("0.00", CultureInfo.InvariantCulture),
                "topmost=" + (Topmost ? "1" : "0"),
                "left=" + (b.IsEmpty ? Left : b.Left).ToString("0", CultureInfo.InvariantCulture),
                "top=" + (b.IsEmpty ? Top : b.Top).ToString("0", CultureInfo.InvariantCulture),
            });
        }
        catch (Exception) { }
        tray.Visible = false;
        tray.Dispose();
        player.Dispose();
        base.OnClosed(e);
    }

    // ---------- Utilitaires ----------

    static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
    static string Mmss(long s) { return (s / 60) + ":" + (s % 60).ToString("00"); }

    static string S(Dictionary<string, object> d, string k)
    {
        object v;
        return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
    }

    static long L(Dictionary<string, object> d, string k)
    {
        object v;
        if (!d.TryGetValue(k, out v) || v == null) return 0;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
    }

    static string Artist(Dictionary<string, object> s)
    {
        string a = S(s, "authors");
        return a != "" ? a : S(s, "performers");
    }

    static string Join(string sep, params string[] parts)
    {
        var l = new List<string>();
        foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) l.Add(p.Trim());
        return string.Join(sep, l);
    }

    static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static TextBlock Txt(double size, Brush fg, FontWeight weight)
    {
        return new TextBlock { FontSize = size, Foreground = fg, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis };
    }

    static void Place(Grid g, UIElement e, int row) { Grid.SetRow(e, row); g.Children.Add(e); }

    static ProgressBar Bar()
    {
        return new ProgressBar { Height = 3, BorderThickness = new Thickness(0), Background = Track, Foreground = Accent };
    }

    static Border IconButton(string glyph, double size, double box, Action click)
    {
        var b = new Border { Width = box, Height = box - 4, Background = Brushes.Transparent, CornerRadius = new CornerRadius(4),
            Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = Dim,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        b.MouseEnter += delegate { b.Background = Hover; };
        b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
        b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
        b.MouseLeftButtonUp += delegate { click(); };
        return b;
    }

    // Logo FIP : fip.svg embarqué en ressource, ses <path> sont dessinés tels quels.
    static ImageSource LogoImage()
    {
        try
        {
            string svg;
            using (var r = new StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("fip.svg")))
                svg = r.ReadToEnd();
            var group = new DrawingGroup();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(svg, "<path fill=\"([^\"]+)\" d=\"([^\"]+)\""))
                group.Children.Add(new GeometryDrawing(Frozen(m.Groups[1].Value), null, Geometry.Parse(m.Groups[2].Value)));
            var img = new DrawingImage(group);
            img.Freeze();
            return img;
        }
        catch (Exception) { return null; }
    }

    static ImageSource GlyphImage(string path)
    {
        var img = new DrawingImage(new GeometryDrawing(Brushes.White, null, Geometry.Parse(path)));
        img.Freeze();
        return img;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
