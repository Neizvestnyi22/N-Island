using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;
using NIsland.Core;
namespace NIsland.Services
{
    public static class InstallCopies
    {
        public static InstallInventory.Identity Identify(string path)
        {
            var before=new FileInfo(path);long size=before.Length;var at=before.LastWriteTimeUtc;
            var v=FileVersionInfo.GetVersionInfo(path);string hash="";
            var meta=new InstallInventory.Identity(v.ProductName,v.OriginalFilename,v.CompanyName,v.ProductVersion,"",size,at);
            if(!InstallInventory.Matches(meta))return meta;
            if(size>250_000_000)throw new IOException("Size limit: 250 MB");
            using(var f=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))hash=Convert.ToHexString(SHA256.HashData(f));
            var after=new FileInfo(path);if(after.Length!=size||after.LastWriteTimeUtc!=at)throw new IOException("File changed during scan");
            return new(v.ProductName,v.OriginalFilename,v.CompanyName,v.ProductVersion,hash,size,at);
        }
        static InstallInventory.ProcessInfo ProcessInfo(Process p)
        {
            try{return new(p.Id,p.MainModule?.FileName,p.StartTime.ToUniversalTime());}
            catch(Exception e){return new(p.Id,null,DateTime.MinValue,e.GetType().Name);}
        }
        public static InstallInventory.ProcessInfo[] Running()
        {
            var list=new List<InstallInventory.ProcessInfo>();
            // Enumerate candidate name only; renamed processes outside selected roots are explicitly not covered.
            foreach(var p in Process.GetProcessesByName("NIsland"))using(p)list.Add(ProcessInfo(p));return list.ToArray();
        }
        public static string SharedData=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"NIsland");
        public static InstallInventory.Scan Scan(string selected=null)
        {
            var roots=new[]{AppContext.BaseDirectory,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NIsland"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"NIsland"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs"),selected};
            return InstallInventory.Inspect(roots,Running(),Identify);
        }
        public static bool RequestClose(InstallInventory.Candidate file,InstallInventory.ProcessInfo selected)
        {
            if(!file.Identified||Identify(file.Path)!=file.Identity)return false;
            using(var p=Process.GetProcessById(selected.Pid))
            {
                if(!InstallInventory.SameProcess(selected,ProcessInfo(p),Environment.ProcessId))return false;
                return p.CloseMainWindow(); // no Kill, no tree-wide termination
            }
        }
        public static InstallInventory.Related Fingerprint(string path)
        {
            var f=new FileInfo(path);long size=f.Length;var at=f.LastWriteTimeUtc;
            using var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            string hash=Convert.ToHexString(SHA256.HashData(stream));
            return new(path,"app",hash,size,at);
        }
        public static InstallInventory.Related[] Related(InstallInventory.Candidate copy)=>InstallInventory.RelatedFiles(copy,AppContext.BaseDirectory,Fingerprint);
        public static bool RecycleRelated(InstallInventory.Related selected,InstallInventory.Candidate copy)
        {
            if(Identify(copy.Path)!=copy.Identity)return false;
            var actual=Fingerprint(selected.Path) with{Kind=selected.Kind};
            if(!InstallInventory.MayRecycleRelated(selected,actual,copy,Environment.ProcessPath,new[]{SharedData,Store.Dir,AppContext.BaseDirectory},Running()))return false;
            if(!Related(copy).Contains(selected))return false;
            FileSystem.DeleteFile(selected.Path,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);return true;
        }
        public static bool Recycle(InstallInventory.Candidate selected)
        {
            var running=Running();
            if(!InstallInventory.MayRecycle(selected,Identify(selected.Path),Environment.ProcessPath,new[]{SharedData,Store.Dir,selected.PreviewData},running))return false;
            // Only the exact selected EXE, never its directory, DLLs, settings, preview data or unrelated files.
            FileSystem.DeleteFile(selected.Path,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);return true;
        }
    }
}
