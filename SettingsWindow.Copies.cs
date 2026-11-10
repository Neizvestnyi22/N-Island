using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NIsland.Core;
using NIsland.Pages;
using NIsland.Services;
namespace NIsland
{
    public partial class SettingsWindow
    {
        UIElement CopiesPanel()
        {
            var rows=new StackPanel();var status=UI.Text("Проверка ещё не запускалась",12,false,UI.Dim);
            bool scanning=false;
            async Task Scan(string path=null)
            {
                if(scanning)return;scanning=true;
                status.Text=I18n.T("Проверяю копии…");rows.Children.Clear();
                try
                {
                    var result=await Task.Run(()=>InstallCopies.Scan(path));
                    status.Text=I18n.T("Найдено файлов")+": "+result.Files.Length+(result.Truncated?" · "+I18n.T("Сканирование ограничено"):"");
                    rows.Children.Add(Txt.Selectable(I18n.T("Сначала удаляй выбранные дополнительные файлы, EXE — последним. Неизвестные файлы, общие и текущие данные сохраняются.")+"\n"+I18n.T("Общие данные — не удаляются")+": "+InstallCopies.SharedData+"\n"+I18n.T("Данные текущего запуска")+": "+Store.Dir,11));
                    foreach(var file in result.Files)
                    {
                        var details=file.Path+"\n"+(file.Identified?I18n.T("Метаданные N-Island совпали; подпись издателя не проверена"):I18n.T("Неизвестный файл — действия запрещены"))+"\n"+(file.Identity?.Version??"?")+" · "+file.Identity?.Size+" bytes";
                        if(file.PreviewData!=null)details+="\n"+I18n.T("Тестовые данные — не удаляются")+": "+file.PreviewData;
                        if(file.Note!=null)details+="\n"+file.Note;
                        var buttons=new StackPanel{Orientation=Orientation.Horizontal};
                        foreach(var proc in file.Processes)
                        {
                            details+="\nPID "+proc.Pid+" · "+proc.Started.ToLocalTime();
                            if(file.Identified&&proc.Pid!=Environment.ProcessId)buttons.Children.Add(UI.Pill("Попросить закрыть",async()=>
                            {
                                if(MessageBox.Show(this,I18n.T("Закрыть только выбранный процесс?")+"\nPID "+proc.Pid+"\n"+file.Path,"N-Island",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                                try {status.Text=I18n.T(await Task.Run(()=>InstallCopies.RequestClose(file,proc))?"Запрос закрытия отправлен; проверь результат":"Не удалось или данные изменились");}catch(Exception e){status.Text=e.Message;}
                            }));
                        }
                        if(file.Identified&&file.Processes.Length==0&&!string.Equals(file.Path,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))buttons.Children.Add(UI.Pill("EXE в корзину",async()=>
                        {
                            if(MessageBox.Show(this,I18n.T("В корзину будет отправлен ТОЛЬКО этот EXE. Данные и остальные файлы не изменятся.")+"\n"+file.Path,"N-Island",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                            try{status.Text=I18n.T(await Task.Run(()=>InstallCopies.Recycle(file))?"EXE отправлен в корзину":"Не удалось или данные изменились");}catch(Exception e){status.Text=e.Message;}
                        }));
                        var item=UI.Col(Txt.Selectable(details,11),buttons);
                        if(file.Identified)foreach(var related in await Task.Run(()=>InstallCopies.Related(file)))
                        {
                            var area=related.Kind=="preview-data"?"Тестовые данные (отдельный выбор)":"Файл приложения (хеш совпал с текущей копией)";
                            item.Children.Add(Txt.Selectable(I18n.T(area)+"\n"+related.Path+"\nSHA256 "+related.Hash,10));
                            item.Children.Add(UI.Pill("Этот файл в корзину",async()=>
                            {
                                var warning=I18n.T("Удалить только выбранный файл в корзину?")+"\n"+related.Path+"\n"+I18n.T("Нет гарантии удаления без следов: SSD, резервные копии и журналы Windows сохраняются.");
                                if(MessageBox.Show(this,warning,"N-Island",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                                try{bool ok=await Task.Run(()=>InstallCopies.RecycleRelated(related,file));status.Text=I18n.T(ok?"Файл отправлен в корзину":"Не удалось или данные изменились");}catch(Exception e){status.Text=e.Message;}
                            }));
                        }
                        rows.Children.Add(item.M(0,8,0,4));
                    }
                    foreach(var proc in result.UnknownProcesses)rows.Children.Add(Txt.Selectable("PID "+proc.Pid+" · "+I18n.T("Путь процесса недоступен — действия запрещены")+" · "+proc.Error,11));
                    foreach(var error in result.Errors)rows.Children.Add(Txt.Selectable(error,10));
                }
                catch(Exception e){status.Text=e.Message;}
                finally{scanning=false;}
            }
            var scanButton=UI.Pill("Проверить",()=>{_=Scan();});
            var pickButton=UI.Pill("Выбрать EXE",()=>{var d=new Microsoft.Win32.OpenFileDialog{Filter="Windows executable|*.exe"};if(d.ShowDialog(this)==true)_=Scan(d.FileName);});
            var controls=UI.Row(scanButton,pickButton);
            var content=UI.Col(controls,status,rows);
            var section=Wide("Точная проверка копий", "Проверяются папка программы, Рабочий стол, Загрузки, Документы и LocalAppData Programs, глубина до 3 папок. Это не поиск по всему диску. Ничего не удаляется автоматически.",content);
            return Group("Копии и процессы",section);
        }
    }
}
