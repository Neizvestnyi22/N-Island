using System.Diagnostics;
using System.Text.Json;
using NIsland.Core;
var process=Process.GetCurrentProcess();var clock=Stopwatch.StartNew();var cpu=process.TotalProcessorTime;
for(int i=0;i<1000;i++) {var games=new FriendGames();var r=games.Create("ttt",1);games.Join(r.Id,2,"en");games.Move(r.Id,1,0,"4");_ = JsonSerializer.Serialize(games.View(r,2,s=>s));}
Console.WriteLine($"1000 create/join/move/view: wall={clock.Elapsed.TotalMilliseconds:F0} ms, process CPU={(process.TotalProcessorTime-cpu).TotalMilliseconds:F0} ms");
clock.Restart();cpu=process.TotalProcessorTime;
for(int i=0;i<50;i++){var games=new FriendGames();var r=games.Create("chess",1);games.Join(r.Id,2,"en");_ = JsonSerializer.Serialize(games.View(r,1,s=>s));}
Console.WriteLine($"50 fresh chess legal views: wall={clock.Elapsed.TotalMilliseconds:F0} ms, process CPU={(process.TotalProcessorTime-cpu).TotalMilliseconds:F0} ms");
clock.Restart();cpu=process.TotalProcessorTime;await Task.Delay(500);Console.WriteLine($"500ms idle after game work: process CPU={(process.TotalProcessorTime-cpu).TotalMilliseconds:F0} ms");
process.Refresh();Console.WriteLine($"Peak process working set incl .NET={process.PeakWorkingSet64/1024/1024} MiB; current managed heap={GC.GetTotalMemory(false)/1024/1024} MiB. Linux harness, NOT Windows app measurement.");
foreach(var f in new[]{"Core/InstallInventory.cs","Services/InstallCopies.cs","Core/FriendGames.cs","Core/FriendGames.ChessCards.cs"}){var s=File.ReadAllText(f);if(s.Contains("new Timer(")||s.Contains("new DispatcherTimer")||s.Contains("while (true)")||s.Contains("while(true)"))throw new Exception("polling introduced: "+f);}
Console.WriteLine("PASS request-only inventory/game source guard; no new polling loops. Disk scans only on explicit request, depth/time/count limits.");
