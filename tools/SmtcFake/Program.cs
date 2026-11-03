using Windows.Media;
using Windows.Media.Control;
using Windows.Media.Playback;

// SmtcFake sessions                      — показать медиасессии Windows и проверить, стабильны ли их объекты
// SmtcFake play <title> <artist> <dur> <timeline:0|1> <seconds> [<title2> <artist2> <dur2>]
//   — беззвучная медиасессия «играет» трек; с timeline=0 время трека не сообщается (как Firefox и часть плееров)
if (args.Length > 0 && args[0] == "sessions")
{
    var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    var a = mgr.GetSessions();
    var b = mgr.GetSessions();
    Console.WriteLine($"sessions: {a.Count}");
    for (int i = 0; i < a.Count; i++)
    {
        var s = a[i];
        var p = await s.TryGetMediaPropertiesAsync();
        var t = s.GetTimelineProperties();
        Console.WriteLine($"  {s.SourceAppUserModelId} | {p.Artist} — {p.Title} | {s.GetPlaybackInfo().PlaybackStatus} | end {t.EndTime} pos {t.Position} upd {t.LastUpdatedTime:HH:mm:ss}");
        Console.WriteLine($"    same object across calls: {ReferenceEquals(a[i], b[i])}, current same: {ReferenceEquals(mgr.GetCurrentSession(), mgr.GetCurrentSession())}");
    }
    return;
}

string title = args[1], artist = args[2];
double dur = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
bool timeline = args[4] == "1";
int seconds = int.Parse(args[5]);

var player = new MediaPlayer();
player.CommandManager.IsEnabled = false;
var smtc = player.SystemMediaTransportControls;
smtc.IsEnabled = true;
smtc.IsPlayEnabled = smtc.IsPauseEnabled = smtc.IsNextEnabled = smtc.IsPreviousEnabled = true;
bool playing = true;
var started = DateTimeOffset.Now;
double pausedAt = 0;

void Show(string t, string a, double d)
{
    var du = smtc.DisplayUpdater;
    du.Type = MediaPlaybackType.Music;
    du.MusicProperties.Title = t;
    du.MusicProperties.Artist = a;
    du.Update();
    started = DateTimeOffset.Now;
    if (timeline)
        smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(d), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(d),
            Position = TimeSpan.Zero,
        });
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} now playing: {a} — {t} ({d}s, timeline {timeline})");
}

smtc.ButtonPressed += (_, e) =>
{
    if (e.Button == SystemMediaTransportControlsButton.Pause) { playing = false; pausedAt = (DateTimeOffset.Now - started).TotalSeconds; smtc.PlaybackStatus = MediaPlaybackStatus.Paused; }
    if (e.Button == SystemMediaTransportControlsButton.Play) { playing = true; started = DateTimeOffset.Now.AddSeconds(-pausedAt); smtc.PlaybackStatus = MediaPlaybackStatus.Playing; }
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} button {e.Button}");
};
smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
Show(title, artist, dur);
if (args.Length >= 9)
{
    await Task.Delay(seconds * 1000 / 2);
    Show(args[6], args[7], double.Parse(args[8], System.Globalization.CultureInfo.InvariantCulture));
    await Task.Delay(seconds * 1000 / 2);
}
else await Task.Delay(seconds * 1000);
smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
smtc.IsEnabled = false;
Console.WriteLine("done");
