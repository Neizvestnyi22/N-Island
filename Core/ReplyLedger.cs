using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NIsland.Core
{
    /// <summary>Only IDs returned by successful bot sends; never incoming/user messages.</summary>
    public sealed class ReplyLedger
    {
        public sealed record Entry(long Chat, long Id, DateTime At);
        readonly string file;
        readonly Dictionary<string,List<Entry>> groups;
        public ReplyLedger(string file=null)
        {
            this.file=file;
            try { groups=file!=null&&File.Exists(file)?JsonSerializer.Deserialize<Dictionary<string,List<Entry>>>(File.ReadAllText(file))??new():new(); }
            catch { groups=new(); } // No deletion if the index is unreadable.
            foreach(var k in groups.Keys.ToArray()) { if(groups[k]==null)groups.Remove(k);else groups[k]=groups[k].Where(e=>e!=null&&e.Chat>0&&e.Id>0).ToList(); }
        }
        public Entry[] Replace(string key,IEnumerable<Entry> sent,IEnumerable<long> keep,DateTime now)
        {
            lock(groups)
            {
                var fresh=sent.Where(e=>e.Chat>0&&e.Id>0).DistinctBy(e=>(e.Chat,e.Id)).ToList();
                var kept=keep.ToHashSet();
                if(fresh.Count==0&&kept.Count==0)return Array.Empty<Entry>();
                groups.TryGetValue(key,out var old);old??=new();
                var obsolete=old.Where(e=>!kept.Contains(e.Id)&&!fresh.Any(n=>n.Chat==e.Chat&&n.Id==e.Id)&&now-e.At<TimeSpan.FromHours(47)&&now>=e.At).ToArray();
                fresh.AddRange(old.Where(e=>kept.Contains(e.Id)&&!fresh.Any(n=>n.Chat==e.Chat&&n.Id==e.Id)));
                groups[key]=fresh;
                foreach(var k in groups.Where(p=>p.Value.All(e=>now-e.At>=TimeSpan.FromHours(47))).Select(p=>p.Key).ToArray())groups.Remove(k);
                while(groups.Count>512)groups.Remove(groups.OrderBy(p=>p.Value.Count==0?DateTime.MinValue:p.Value.Max(e=>e.At)).First().Key);
                Save();return obsolete;
            }
        }
        public void Save()
        {
            if(file==null)return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            var tmp=file+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(groups));File.Move(tmp,file,true);
        }
    }
}
