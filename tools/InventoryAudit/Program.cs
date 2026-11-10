using NIsland.Core;
int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
var root=Path.Combine(Path.GetTempPath(),"inventory-audit-"+Guid.NewGuid());Directory.CreateDirectory(root);
try {
 var dir=Path.Combine(root,"copy");Directory.CreateDirectory(dir);var exe=Path.Combine(dir,"NIsland.exe");File.WriteAllText(exe,"fixture");Directory.CreateDirectory(Path.Combine(dir,"data-preview"));
 var unknown=Path.Combine(root,"NIsland.exe");File.WriteAllText(unknown,"not the app");
 var identity=new InstallInventory.Identity("N-Island","NIsland.dll","test","2.5.0-preview.2","hash",7,DateTime.UtcNow);
 var proc=new InstallInventory.ProcessInfo(123,exe,DateTime.UtcNow);
 var scan=InstallInventory.Inspect(new[]{root},new[]{proc,new InstallInventory.ProcessInfo(124,null,DateTime.MinValue,"denied")},p=>p==exe?identity:null);
 Check(scan.Files.Length==2,"candidate count");var selected=scan.Files.Single(f=>f.Path==exe);Check(selected.Identified&&selected.Processes.Single().Pid==123&&selected.PreviewData!=null,"identity/process/data");
 Check(!scan.Files.Single(f=>f.Path==unknown).Identified,"same-name false positive");Check(scan.UnknownProcesses.Length==1,"inaccessible process dropped");
 Check(!InstallInventory.MayRecycle(selected,identity,exe,Array.Empty<string>(),Array.Empty<InstallInventory.ProcessInfo>()),"self deletion");
 Check(!InstallInventory.MayRecycle(selected,identity,unknown,new[]{dir},Array.Empty<InstallInventory.ProcessInfo>()),"data deletion");
 Check(!InstallInventory.MayRecycle(selected,identity,unknown,Array.Empty<string>(),new[]{proc}),"running deletion");
 Check(!InstallInventory.MayRecycle(selected,identity with{Hash="changed"},unknown,Array.Empty<string>(),Array.Empty<InstallInventory.ProcessInfo>()),"changed file deletion");
 Check(InstallInventory.MayRecycle(selected,identity,unknown,Array.Empty<string>(),Array.Empty<InstallInventory.ProcessInfo>()),"known closed copy blocked");
 Check(!InstallInventory.MayRecycle(selected,identity,unknown,Array.Empty<string>(),scan.UnknownProcesses),"unknown process unsafe deletion");
 Check(InstallInventory.SameProcess(proc,proc,999)&&!InstallInventory.SameProcess(proc,proc with{Started=proc.Started.AddSeconds(1)},999),"pid reuse");
 Check(!InstallInventory.SameProcess(proc,proc,123),"self close");
 Check(InstallInventory.Inspect(new[]{Path.Combine(root,"missing")},Array.Empty<InstallInventory.ProcessInfo>(),_=>null).Files.Length==0,"empty scan");
 Check(InstallInventory.Inspect(new[]{root},Array.Empty<InstallInventory.ProcessInfo>(),_=>throw new IOException()).Files.All(f=>!f.Identified),"read error grants action");
 Check(InstallInventory.Inspect(new[]{root},Array.Empty<InstallInventory.ProcessInfo>(),_=>identity,limit:0).Truncated,"limit not reported");
 var link=Path.Combine(root,"linked");Directory.CreateSymbolicLink(link,dir);Check(InstallInventory.Linked(link),"link not detected");
 var companion=Path.Combine(dir,"NIsland.runtimeconfig.json");File.WriteAllText(companion,"known");var reference=Path.Combine(root,"ref");Directory.CreateDirectory(reference);File.WriteAllText(Path.Combine(reference,"NIsland.runtimeconfig.json"),"known");File.WriteAllText(Path.Combine(dir,"private.txt"),"preserve");
 InstallInventory.Related Fingerprint(string path)=>new(path,"app",File.ReadAllText(path),new FileInfo(path).Length,File.GetLastWriteTimeUtc(path));
 var related=InstallInventory.RelatedFiles(selected,reference,Fingerprint);Check(related.Length==1&&related[0].Path==companion,"unknown companion included");
 Check(!InstallInventory.MayRecycleRelated(related[0],related[0] with{Hash="changed"},selected,unknown,Array.Empty<string>(),Array.Empty<InstallInventory.ProcessInfo>()),"related changed allowed");
 Check(!InstallInventory.MayRecycleRelated(related[0],related[0],selected,unknown,new[]{dir},Array.Empty<InstallInventory.ProcessInfo>()),"shared related allowed");
 File.WriteAllText(Path.Combine(selected.PreviewData,"settings.json"),"user data");Check(InstallInventory.RelatedFiles(selected,reference,Fingerprint).All(x=>x.Kind!="preview-data"),"unmarked legacy data authorized");
 File.WriteAllText(Path.Combine(selected.PreviewData,".nisland-preview-data"),"N-Island preview data v1");Check(InstallInventory.RelatedFiles(selected,reference,Fingerprint).Count(x=>x.Kind=="preview-data")==1,"marked preview data missing");
 Check(File.Exists(Path.Combine(dir,"private.txt"))&&File.Exists(Path.Combine(selected.PreviewData,"settings.json")),"audit deleted fixture");
 Console.WriteLine($"PASS {checks} non-destructive inventory scenarios: metadata mismatch, missing/denied, paths, PID reuse, data-preview, symlink, limits, revalidation.");
} finally {Directory.Delete(root,true);}
