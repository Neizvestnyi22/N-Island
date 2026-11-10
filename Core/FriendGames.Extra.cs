using System;
using System.Collections.Generic;
using System.Linq;
namespace NIsland.Core
{
    public sealed partial class FriendGames
    {
        public static readonly Game[] ExtraCatalog=NewExtraCatalog();
        static Game[] NewExtraCatalog()=>new Game[]{
            new("gomoku","⚪ Пять в ряд","Поле 5×5: первым собери пять фишек в ряд."),
            new("hex","⬡ Гекс","● соединяет верх и низ, ○ — левый и правый края. Соседи: ↔ ↕ ↗ ↙."),
            new("domineering","▥ Домино-захват","● кладёт вертикальное домино, ○ — горизонтальное. Нет хода — проигрыш."),
            new("chomp","🍫 Шоколад","Выбери клетку: исчезнет прямоугольник справа снизу. Верхняя левая ☠ ядовита."),
            new("obstruction","🚧 Блокада","Ставь фишку: восемь соседних клеток блокируются. Нет хода — проигрыш."),
            new("isolation","🏝 Изоляция","Сначала шагни на соседнюю клетку, затем удали пустую. Не можешь шагнуть — проигрыш."),
            new("breakthrough","♟ Прорыв","Выбери пешку, затем шаг: вперёд прямо или по диагонали. Бей по диагонали. Дойди до края."),
            new("mancala","🫘 Манкала","Три лунки у каждого. Сей против часовой стрелки, минуя чужой дом. Больше камней — победа."),
            new("sos","🔤 SOS","Выбери S или O и клетку. Каждое новое SOS даёт очко и ещё ход. Больше очков — победа."),
            new("numberttt","🔢 Сумма 15","● выбирает нечётное, ○ — чётное, затем клетку. Собери ряд с суммой 15."),
            new("kayles","🎳 Кегли","Сбей одну или две соседние кегли. Последний ход побеждает."),
            new("wythoff","💎 Две кучи","Убери камни из одной кучи или поровну из обеих. Последний ход побеждает."),
            new("euclid","📐 Евклид","Вычти из большего числа кратное меньшего, не уходя в минус. Получи ноль — победа."),
            new("treble","☘ Тройка","Ставьте общие X на полосе. Кто завершил XXX, побеждает."),
            new("silver","🪙 Серебряный доллар","Выбери монету и сдвинь влево, не перепрыгивая другие. Нет хода — проигрыш.")
        };
        static void InitExtra(Room r)
        {
            r.Board=r.Kind switch{
                "gomoku"=>new int[25],"hex" or "domineering" or "chomp" or "obstruction" or "isolation" or "breakthrough"=>new int[16],
                "sos" or "numberttt"=>new int[9],"mancala"=>new[]{3,3,3,0,3,3,3,0},"kayles"=>new int[12],
                "wythoff"=>new[]{7,9},"euclid"=>new[]{15,24},"treble"=>new int[13],"silver"=>new[]{0,0,1,0,0,1,0,0,1,0,0,1},_=>r.Board};
            if(r.Kind=="isolation"){r.Board[0]=1;r.Board[15]=2;}
            if(r.Kind=="breakthrough"){for(int i=0;i<4;i++){r.Board[i]=2;r.Board[12+i]=1;}r.Aux=new[]{-1};}
            if(r.Kind=="silver")r.Pot=-1;
            if(r.Kind=="sos")r.Pot=1;
        }
        static IEnumerable<int> Adj(int cell,int width,int height,bool diagonal=true)
        {
            int x=cell%width,y=cell/width;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                if((dx!=0||dy!=0)&&(diagonal||dx==0||dy==0)&&x+dx>=0&&x+dx<width&&y+dy>=0&&y+dy<height)yield return (y+dy)*width+x+dx;
        }
        static bool HexWin(int[] board,int mark)
        {
            var seen=new HashSet<int>();var todo=new Queue<int>();
            for(int i=0;i<4;i++){int n=mark==1?i:i*4;if(board[n]==mark){todo.Enqueue(n);seen.Add(n);}}
            while(todo.Count>0){int i=todo.Dequeue(),x=i%4,y=i/4;if(mark==1?y==3:x==3)return true;
                foreach(var (dx,dy) in new[]{(-1,0),(1,0),(0,-1),(0,1),(1,-1),(-1,1)}){int xx=x+dx,yy=y+dy,n=yy*4+xx;if(xx>=0&&xx<4&&yy>=0&&yy<4&&board[n]==mark&&seen.Add(n))todo.Enqueue(n);}}
            return false;
        }
        static List<int> DominoMoves(Room r,int p)=>Enumerable.Range(0,16).Where(i=>r.Board[i]==0&&(p==0?i/4<3&&r.Board[i+4]==0:i%4<3&&r.Board[i+1]==0)).ToList();
        static List<(int From,int To)> PawnMoves(Room r,int p)
        {
            var a=new List<(int,int)>();int mark=p+1,dy=p==0?-1:1;
            for(int i=0;i<16;i++)if(r.Board[i]==mark){int x=i%4,y=i/4,yy=y+dy;if(yy<0||yy>=4)continue;for(int dx=-1;dx<=1;dx++){int xx=x+dx;if(xx<0||xx>=4)continue;int n=yy*4+xx;if(dx==0?r.Board[n]==0:r.Board[n]!=mark)a.Add((i,n));}}
            return a;
        }
        static IEnumerable<int[]> Triples()=>new[]{new[]{0,1,2},new[]{3,4,5},new[]{6,7,8},new[]{0,3,6},new[]{1,4,7},new[]{2,5,8},new[]{0,4,8},new[]{2,4,6}};
        static bool SilverHasMove(Room r)=>Enumerable.Range(1,11).Any(i=>r.Board[i]==1&&r.Board[i-1]==0);
        static bool MoveExtra(Room r,int p,string op,out bool extra)
        {
            extra=false;int mark=p+1,n;bool Cell(int size,out int cell){cell=-1;return int.TryParse(op,out cell)&&cell>=0&&cell<size;}
            switch(r.Kind)
            {
                case "gomoku":case "hex":
                    if(!Cell(r.Board.Length,out n)||r.Board[n]!=0)return false;r.Board[n]=mark;
                    if(r.Kind=="hex"?HexWin(r.Board,mark):Line(r.Board,5,5,mark))r.Winner=mark;else if(r.Board.All(v=>v!=0))r.Winner=0;break;
                case "domineering":
                    if(!Cell(16,out n)||!DominoMoves(r,p).Contains(n))return false;r.Board[n]=r.Board[n+(p==0?4:1)]=mark;
                    if(DominoMoves(r,1-p).Count==0)r.Winner=mark;break;
                case "chomp":
                    if(!Cell(16,out n)||r.Board[n]!=0)return false;
                    for(int y=n/4;y<4;y++)for(int x=n%4;x<4;x++)r.Board[y*4+x]=1;
                    if(n==0)r.Winner=3-mark;break;
                case "obstruction":
                    if(!Cell(16,out n)||r.Board[n]!=0)return false;r.Board[n]=mark;foreach(var i in Adj(n,4,4))if(r.Board[i]==0)r.Board[i]=3;
                    if(r.Board.All(v=>v!=0))r.Winner=mark;break;
                case "isolation":
                    if(!Cell(16,out n)||r.Board[n]!=0)return false;
                    if(r.Pot==0){int pos=Array.IndexOf(r.Board,mark);if(!Adj(pos,4,4).Contains(n))return false;r.Board[pos]=0;r.Board[n]=mark;r.Pot=1;extra=true;}
                    else{r.Board[n]=3;r.Pot=0;int pos=Array.IndexOf(r.Board,3-mark);if(!Adj(pos,4,4).Any(i=>r.Board[i]==0))r.Winner=mark;}
                    break;
                case "breakthrough":
                    if(op.StartsWith("s")&&int.TryParse(op[1..],out n)&&PawnMoves(r,p).Any(m=>m.From==n)){r.Aux[0]=n;extra=true;break;}
                    if(!Cell(16,out n)||!PawnMoves(r,p).Contains((r.Aux[0],n)))return false;
                    r.Board[r.Aux[0]]=0;r.Board[n]=mark;r.Aux[0]=-1;
                    if(n/4==(p==0?0:3)||PawnMoves(r,1-p).Count==0)r.Winner=mark;break;
                case "mancala":
                    if(!Cell(8,out n)||(p==0?n>2:n<4||n>6)||r.Board[n]==0)return false;
                    int stones=r.Board[n],at=n,home=p==0?3:7,other=p==0?7:3;r.Board[n]=0;
                    while(stones>0){at=(at+1)%8;if(at==other)continue;r.Board[at]++;stones--;}
                    if((p==0?at<3:at>=4&&at<=6)&&r.Board[at]==1&&r.Board[6-at]>0){r.Board[home]+=1+r.Board[6-at];r.Board[at]=r.Board[6-at]=0;}
                    if(Enumerable.Range(0,3).All(i=>r.Board[i]==0)||Enumerable.Range(4,3).All(i=>r.Board[i]==0))
                    {for(int i=0;i<3;i++){r.Board[3]+=r.Board[i];r.Board[i]=0;r.Board[7]+=r.Board[i+4];r.Board[i+4]=0;}r.Winner=r.Board[3]==r.Board[7]?0:r.Board[3]>r.Board[7]?1:2;}
                    else extra=at==home;break;
                case "sos":
                    if(op=="S"||op=="O"){r.Pot=op=="S"?1:2;extra=true;break;}
                    if(!Cell(9,out n)||r.Board[n]!=0)return false;r.Board[n]=r.Pot;
                    int scored=Triples().Count(a=>a.Contains(n)&&r.Board[a[0]]==1&&r.Board[a[1]]==2&&r.Board[a[2]]==1);
                    r.Scores[p]+=scored;extra=scored>0;
                    if(r.Board.All(v=>v!=0))r.Winner=r.Scores[0]==r.Scores[1]?0:r.Scores[0]>r.Scores[1]?1:2;break;
                case "numberttt":
                    if(op.StartsWith("n")&&int.TryParse(op[1..],out n)&&n>=1&&n<=9&&n%2==(p==0?1:0)&&!r.Board.Contains(n)){r.Pot=n;extra=true;break;}
                    if(!Cell(9,out n)||r.Board[n]!=0||r.Pot<1||r.Pot%2!=(p==0?1:0)||r.Board.Contains(r.Pot))return false;
                    r.Board[n]=r.Pot;r.Pot=0;
                    if(Triples().Any(a=>a.All(i=>r.Board[i]>0)&&a.Sum(i=>r.Board[i])==15))r.Winner=mark;else if(r.Board.All(v=>v>0))r.Winner=0;break;
                case "kayles":
                    var pair=op.Split('.');if(pair.Length!=2||!int.TryParse(pair[0],out n)||n<0||n>=12||!int.TryParse(pair[1],out int count)||count<1||count>2||n+count>12||Enumerable.Range(n,count).Any(i=>r.Board[i]!=0))return false;
                    for(int i=n;i<n+count;i++)r.Board[i]=1;if(r.Board.All(v=>v==1))r.Winner=mark;break;
                case "wythoff":
                    if(op.Length<2||!int.TryParse(op[1..],out n)||n<1)return false;
                    if(op[0]=='a'&&n<=r.Board[0])r.Board[0]-=n;
                    else if(op[0]=='b'&&n<=r.Board[1])r.Board[1]-=n;
                    else if(op[0]=='d'&&n<=r.Board.Min()){r.Board[0]-=n;r.Board[1]-=n;}else return false;
                    if(r.Board.Sum()==0)r.Winner=mark;break;
                case "euclid":
                    if(!int.TryParse(op,out n)||n<1)return false;int hi=r.Board[0]>=r.Board[1]?0:1,lo=1-hi;
                    if(r.Board[lo]==0||n>r.Board[hi]/r.Board[lo])return false;r.Board[hi]-=n*r.Board[lo];if(r.Board[hi]==0)r.Winner=mark;break;
                case "treble":
                    if(!Cell(13,out n)||r.Board[n]!=0)return false;r.Board[n]=1;if(Line(r.Board,13,3,1))r.Winner=mark;else if(r.Board.All(v=>v!=0))r.Winner=0;break;
                case "silver":
                    if(op.StartsWith("s")&&int.TryParse(op[1..],out n)&&n>0&&n<12&&r.Board[n]==1&&r.Board[n-1]==0){r.Pot=n;extra=true;break;}
                    if(!Cell(12,out n)||r.Pot<=0||r.Pot>=12||r.Board[r.Pot]!=1||n>=r.Pot||Enumerable.Range(n,r.Pot-n).Any(i=>r.Board[i]!=0))return false;
                    r.Board[r.Pot]=0;r.Board[n]=1;r.Pot=-1;if(!SilverHasMove(r))r.Winner=mark;break;
                default:return false;
            }
            return true;
        }
        static void ViewExtra(Room r,int p,Func<string,string> t,Action<string,string> button,ref int columns,out string info)
        {
            info="";int mark=p+1;columns=4;
            string Piece(int v)=>v==0?"·":v==1?"●":v==2?"○":"×";
            switch(r.Kind)
            {
                case "gomoku":case "hex":case "obstruction":case "chomp":case "domineering":
                    columns=r.Kind=="gomoku"?5:4;
                    for(int i=0;i<r.Board.Length;i++)button(r.Kind=="chomp"?(r.Board[i]!=0?"×":i==0?"☠":"▪"):Piece(r.Board[i]),r.Board[i]==0&&(r.Kind!="domineering"||DominoMoves(r,p).Contains(i))?i.ToString():"");break;
                case "isolation":
                    int pos=Array.IndexOf(r.Board,mark);var valid=r.Pot==0?Adj(pos,4,4).Where(i=>r.Board[i]==0).ToHashSet():Enumerable.Range(0,16).Where(i=>r.Board[i]==0).ToHashSet();
                    for(int i=0;i<16;i++)button(Piece(r.Board[i]),valid.Contains(i)?i.ToString():"");info=t(r.Pot==0?"Шаг":"Удали клетку");break;
                case "breakthrough":
                    var moves=PawnMoves(r,p);for(int i=0;i<16;i++)button(Piece(r.Board[i])+(r.Aux[0]==i?"✓":""),r.Board[i]==mark&&moves.Any(m=>m.From==i)?"s"+i:moves.Contains((r.Aux[0],i))?i.ToString():"");break;
                case "mancala":
                    columns=4;foreach(var i in new[]{6,5,4,7,0,1,2,3})button(r.Board[i].ToString(),r.Board[i]>0&&(p==0?i<3:i>=4&&i<=6)?i.ToString():"");info="● "+r.Board[3]+" : "+r.Board[7]+" ○";break;
                case "sos":
                    columns=3;button("S"+(r.Pot==1?"✓":""),"S");button("O"+(r.Pot==2?"✓":""),"O");button("","");
                    for(int i=0;i<9;i++)button(r.Board[i]==0?"·":r.Board[i]==1?"S":"O",r.Board[i]==0?i.ToString():"");info=r.Scores[0]+":"+r.Scores[1];break;
                case "numberttt":
                    columns=3;for(int i=1;i<=9;i++)if(i%2==(p==0?1:0)&&!r.Board.Contains(i))button(i+(r.Pot==i?"✓":""),"n"+i);
                    for(int i=0;i<9;i++)button(r.Board[i]==0?"·":r.Board[i].ToString(),r.Board[i]==0&&r.Pot>0?i.ToString():"");info=t("Выбрано")+": "+r.Pot;break;
                case "kayles":
                    columns=6;for(int i=0;i<12;i++)button(r.Board[i]==0?(i+1).ToString():"×",r.Board[i]==0?i+".1":"");for(int i=0;i<11;i++)if(r.Board[i]==0&&r.Board[i+1]==0)button((i+1)+"+"+(i+2),i+".2");break;
                case "wythoff":
                    columns=5;for(int h=0;h<2;h++)for(int i=1;i<=r.Board[h];i++)button((h==0?"A−":"B−")+i,(h==0?"a":"b")+i);
                    for(int i=1;i<=r.Board.Min();i++)button("AB−"+i,"d"+i);info=r.Board[0]+" : "+r.Board[1];break;
                case "euclid":
                    columns=4;int min=r.Board.Min(),max=r.Board.Max();if(min>0)for(int i=1;i<=max/min;i++)button("−"+(i*min),i.ToString());info=r.Board[0]+" : "+r.Board[1];break;
                case "treble":
                    columns=7;for(int i=0;i<13;i++)button(r.Board[i]==1?"X":"·",r.Board[i]==0?i.ToString():"");break;
                case "silver":
                    columns=6;for(int i=0;i<12;i++)button(r.Board[i]==1?(r.Pot==i?"◉":"●"):(i+1).ToString(),r.Board[i]==1&&i>0&&r.Board[i-1]==0?"s"+i:r.Pot>i&&r.Board[r.Pot]==1&&Enumerable.Range(i,r.Pot-i).All(j=>r.Board[j]==0)?i.ToString():"");break;
            }
        }
    }
}
