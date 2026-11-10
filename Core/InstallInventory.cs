using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace NIsland.Core
{
    /// <summary>Non-destructive inventory. Metadata identity is not proof of publisher authenticity.</summary>
    public static class InstallInventory
    {
        public record Identity(string Product,string Original,string Company,string Version,string Hash,long Size,DateTime Written);
        public record ProcessInfo(int Pid,string Path,DateTime Started,string Error=null);
        public record Candidate(string Path,Identity Identity,bool Identified,string Note,ProcessInfo[] Processes,string PreviewData);
        public record Scan(Candidate[] Files,ProcessInfo[] UnknownProcesses,string[] Errors,bool Truncated);
        public record Related(string Path,string Kind,string Hash,long Size,DateTime Written);
        public static Related[] RelatedFiles(Candidate copy,string referenceDir,Func<string,Related> fingerprint)
        {
            if(!copy.Identified)return Array.Empty<Related>();
            var names=new[]{"NIsland.dll","NIsland.deps.json","NIsland.runtimeconfig.json","ChessLibrary.dll","Ardalis.SmartEnum.dll","README-RU.txt","LICENSE.txt","THIRD-PARTY-NOTICES.txt","TEST-PREVIEW.cmd"};
            var found=new List<Related>();var dir=Path.GetDirectoryName(copy.Path);
            foreach(var name in names)
            {
                var path=Path.Combine(dir,name);var reference=Path.Combine(referenceDir,name);
                if(!File.Exists(path)||!File.Exists(reference)||Linked(path)||Linked(reference))continue;
                try{var file=fingerprint(path);var known=fingerprint(reference);if(file.Hash==known.Hash&&file.Size==known.Size)found.Add(file with{Kind="app"});}catch{}
            }
            // Data may belong to a different version. Explicitly selected, never included in the app area.
            if(copy.PreviewData!=null&&!Linked(copy.PreviewData)&&File.Exists(Path.Combine(copy.PreviewData,".nisland-preview-data"))&&File.ReadAllText(Path.Combine(copy.PreviewData,".nisland-preview-data")).Trim()=="N-Island preview data v1")foreach(var name in new[]{"settings.json","data.json","friend-games.json","bot-replies.json","log.txt"})
            {
                var path=Path.Combine(copy.PreviewData,name);if(!File.Exists(path)||Linked(path))continue;
                try{found.Add(fingerprint(path) with{Kind="preview-data"});}catch{}
            }
            return found.ToArray();
        }
        public static bool MayRecycleRelated(Related selected,Related current,Candidate copy,string currentExe,IEnumerable<string> protectedData,IEnumerable<ProcessInfo> running)
        {
            if(!copy.Identified||selected!=current||Linked(selected.Path)||Linked(copy.Path)||!File.Exists(copy.Path)||string.Equals(copy.Path,currentExe,StringComparison.OrdinalIgnoreCase))return false;
            if(running.Any(p=>p.Path==null||string.Equals(p.Path,copy.Path,StringComparison.OrdinalIgnoreCase)))return false;
            if(protectedData.Any(d=>Inside(selected.Path,d)))return false;
            string root=selected.Kind=="preview-data"?copy.PreviewData:Path.GetDirectoryName(copy.Path);
            return root!=null&&string.Equals(Path.GetDirectoryName(selected.Path),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);
        }
        public static bool Matches(Identity i)=>i!=null&&(i.Product=="N-Island"||i.Product=="NIsland")&&string.Equals(i.Original,"NIsland.dll",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(i.Version);
        public static bool Linked(string path)
        {
            for(var p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))
                if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)return true;
            return false;
        }
        public static Scan Inspect(IEnumerable<string> roots,IEnumerable<ProcessInfo> processes,Func<string,Identity> identify,int maxDepth=3,int limit=3000)
        {
            var errors=new List<string>();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int examined=0;bool truncated=false;var clock=System.Diagnostics.Stopwatch.StartNew();var ps=processes.ToArray();
            foreach(var proc in ps.Where(p=>p.Path!=null))paths.Add(Path.GetFullPath(proc.Path));
            foreach(var root in roots.Where(r=>!string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var todo=new Stack<(string,int)>();todo.Push((root,0));
                while(todo.Count>0)
                {
                    if(examined++>=limit||clock.Elapsed>TimeSpan.FromSeconds(5)){truncated=true;break;}var (dir,depth)=todo.Pop();
                    try
                    {
                        if(File.Exists(dir)){if(!Linked(dir))paths.Add(Path.GetFullPath(dir));continue;}
                        if(!Directory.Exists(dir)||Linked(dir))continue;
                        int entries=0;
                        foreach(var f in Directory.EnumerateFiles(dir,"*",SearchOption.TopDirectoryOnly))
                        {if(++entries>1000){truncated=true;break;}if(string.Equals(Path.GetFileName(f),"NIsland.exe",StringComparison.OrdinalIgnoreCase))paths.Add(Path.GetFullPath(f));}
                        if(depth<maxDepth)foreach(var d in Directory.EnumerateDirectories(dir).Take(129)){if(++entries>1128){truncated=true;break;}if(!Linked(d))todo.Push((d,depth+1));}
                    }
                    catch(Exception e){errors.Add(dir+": "+e.GetType().Name);}
                }
                if(truncated)break;
            }
            var files=new List<Candidate>();
            foreach(var path in paths.Take(100))
            {
                Identity identity=null;string note=null;
                try {if(Linked(path))note="Reparse point";else identity=identify(path);}catch(Exception e){note=e.GetType().Name;}
                bool match=Matches(identity);string data=Path.Combine(Path.GetDirectoryName(path),"data-preview");
                if(!Directory.Exists(data))data=null;
                files.Add(new(path,identity,match,note,ps.Where(p=>p.Path!=null&&string.Equals(Path.GetFullPath(p.Path),path,StringComparison.OrdinalIgnoreCase)).ToArray(),data));
            }
            if(paths.Count>100)truncated=true;
            return new(files.ToArray(),ps.Where(p=>p.Path==null).ToArray(),errors.ToArray(),truncated);
        }
        public static bool MayRecycle(Candidate selected,Identity current,string currentExe,IEnumerable<string> dataRoots,IEnumerable<ProcessInfo> running)
        {
            if(!selected.Identified||!Matches(current)||selected.Identity!=current||Linked(selected.Path)||string.Equals(Path.GetFullPath(selected.Path),Path.GetFullPath(currentExe),StringComparison.OrdinalIgnoreCase))return false;
            if(dataRoots.Where(x=>!string.IsNullOrWhiteSpace(x)).Any(d=>Inside(selected.Path,d)))return false;
            return !running.Any(p=>p.Path==null||string.Equals(Path.GetFullPath(p.Path),Path.GetFullPath(selected.Path),StringComparison.OrdinalIgnoreCase));
        }
        static bool Inside(string file,string dir)
        {
            var d=Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));var f=Path.GetFullPath(file);
            return f.Equals(d,StringComparison.OrdinalIgnoreCase)||f.StartsWith(d+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        }
        public static bool SameProcess(ProcessInfo selected,ProcessInfo actual,int self)
            =>selected.Pid!=self&&selected.Pid==actual.Pid&&selected.Started==actual.Started&&selected.Path!=null&&actual.Path!=null&&string.Equals(Path.GetFullPath(selected.Path),Path.GetFullPath(actual.Path),StringComparison.OrdinalIgnoreCase);
    }
}
